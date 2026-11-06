using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Domain;

public sealed class Session : AggregateRoot
{
    public const string SessionInvalidCode = "session_invalid";
    public const string SubjectMismatchCode = "subject_mismatch";

    private static readonly TimeSpan InactivityWindow = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RememberMeLifetime = TimeSpan.FromDays(30);

    private Session()
    {
    }

    public SessionId Id { get; private set; }

    public TokenHash TokenHash { get; private set; } = null!;

    public PlayerId PlayerId { get; private set; }

    public bool RememberMe { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset LastSeenAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public DateTimeOffset? ReauthenticatedAt { get; private set; }

    public static Session Start(PlayerId playerId, TokenHash tokenHash, bool rememberMe, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(tokenHash);

        var session = new Session
        {
            Id = new SessionId(Guid.CreateVersion7()),
            TokenHash = tokenHash,
            PlayerId = playerId,
            RememberMe = rememberMe,
            CreatedAt = now,
            LastSeenAt = now,
            ExpiresAt = now + (rememberMe ? RememberMeLifetime : InactivityWindow),
        };
        session.Raise(new SessionStarted(session.Id, playerId, rememberMe));
        return session;
    }

    public bool IsValid(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;

    public bool Touch(DateTimeOffset now)
    {
        if (!IsValid(now))
        {
            return false;
        }

        LastSeenAt = now;
        if (!RememberMe)
        {
            ExpiresAt = now + InactivityWindow;
        }

        return true;
    }

    public void Revoke(SessionRevocationReason reason, DateTimeOffset now)
    {
        if (RevokedAt is not null)
        {
            return;
        }

        RevokedAt = now;
        Raise(new SessionRevoked(Id, PlayerId, reason));
    }

    public Result<Session> MarkReauthenticated(ExternalIdentity linked, ExternalIdentity presented, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(linked);
        ArgumentNullException.ThrowIfNull(presented);

        if (!IsValid(now))
        {
            return Result<Session>.Failure(new DomainError(SessionInvalidCode));
        }

        if (linked != presented)
        {
            return Result<Session>.Failure(new DomainError(SubjectMismatchCode));
        }

        ReauthenticatedAt = now;
        return Result<Session>.Success(this);
    }

    public RecentGoogleLogin ToRecentGoogleLogin() => new(PlayerId, ReauthenticatedAt);
}
