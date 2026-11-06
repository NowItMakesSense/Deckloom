namespace Deckloom.Accounts.Application;

public sealed record AvatarPresetOption(string Id, string Label, string ImagePath, bool IsDefault);

public sealed class ListAvatarPresets(IAvatarPresetCatalog presets)
{
    public async Task<IReadOnlyList<AvatarPresetOption>> ExecuteAsync(CancellationToken ct = default)
    {
        var active = await presets.ListActiveAsync(ct);
        return active.Select(p => new AvatarPresetOption(p.Id, p.Label, p.ImagePath, p.IsDefault)).ToList();
    }
}
