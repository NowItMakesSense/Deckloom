using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Testing;

public sealed class RecordingPlayerDecksPurger : IPlayerDecksPurger, ICommitParticipant
{
    private readonly List<PlayerId> _purged = [];
    private readonly List<PlayerId> _pending = [];
    private Exception? _failure;

    public RecordingPlayerDecksPurger(InMemoryUnitOfWork unitOfWork) => unitOfWork.Enlist(this);

    public IReadOnlyList<PlayerId> Purged => _purged;

    public void FailWith(Exception exception) => _failure = exception;

    public Task PurgeAsync(PlayerId playerId, CancellationToken ct = default)
    {
        if (_failure is not null)
        {
            throw _failure;
        }

        _pending.Add(playerId);
        return Task.CompletedTask;
    }

    Action ICommitParticipant.Prepare()
    {
        var toPurge = _pending.ToList();
        return () =>
        {
            _purged.AddRange(toPurge);
            _pending.Clear();
        };
    }

    void ICommitParticipant.Discard() => _pending.Clear();
}
