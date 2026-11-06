using Deckloom.Accounts.Domain;
using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Application;

public sealed class AuthenticateSession(
    ISessionRepository sessions,
    IPlayerRepository players,
    IUnitOfWork unitOfWork,
    ITokenHasher tokenHasher,
    IAvatarPresetCatalog avatarPresetCatalog,
    TimeProvider time)
{
    private static readonly DomainError Unauthenticated = new("unauthenticated");

    public async Task<Result<AuthenticatedPlayer>> ExecuteAsync(
        AuthenticateSessionCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = time.GetUtcNow();
        var session = await new SessionLookup(sessions, tokenHasher).FindAsync(command.SessionToken, ct);
        if (session is null || !session.IsValid(now))
        {
            return Result<AuthenticatedPlayer>.Failure(Unauthenticated);
        }

        var player = await players.GetById(session.PlayerId, ct);
        if (player is null)
        {
            return Result<AuthenticatedPlayer>.Failure(Unauthenticated);
        }

        await unitOfWork.ExecuteAsync(async innerCt =>
        {
            session.Touch(now);
            await sessions.Save(session, innerCt);
        }, ct);

        var summary = await new SessionSummaryFactory(avatarPresetCatalog).CreateAsync(player, ct);
        return Result<AuthenticatedPlayer>.Success(new AuthenticatedPlayer(player.Id, session.Id, summary));
    }
}
