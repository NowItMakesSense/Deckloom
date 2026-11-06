using Deckloom.Accounts.Domain;
using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Application;

public sealed record GetMyProfileCommand(AuthenticatedPlayer? Caller);

public sealed record Profile(
    string DisplayName,
    string? Bio,
    FavoriteTcgItem FavoriteTcg,
    IReadOnlyList<FormatOption> PreferredFormats,
    AvatarView Avatar,
    bool HasPassword,
    bool HasGoogle);

public sealed class GetMyProfile(
    IPlayerRepository players,
    IFormatCatalog formats,
    IAvatarPresetCatalog avatarPresetCatalog)
{
    private static readonly DomainError Unauthenticated = new("unauthenticated");

    public async Task<Result<Profile>> ExecuteAsync(GetMyProfileCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Caller is null)
        {
            return Result<Profile>.Failure(Unauthenticated);
        }

        var player = await players.GetById(command.Caller.PlayerId, ct);
        if (player is null)
        {
            return Result<Profile>.Failure(Unauthenticated);
        }

        return Result<Profile>.Success(await new ProfileBuilder(formats, avatarPresetCatalog).BuildAsync(player, ct));
    }
}
