using System.Reflection;
using Deckloom.Accounts.Domain;
using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Testing;

public sealed class InMemoryPlayerRepository : IPlayerRepository, ICommitParticipant
{
    private const string EmailConstraint = "ux_player_email";
    private const string ExternalIdentityConstraint = "ux_player_external_identity_provider_subject";

    // Estado confirmado: compartilhavel entre repositorios de UoWs distintas (o "banco"); o rastreamento fica na UoW.
    private sealed class CommittedState
    {
        public List<Player> Players { get; set; } = [];

        public List<(Player Player, Player Snapshot)> Snapshots { get; set; } = [];
    }

    private readonly CommittedState _state;
    private readonly List<(bool IsAdd, Player Player)> _pending = [];

    private readonly InMemoryUnitOfWork _unitOfWork;

    public InMemoryPlayerRepository(InMemoryUnitOfWork unitOfWork)
        : this(unitOfWork, new CommittedState())
    {
    }

    // Nova UoW sobre o mesmo estado confirmado de outro repositorio; o conjunto rastreado comeca vazio.
    public InMemoryPlayerRepository(InMemoryUnitOfWork unitOfWork, InMemoryPlayerRepository sharedStateWith)
        : this(unitOfWork, sharedStateWith._state)
    {
    }

    private InMemoryPlayerRepository(InMemoryUnitOfWork unitOfWork, CommittedState state)
    {
        _unitOfWork = unitOfWork;
        _state = state;
        unitOfWork.RegisterPlayerSource();
        unitOfWork.Enlist(this);
    }

    public Task<Player?> GetById(PlayerId id, CancellationToken ct = default) =>
        Find(p => p.Id == id);

    public Task<Player?> FindByEmail(Email email, CancellationToken ct = default) =>
        Find(p => p.Email == email);

    public Task<Player?> FindByGoogleSubject(string subject, CancellationToken ct = default) =>
        Find(p => p.ExternalIdentity?.Subject == subject);

    public Task<Player?> FindByResetTokenHash(TokenHash tokenHash, CancellationToken ct = default) =>
        Find(p => p.PasswordResetToken?.TokenHash.Equals(tokenHash) == true);

    // Consulta de existencia: nao rastreia o Jogador, como um AnyAsync do EF.
    public Task<bool> ExistsByEmail(Email email, CancellationToken ct = default) =>
        Task.FromResult(_state.Players.Any(p => p.Email == email));

    public Task Add(Player player, CancellationToken ct = default)
    {
        _unitOfWork.TrackPlayer(player);
        _pending.Add((true, player));
        return Task.CompletedTask;
    }

    public Task Save(Player player, CancellationToken ct = default) => Task.CompletedTask;

    public Task Remove(Player player, CancellationToken ct = default)
    {
        _pending.Add((false, player));
        return Task.CompletedTask;
    }

    // Limpeza de retencao: imediata (sem UoW). Atualiza tambem o snapshot para que um Discard nao ressuscite o token.
    public Task<int> DeleteExpiredResetTokens(DateTimeOffset now, int batchSize, CancellationToken ct = default)
    {
        lock (_state)
        {
            var batch = _state.Players
                .Where(p => p.PasswordResetToken is not null && now >= p.PasswordResetToken.ExpiresAt)
                .Take(batchSize)
                .ToList();
            var cleared = batch.Count(p => p.ClearExpiredResetToken(now));
            foreach (var (_, snapshot) in _state.Snapshots)
            {
                snapshot.ClearExpiredResetToken(now);
            }

            return Task.FromResult(cleared);
        }
    }

    Action ICommitParticipant.Prepare()
    {
        var result = _state.Players.ToList();
        foreach (var (isAdd, player) in _pending)
        {
            if (isAdd)
            {
                result.Add(player);
            }
            else
            {
                result.RemoveAll(p => p.Id == player.Id);
            }
        }

        if (result.GroupBy(p => p.Email).Any(g => g.Count() > 1))
        {
            throw new UniqueConstraintViolationException(EmailConstraint);
        }

        if (result.Where(p => p.ExternalIdentity is not null)
            .GroupBy(p => (p.ExternalIdentity!.Provider, p.ExternalIdentity.Subject)).Any(g => g.Count() > 1))
        {
            throw new UniqueConstraintViolationException(ExternalIdentityConstraint);
        }

        var removed = _pending.Where(change => !change.IsAdd).Select(change => change.Player).ToList();
        return () =>
        {
            _state.Players = result;
            removed.ForEach(_unitOfWork.NotifyPlayerRemoved);
            _state.Snapshots = result.Select(p => (p, Copy(p))).ToList();
            _pending.Clear();
        };
    }

    // Rollback: a instancia (identidade preservada) volta ao ultimo estado commitado, como um rollback real do armazenamento.
    void ICommitParticipant.Discard()
    {
        _pending.Clear();
        foreach (var (player, snapshot) in _state.Snapshots)
        {
            Restore(player, snapshot);
        }
    }

    private Task<Player?> Find(Func<Player, bool> predicate)
    {
        var player = _state.Players.FirstOrDefault(predicate);
        if (player is not null)
        {
            _unitOfWork.TrackPlayer(player);
        }

        return Task.FromResult(player);
    }

    private static readonly MethodInfo MemberwiseCloneMethod =
        typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly FieldInfo EventsField =
        typeof(AggregateRoot).GetField("_domainEvents", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly FieldInfo ExternalIdentitiesField =
        typeof(Player).GetField("_externalIdentities", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static Player Copy(Player player)
    {
        var copy = (Player)MemberwiseCloneMethod.Invoke(player, null)!;
        var events = (List<DomainEvent>)EventsField.GetValue(player)!;
        EventsField.SetValue(copy, new List<DomainEvent>(events));
        var identities = (List<ExternalIdentity>)ExternalIdentitiesField.GetValue(player)!;
        ExternalIdentitiesField.SetValue(copy, new List<ExternalIdentity>(identities));
        return copy;
    }

    private static void Restore(Player target, Player snapshot)
    {
        for (var type = typeof(Player); type is not null; type = type.BaseType)
        {
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                if (field == EventsField)
                {
                    var events = (List<DomainEvent>)EventsField.GetValue(target)!;
                    events.Clear();
                    events.AddRange((List<DomainEvent>)EventsField.GetValue(snapshot)!);
                }
                else if (field == ExternalIdentitiesField)
                {
                    ExternalIdentitiesField.SetValue(
                        target, new List<ExternalIdentity>((List<ExternalIdentity>)ExternalIdentitiesField.GetValue(snapshot)!));
                }
                else
                {
                    field.SetValue(target, field.GetValue(snapshot));
                }
            }
        }
    }
}
