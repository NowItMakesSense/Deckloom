using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Application;

public sealed record FavoriteTcgItem(string Id, string Name);

public sealed class ListFavoriteTcgs
{
    private readonly IReadOnlyList<FavoriteTcg> _supported = FavoriteTcg.Supported;

    public Task<IReadOnlyList<FavoriteTcgItem>> ExecuteAsync(CancellationToken ct = default)
    {
        IReadOnlyList<FavoriteTcgItem> items = _supported
            .Select(t => new FavoriteTcgItem(t.Id, t.Label))
            .ToList();
        return Task.FromResult(items);
    }
}
