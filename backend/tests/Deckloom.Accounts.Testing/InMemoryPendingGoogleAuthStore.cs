using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Testing;

public sealed class InMemoryPendingGoogleAuthStore : IPendingGoogleAuthStore, ICommitParticipant
{
    private readonly Lock _gate = new();
    private readonly Dictionary<TokenHash, PendingGoogleAuth> _pendings = [];
    private readonly TimeProvider _clock;
    private readonly InMemoryUnitOfWork _unitOfWork;

    // Pendings claimed by ConsumeAsync in the open unit of work: gone for other contenders at once (only one claims),
    // made permanent on commit, restored on discard.
    private readonly List<PendingGoogleAuth> _consumed = [];

    // Pendings added in the open unit of work: visible only after commit, dropped on discard.
    private readonly List<PendingGoogleAuth> _added = [];

    public InMemoryPendingGoogleAuthStore(TimeProvider clock, InMemoryUnitOfWork unitOfWork)
    {
        _clock = clock;
        _unitOfWork = unitOfWork;
        unitOfWork.Enlist(this);
        unitOfWork.OnPlayerRemoved(player =>
        {
            lock (_gate)
            {
                var owned = _pendings.Where(p => p.Value is PendingGoogleLink link && link.TargetPlayerId == player.Id)
                    .Select(p => p.Key).ToList();
                owned.ForEach(hash => _pendings.Remove(hash));
            }
        });
    }

    public Task AddAsync(PendingGoogleAuth pending, CancellationToken ct = default)
    {
        lock (_gate)
        {
            // Inside an open unit of work the add waits for the commit; outside one it is immediate (seeding, single
            // writes that have no block to roll back).
            if (_unitOfWork.IsTransactionOpen)
            {
                _added.Add(pending);
            }
            else
            {
                _pendings[pending.TokenHash] = pending;
            }
        }

        return Task.CompletedTask;
    }

    public Task<PendingGoogleAuth?> FindAsync(TokenHash tokenHash, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var found = _pendings.GetValueOrDefault(tokenHash);
            return Task.FromResult(found is not null && !IsExpired(found) ? found : null);
        }
    }

    public Task<PendingGoogleAuth?> ConsumeAsync(TokenHash tokenHash, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_pendings.Remove(tokenHash, out var found))
            {
                return Task.FromResult<PendingGoogleAuth?>(null);
            }

            if (IsExpired(found))
            {
                return Task.FromResult<PendingGoogleAuth?>(null);
            }

            _consumed.Add(found);
            return Task.FromResult<PendingGoogleAuth?>(found);
        }
    }

    Action ICommitParticipant.Prepare() => () =>
    {
        lock (_gate)
        {
            _added.ForEach(pending => _pendings[pending.TokenHash] = pending);
            _added.Clear();
            _consumed.Clear();
        }
    };

    void ICommitParticipant.Discard()
    {
        lock (_gate)
        {
            _consumed.ForEach(pending => _pendings[pending.TokenHash] = pending);
            _consumed.Clear();
            _added.Clear();
        }
    }

    public Task<int> DeleteExpiredAsync(int batchSize, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var expired = _pendings.Where(p => IsExpired(p.Value)).Select(p => p.Key).Take(batchSize).ToList();
            expired.ForEach(hash => _pendings.Remove(hash));
            return Task.FromResult(expired.Count);
        }
    }

    private bool IsExpired(PendingGoogleAuth pending) => _clock.GetUtcNow() >= pending.ExpiresAt;
}
