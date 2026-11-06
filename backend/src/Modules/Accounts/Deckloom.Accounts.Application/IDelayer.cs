namespace Deckloom.Accounts.Application;

public interface IDelayer
{
    Task DelayAsync(TimeSpan delay, CancellationToken ct = default);
}
