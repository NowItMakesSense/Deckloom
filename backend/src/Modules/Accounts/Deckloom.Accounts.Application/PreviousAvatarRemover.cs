namespace Deckloom.Accounts.Application;

// Remove do armazenamento a imagem do avatar anterior (REQ-021). Deve ser chamada dentro de IUnitOfWork.ExecuteAsync,
// junto com o Save do Jogador, para que um rollback restaure a imagem. Reutilizavel pelo upload de avatar.
internal sealed class PreviousAvatarRemover(IAvatarStorage avatarStorage)
{
    public Task RemoveAsync(string? previousStorageKey, CancellationToken ct = default) =>
        previousStorageKey is null ? Task.CompletedTask : avatarStorage.RemoveAsync(previousStorageKey, ct);
}
