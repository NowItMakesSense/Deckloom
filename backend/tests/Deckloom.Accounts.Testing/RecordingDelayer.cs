using Deckloom.Accounts.Application;

namespace Deckloom.Accounts.Testing;

public sealed class RecordingDelayer : IDelayer
{
    public List<TimeSpan> Delays { get; } = [];

    public TransactionProbe TransactionProbe { get; } = new();

    public Task DelayAsync(TimeSpan delay, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        TransactionProbe.Record("Delay");
        Delays.Add(delay);
        return Task.CompletedTask;
    }
}
