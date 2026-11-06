using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Testing;

public sealed class InMemoryAvatarStorage : IAvatarStorage, ICommitParticipant
{
    private readonly Dictionary<string, byte[]> _committed;
    private readonly List<(string Key, byte[]? Image)> _pending = [];

    // Same limit and preconditions as PostgresAvatarStorage.SaveAsync (256 KB by default).
    private const int MaxBytes = 262144;

    private readonly InMemoryUnitOfWork _unitOfWork;

    public InMemoryAvatarStorage(InMemoryUnitOfWork unitOfWork)
        : this(unitOfWork, [])
    {
    }

    // Nova UoW sobre as mesmas imagens confirmadas de outro armazenamento (o "banco" compartilhado).
    public InMemoryAvatarStorage(InMemoryUnitOfWork unitOfWork, InMemoryAvatarStorage sharedStateWith)
        : this(unitOfWork, sharedStateWith._committed)
    {
    }

    private InMemoryAvatarStorage(InMemoryUnitOfWork unitOfWork, Dictionary<string, byte[]> committed)
    {
        _committed = committed;
        _unitOfWork = unitOfWork;
        unitOfWork.Enlist(this);
        unitOfWork.OnPlayerRemoved(player =>
        {
            if (player.Avatar is CustomAvatar custom)
            {
                _committed.Remove(custom.StorageKey);
            }
        });
    }

    public Task SaveAsync(string key, byte[] image, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(image);
        if (image.Length > MaxBytes)
        {
            throw new ArgumentException($"Image exceeds the limit of {MaxBytes} bytes.", nameof(image));
        }

        // Como o PostgresAvatarStorage: so Jogadores lidos/adicionados no escopo contam, e a checagem nunca e pulada.
        if (!_unitOfWork.HasPlayerSource)
        {
            throw new InvalidOperationException("The unit of work has no Player repository to own the storage key.");
        }

        if (!_unitOfWork.TrackedPlayers.Any(p => p.Avatar is CustomAvatar custom && custom.StorageKey == key))
        {
            throw new InvalidOperationException("No Player of the scope has a custom avatar with the given storage key.");
        }

        _pending.Add((key, image.ToArray()));
        return Task.CompletedTask;
    }

    // Seeding for tests whose Player is not yet in the scope: stores a committed image, skipping the owner check.
    public void SeedCommitted(string key, byte[] image) => _committed[key] = image.ToArray();

    public Task<byte[]?> ReadAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(_committed.TryGetValue(key, out var image) ? image.ToArray() : null);

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        _pending.Add((key, null));
        return Task.CompletedTask;
    }

    Action ICommitParticipant.Prepare()
    {
        var changes = _pending.ToList();
        return () =>
        {
            foreach (var (key, image) in changes)
            {
                if (image is null)
                {
                    _committed.Remove(key);
                }
                else
                {
                    _committed[key] = image;
                }
            }

            _pending.Clear();
        };
    }

    void ICommitParticipant.Discard() => _pending.Clear();
}
