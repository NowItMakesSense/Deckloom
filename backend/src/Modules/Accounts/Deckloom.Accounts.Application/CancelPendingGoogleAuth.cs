using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Application;

public sealed class CancelPendingGoogleAuth(
    IPendingGoogleAuthStore store,
    ITokenHasher tokenHasher,
    IUnitOfWork unitOfWork)
{
    // Idempotente: token ausente, desconhecido, expirado ou ja cancelado tambem e sucesso.
    public async Task<Result<PendingGoogleAuthCancelled>> ExecuteAsync(
        CancelPendingGoogleAuthCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.PendingToken))
        {
            return Result<PendingGoogleAuthCancelled>.Success(new PendingGoogleAuthCancelled());
        }

        var tokenHash = tokenHasher.Hash(command.PendingToken);
        return await unitOfWork.ExecuteAsync(async innerCt =>
        {
            await store.ConsumeAsync(tokenHash, innerCt);
            return Result<PendingGoogleAuthCancelled>.Success(new PendingGoogleAuthCancelled());
        }, ct);
    }
}
