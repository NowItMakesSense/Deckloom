namespace Deckloom.Accounts.Domain;

public interface IPlayerRepository
{
    Task<Player?> GetById(PlayerId id, CancellationToken ct = default);

    Task<Player?> FindByEmail(Email email, CancellationToken ct = default);

    Task<Player?> FindByGoogleSubject(string subject, CancellationToken ct = default);

    Task<Player?> FindByResetTokenHash(TokenHash tokenHash, CancellationToken ct = default);

    Task<bool> ExistsByEmail(Email email, CancellationToken ct = default);

    Task Add(Player player, CancellationToken ct = default);

    Task Save(Player player, CancellationToken ct = default);

    Task Remove(Player player, CancellationToken ct = default);

    Task<int> DeleteExpiredResetTokens(DateTimeOffset now, int batchSize, CancellationToken ct = default);
}
