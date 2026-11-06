using System.Globalization;
using Deckloom.Accounts.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Deckloom.DeckSuggestion.Infrastructure.Tests;

// T-062 (REQ-037, 046). Tabela: deck_suggestion.format; ADR-0007 (dono: Sugestao de Deck) e ADR-0001.
// Contrato para o implementer (GREEN), namespace Deckloom.DeckSuggestion.Infrastructure:
//   sealed class FormatCatalogDbContext : DbContext
//     public const string SchemaName = "deck_suggestion"
//     ctor(DbContextOptions<FormatCatalogDbContext> options)
//     Somente leitura: sem propriedade publica DbSet<>; consultas sem rastreamento (NoTracking por padrao);
//       SaveChanges e SaveChangesAsync lancam InvalidOperationException (nunca gravam).
//     Mapeia so a tabela format (snake_case; schema deck_suggestion; o SQLite ignora o schema): id text PK pk_format,
//       name, is_active, sort_order (todas NOT NULL).
//   sealed class EfFormatCatalog : IFormatCatalog   // porta de Deckloom.Accounts.Application (ACL, ADR-0001/0007)
//     ctor(FormatCatalogDbContext context)
//     ListActiveAsync: so is_active = true, ordenado por sort_order e depois id (ordinal); Name vem de name.
//     ContainsAsync: true so para Formato ativo (inativo ou inexistente = false).
//     Sem cache neste RED (ver duvida aberta no relatorio).
public sealed class FormatCatalogTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<FormatCatalogDbContext> _options;

    public FormatCatalogTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<FormatCatalogDbContext>().UseSqlite(_connection).Options;
        using var context = new FormatCatalogDbContext(_options);
        context.Database.EnsureCreated();

        // T-065: o modelo ja traz a carga inicial (HasData); estes testes controlam o catalogo por completo.
        Execute("DELETE FROM format");
    }

    public void Dispose() => _connection.Dispose();

    private void Format(string id, string name, int order, bool isActive = true) =>
        Execute($"INSERT INTO format (id, name, is_active, sort_order) VALUES ('{id}', '{name}', {(isActive ? 1 : 0)}, {order})");

    private void SeedCatalog()
    {
        Format("modern", "Modern", 2);
        Format("standard", "Standard", 1);
        Format("commander", "Commander", 4);
        Format("pauper", "Pauper", 3);
        Format("legacy", "Legacy", 5, isActive: false);
    }

    private void Execute(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private T Scalar<T>(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        return (T)Convert.ChangeType(command.ExecuteScalar()!, typeof(T), CultureInfo.InvariantCulture);
    }

    private List<string> Column(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var values = new List<string>();
        while (reader.Read())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    [Fact]
    public async Task REQ_037_active_formats_are_listed_ordered_by_sort_order()
    {
        SeedCatalog();
        await using var context = new FormatCatalogDbContext(_options);

        var items = await new EfFormatCatalog(context).ListActiveAsync();

        Assert.Equal(["standard", "modern", "pauper", "commander"], items.Select(i => i.Id.Value));
        Assert.Equal("Standard", items[0].Name);
    }

    [Fact]
    public async Task REQ_037_formats_with_the_same_sort_order_are_ordered_by_id()
    {
        Format("b-format", "B", 1);
        Format("a-format", "A", 1);
        await using var context = new FormatCatalogDbContext(_options);

        var items = await new EfFormatCatalog(context).ListActiveAsync();

        Assert.Equal(["a-format", "b-format"], items.Select(i => i.Id.Value));
    }

    [Fact]
    public async Task REQ_046_deactivating_a_format_removes_it_from_the_listing_without_deleting_the_row()
    {
        SeedCatalog();
        await using var context = new FormatCatalogDbContext(_options);
        var catalog = new EfFormatCatalog(context);
        Assert.Contains(await catalog.ListActiveAsync(), i => i.Id == new FormatId("pauper"));

        Execute("UPDATE format SET is_active = 0 WHERE id = 'pauper'");

        Assert.DoesNotContain(await catalog.ListActiveAsync(), i => i.Id == new FormatId("pauper"));
        Assert.Equal(5, Scalar<int>("SELECT COUNT(*) FROM format"));
        Assert.Equal(0, Scalar<int>("SELECT is_active FROM format WHERE id = 'pauper'"));
    }

    [Fact]
    public async Task REQ_046_reactivating_a_format_brings_it_back_to_the_listing()
    {
        SeedCatalog();
        await using var context = new FormatCatalogDbContext(_options);
        var catalog = new EfFormatCatalog(context);

        Execute("UPDATE format SET is_active = 1 WHERE id = 'legacy'");

        var items = await catalog.ListActiveAsync();

        Assert.Equal("legacy", items[^1].Id.Value);
    }

    [Fact]
    public async Task REQ_046_contains_is_true_only_for_active_formats()
    {
        SeedCatalog();
        await using var context = new FormatCatalogDbContext(_options);
        var catalog = new EfFormatCatalog(context);

        Assert.True(await catalog.ContainsAsync(new FormatId("modern")));
        Assert.False(await catalog.ContainsAsync(new FormatId("legacy")));
        Assert.False(await catalog.ContainsAsync(new FormatId("nao-existe")));
    }

    [Fact]
    public async Task REQ_037_the_read_context_cannot_write_save_changes_throws_and_nothing_is_tracked()
    {
        SeedCatalog();
        await using var context = new FormatCatalogDbContext(_options);
        await new EfFormatCatalog(context).ListActiveAsync();

        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        Assert.Equal(5, Scalar<int>("SELECT COUNT(*) FROM format"));
    }

    [Fact]
    public void REQ_037_the_read_context_exposes_no_public_db_set()
    {
        var sets = typeof(FormatCatalogDbContext).GetProperties()
            .Where(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>));

        Assert.Empty(sets);
    }

    [Fact]
    public void REQ_037_the_context_maps_only_the_format_table_in_the_deck_suggestion_schema()
    {
        using var context = new FormatCatalogDbContext(_options);

        Assert.Equal("deck_suggestion", FormatCatalogDbContext.SchemaName);
        Assert.Equal(FormatCatalogDbContext.SchemaName, RelationalModelExtensions.GetDefaultSchema(context.Model));
        Assert.Single(context.Model.GetEntityTypes());
    }

    [Fact]
    public void REQ_037_the_schema_has_the_documented_columns_and_primary_key()
    {
        Assert.Equal(
            ["id", "is_active", "name", "sort_order"],
            Column("SELECT name FROM pragma_table_info('format') ORDER BY name"));
        Assert.Equal(
            ["id", "is_active", "name", "sort_order"],
            Column("SELECT name FROM pragma_table_info('format') WHERE \"notnull\" = 1 OR pk > 0 ORDER BY name"));
        Assert.Equal(["id"], Column("SELECT name FROM pragma_table_info('format') WHERE pk > 0"));
    }
}
