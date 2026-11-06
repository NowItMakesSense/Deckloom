using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Infrastructure;
using Deckloom.Accounts.Testing;
using Deckloom.DeckSuggestion.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Deckloom.Api.Tests;

// T-147 (retro item 3; docs/test-strategy.md > "Real composition"): a composicao de PRODUCAO (AddAccountsModule e ApiHost)
// com os adapters EF reais sobre SQLite em memoria, em http puro e ambiente Development, como o compose local.
// Diferente do host E2E: sem o atalho Request.Scheme = https e sem os fakes de persistencia. So o provedor dos DbContext
// (Npgsql -> SQLite) e as fronteiras externas (verificador Google) sao trocados; o DatabaseRoleStartupCheck nao e registrado.
// Nenhum banco real, .env ou credencial: as opcoes obrigatorias vem de SyntheticHostSettings (valores ficticios).
internal sealed class RealCompositionWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string PublicOrigin = "http://localhost:8081";
    public const string SchemeProbePath = "/__probe/scheme";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public RealCompositionWebApplicationFactory()
    {
        _connection.Open();
        CreateSchema();
    }

    public FakeGoogleIdentityVerifier Google { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSyntheticModuleSettings();
        builder.UseSetting("Auth:PublicOrigin", PublicOrigin);
        builder.ConfigureServices(services =>
        {
            services.RemoveDatabaseRoleCheck();
            SwapDbContextsToSqlite(services);
            services.RemoveAll<IGoogleIdentityVerifier>();
            services.AddSingleton<IGoogleIdentityVerifier>(Google);
            services.AddTransient<IStartupFilter, SchemeProbeStartupFilter>();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }

    private DbContextOptions<AccountsDbContext> AccountsOptions() =>
        new DbContextOptionsBuilder<AccountsDbContext>().UseSqlite(_connection).Options;

    // Troca so o provedor: o esquema vem do modelo (EnsureCreated) e a tabela format, que e de Sugestao de Deck e fica
    // fora das migrations de Accounts, e criada e semeada aqui.
    private void CreateSchema()
    {
        using (var context = new SqliteAccountsDbContext(AccountsOptions()))
        {
            context.Database.EnsureCreated();
        }

        using var command = _connection.CreateCommand();
        command.CommandText =
            "CREATE TABLE IF NOT EXISTS format (id text NOT NULL PRIMARY KEY, name text NOT NULL, " +
            "is_active integer NOT NULL, sort_order integer NOT NULL); " +
            "INSERT OR IGNORE INTO format (id, name, is_active, sort_order) VALUES " +
            "('standard', 'Standard', 1, 1), ('modern', 'Modern', 1, 2), ('commander', 'Commander', 1, 3)";
        command.ExecuteNonQuery();
    }

    private void SwapDbContextsToSqlite(IServiceCollection services)
    {
        var stale = services
            .Where(d => IsAbout(d.ServiceType, typeof(AccountsDbContext)) || IsAbout(d.ServiceType, typeof(FormatCatalogDbContext)))
            .ToList();
        foreach (var descriptor in stale)
        {
            services.Remove(descriptor);
        }

        var options = AccountsOptions();
        services.AddSingleton<IDbContextFactory<AccountsDbContext>>(new SqliteAccountsFactory(options));
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<AccountsDbContext>>().CreateDbContext());
        services.AddDbContext<FormatCatalogDbContext>(o => o.UseSqlite(_connection));
    }

    private static bool IsAbout(Type serviceType, Type context) =>
        serviceType == context || serviceType.GenericTypeArguments.Contains(context);

    private sealed class SqliteAccountsFactory(DbContextOptions<AccountsDbContext> options) : IDbContextFactory<AccountsDbContext>
    {
        public AccountsDbContext CreateDbContext() => new SqliteAccountsDbContext(options);
    }

    // O token de concorrencia de producao e a coluna xmin do PostgreSQL; no SQLite ele vira uma coluna comum, como
    // no TestAccountsDbContext dos testes de infraestrutura.
    private sealed class SqliteAccountsDbContext(DbContextOptions<AccountsDbContext> options) : AccountsDbContext(options)
    {
        private const string RowVersion = "RowVersion";

        protected override void ConfigureConcurrencyToken(EntityTypeBuilder<Player> player) =>
            player.Property<Guid>(RowVersion).IsConcurrencyToken();

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            ChangeTracker.DetectChanges();
            foreach (var entry in ChangeTracker.Entries<Player>()
                         .Where(e => e.State is EntityState.Added or EntityState.Modified))
            {
                entry.Property(RowVersion).CurrentValue = Guid.NewGuid();
            }

            return base.SaveChangesAsync(cancellationToken);
        }
    }

    // Sonda de teste: prova que a fabrica NAO trata a requisicao como https (Request.IsHttps == false).
    private sealed class SchemeProbeStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Map(SchemeProbePath, branch => branch.Run(context =>
                context.Response.WriteAsync(context.Request.IsHttps ? "https" : "http")));
            next(app);
        };
    }
}
