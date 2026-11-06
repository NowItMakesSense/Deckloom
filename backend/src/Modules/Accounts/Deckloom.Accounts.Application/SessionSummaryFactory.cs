using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Application;

public sealed class SessionSummaryFactory(IAvatarPresetCatalog avatarPresetCatalog)
{
    public async Task<SessionSummary> CreateAsync(Player player, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(player);

        return new SessionSummary(
            player.DisplayName.Value,
            await new AvatarViewBuilder(avatarPresetCatalog).BuildAsync(player.Avatar, ct),
            player.PasswordHash is not null,
            player.ExternalIdentity is not null);
    }
}
