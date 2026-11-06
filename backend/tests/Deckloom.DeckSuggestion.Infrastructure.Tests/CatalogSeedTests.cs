using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Deckloom.DeckSuggestion.Infrastructure.Tests;

// T-065 (REQ-037). Carga inicial de Formatos via HasData no modelo de DeckSuggestion (vira migracao em T-066).
// O SQLite em memoria aplica o modelo (EnsureCreated) com os dados semente; nenhum banco do sistema e tocado.
public sealed class CatalogSeedTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public CatalogSeedTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<FormatCatalogDbContext>().UseSqlite(_connection).Options;
        using var context = new FormatCatalogDbContext(options);
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private List<(string Id, string Name, bool IsActive)> Rows()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT id, name, is_active FROM format ORDER BY id";
        using var reader = command.ExecuteReader();
        var rows = new List<(string, string, bool)>();
        while (reader.Read())
        {
            rows.Add((reader.GetString(0), reader.GetString(1), reader.GetInt32(2) == 1));
        }

        return rows;
    }

    [Fact]
    public void REQ_037_seed_contains_exactly_the_five_initial_formats()
    {
        var names = Rows().Select(r => r.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Assert.Equal(["Commander", "Modern", "Pauper", "Pioneer", "Standard"], names);
    }

    [Fact]
    public void REQ_037_all_seeded_formats_are_active()
    {
        var rows = Rows();

        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.True(r.IsActive, $"formato {r.Id} deveria estar ativo"));
    }
}
