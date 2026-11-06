using System.Globalization;
using Deckloom.Catalog.Application;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Deckloom.Catalog.Infrastructure.Tests;

// T-P04 (REQ-P02). Porta de leitura ITcgCatalog contra EfTcgCatalog (SQLite em memoria, sem banco real; ADR-0012).
public sealed class EfTcgCatalogTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<TcgCatalogDbContext> _options;

    public EfTcgCatalogTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<TcgCatalogDbContext>().UseSqlite(_connection).Options;
        using var context = new TcgCatalogDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

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

    private void Tcg(string id, string name, int order, bool isActive = true) =>
        Execute($"INSERT INTO tcg (id, name, is_active, sort_order) VALUES ('{id}', '{name}', {(isActive ? 1 : 0)}, {order})");

    private EfTcgCatalog Catalog() => new(new TcgCatalogDbContext(_options));

    // Consome o adapter somente pela porta, como o consumidor futuro fara.
#pragma warning disable CA1859 // O teste prova o contrato da porta: o parametro deve ser a interface
    private static Task<IReadOnlyList<TcgItem>> ListViaPort(ITcgCatalog port) => port.ListActiveAsync();
#pragma warning restore CA1859

    [Fact]
    public async Task REQ_P02_the_port_lists_the_five_mvp_tcgs_from_the_seed()
    {
        var items = await ListViaPort(Catalog());

        Assert.Equal(["mtg", "pokemon", "yugioh", "onepiece", "lorcana"], items.Select(i => i.Id));
        Assert.Equal(
            ["Magic: The Gathering", "Pokemon TCG", "Yu-Gi-Oh!", "One Piece", "Lorcana"],
            items.Select(i => i.Name));
    }

    [Fact]
    public async Task REQ_P02_inactive_tcgs_are_not_listed_but_the_row_is_kept()
    {
        Execute("UPDATE tcg SET is_active = 0 WHERE id = 'yugioh'");

        var items = await Catalog().ListActiveAsync();

        Assert.Equal(["mtg", "pokemon", "onepiece", "lorcana"], items.Select(i => i.Id));
        Assert.Equal(5, Scalar<int>("SELECT COUNT(*) FROM tcg"));
    }

    [Fact]
    public async Task REQ_P02_tcgs_with_the_same_sort_order_are_ordered_by_id()
    {
        Execute("DELETE FROM tcg");
        Tcg("b-tcg", "B", 1);
        Tcg("a-tcg", "A", 1);

        var items = await Catalog().ListActiveAsync();

        Assert.Equal(["a-tcg", "b-tcg"], items.Select(i => i.Id));
    }

    [Fact]
    public async Task REQ_P02_contains_is_true_only_for_active_tcgs()
    {
        Execute("UPDATE tcg SET is_active = 0 WHERE id = 'lorcana'");
        var catalog = Catalog();

        Assert.True(await catalog.ContainsAsync("mtg"));
        Assert.False(await catalog.ContainsAsync("lorcana"));
        Assert.False(await catalog.ContainsAsync("nao-existe"));
    }

    [Fact]
    public async Task REQ_P02_the_read_context_cannot_write_and_tracks_nothing()
    {
        await using var context = new TcgCatalogDbContext(_options);
        await new EfTcgCatalog(context).ListActiveAsync();

        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        Assert.Equal(5, Scalar<int>("SELECT COUNT(*) FROM tcg"));
    }

    [Fact]
    public void REQ_P02_the_read_context_exposes_no_public_db_set()
    {
        var sets = typeof(TcgCatalogDbContext).GetProperties()
            .Where(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>));

        Assert.Empty(sets);
    }

    [Fact]
    public void REQ_P02_the_table_has_the_documented_columns_and_primary_key()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT name FROM pragma_table_info('tcg') WHERE \"notnull\" = 1 OR pk > 0 ORDER BY name";
        using var reader = command.ExecuteReader();
        var columns = new List<string>();
        while (reader.Read())
        {
            columns.Add(reader.GetString(0));
        }

        Assert.Equal(["id", "is_active", "name", "sort_order"], columns);
        Assert.Equal("id", Scalar<string>("SELECT name FROM pragma_table_info('tcg') WHERE pk > 0"));
    }
}
