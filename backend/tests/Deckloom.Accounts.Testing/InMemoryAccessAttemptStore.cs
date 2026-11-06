using Deckloom.Accounts.Application;

namespace Deckloom.Accounts.Testing;

// Seguro para chamadas concorrentes (SEC-01: testes de varias tentativas em paralelo na mesma conta).
public sealed class InMemoryAccessAttemptStore : IAccessAttemptStore, ICommitParticipant
{
    private readonly object _lock = new();
    private readonly List<(long Id, AccessAttempt Attempt)> _attempts = [];
    private readonly List<byte[]> _pendingAccountDeletes = [];
    private long _nextId;

    private readonly InMemoryUnitOfWork _unitOfWork;

    public InMemoryAccessAttemptStore(InMemoryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
        unitOfWork.Enlist(this);
    }

    public TransactionProbe TransactionProbe { get; } = new();

    public Task<long> RecordAsync(AccessAttempt attempt, CancellationToken ct = default)
    {
        EnsureNoOpenUnitOfWork();
        lock (_lock)
        {
            var id = ++_nextId;
            _attempts.Add((id, attempt));
            return Task.FromResult(id);
        }
    }

    public Task<int> DeleteByIdAsync(long id, CancellationToken ct = default)
    {
        EnsureNoOpenUnitOfWork();
        lock (_lock)
        {
            return Task.FromResult(_attempts.RemoveAll(a => a.Id == id));
        }
    }

    public Task<int> CountByIpAsync(byte[] ipKeyHash, DateTimeOffset since, CancellationToken ct = default)
    {
        lock (_lock)
        {
            TransactionProbe.Record("CountByIp");
            return Task.FromResult(CountInWindow(a => a.IpKeyHash.AsSpan().SequenceEqual(ipKeyHash), since));
        }
    }

    public Task<int> CountByAccountAsync(byte[] accountKeyHash, DateTimeOffset since, CancellationToken ct = default)
    {
        lock (_lock)
        {
            TransactionProbe.Record("CountByAccount");
            return Task.FromResult(CountInWindow(a => BelongsTo(a, accountKeyHash), since));
        }
    }

    public Task<int> DeleteByAccountAsync(byte[] accountKeyHash, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _pendingAccountDeletes.Add(accountKeyHash);
            return Task.FromResult(_attempts.Count(a => BelongsTo(a.Attempt, accountKeyHash)));
        }
    }

    public Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, int batchSize, CancellationToken ct = default)
    {
        EnsureNoOpenUnitOfWork();
        lock (_lock)
        {
            var batch = _attempts.Where(a => a.Attempt.AttemptedAt < cutoff).Take(batchSize).ToList();
            return Task.FromResult(batch.Count(_attempts.Remove));
        }
    }

    Action ICommitParticipant.Prepare()
    {
        List<byte[]> targets;
        lock (_lock)
        {
            targets = _pendingAccountDeletes.ToList();
        }

        return () =>
        {
            lock (_lock)
            {
                _attempts.RemoveAll(a => targets.Any(target => BelongsTo(a.Attempt, target)));
                _pendingAccountDeletes.Clear();
            }
        };
    }

    void ICommitParticipant.Discard()
    {
        lock (_lock)
        {
            _pendingAccountDeletes.Clear();
        }
    }

    // Immediate operations must not run inside a unit of work: they would survive its rollback (REQ-032).
    private void EnsureNoOpenUnitOfWork()
    {
        if (_unitOfWork.IsTransactionOpen)
        {
            throw new InvalidOperationException("Immediate access attempt operations cannot run inside a unit of work.");
        }
    }

    // Window start is inclusive (attempted_at >= since), like the SQL the real adapter runs.
    private int CountInWindow(Func<AccessAttempt, bool> matches, DateTimeOffset since) =>
        _attempts.Count(a => a.Attempt.AttemptedAt >= since && matches(a.Attempt));

    private static bool BelongsTo(AccessAttempt attempt, byte[] accountKeyHash) =>
        attempt.AccountKeyHash is not null && attempt.AccountKeyHash.AsSpan().SequenceEqual(accountKeyHash);
}
