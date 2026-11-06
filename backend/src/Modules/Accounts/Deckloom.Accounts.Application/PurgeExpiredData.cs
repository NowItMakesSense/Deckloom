using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Application;

public sealed record PurgeExpiredDataSummary(int Sessions, int ResetTokens, int PendingGoogleAuths, int AccessAttempts);

// Job de retencao (REQ-047): cada limpeza e independente e idempotente, por isso sem IUnitOfWork nem Result.
public sealed class PurgeExpiredData(
    ISessionRepository sessions,
    IPlayerRepository players,
    IPendingGoogleAuthStore pendingGoogleAuths,
    IAccessAttemptStore accessAttempts,
    TimeProvider clock,
    int batchSize)
{
    private static readonly TimeSpan AccessAttemptRetention = TimeSpan.FromHours(24);

    private readonly int _batchSize = batchSize > 0
        ? batchSize
        : throw new ArgumentOutOfRangeException(nameof(batchSize), batchSize, "Cleanup:BatchSize must be greater than zero.");

    public async Task<PurgeExpiredDataSummary> ExecuteAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();

        return new PurgeExpiredDataSummary(
            Sessions: await DrainAsync(sessions.DeleteExpiredOrRevoked, now, ct),
            ResetTokens: await DrainAsync(players.DeleteExpiredResetTokens, now, ct),
            PendingGoogleAuths: await DrainAsync(
                (_, size, token) => pendingGoogleAuths.DeleteExpiredAsync(size, token), now, ct),
            AccessAttempts: await DrainAsync(accessAttempts.DeleteOlderThanAsync, now - AccessAttemptRetention, ct));
    }

    // Um unico laco para os quatro tipos de dado: repete ate um lote vir menor que o tamanho; o cancelamento so e
    // observado entre lotes (cada lote ja confirmado fica, sem erro).
    private async Task<int> DrainAsync(
        Func<DateTimeOffset, int, CancellationToken, Task<int>> deleteBatch,
        DateTimeOffset reference,
        CancellationToken ct)
    {
        var total = 0;
        while (!ct.IsCancellationRequested)
        {
            var deleted = await deleteBatch(reference, _batchSize, ct);
            total += deleted;
            if (deleted < _batchSize)
            {
                break;
            }
        }

        return total;
    }
}
