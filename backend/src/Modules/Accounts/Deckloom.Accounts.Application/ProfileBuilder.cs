using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Application;

// Monta o Profile do Jogador (formatos preferidos so ativos, avatar resolvido); compartilhado por GetMyProfile e UpdateMyProfile.
internal sealed class ProfileBuilder(IFormatCatalog formats, IAvatarPresetCatalog avatarPresetCatalog)
{
    public async Task<Profile> BuildAsync(Player player, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(player);

        var active = await formats.ListActiveAsync(ct);
        var preferred = active
            .Where(f => player.Formats.Ids.Contains(f.Id))
            .Select(f => new FormatOption(f.Id.Value, f.Name))
            .ToList();

        return new Profile(
            player.DisplayName.Value,
            player.Bio?.Value,
            new FavoriteTcgItem(player.FavoriteTcg.Id, player.FavoriteTcg.Label),
            preferred,
            await new AvatarViewBuilder(avatarPresetCatalog).BuildAsync(player.Avatar, ct),
            player.PasswordHash is not null,
            player.ExternalIdentity is not null);
    }
}
