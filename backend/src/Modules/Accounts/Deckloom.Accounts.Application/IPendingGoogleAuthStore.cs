using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Application;

public interface IPendingGoogleAuthStore
{
    Task AddAsync(PendingGoogleAuth pending, CancellationToken ct = default);

    Task<PendingGoogleAuth?> FindAsync(TokenHash tokenHash, CancellationToken ct = default);

    Task<PendingGoogleAuth?> ConsumeAsync(TokenHash tokenHash, CancellationToken ct = default);

    Task<int> DeleteExpiredAsync(int batchSize, CancellationToken ct = default);
}
