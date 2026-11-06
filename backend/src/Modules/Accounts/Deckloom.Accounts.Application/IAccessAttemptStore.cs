namespace Deckloom.Accounts.Application;

public interface IAccessAttemptStore
{
    // Imediato e duravel antes de retornar; devolve o id da linha gravada.
    Task<long> RecordAsync(AccessAttempt attempt, CancellationToken ct = default);

    // Imediato, fora do unit of work: apaga so o evento provisorio da tentativa.
    Task<int> DeleteByIdAsync(long id, CancellationToken ct = default);

    Task<int> CountByIpAsync(byte[] ipKeyHash, DateTimeOffset since, CancellationToken ct = default);

    Task<int> CountByAccountAsync(byte[] accountKeyHash, DateTimeOffset since, CancellationToken ct = default);

    Task<int> DeleteByAccountAsync(byte[] accountKeyHash, CancellationToken ct = default);

    Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, int batchSize, CancellationToken ct = default);
}
