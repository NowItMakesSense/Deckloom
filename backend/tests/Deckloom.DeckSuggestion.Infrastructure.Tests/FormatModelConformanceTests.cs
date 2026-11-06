using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Deckloom.DeckSuggestion.Infrastructure.Tests;

// T-066 (RED, ADR-0015, ADR-0007). A tabela deck_suggestion.format pertence a Sugestao de Deck. Metadados do modelo
// contra Database/tables/deck_suggestion.format.md e Database/primary-keys/pk_format.md; mapeamento qualificado por
// schema e tabela de historico de migracoes em "deck_suggestion". Sem conexao: o contexto usa SQLite que nunca abre.
public sealed class FormatModelConformanceTests
{
    private const string Schema = "deck_suggestion";
    private const string FactoryInterface = "Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory`1";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Raiz do repositorio (.git) nao encontrada.");
    }

    private static IModel BuildModel()
    {
        var options = new DbContextOptionsBuilder<FormatCatalogDbContext>().UseSqlite("DataSource=:memory:").Options;
        using var context = new FormatCatalogDbContext(options);
        return context.GetService<IDesignTimeModel>().Model;
    }

    private static List<string> DocumentedColumns()
    {
        var path = Path.Combine(RepoRoot(), "Database", "tables", "deck_suggestion.format.md");
        Assert.True(File.Exists(path), "Database/tables/deck_suggestion.format.md ausente.");
        return File.ReadAllLines(path)
            .Select(l => Regex.Match(l, @"^\| ([a-z_]+) \|"))
            .Where(m => m.Success && m.Groups[1].Value != "Column")
            .Select(m => m.Groups[1].Value)
            .ToList();
    }

    [Fact]
    public void T066_model_maps_format_in_the_deck_suggestion_schema_with_pk_format_and_documented_columns()
    {
        var entity = Assert.Single(BuildModel().GetEntityTypes());
        var store = StoreObjectIdentifier.Table("format", Schema);

        Assert.Equal("format", entity.GetTableName());
        Assert.Equal(Schema, entity.GetSchema());
        Assert.Equal("pk_format", entity.FindPrimaryKey()!.GetName());
        Assert.Equal(["id"], entity.FindPrimaryKey()!.Properties.Select(p => p.GetColumnName(store)).ToList());

        var actual = entity.GetProperties().Select(p => p.GetColumnName(store)!).Order().ToList();
        Assert.Equal(DocumentedColumns().Order().ToList(), actual);
    }

    [Fact]
    public void T066_model_has_an_explicit_deck_suggestion_default_schema_and_no_unqualified_entity()
    {
        var model = BuildModel();

        Assert.Equal(Schema, model.GetDefaultSchema());
        var offenders = model.GetEntityTypes().Where(e => e.GetSchema() != Schema).Select(e => e.DisplayName()).ToList();
        Assert.True(offenders.Count == 0, $"Entidades fora de {Schema}: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void T066_deck_suggestion_module_configures_the_history_table_in_its_own_schema()
    {
        var assembly = typeof(FormatCatalogDbContext).Assembly;
        var factory = assembly.GetTypes().FirstOrDefault(t => !t.IsAbstract && t.GetInterfaces().Any(i =>
            i.IsGenericType
            && i.GetGenericTypeDefinition().FullName == FactoryInterface
            && i.GetGenericArguments()[0] == typeof(FormatCatalogDbContext)));

        Assert.True(
            factory is not null,
            "Fabrica de tempo de design ausente: falta uma IDesignTimeDbContextFactory<FormatCatalogDbContext> em "
            + "Deckloom.DeckSuggestion.Infrastructure (T-066 GREEN).");

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
