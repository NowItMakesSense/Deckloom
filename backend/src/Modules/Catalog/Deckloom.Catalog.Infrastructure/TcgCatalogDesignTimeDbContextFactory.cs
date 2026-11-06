using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Deckloom.Catalog.Infrastructure;

// Usada apenas pelo "dotnet ef migrations add": nao abre conexao (o EF so monta o modelo) e a connection string e
// ficticia, sem credencial real. A aplicacao em runtime configura o contexto por injecao de dependencia.
internal sealed class TcgCatalogDesignTimeDbContextFactory : IDesignTimeDbContextFactory<TcgCatalogDbContext>
{
    private const string PlaceholderConnectionString = "Host=design-time.invalid;Database=design_time";

    public TcgCatalogDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TcgCatalogDbContext>()
            .UseNpgsql(
                PlaceholderConnectionString,
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", TcgCatalogDbContext.SchemaName))
            .Options;

        return new TcgCatalogDbContext(options);
    }
}
