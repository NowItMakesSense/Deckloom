using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Testing;

public sealed record FormatEntry(FormatId Id, string Name, int SortOrder, bool IsActive);

public sealed class InMemoryFormatCatalog : IFormatCatalog
{
    private readonly Dictionary<FormatId, FormatEntry> _entries;

    public InMemoryFormatCatalog(IEnumerable<FormatEntry> entries) =>
        _entries = entries.ToDictionary(e => e.Id);

    public void Add(FormatEntry entry) => _entries.Add(entry.Id, entry);

    public void Activate(FormatId id) => SetActive(id, true);

    public void Deactivate(FormatId id) => SetActive(id, false);

    public Task<IReadOnlyList<FormatItem>> ListActiveAsync(CancellationToken ct = default)
    {
        IReadOnlyList<FormatItem> items = _entries.Values
            .Where(e => e.IsActive)
            .OrderBy(e => e.SortOrder)
            .ThenBy(e => e.Id.Value, StringComparer.Ordinal)
            .Select(e => new FormatItem(e.Id, e.Name))
            .ToList();
        return Task.FromResult(items);
    }

    public Task<bool> ContainsAsync(FormatId id, CancellationToken ct = default) =>
        Task.FromResult(_entries.TryGetValue(id, out var entry) && entry.IsActive);

    private void SetActive(FormatId id, bool isActive) =>
        _entries[id] = _entries[id] with { IsActive = isActive };
}
