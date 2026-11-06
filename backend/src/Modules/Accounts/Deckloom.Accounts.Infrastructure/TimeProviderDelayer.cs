using Deckloom.Accounts.Application;

namespace Deckloom.Accounts.Infrastructure;

// Adapter de producao de IDelayer: espera medida pelo TimeProvider injetado (TimeProvider.System em producao).
public sealed class TimeProviderDelayer(TimeProvider time) : IDelayer
{
    public Task DelayAsync(TimeSpan delay, CancellationToken ct = default) =>
        delay <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(delay, time, ct);
}
