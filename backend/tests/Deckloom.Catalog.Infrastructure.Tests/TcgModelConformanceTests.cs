using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Deckloom.Catalog.Infrastructure.Tests;

// T-P04 (ADR-0015, ADR-0007). A tabela catalog.tcg pertence ao Catalogo: mapeamento qualificado por schema e tabela
// de historico de migracoes em "catalog". Sem conexao: o SQLite nunca abre.
public sealed class TcgModelConformanceTests
{
    private const string Schema = "catalog";
    private const string FactoryInterface = "Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory`1";

    [Fact]
    public void REQ_P02_model_has_an_explicit_catalog_default_schema_and_only_the_tcg_table()
    {
        var options = new DbContextOptionsBuilder<TcgCatalogDbContext>().UseSqlite("DataSource=:memory:").Options;
        using var context = new TcgCatalogDbContext(options);
        var model = context.GetService<IDesignTimeModel>().Model;

        Assert.Equal(Schema, TcgCatalogDbContext.SchemaName);
        Assert.Equal(Schema, model.GetDefaultSchema());
        var entity = Assert.Single(model.GetEntityTypes());
        Assert.Equal("tcg", entity.GetTableName());
        Assert.Equal(Schema, entity.GetSchema());
    }

    [Fact]
    public void REQ_P02_catalog_module_configures_the_history_table_in_its_own_schema()
    {
        var assembly = typeof(TcgCatalogDbContext).Assembly;
        var factory = assembly.GetTypes().FirstOrDefault(t => !t.IsAbstract && t.GetInterfaces().Any(i =>
            i.IsGenericType
            && i.GetGenericTypeDefinition().FullName == FactoryInterface
            && i.GetGenericArguments()[0] == typeof(TcgCatalogDbContext)));

        Assert.True(factory is not null, "Falta uma IDesignTimeDbContextFactory<TcgCatalogDbContext> no Catalogo.");

        var instance = Activator.CreateInstance(factory, nonPublic: true)!;
        var create = factory.GetMethod("CreateDbContext", BindingFlags.Public | BindingFlags.Instance, [typeof(string[])])!;
        using var context = (DbContext)create.Invoke(instance, [Array.Empty<string>()])!;

        var relational = context.GetService<IDbContextOptions>().Extensions
            .OfType<RelationalOptionsExtension>().SingleOrDefault();

        Assert.True(relational is not null, "Contexto sem provider relacional configurado pela fabrica.");
        Assert.Equal("__ef_migrations_history", relational.MigrationsHistoryTableName);
        Assert.Equal(Schema, relational.MigrationsHistoryTableSchema);
        Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
    }
}
