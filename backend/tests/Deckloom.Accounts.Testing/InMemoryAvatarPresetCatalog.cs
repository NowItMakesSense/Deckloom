using Deckloom.Accounts.Application;

namespace Deckloom.Accounts.Testing;

public sealed record AvatarPresetEntry(
    string Id, string Label, string ImagePath, int SortOrder, bool IsDefault, bool IsActive);

public sealed class InMemoryAvatarPresetCatalog : IAvatarPresetCatalog
{
    private readonly Dictionary<string, AvatarPresetEntry> _entries;

    public InMemoryAvatarPresetCatalog(IEnumerable<AvatarPresetEntry> entries)
    {
        _entries = entries.ToDictionary(e => e.Id, StringComparer.Ordinal);
        var defaults = _entries.Values.Where(e => e.IsDefault).ToList();
        if (defaults.Count != 1 || !defaults[0].IsActive)
        {
            throw new ArgumentException("O catalogo exige exatamente um preset padrao, e ele deve estar ativo.", nameof(entries));
        }
    }

    public void Activate(string id) => _entries[id] = _entries[id] with { IsActive = true };

    public void Deactivate(string id)
    {
        var entry = _entries[id];
        if (entry.IsDefault)
        {
            throw new InvalidOperationException("O preset padrao nao pode ser desativado.");
        }

        _entries[id] = entry with { IsActive = false };
    }

    public Task<IReadOnlyList<AvatarPresetItem>> ListActiveAsync(CancellationToken ct = default)
    {
        IReadOnlyList<AvatarPresetItem> items = _entries.Values
            .Where(e => e.IsActive)
            .OrderBy(e => e.SortOrder)
            .ThenBy(e => e.Id, StringComparer.Ordinal)
            .Select(ToItem)
            .ToList();
        return Task.FromResult(items);
    }

    public Task<AvatarPresetItem> GetDefaultAsync(CancellationToken ct = default) =>
        Task.FromResult(ToItem(_entries.Values.Single(e => e.IsDefault && e.IsActive)));

    public Task<bool> ContainsActiveAsync(string presetId, CancellationToken ct = default) =>
        Task.FromResult(_entries.TryGetValue(presetId, out var entry) && entry.IsActive);

    private static AvatarPresetItem ToItem(AvatarPresetEntry e) => new(e.Id, e.Label, e.ImagePath, e.IsDefault);
}
