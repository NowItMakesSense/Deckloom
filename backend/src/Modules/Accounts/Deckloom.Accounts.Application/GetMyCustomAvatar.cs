using Deckloom.Accounts.Domain;
using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Application;

public sealed record GetMyCustomAvatarCommand(AuthenticatedPlayer? Caller);

public sealed class GetMyCustomAvatar(IPlayerRepository players, IAvatarStorage avatarStorage)
{
    private static readonly DomainError Unauthenticated = new("unauthenticated");
    private static readonly DomainError NotFound = new("not_found");

    public async Task<Result<byte[]>> ExecuteAsync(GetMyCustomAvatarCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Caller is null)
        {
            return Result<byte[]>.Failure(Unauthenticated);
        }

        var player = await players.GetById(command.Caller.PlayerId, ct);
        if (player is null)
        {
            return Result<byte[]>.Failure(Unauthenticated);
        }

        if (player.Avatar is not CustomAvatar custom)
        {
            return Result<byte[]>.Failure(NotFound);
        }

        var image = await avatarStorage.ReadAsync(custom.StorageKey, ct);
        return image is null ? Result<byte[]>.Failure(NotFound) : Result<byte[]>.Success(image);
    }
}
