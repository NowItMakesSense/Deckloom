using Deckloom.Accounts.Application;
using Microsoft.EntityFrameworkCore;

namespace Deckloom.Accounts.Infrastructure;

public sealed class EfAvatarPresetCatalog : IAvatarPresetCatalog
{
    private readonly IAvatarPresetCache _cache;

    public EfAvatarPresetCatalog(IDbContextFactory<AccountsDbContext> contexts, TimeProvider time)
        : this(new ExpiringAvatarPresetCache(contexts, time))
    {
    }

    internal EfAvatarPresetCatalog(IAvatarPresetCache cache) => _cache = cache;

    public async Task<IReadOnlyList<AvatarPresetItem>> ListActiveAsync(CancellationToken ct = default) =>
        (await _cache.GetActiveAsync(ct)).Select(ToItem).ToList();

    public async Task<AvatarPresetItem> GetDefaultAsync(CancellationToken ct = default) =>
        ToItem((await _cache.GetActiveAsync(ct)).First(p => p.IsDefault));

    public async Task<bool> ContainsActiveAsync(string presetId, CancellationToken ct = default) =>
        (await _cache.GetActiveAsync(ct)).Any(p => string.Equals(p.Id, presetId, StringComparison.Ordinal));

    private static AvatarPresetItem ToItem(AvatarPresetRow row) =>
        new(row.Id, row.Label, row.ImagePath, row.IsDefault);
}
