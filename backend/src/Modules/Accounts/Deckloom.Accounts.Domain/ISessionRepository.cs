namespace Deckloom.Accounts.Domain;

public interface ISessionRepository
{
    Task Add(Session session, CancellationToken ct = default);

    Task<Session?> FindById(SessionId id, CancellationToken ct = default);

    Task<Session?> FindByTokenHash(TokenHash tokenHash, CancellationToken ct = default);

    Task Save(Session session, CancellationToken ct = default);

    Task<int> RevokeAllFor(
        PlayerId playerId,
        SessionRevocationReason reason,
        DateTimeOffset now,
        CancellationToken ct = default);

    Task<int> DeleteExpiredOrRevoked(DateTimeOffset now, int batchSize, CancellationToken ct = default);
}
