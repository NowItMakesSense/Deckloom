namespace Deckloom.Accounts.Application;

public sealed record AvatarPresetItem(string Id, string Label, string ImagePath, bool IsDefault);

public interface IAvatarPresetCatalog
{
    Task<IReadOnlyList<AvatarPresetItem>> ListActiveAsync(CancellationToken ct = default);

    Task<AvatarPresetItem> GetDefaultAsync(CancellationToken ct = default);

    Task<bool> ContainsActiveAsync(string presetId, CancellationToken ct = default);
}
