using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;

namespace Deckloom.Accounts.Application.Tests;

// T-137: a sonda dos dubles enxerga a transacao do InMemoryUnitOfWork (verdadeiro dentro de ExecuteAsync, falso fora)
// e nao registra nada sem Observe.
public class TransactionProbeTests
{
    private static readonly Password SomePassword = Password.Create("correct horse battery").Value;

    private readonly InMemoryUnitOfWork _uow = new();

    private async Task<(bool Inside, bool Outside)> SampleAsync(TransactionProbe probe, Func<Task> call)
    {
        probe.Observe(_uow);
        await call();
        await _uow.ExecuteAsync(async _ => await call());
        return (probe.Samples[1].TransactionOpen, probe.Samples[0].TransactionOpen);
    }

    [Fact]
    public async Task Delayer_records_the_transaction_state_of_each_delay()
    {
        var delayer = new RecordingDelayer();

        var (inside, outside) = await SampleAsync(
            delayer.TransactionProbe, () => delayer.DelayAsync(TimeSpan.FromSeconds(1)));

        Assert.True(inside);
        Assert.False(outside);
    }

    [Fact]
    public async Task Hasher_records_the_transaction_state_of_hash_and_verify()
    {
        var hasher = new FakePasswordHasher(600_000);
        var stored = (await hasher.HashAsync(SomePassword)).ToStoredString();
        hasher.TransactionProbe.Observe(_uow);

        await hasher.HashAsync(SomePassword);
        await hasher.VerifyAsync(SomePassword, PasswordHash.Parse(stored).Value);
        await _uow.ExecuteAsync(async ct =>
        {
            await hasher.HashAsync(SomePassword, ct);
            await hasher.VerifyAsync(SomePassword, PasswordHash.Parse(stored).Value, ct);
        });

        Assert.Equal(
            [("Hash", false), ("Verify", false), ("Hash", true), ("Verify", true)],
            hasher.TransactionProbe.Samples.Select(s => (s.Call, s.TransactionOpen)));
    }

    [Fact]
    public async Task Google_verifier_records_the_transaction_state_of_the_exchange()
    {
        var google = new FakeGoogleIdentityVerifier();
        google.Cancel();

        var (inside, outside) = await SampleAsync(
            google.TransactionProbe, () => google.CompleteAsync("code", "verifier", "nonce"));

        Assert.True(inside);
        Assert.False(outside);
    }

    [Fact]
    public async Task Notifier_records_the_transaction_state_of_each_email()
    {
        var notifier = new RecordingPasswordResetNotifier();

        var (inside, outside) = await SampleAsync(
            notifier.TransactionProbe, () => notifier.NotifyAsync(PlayerFixtures.AnEmail(), "https://x.test/r#token=t"));

        Assert.True(inside);
        Assert.False(outside);
    }

    [Fact]
    public async Task Image_processor_records_the_transaction_state_of_each_processing()
    {
        var processor = new FakeAvatarImageProcessor();

        var (inside, outside) = await SampleAsync(
            processor.TransactionProbe, () => processor.ProcessAsync([1, 2, 3]));

        Assert.True(inside);
        Assert.False(outside);
    }

    [Fact]
    public async Task Attempt_store_records_the_transaction_state_of_the_throttle_reads()
    {
        var store = new InMemoryAccessAttemptStore(_uow);
        var key = new byte[] { 1 };

        var (inside, outside) = await SampleAsync(
            store.TransactionProbe, () => store.CountByIpAsync(key, DateTimeOffset.UnixEpoch));

        Assert.True(inside);
        Assert.False(outside);
    }

    [Fact]
    public async Task Probe_records_nothing_until_it_observes_a_unit_of_work()
    {
        var delayer = new RecordingDelayer();

        await delayer.DelayAsync(TimeSpan.FromSeconds(1));

        Assert.Empty(delayer.TransactionProbe.Samples);
    }
}
