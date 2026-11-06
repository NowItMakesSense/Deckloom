using System.Text.RegularExpressions;

namespace Deckloom.Accounts.Infrastructure.Tests;

// T-065 (REQ-018, REQ-022; U11, ADR-0007). Carga inicial de avatar_preset via HasData no modelo de Accounts.
// Padrao de id: Database/tables/accounts.avatar_preset.md -> ^[a-z0-9-]{1,40}$. A correspondencia com os SVG e T-129.
public sealed partial class CatalogSeedTests : IDisposable
{
    private readonly SqliteInMemoryFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [GeneratedRegex("^[a-z0-9-]{1,40}$")]
    private static partial Regex PresetIdPattern();

    private List<(string Id, bool IsDefault)> ActivePresets()
    {
        using var command = _fixture.Connection.CreateCommand();
        command.CommandText = "SELECT id, is_default FROM avatar_preset WHERE is_active = 1 ORDER BY id";
        using var reader = command.ExecuteReader();
        var rows = new List<(string, bool)>();
        while (reader.Read())
        {
            rows.Add((reader.GetString(0), reader.GetInt32(1) == 1));
        }

        return rows;
    }

    [Fact]
    public void REQ_018_seed_has_between_8_and_12_active_presets()
    {
        var count = ActivePresets().Count;

        Assert.InRange(count, 8, 12);
    }

    [Fact]
    public void REQ_018_seeded_preset_ids_follow_the_documented_pattern_and_are_unique()
    {
        var ids = ActivePresets().Select(p => p.Id).ToList();

        Assert.NotEmpty(ids);
        Assert.All(ids, id => Assert.Matches(PresetIdPattern(), id));
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void REQ_022_seed_has_exactly_one_active_default_preset()
    {
        var defaults = ActivePresets().Count(p => p.IsDefault);

        Assert.Equal(1, defaults);
    }
}
