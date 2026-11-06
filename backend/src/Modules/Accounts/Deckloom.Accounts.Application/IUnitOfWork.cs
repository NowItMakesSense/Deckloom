namespace Deckloom.Accounts.Application;

public interface IUnitOfWork
{
    Task CommitAsync(CancellationToken ct = default);

    Task ExecuteAsync(Func<CancellationToken, Task> work, CancellationToken ct = default);

    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default);
}
