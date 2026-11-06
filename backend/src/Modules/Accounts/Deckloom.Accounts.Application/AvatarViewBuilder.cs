using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Application;

internal sealed class AvatarViewBuilder(IAvatarPresetCatalog avatarPresetCatalog)
{
    public const string CustomAlt = "Seu avatar personalizado";

    public async Task<AvatarView> BuildAsync(Avatar avatar, CancellationToken ct = default)
    {
        return avatar switch
        {
            PresetAvatar preset => await BuildPresetAsync(preset, ct),
            CustomAvatar custom => BuildCustom(custom),
            _ => throw new NotSupportedException($"Unsupported avatar type '{avatar.GetType().Name}'."),
        };
    }

    private static AvatarView BuildCustom(CustomAvatar custom) =>
        new(
            AvatarView.CustomKind,
            null,
            $"/api/v1/me/avatar/custom?v={Uri.EscapeDataString(custom.StorageKey)}",
            CustomAlt);

    private async Task<AvatarView> BuildPresetAsync(PresetAvatar preset, CancellationToken ct)
    {
        var items = await avatarPresetCatalog.ListActiveAsync(ct);
        var item = items.FirstOrDefault(i => string.Equals(i.Id, preset.PresetId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Avatar preset '{preset.PresetId}' is not in the active catalog.");

        return new AvatarView(AvatarView.PresetKind, item.Id, item.ImagePath, $"Avatar: {item.Label}");
    }
}
