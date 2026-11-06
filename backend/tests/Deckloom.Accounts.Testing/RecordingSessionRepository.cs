using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Testing;

// Decorador de ISessionRepository (T-159, R-44): registra cada RevokeAllFor com o motivo e se ocorreu dentro da unidade de
// trabalho, sem depender da cascata do InMemorySessionRepository (OnPlayerRemoved) para provar a revogacao.
public sealed class RecordingSessionRepository(InMemorySessionRepository inner, InMemoryUnitOfWork unitOfWork) : ISessionRepository
{
    public List<RevokeAllForCall> RevokeAllForCalls { get; } = [];

    public Task Add(Session session, CancellationToken ct = default) => inner.Add(session, ct);

    public Task<Session?> FindById(SessionId id, CancellationToken ct = default) => inner.FindById(id, ct);

    public Task<Session?> FindByTokenHash(TokenHash tokenHash, CancellationToken ct = default) =>
        inner.FindByTokenHash(tokenHash, ct);

    public Task Save(Session session, CancellationToken ct = default) => inner.Save(session, ct);

    public Task<int> RevokeAllFor(
        PlayerId playerId,
        SessionRevocationReason reason,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        RevokeAllForCalls.Add(new RevokeAllForCall(playerId, reason, now, unitOfWork.IsTransactionOpen));
        return inner.RevokeAllFor(playerId, reason, now, ct);
    }

    public Task<int> DeleteExpiredOrRevoked(DateTimeOffset now, int batchSize, CancellationToken ct = default) =>
        inner.DeleteExpiredOrRevoked(now, batchSize, ct);

    public sealed record RevokeAllForCall(
        PlayerId PlayerId,
        SessionRevocationReason Reason,
        DateTimeOffset Now,
        bool InsideUnitOfWork);
}
