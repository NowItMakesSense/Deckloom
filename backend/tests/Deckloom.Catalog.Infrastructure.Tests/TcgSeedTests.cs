using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Deckloom.Catalog.Infrastructure.Tests;

// T-P04 (REQ-P02, AC-P04a). Carga dos 5 TCGs do MVP via HasData no modelo do Catalogo (vira migration).
// O SQLite em memoria aplica o modelo (EnsureCreated) com os dados semente; nenhum banco do sistema e tocado.
public sealed class TcgSeedTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public TcgSeedTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<TcgCatalogDbContext>().UseSqlite(_connection).Options;
        using var context = new TcgCatalogDbContext(options);
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private List<(string Id, string Name, bool IsActive, int SortOrder)> Rows()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT id, name, is_active, sort_order FROM tcg ORDER BY sort_order";
        using var reader = command.ExecuteReader();
        var rows = new List<(string, string, bool, int)>();
        while (reader.Read())
        {
            rows.Add((reader.GetString(0), reader.GetString(1), reader.GetInt32(2) == 1, reader.GetInt32(3)));
        }

        return rows;
    }

    [Fact]
    public void REQ_P02_seed_contains_exactly_the_five_mvp_tcgs_in_order()
    {
        Assert.Equal(
            ["Magic: The Gathering", "Pokemon TCG", "Yu-Gi-Oh!", "One Piece", "Lorcana"],
            Rows().Select(r => r.Name));
    }

    [Fact]
    public void REQ_P02_seed_uses_stable_lowercase_ids_and_mtg_for_magic()
    {
        var ids = Rows().Select(r => r.Id).ToList();

        Assert.Equal(["mtg", "pokemon", "yugioh", "onepiece", "lorcana"], ids);
        Assert.All(ids, id => Assert.Matches("^[a-z]+$", id));
    }

    [Fact]
    public void REQ_P02_all_seeded_tcgs_are_active_with_distinct_sort_order()
    {
        var rows = Rows();

        Assert.All(rows, r => Assert.True(r.IsActive, $"TCG {r.Id} deveria estar ativo"));
        Assert.Equal(rows.Count, rows.Select(r => r.SortOrder).Distinct().Count());
    }
}
