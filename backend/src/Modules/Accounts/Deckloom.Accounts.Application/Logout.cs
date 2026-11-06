using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Application;

public sealed class Logout(
    ISessionRepository sessions,
    IUnitOfWork unitOfWork,
    ITokenHasher tokenHasher,
    TimeProvider time,
    IAccountsEventLog? events = null)
{
    private readonly IAccountsEventLog _events = events ?? NullAccountsEventLog.Instance;

    public async Task ExecuteAsync(LogoutCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var session = await new SessionLookup(sessions, tokenHasher).FindAsync(command.SessionToken, ct);
        if (session is null)
        {
            return;
        }

        var now = time.GetUtcNow();
        await unitOfWork.ExecuteAsync(async innerCt =>
        {
            session.Revoke(SessionRevocationReason.Logout, now);
            await sessions.Save(session, innerCt);
        }, ct);

        _events.LoggedOut(session.PlayerId, session.Id);
        _events.SessionRevoked(session.PlayerId, session.Id, "logout");
    }
}
