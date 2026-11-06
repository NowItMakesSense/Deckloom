using System.Reflection;
using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Infrastructure;
using Deckloom.Accounts.Testing;
using Deckloom.DeckSuggestion.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Deckloom.Api.Tests;

// T-094 (RED): raiz de composicao (Deckloom.Api.Composition) e opcoes tipadas validadas no start.
// Todos os segredos abaixo sao strings FICTICIAS do proprio teste (nunca lidas de .env). Nenhuma conexao de banco
// e aberta: a composicao e validada por provedor de servicos / IStartupValidator, sem executar IHostedService.
//
// Suposicoes de nome (ver perguntas ao orquestrador), localizadas por reflexao para que a falha seja de
// comportamento ausente e nao de compilacao:
//  - extensao estatica `AddAccountsModule(IServiceCollection, IConfiguration, IHostEnvironment)` em Deckloom.Api.Composition;
//  - opcoes tipadas Deckloom.Api.Composition.{AuthOptions, LegalOptions, EmailOptions, AvatarOptions}.
public sealed class CompositionTests
{
    private const string CompositionNamespace = "Deckloom.Api.Composition";
    private const string Host = "db-host-synthetic.internal";
    private const string DbPassword = "test-db-password-9f3a";
    private const string HmacKey = "test-hmac-key-0123456789abcdef0123456789abcdef";
    private const string GoogleSecret = "test-google-client-secret-7c1d";
    private const string SmtpPassword = "test-smtp-password-5b2e";

    private static readonly string ValidConnection =
        $"Host={Host};Database=deckloom;Username=deckloom_app;Password={DbPassword};SSL Mode=VerifyFull;Root Certificate=/certs/ca.pem";

    private static Dictionary<string, string?> ValidConfig(string? connection = null) => new()
    {
        ["Auth:PublicOrigin"] = "https://deckloom.example.test",
        ["Auth:Google:ClientId"] = "test-client-id.apps.example.test",
        ["Auth:Google:ClientSecret"] = GoogleSecret,
        ["Auth:RateLimitHmacKey"] = HmacKey,
        ["Legal:TermsVersion"] = "2026-10",
        ["Legal:TermsUrl"] = "https://deckloom.example.test/termos",
        ["Legal:PrivacyUrl"] = "https://deckloom.example.test/privacidade",
        ["Email:Smtp:Host"] = "smtp.example.test",
        ["Email:Smtp:Port"] = "587",
        ["Email:Smtp:UseTls"] = "true",
        ["Email:Smtp:User"] = "mailer",
        ["Email:Smtp:Password"] = SmtpPassword,
        ["Email:From"] = "no-reply@deckloom.example.test",
        ["Avatar:MaxBytes"] = "123456",
        ["ConnectionStrings:Deckloom"] = connection ?? ValidConnection,
    };

    // ---- 1. Portas -> adapters de producao -------------------------------------------------------------------

    public static TheoryData<Type, Type> ProductionAdapters => new()
    {
        { typeof(IPlayerRepository), typeof(EfPlayerRepository) },
        { typeof(ISessionRepository), typeof(EfSessionRepository) },
        { typeof(IAccessAttemptStore), typeof(EfAccessAttemptStore) },
        { typeof(IPendingGoogleAuthStore), typeof(EfPendingGoogleAuthStore) },
        { typeof(IUnitOfWork), typeof(EfUnitOfWork) },
        { typeof(IAvatarStorage), typeof(PostgresAvatarStorage) },
        { typeof(IAvatarImageProcessor), typeof(SkiaAvatarImageProcessor) },
        { typeof(IAvatarPresetCatalog), typeof(EfAvatarPresetCatalog) },
        { typeof(IFormatCatalog), typeof(EfFormatCatalog) },
        { typeof(IPasswordHasher), typeof(Pbkdf2PasswordHasher) },
        { typeof(IKeyedHasher), typeof(HmacKeyedHasher) },
        { typeof(ITokenHasher), typeof(Deckloom.Accounts.Infrastructure.Sha256TokenHasher) },
        { typeof(ISecureTokenGenerator), typeof(SecureTokenGenerator) },
        { typeof(IPasswordResetNotifier), typeof(ChannelPasswordResetNotifier) },
        { typeof(IPlayerDecksPurger), typeof(NoDecksYetPurger) },
    };

    [Theory]
    [MemberData(nameof(ProductionAdapters))]
    public void T094_composition_registers_port_with_its_production_adapter(Type port, Type adapter)
    {
        using var provider = BuildProvider(ValidConfig(), "Production");
        using var scope = provider.CreateScope();

        var resolved = scope.ServiceProvider.GetRequiredService(port);

        Assert.IsType(adapter, resolved);
    }

    [Theory]
    [InlineData(typeof(IGoogleIdentityVerifier))]
    [InlineData(typeof(IDelayer))]
    [InlineData(typeof(ITermsVersionProvider))]
    public void T094_composition_resolves_remaining_ports_without_test_fakes(Type port)
    {
        using var provider = BuildProvider(ValidConfig(), "Production");
        using var scope = provider.CreateScope();

        var resolved = scope.ServiceProvider.GetRequiredService(port);

        Assert.NotEqual(typeof(InMemoryUnitOfWork).Assembly, resolved.GetType().Assembly);
    }

    [Fact]
    public void T094_composition_uses_the_system_clock()
    {
        using var provider = BuildProvider(ValidConfig(), "Production");

        Assert.Same(TimeProvider.System, provider.GetRequiredService<TimeProvider>());
    }

    // ---- 2. Opcoes obrigatorias ausentes --------------------------------------------------------------------

    [Theory]
    [InlineData("Auth:RateLimitHmacKey")]
    [InlineData("Auth:Google:ClientSecret")]
    [InlineData("ConnectionStrings:Deckloom")]
    public void T094_missing_required_option_fails_validation_without_leaking_any_configured_value(string missingKey)
    {
        var config = ValidConfig();
        config.Remove(missingKey);
        using var provider = BuildProvider(config, "Production");

        var failure = Assert.ThrowsAny<Exception>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        var message = FullMessage(failure);
        Assert.Contains(missingKey, message, StringComparison.Ordinal);
        AssertNoConfiguredValue(message, config);
    }

    [Theory]
    [InlineData("Auth:RateLimitHmacKey")]
    [InlineData("Auth:Google:ClientSecret")]
    [InlineData("ConnectionStrings:Deckloom")]
    public void T094_host_startup_fails_when_required_option_is_missing_and_message_has_no_values(string missingKey)
    {
        var config = ValidConfig();
        config.Remove(missingKey);

        var failure = StartAndCaptureFailure(config);

        var message = FullMessage(failure);
        Assert.Contains(missingKey, message, StringComparison.Ordinal);
        AssertNoConfiguredValue(message, config);
    }

    // QA-17: sob CPU saturada a falha de start do WebApplicationFactory as vezes chega como ObjectDisposedException do
    // 'IServiceProvider' (corrida do DeferredHostBuilder: o host da aplicacao e descartado antes de StartAsync pedir o
    // IHostLifetime) em vez da OptionsValidationException. A corrida e do framework de teste; repete o start ate a falha
    // real aparecer (no maximo 5 tentativas), sem relaxar a assercao da mensagem.
    private static Exception StartAndCaptureFailure(Dictionary<string, string?> config)
    {
        Exception? failure = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var factory = new ComposedFactory(config, "Production", swapAdaptersForFakes: false);
            failure = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
            if (failure is not ObjectDisposedException { ObjectName: "IServiceProvider" })
            {
                return failure;
            }
        }

        return failure!;
    }

    [Fact]
    public void T094_valid_configuration_passes_startup_validation()
    {
        using var provider = BuildProvider(ValidConfig(), "Production");

        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    // ---- 2b. Orcamento por conta (SEC-01) -------------------------------------------------------------------

    [Fact]
    public void SEC01_account_hourly_budget_defaults_to_thirty()
    {
        using var provider = BuildProvider(ValidConfig(), "Production");

        Assert.Equal(30, provider.GetRequiredService<ProgressiveDelayPolicy>().AccountHourlyBudget);
    }

    [Fact]
    public void SEC01_account_hourly_budget_is_read_from_auth_progressive_delay()
    {
        var config = ValidConfig();
        config["Auth:ProgressiveDelay:AccountHourlyBudget"] = "5";
        using var provider = BuildProvider(config, "Production");

        provider.GetRequiredService<IStartupValidator>().Validate();

        Assert.Equal(5, provider.GetRequiredService<ProgressiveDelayPolicy>().AccountHourlyBudget);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("101")]
    public void SEC01_account_hourly_budget_outside_one_to_one_hundred_fails_startup_validation(string budget)
    {
        var config = ValidConfig();
        config["Auth:ProgressiveDelay:AccountHourlyBudget"] = budget;
        using var provider = BuildProvider(config, "Production");

        var failure = Assert.ThrowsAny<Exception>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains("Auth:ProgressiveDelay:AccountHourlyBudget", FullMessage(failure), StringComparison.Ordinal);
    }

    // ---- 2c. Transporte de saida falha fechado fora de Development (SEC-06) ---------------------------------

    [Theory]
    [InlineData("Email:Smtp:UseTls", "false")]
    [InlineData("Auth:Google:Authority", "http://accounts.example.test")]
    [InlineData("Auth:PublicOrigin", "http://deckloom.example.test")]
    public void SEC06_outside_development_an_insecure_transport_setting_refuses_startup_naming_only_the_key(
        string key, string insecureValue)
    {
        var config = ValidConfig();
        config[key] = insecureValue;
        using var provider = BuildProvider(config, "Production");

        var failure = Assert.ThrowsAny<Exception>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        var message = FullMessage(failure);
        Assert.Contains(key, message, StringComparison.Ordinal);
        Assert.DoesNotContain(insecureValue, message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("Production")]
    public void SEC06_every_environment_other_than_development_refuses_the_three_insecure_settings_together(string environment)
    {
        var config = ValidConfig();
        config["Email:Smtp:UseTls"] = "false";
        config["Auth:Google:Authority"] = "http://accounts.example.test";
        config["Auth:PublicOrigin"] = "http://deckloom.example.test";
        using var provider = BuildProvider(config, environment);

        var message = FullMessage(
            Assert.ThrowsAny<Exception>(() => provider.GetRequiredService<IStartupValidator>().Validate()));

        Assert.Contains("Email:Smtp:UseTls", message, StringComparison.Ordinal);
        Assert.Contains("Auth:Google:Authority", message, StringComparison.Ordinal);
        Assert.Contains("Auth:PublicOrigin", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Auth:Google:Authority", "ftp://accounts.example.test")]
    [InlineData("Auth:PublicOrigin", "ftp://deckloom.example.test")]
    public void SEC06_outside_development_a_non_https_scheme_is_refused_naming_only_the_key(string key, string value)
    {
        var config = ValidConfig();
        config[key] = value;
        using var provider = BuildProvider(config, "Staging");

        var message = FullMessage(
            Assert.ThrowsAny<Exception>(() => provider.GetRequiredService<IStartupValidator>().Validate()));

        Assert.Contains(key, message, StringComparison.Ordinal);
        Assert.DoesNotContain(value, message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("Production")]
    public void SEC06_outside_development_https_authority_and_public_origin_pass(string environment)
    {
        var config = ValidConfig();
        config["Auth:Google:Authority"] = "https://accounts.example.test";
        config["Auth:PublicOrigin"] = "https://deckloom.example.test";
        using var provider = BuildProvider(config, environment);

        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    [Fact]
    public void SEC06_development_allows_plain_smtp_and_http_authority_and_public_origin()
    {
        var config = ValidConfig();
        config["Email:Smtp:UseTls"] = "false";
        config["Email:Smtp:User"] = "";
        config["Email:Smtp:Password"] = "";
        config["Auth:Google:Authority"] = "http://localhost:8081";
        config["Auth:PublicOrigin"] = "http://localhost:5173";
        using var provider = BuildProvider(config, "Development");

        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    // ---- 2d. Credencial SMTP nunca em conexao sem TLS (SEC-22, T-179) ---------------------------------------

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    [InlineData("Production")]
    public void SEC22_smtp_user_with_plain_connection_refuses_startup_in_any_environment_naming_only_the_keys(string environment)
    {
        var config = ValidConfig();
        config["Email:Smtp:UseTls"] = "false";
        using var provider = BuildProvider(config, environment);

        var message = FullMessage(
            Assert.ThrowsAny<Exception>(() => provider.GetRequiredService<IStartupValidator>().Validate()));

        Assert.Contains("Email:Smtp:User", message, StringComparison.Ordinal);
        Assert.Contains("Email:Smtp:UseTls", message, StringComparison.Ordinal);
        Assert.DoesNotContain("mailer", message, StringComparison.Ordinal);
        Assert.DoesNotContain(SmtpPassword, message, StringComparison.Ordinal);
    }

    [Fact]
    public void SEC22_development_without_smtp_user_and_plain_connection_still_starts()
    {
        var config = ValidConfig();
        config["Email:Smtp:UseTls"] = "false";
        config["Email:Smtp:User"] = "";
        config["Email:Smtp:Password"] = "";
        using var provider = BuildProvider(config, "Development");

        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    [Fact]
    public void SEC22_whitespace_only_smtp_user_counts_as_empty_like_the_transport_does()
    {
        var config = ValidConfig();
        config["Email:Smtp:UseTls"] = "false";
        config["Email:Smtp:User"] = "   ";
        using var provider = BuildProvider(config, "Development");

        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void SEC22_smtp_user_with_tls_starts(string environment)
    {
        using var provider = BuildProvider(ValidConfig(), environment);

        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    // ---- 3. Opcoes tipadas ----------------------------------------------------------------------------------

    [Theory]
    [InlineData("AuthOptions", "PublicOrigin", "https://deckloom.example.test")]
    [InlineData("LegalOptions", "TermsVersion", "2026-10")]
    [InlineData("EmailOptions", "From", "no-reply@deckloom.example.test")]
    [InlineData("AvatarOptions", "MaxBytes", 123456)]
    public void T094_options_bind_as_typed_options_from_their_sections(string optionsType, string property, object expected)
    {
        using var provider = BuildProvider(ValidConfig(), "Production");
        var type = FindCompositionType(optionsType);

        var value = ResolveOptionsValue(provider, type);

        var actual = type.GetProperty(property)?.GetValue(value);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void T094_secret_options_do_not_print_their_values_in_to_string()
    {
        using var provider = BuildProvider(ValidConfig(), "Production");
        var auth = ResolveOptionsValue(provider, FindCompositionType("AuthOptions"));

        var text = auth.ToString() ?? "";

        Assert.DoesNotContain(HmacKey, text, StringComparison.Ordinal);
        Assert.DoesNotContain(GoogleSecret, text, StringComparison.Ordinal);
    }

    // ---- 4. Controle de acesso ao banco (design: DB1, DB8) --------------------------------------------------

    [Theory]
    [InlineData("deckloom_migrator")]
    [InlineData("deckloom_owner")]
    [InlineData("postgres")]
    public void T094_connection_string_with_a_role_other_than_deckloom_app_fails_validation(string role)
    {
        var connection = ValidConnection.Replace("Username=deckloom_app", $"Username={role}", StringComparison.Ordinal);
        var config = ValidConfig(connection);
        using var provider = BuildProvider(config, "Production");

        var failure = Assert.ThrowsAny<Exception>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        var message = FullMessage(failure);
        Assert.Contains("ConnectionStrings:Deckloom", message, StringComparison.Ordinal);
        AssertNoConfiguredValue(message, config);
    }

    [Theory]
    [InlineData("Require")]
    [InlineData("VerifyCA")]
    [InlineData("Prefer")]
    [InlineData("Disable")]
    public void T094_outside_development_ssl_mode_other_than_verify_full_fails_validation(string sslMode)
    {
        var connection = ValidConnection.Replace("SSL Mode=VerifyFull", $"SSL Mode={sslMode}", StringComparison.Ordinal);
        var config = ValidConfig(connection);
        using var provider = BuildProvider(config, "Production");

        var failure = Assert.ThrowsAny<Exception>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        var message = FullMessage(failure);
        Assert.Contains("ConnectionStrings:Deckloom", message, StringComparison.Ordinal);
        AssertNoConfiguredValue(message, config);
    }

    [Fact]
    public void T094_outside_development_verify_full_without_root_certificate_fails_validation()
    {
        var connection = ValidConnection.Replace(";Root Certificate=/certs/ca.pem", "", StringComparison.Ordinal);
        using var provider = BuildProvider(ValidConfig(connection), "Production");

        Assert.ThrowsAny<Exception>(() => provider.GetRequiredService<IStartupValidator>().Validate());
    }

    [Fact]
    public void T094_development_accepts_ssl_disable_for_the_compose_internal_network()
    {
        var connection = $"Host={Host};Database=deckloom;Username=deckloom_app;Password={DbPassword};SSL Mode=Disable";
        using var provider = BuildProvider(ValidConfig(connection), "Development");

        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    [Fact]
    public void T094_development_still_rejects_a_role_other_than_deckloom_app()
    {
        var connection = $"Host={Host};Database=deckloom;Username=deckloom_migrator;Password={DbPassword};SSL Mode=Disable";
        using var provider = BuildProvider(ValidConfig(connection), "Development");

        Assert.ThrowsAny<Exception>(() => provider.GetRequiredService<IStartupValidator>().Validate());
    }

    [Theory]
    [InlineData("ConnectionStrings:Migrator")]
    [InlineData("ConnectionStrings:Owner")]
    [InlineData("ConnectionStrings:Postgres")]
    [InlineData("ConnectionStrings:Admin")]
    public void T094_any_extra_database_credential_key_makes_startup_validation_fail(string extraKey)
    {
        var config = ValidConfig();
        config[extraKey] = "Host=other-synthetic.internal;Username=deckloom_migrator;Password=test-other-pw-1";
        using var provider = BuildProvider(config, "Production");

        var failure = Assert.ThrowsAny<Exception>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        AssertNoConfiguredValue(FullMessage(failure), config);
    }

    // ---- 5. Fabrica de teste troca so persistencia/externos --------------------------------------------------

    [Fact]
    public void T094_test_factory_swaps_only_persistence_and_external_adapters_for_fakes()
    {
        using var factory = new ComposedFactory(ValidConfig(), "Development", swapAdaptersForFakes: true);
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;

        Assert.IsType<InMemoryPlayerRepository>(sp.GetRequiredService<IPlayerRepository>());
        Assert.IsType<InMemoryUnitOfWork>(sp.GetRequiredService<IUnitOfWork>());
        Assert.IsType<FakeGoogleIdentityVerifier>(sp.GetRequiredService<IGoogleIdentityVerifier>());
        Assert.IsType<RecordingPasswordResetNotifier>(sp.GetRequiredService<IPasswordResetNotifier>());

        // Casos de uso e opcoes continuam vindos da composicao real.
        Assert.NotNull(sp.GetRequiredService<LoginWithPassword>());
        Assert.NotNull(ResolveOptionsValue(factory.Services, FindCompositionType("AuthOptions")));
    }

    [Fact]
    public void T094_test_secrets_are_literals_in_test_code_and_not_read_from_dotenv_or_environment()
    {
        var config = ValidConfig();

        Assert.Equal(HmacKey, config["Auth:RateLimitHmacKey"]);
        Assert.Equal(GoogleSecret, config["Auth:Google:ClientSecret"]);
        Assert.False(File.Exists(Path.Combine(AppContext.BaseDirectory, ".env")));
    }

    // ---- helpers -------------------------------------------------------------------------------------------

    private static ServiceProvider BuildProvider(Dictionary<string, string?> values, string environment)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        InvokeAddAccountsModule(services, configuration, new TestEnvironment(environment));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static void InvokeAddAccountsModule(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var method = typeof(Program).Assembly.GetTypes()
            .Where(t => t.Namespace == CompositionNamespace && t.IsAbstract && t.IsSealed)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .FirstOrDefault(m => m.Name == "AddAccountsModule");
        Assert.True(method is not null, $"{CompositionNamespace}.*.AddAccountsModule(IServiceCollection, IConfiguration, IHostEnvironment) nao existe");

        var args = method!.GetParameters()
            .Select(p => p.ParameterType.IsAssignableFrom(typeof(IServiceCollection)) ? (object)services
                : p.ParameterType.IsAssignableFrom(typeof(IConfigurationRoot)) ? configuration
                : p.ParameterType.IsAssignableFrom(typeof(TestEnvironment)) ? environment
                : throw new InvalidOperationException($"parametro inesperado {p.ParameterType}"))
            .ToArray();
        method.Invoke(null, args);
    }

    private static Type FindCompositionType(string name)
    {
        var type = typeof(Program).Assembly.GetType($"{CompositionNamespace}.{name}");
        Assert.True(type is not null, $"{CompositionNamespace}.{name} nao existe");
        return type!;
    }

    private static object ResolveOptionsValue(IServiceProvider provider, Type optionsType)
    {
        var monitor = provider.GetRequiredService(typeof(IOptions<>).MakeGenericType(optionsType));
        return monitor.GetType().GetProperty("Value")!.GetValue(monitor)!;
    }

    private static string FullMessage(Exception exception)
    {
        var parts = new List<string>();
        for (var e = exception; e is not null; e = e.InnerException!)
        {
            parts.Add(e.Message);
            if (e is OptionsValidationException ove)
            {
                parts.AddRange(ove.Failures);
            }
            if (e.InnerException is null)
            {
                break;
            }
        }
        return string.Join(" | ", parts);
    }

    private static void AssertNoConfiguredValue(string message, Dictionary<string, string?> config)
    {
        foreach (var value in config.Values.Where(v => !string.IsNullOrEmpty(v) && v.Length >= 6))
        {
            Assert.DoesNotContain(value!, message, StringComparison.OrdinalIgnoreCase);
        }
        Assert.DoesNotContain(DbPassword, message, StringComparison.Ordinal);
        Assert.DoesNotContain(Host, message, StringComparison.Ordinal);
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "Deckloom.Api";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class ComposedFactory(Dictionary<string, string?> config, string environment, bool swapAdaptersForFakes)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            foreach (var (key, value) in config)
            {
                builder.UseSetting(key, value);
            }

            if (!swapAdaptersForFakes)
            {
                return;
            }

            builder.ConfigureServices(services =>
            {
                var uow = new InMemoryUnitOfWork();
                services.RemoveAll<IUnitOfWork>();
                services.RemoveDatabaseRoleCheck();
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
            });
        }
    }
}
