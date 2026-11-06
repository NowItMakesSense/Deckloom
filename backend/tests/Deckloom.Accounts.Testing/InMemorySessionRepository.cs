using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Testing;

public sealed class InMemorySessionRepository : ISessionRepository, ICommitParticipant
{
    private readonly List<Session> _committed = [];
    private readonly List<Session> _pending = [];
    private readonly List<(PlayerId PlayerId, SessionRevocationReason Reason, DateTimeOffset Now)> _pendingRevocations = [];

    public InMemorySessionRepository(InMemoryUnitOfWork unitOfWork)
    {
        unitOfWork.Enlist(this);
        unitOfWork.OnPlayerRemoved(player =>
        {
            lock (_committed)
            {
                _committed.RemoveAll(s => s.PlayerId == player.Id);
            }
        });
    }

    public Task Add(Session session, CancellationToken ct = default)
    {
        _pending.Add(session);
        return Task.CompletedTask;
    }

    public Task<Session?> FindById(SessionId id, CancellationToken ct = default) =>
        Task.FromResult(_committed.FirstOrDefault(s => s.Id == id));

    public Task<Session?> FindByTokenHash(TokenHash tokenHash, CancellationToken ct = default) =>
        Task.FromResult(_committed.FirstOrDefault(s => s.TokenHash.Equals(tokenHash)));

    public Task Save(Session session, CancellationToken ct = default) => Task.CompletedTask;

    public Task<int> RevokeAllFor(
        PlayerId playerId,
        SessionRevocationReason reason,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        _pendingRevocations.Add((playerId, reason, now));
        return Task.FromResult(RevocableSessions(playerId).Count());
    }

    // Limpeza de retencao: imediata (sem UoW), idempotente; o lock evita contagem dupla em execucoes sobrepostas.
    public Task<int> DeleteExpiredOrRevoked(DateTimeOffset now, int batchSize, CancellationToken ct = default)
    {
        lock (_committed)
        {
            var batch = _committed.Where(s => !s.IsValid(now)).Take(batchSize).ToList();
            return Task.FromResult(batch.Count(_committed.Remove));
        }
    }

    Action ICommitParticipant.Prepare()
    {
        var toAdd = _pending.ToList();
        var revocations = _pendingRevocations
            .SelectMany(r => RevocableSessions(r.PlayerId).Select(s => (Session: s, r.Reason, r.Now)))
            .ToList();
        return () =>
        {
            _committed.AddRange(toAdd);
            _pending.Clear();
            _pendingRevocations.Clear();
            foreach (var (session, reason, now) in revocations.Where(r => r.Session.RevokedAt is null))
            {
                session.Revoke(reason, now);
            }
        };
    }

    void ICommitParticipant.Discard()
    {
        _pending.Clear();
        _pendingRevocations.Clear();
    }

    private IEnumerable<Session> RevocableSessions(PlayerId playerId) =>
        _committed.Where(s => s.PlayerId == playerId && s.RevokedAt is null);
}
