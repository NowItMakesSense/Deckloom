using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Testing;

public sealed class InMemoryUnitOfWork : IUnitOfWork
{
    private readonly List<ICommitParticipant> _participants = [];
    private readonly List<Action<Player>> _playerRemovalCascades = [];

    public bool IsTransactionOpen { get; private set; }

    public bool? TransactionOpenDuringLastCommit { get; private set; }

    public async Task ExecuteAsync(Func<CancellationToken, Task> work, CancellationToken ct = default) =>
        await ExecuteAsync<object?>(async token =>
        {
            await work(token);
            return null;
        }, ct);

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        ct.ThrowIfCancellationRequested();
        if (IsTransactionOpen)
        {
            throw new InvalidOperationException("A unit of work cannot be executed inside another execution.");
        }

        IsTransactionOpen = true;
        try
        {
            var result = await work(ct);
            Commit(ct);
            return result;
        }
        catch
        {
            Discard();
            throw;
        }
        finally
        {
            IsTransactionOpen = false;
        }
    }

    // Jogadores lidos ou adicionados pelos repositorios de Jogador desta UoW (o rastreamento de um DbContext): vive
    // enquanto a UoW vive e nao e limpo no commit. A uniao de todos os repositorios da UoW e o conjunto de donos.
    private readonly List<Player> _trackedPlayers = [];
    private bool _hasPlayerSource;

    internal bool HasPlayerSource => _hasPlayerSource;

    internal IReadOnlyList<Player> TrackedPlayers => _trackedPlayers;

    internal void RegisterPlayerSource() => _hasPlayerSource = true;

    internal void TrackPlayer(Player player)
    {
        if (!_trackedPlayers.Contains(player))
        {
            _trackedPlayers.Add(player);
        }
    }

    internal void Enlist(ICommitParticipant participant) => _participants.Add(participant);

    // Cascata do armazenamento (ON DELETE CASCADE): quem possui dados de um Jogador se registra e os apaga quando a
    // remocao dele e confirmada. O estado fica no escopo da UoW, entao os fakes continuam isolados entre escopos.
    internal void OnPlayerRemoved(Action<Player> cascade) => _playerRemovalCascades.Add(cascade);

    // O EF desanexa a entidade removida apos o SaveChanges; os demais Jogadores continuam rastreados (R-51).
    internal void NotifyPlayerRemoved(Player player)
    {
        _trackedPlayers.RemoveAll(p => p.Id == player.Id);
        _playerRemovalCascades.ForEach(cascade => cascade(player));
    }

    public Task CommitAsync(CancellationToken ct = default)
    {
        Commit(ct);
        return Task.CompletedTask;
    }

    // Como o transaction.CommitAsync(ct) do ExecuteAsync do EfUnitOfWork: o ct cancelado antes da aplicacao lanca e descarta tudo (R-50).
    // So vale dentro do ExecuteAsync: no CommitAsync avulso o EF mantem o pendente no contexto; a Application nao usa esse caminho (R-56).
    private void Commit(CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var applications = _participants.Select(p => p.Prepare()).ToList();
            TransactionOpenDuringLastCommit = IsTransactionOpen;
            applications.ForEach(apply => apply());
        }
        catch
        {
            Discard();
            throw;
        }
    }

    // Rollback: equivale ao ChangeTracker.Clear() do EfUnitOfWork (R-51); o rastreamento so volta com nova leitura.
    private void Discard()
    {
        _participants.ForEach(p => p.Discard());
        _trackedPlayers.Clear();
    }
}

internal interface ICommitParticipant
{
    // Validates the pending changes and returns the action that applies them; throws without side effects.
    Action Prepare();

    void Discard();
}
