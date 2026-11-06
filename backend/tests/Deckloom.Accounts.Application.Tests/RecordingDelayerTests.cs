using System.Diagnostics;
using Deckloom.Accounts.Testing;

namespace Deckloom.Accounts.Application.Tests;

// T-025 (REQ-032): registra os atrasos, nao espera, respeita token ja cancelado.
public class RecordingDelayerTests
{
    [Fact]
    public async Task Records_each_delay_in_order()
    {
        var delayer = new RecordingDelayer();

        await delayer.DelayAsync(TimeSpan.FromSeconds(1));
        await delayer.DelayAsync(TimeSpan.FromSeconds(4));

        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4)], delayer.Delays);
    }

    [Fact]
    public async Task Does_not_actually_wait()
    {
        var delayer = new RecordingDelayer();
        var watch = Stopwatch.StartNew();

        var task = delayer.DelayAsync(TimeSpan.FromHours(1));
        await task;

        Assert.True(task.IsCompletedSuccessfully);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Throws_when_the_token_is_already_cancelled()
    {
        var delayer = new RecordingDelayer();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => delayer.DelayAsync(TimeSpan.FromSeconds(1), cts.Token));
    }

    [Fact]
    public void Starts_with_no_delays()
    {
        Assert.Empty(new RecordingDelayer().Delays);
    }
}
