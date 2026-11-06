using System.Reflection;
using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Infrastructure;
using Deckloom.Accounts.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Deckloom.Api.Tests;

// T-135 (RED): verificacao do papel de conexao (DB7/UA2) na inicializacao do host. Nenhuma conexao de banco e aberta:
// o inspetor e um falso criado por reflexao (DispatchProxy), pois a porta `IDatabaseRoleInspector` ainda nao existe;
// a falha e portanto de comportamento ausente, nao de compilacao. Dados 100% sinteticos.
//
// Suposicoes (ver perguntas ao orquestrador):
//  - `IDatabaseRoleInspector` e publica e esta em qualquer assembly de producao carregado (nome simples da interface);
//  - tem um unico metodo que devolve `RoleSnapshot` (sincrono, Task<RoleSnapshot> ou ValueTask<RoleSnapshot>);
//  - a verificacao so roda quando o inspetor esta registrado; as fabricas de teste/host E2E o removem
//    (como removem as demais portas de persistencia) e o host ainda inicia.
public sealed class StartupRoleCheckTests
{
    private const string InspectorName = "IDatabaseRoleInspector";
    private const string SyntheticRole = "synthetic_role_zz81";
    private const string SyntheticDatabase = "synthetic_db_zz81";
    private const string SyntheticHost = "db-host-synthetic.internal";
    private const string SyntheticPassword = "test-db-secret-9f3a";
    private const string CatalogLeak = "permission denied for pg_roles synthetic-leak-77";

    private static RoleSnapshot Conforming() => new() { RoleName = SyntheticRole, DatabaseName = SyntheticDatabase };

    // ---- (a) executa antes de aceitar requisicoes -------------------------------------------------------------

    [Fact]
    public void T135_host_startup_runs_the_role_check_before_any_request_is_accepted()
    {
        var inspector = FakeInspector.Returning(Conforming());
        using var factory = new RoleCheckFactory(inspector);

        _ = factory.Services; // sobe o host (hosted services/filtros de inicializacao) sem enviar requisicao

        Assert.Equal(1, inspector.CallCount);
    }

    [Fact]
    public void T135_composition_with_the_postgres_adapter_registers_the_npgsql_inspector()
    {
        var inspectorType = FindInspectorType();
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(ProductionLikeConfig()).Build();
        InvokeAddAccountsModule(services, configuration, new TestEnvironment("Production"));

        var descriptor = services.LastOrDefault(d => d.ServiceType == inspectorType);

        Assert.NotNull(descriptor);
        Assert.Equal("NpgsqlDatabaseRoleInspector", descriptor!.ImplementationType?.Name);
    }

    // ---- (b) snapshot nao conforme --------------------------------------------------------------------------

    [Fact]
    public void T135_non_conforming_snapshot_refuses_startup_listing_only_condition_codes()
    {
        var snapshot = Conforming() with { IsSuperuser = true, BypassesRowLevelSecurity = true };
        using var factory = new RoleCheckFactory(FakeInspector.Returning(snapshot));

        var failure = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        var message = FullMessage(failure);
        Assert.Contains(DatabaseRolePolicy.SuperuserCode, message, StringComparison.Ordinal);
        Assert.Contains(DatabaseRolePolicy.BypassRlsCode, message, StringComparison.Ordinal);
        Assert.DoesNotContain(DatabaseRolePolicy.CreateRoleCode, message, StringComparison.Ordinal);
        AssertNoConnectionValues(message);
    }

    [Fact]
    public void T135_non_conforming_snapshot_does_not_leak_role_database_or_connection_values()
    {
        var snapshot = Conforming() with { OwnsObjects = true, CanCreateInDatabase = true };
        using var factory = new RoleCheckFactory(FakeInspector.Returning(snapshot));

        var failure = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        var message = FullMessage(failure);
        Assert.Contains(DatabaseRolePolicy.OwnsObjectsCode, message, StringComparison.Ordinal);
        Assert.Contains(DatabaseRolePolicy.CreateInDatabaseCode, message, StringComparison.Ordinal);
        AssertNoConnectionValues(message);
    }

    // ---- (c) snapshot conforme -----------------------------------------------------------------------------

    [Fact]
    public void T135_conforming_snapshot_lets_the_host_start()
    {
        var inspector = FakeInspector.Returning(Conforming());
        using var factory = new RoleCheckFactory(inspector);

        using var client = factory.CreateClient();

        Assert.NotNull(client);
        Assert.Equal(1, inspector.CallCount);
    }

    // ---- (d) falha ao consultar: fail closed ---------------------------------------------------------------

    [Fact]
    public void T135_inspector_query_failure_refuses_startup_with_a_generic_message()
    {
        var inspector = FakeInspector.Failing(new InvalidOperationException(CatalogLeak));
        using var factory = new RoleCheckFactory(inspector);

        var failure = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Equal(1, inspector.CallCount);
        Assert.DoesNotContain(CatalogLeak, failure.Message, StringComparison.Ordinal);
        AssertNoConnectionValues(FullMessage(failure));
    }

    [Fact]
    public void T135_cancelled_or_unavailable_database_also_refuses_startup()
    {
        using var factory = new RoleCheckFactory(FakeInspector.Failing(new TimeoutException("db unavailable")));

        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    // ---- (e) fakes de persistencia / host E2E: sem verificacao -----------------------------------------------

    [Fact]
    public void T135_host_with_persistence_fakes_does_not_register_nor_call_the_inspector()
    {
        _ = FindInspectorType(); // a porta precisa existir para esta garantia fazer sentido
        using var factory = new RoleCheckFactory(inspector: null);

        using var client = factory.CreateClient();

        Assert.Null(factory.Services.GetService(FindInspectorType()));
    }

    // ---- (f) nenhuma verificacao de migracoes pendentes -----------------------------------------------------

    [Fact]
    public void T135_startup_does_not_check_pending_migrations()
    {
        var inspector = FakeInspector.Returning(Conforming());
        using var factory = new RoleCheckFactory(inspector);

        _ = factory.Services;

        // A verificacao de papel nao pode introduzir nenhum servico de verificacao de migracoes.
        var all = factory.Services.GetServices<IHostedService>().Select(s => s.GetType().FullName ?? "");
        Assert.DoesNotContain(all, n => n.Contains("Migration", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(FindInspectorType());
    }

    // ---- helpers ---------------------------------------------------------------------------------------------

    private static Type FindInspectorType()
    {
        var type = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(SafeTypes)
            .FirstOrDefault(t => t.IsInterface && t.Name == InspectorName);
        Assert.True(type is not null, $"{InspectorName} nao existe (porta de T-135 ausente)");
        return type!;
    }

    private static IEnumerable<Type> SafeTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }

    private static void AssertNoConnectionValues(string message)
    {
        Assert.DoesNotContain(SyntheticRole, message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(SyntheticDatabase, message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(SyntheticHost, message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(SyntheticPassword, message, StringComparison.Ordinal);
        Assert.DoesNotContain("Password=", message, StringComparison.OrdinalIgnoreCase);
    }

    private static string FullMessage(Exception exception)
    {
        var parts = new List<string>();
        void Walk(Exception? e)
        {
            if (e is null)
            {
                return;
            }

            parts.Add(e.Message);
            if (e is AggregateException agg)
            {
                foreach (var inner in agg.InnerExceptions)
                {
                    Walk(inner);
                }
            }
            else
            {
                Walk(e.InnerException);
            }
        }

        Walk(exception);
        return string.Join(" | ", parts);
    }

    private static Dictionary<string, string?> ProductionLikeConfig() => new()
    {
        ["Auth:PublicOrigin"] = "https://deckloom.example.test",
        ["Auth:Google:ClientId"] = "test-client-id.apps.example.test",
        ["Auth:Google:ClientSecret"] = "test-google-client-secret-7c1d",
        ["Auth:RateLimitHmacKey"] = "test-hmac-key-0123456789abcdef0123456789abcdef",
        ["Legal:TermsVersion"] = "2026-10",
        ["Legal:TermsUrl"] = "https://deckloom.example.test/termos",
        ["Legal:PrivacyUrl"] = "https://deckloom.example.test/privacidade",
        ["Email:Smtp:Host"] = "smtp.example.test",
        ["Email:From"] = "no-reply@deckloom.example.test",
        ["ConnectionStrings:Deckloom"] =
            $"Host={SyntheticHost};Database=deckloom;Username=deckloom_app;Password={SyntheticPassword};SSL Mode=VerifyFull;Root Certificate=/certs/ca.pem",
    };

    private static void InvokeAddAccountsModule(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var method = typeof(Program).Assembly.GetTypes()
            .Where(t => t.Namespace == "Deckloom.Api.Composition" && t.IsAbstract && t.IsSealed)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .FirstOrDefault(m => m.Name == "AddAccountsModule");
        Assert.True(method is not null, "Deckloom.Api.Composition.*.AddAccountsModule nao existe");
        method!.Invoke(null, [services, configuration, environment]);
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "Deckloom.Api";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    /// <summary>Inspetor falso gerado por reflexao para a porta ainda inexistente.</summary>
    public class FakeInspector : DispatchProxy
    {
        private int _calls;

        public RoleSnapshot? Snapshot { get; set; }

        public Exception? Failure { get; set; }

        public int CallCount => Volatile.Read(ref _calls);

        public object Instance => this;

        public static FakeInspector Returning(RoleSnapshot snapshot) => Create(snapshot, null);

        public static FakeInspector Failing(Exception failure) => Create(null, failure);

        private static FakeInspector Create(RoleSnapshot? snapshot, Exception? failure)
        {
            var create = typeof(DispatchProxy).GetMethods()
                .Single(m => m.Name == nameof(DispatchProxy.Create) && m.GetGenericArguments().Length == 2)
                .MakeGenericMethod(FindInspectorType(), typeof(FakeInspector));
            var proxy = (FakeInspector)create.Invoke(null, null)!;
            proxy.Snapshot = snapshot;
            proxy.Failure = failure;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Interlocked.Increment(ref _calls);
            if (Failure is not null)
            {
                throw Failure;
            }

            var returnType = targetMethod!.ReturnType;
            if (returnType == typeof(RoleSnapshot))
            {
                return Snapshot;
            }

            if (returnType == typeof(Task<RoleSnapshot>))
            {
                return Task.FromResult(Snapshot!);
            }

            if (returnType == typeof(ValueTask<RoleSnapshot>))
            {
                return new ValueTask<RoleSnapshot>(Snapshot!);
            }

            throw new NotSupportedException($"retorno {returnType} nao suportado pelo falso");
        }
    }

    /// <summary>Host real com a composicao de producao, persistencia trocada por fakes e o inspetor controlado pelo teste.</summary>
    private sealed class RoleCheckFactory(FakeInspector? inspector) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSyntheticModuleSettings();
            builder.ConfigureServices(services =>
            {
                var uow = new InMemoryUnitOfWork();
                services.RemoveAll<IUnitOfWork>();
                services.RemoveAll<IPlayerRepository>();
                services.RemoveAll<ISessionRepository>();
                services.RemoveAll<IAccessAttemptStore>();
                services.RemoveAll<IPendingGoogleAuthStore>();
                services.RemoveAll<IGoogleIdentityVerifier>();
                services.RemoveAll<IPlayerDecksPurger>();
                services.RemoveAll<IAvatarStorage>();
                services.RemoveAll<IAvatarImageProcessor>();
                services.RemoveAll<IPasswordResetNotifier>();
                services.RemoveAll<IAvatarPresetCatalog>();
                services.RemoveAll<IFormatCatalog>();
                services.AddSingleton<IUnitOfWork>(uow);
                services.AddSingleton<IPlayerRepository>(new InMemoryPlayerRepository(uow));
                services.AddSingleton<ISessionRepository>(new InMemorySessionRepository(uow));
                services.AddSingleton<IAccessAttemptStore>(new InMemoryAccessAttemptStore(uow));
                services.AddSingleton<IPendingGoogleAuthStore>(new InMemoryPendingGoogleAuthStore(TimeProvider.System, uow));
                services.AddSingleton<IGoogleIdentityVerifier>(new FakeGoogleIdentityVerifier());
                services.AddSingleton<IPlayerDecksPurger>(new RecordingPlayerDecksPurger(uow));
                services.AddSingleton<IAvatarStorage>(new InMemoryAvatarStorage(uow));
                services.AddSingleton<IAvatarImageProcessor>(new FakeAvatarImageProcessor());
                services.AddSingleton<IPasswordResetNotifier>(new RecordingPasswordResetNotifier());
                services.AddSingleton<IAvatarPresetCatalog>(new InMemoryAvatarPresetCatalog([new AvatarPresetEntry("d", "d", "/d.png", 0, true, true)]));
                services.AddSingleton<IFormatCatalog>(new InMemoryFormatCatalog([]));

                var inspectorType = FindInspectorType();
                services.RemoveAll(inspectorType);
                if (inspector is not null)
                {
                    services.AddSingleton(inspectorType, inspector.Instance);
                }
            });
        }
    }
}
