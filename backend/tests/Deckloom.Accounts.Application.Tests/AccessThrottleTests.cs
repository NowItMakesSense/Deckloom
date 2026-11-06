using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;
using Microsoft.Extensions.Time.Testing;
using static Deckloom.Accounts.Application.Tests.PlayerFixtures;

namespace Deckloom.Accounts.Application.Tests;

// T-029 (REQ-032, REQ-041): AccessThrottle aplica o atraso progressivo (ADR-0005) sem dormir de verdade.
// Forma esperada (a confirmar com o usuario, ver relatorio do test agent):
//   new AccessThrottle(IAccessAttemptStore, IKeyedHasher, IDelayer, ProgressiveDelayPolicy, TimeProvider)
//   Task<AccessAttemptKeys> BeforeAttemptAsync(AccessAction action, string? email, string ip, CancellationToken ct = default)
//     -> calcula HMAC de IP e (exceto Registration) do email normalizado, conta a janela de 15 min,
//        chama IDelayer com ProgressiveDelayPolicy.DelayFor(...) e devolve as chaves.
//   Task RecordAsync(AccessAttemptKeys keys, CancellationToken ct = default)  -> grava o evento (AttemptedAt = agora)
//   Task ClearAccountFailuresAsync(AccessAttemptKeys keys, CancellationToken ct = default) -> sucesso de login
//   record AccessAttemptKeys(AccessAction Action, byte[] IpKeyHash, byte[]? AccountKeyHash)
// Quem decide chamar RecordAsync e o caso de uso (falha de login/vinculo/exclusao; toda solicitacao de redefinicao;
// recusa de cadastro por email em uso). O throttle so guarda a chave de conta quando a acao nao e Registration.
public class AccessThrottleTests
{
    private const string Ip = "203.0.113.7";
    private const string OtherIp = "198.51.100.1";
    private const string Email = "ana@example.com";

    private readonly InMemoryUnitOfWork _uow = new();
    private readonly InMemoryAccessAttemptStore _store;
    private readonly FakeKeyedHasher _hasher = new();
    private readonly RecordingDelayer _delayer = new();
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly AccessThrottle _throttle;

    public AccessThrottleTests()
    {
        _store = new InMemoryAccessAttemptStore(_uow);
        _throttle = new AccessThrottle(
            _store, _hasher, _delayer, new ProgressiveDelayPolicy(new ProgressiveDelayOptions()), _clock);
    }

    private byte[] AccountKey(string email) => _hasher.Hash(email);

    private byte[] IpKey(string ip) => _hasher.Hash(ip);

    private async Task Seed(AccessAction action, string? email, string ip, int times, DateTimeOffset? at = null)
    {
        for (var i = 0; i < times; i++)
        {
            await _store.RecordAsync(
                new AccessAttempt(action, IpKey(ip), email is null ? null : AccountKey(email), at ?? Now.AddMinutes(-1)));
        }
    }

    private Task<int> CountAccount(string email) => _store.CountByAccountAsync(AccountKey(email), Now.AddHours(-1));

    private Task<int> CountIp(string ip) => _store.CountByIpAsync(IpKey(ip), Now.AddHours(-1));

    private void AssertNoDelay() => Assert.Empty(_delayer.Delays); // atraso zero: o throttle nao chama o IDelayer

    [Theory]
    [InlineData(2, 0)]
    [InlineData(3, 1)]
    [InlineData(4, 2)]
    [InlineData(5, 4)]
    [InlineData(6, 8)]
    [InlineData(7, 10)]
    public async Task REQ032_BeforeAttempt_delays_by_the_account_key_count_using_the_policy_without_sleeping(
        int failures, int expectedSeconds)
    {
        await Seed(AccessAction.Login, Email, OtherIp, failures);

        await _throttle.BeforeAttemptAsync(AccessAction.Login, Email, Ip);

        if (expectedSeconds == 0)
        {
            AssertNoDelay();
        }
        else
        {
            Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), Assert.Single(_delayer.Delays));
        }
    }

    [Fact]
    public async Task REQ032_BeforeAttempt_delays_by_the_ip_key_count_across_different_accounts()
    {
        for (var i = 0; i < 10; i++)
        {
            await Seed(AccessAction.Login, $"alvo{i}@example.com", Ip, 1);
        }

        await _throttle.BeforeAttemptAsync(AccessAction.Login, "outra@example.com", Ip);

        Assert.Equal(TimeSpan.FromSeconds(1), Assert.Single(_delayer.Delays));
    }

    [Fact]
    public async Task REQ032_BeforeAttempt_applies_the_greater_of_account_and_ip_delay()
    {
        await Seed(AccessAction.Login, Email, OtherIp, 4); // conta: 2 s
        for (var i = 0; i < 12; i++)
        {
            await Seed(AccessAction.Login, $"alvo{i}@example.com", Ip, 1); // IP: 4 s
        }

        await _throttle.BeforeAttemptAsync(AccessAction.Login, Email, Ip);

        Assert.Equal(TimeSpan.FromSeconds(4), Assert.Single(_delayer.Delays));
    }

    [Fact]
    public async Task REQ032_BeforeAttempt_counts_only_events_inside_the_fifteen_minute_window()
    {
        await Seed(AccessAction.Login, Email, OtherIp, 3, Now.AddMinutes(-16));
        await Seed(AccessAction.Login, Email, OtherIp, 2, Now.AddMinutes(-14));

        await _throttle.BeforeAttemptAsync(AccessAction.Login, Email, Ip);

        AssertNoDelay(); // 2 eventos na janela, abaixo do limiar 3
    }

    [Fact]
    public async Task REQ032_BeforeAttempt_never_blocks_even_after_many_failures_and_caps_at_ten_seconds()
    {
        await Seed(AccessAction.Login, Email, Ip, 25); // abaixo do orcamento horario (30): so o atraso vale

        var keys = await _throttle.BeforeAttemptAsync(AccessAction.Login, Email, Ip);

        Assert.False(keys.BudgetExceeded);
        Assert.Equal(TimeSpan.FromSeconds(10), Assert.Single(_delayer.Delays));
    }

    [Fact]
    public async Task REQ032_legitimate_player_on_another_ip_enters_without_the_delay_of_an_attacking_ip()
    {
        for (var i = 0; i < 14; i++)
        {
            await Seed(AccessAction.Login, $"alvo{i}@example.com", OtherIp, 1); // IP atacante no teto
        }

        await _throttle.BeforeAttemptAsync(AccessAction.Login, "legitimo@example.com", "192.0.2.50");

        AssertNoDelay();
    }

    [Fact]
    public async Task REQ032_BeforeAttempt_returns_hmac_keys_of_the_ip_and_the_normalized_email()
    {
        var keys = await _throttle.BeforeAttemptAsync(AccessAction.Login, "  Ana@Example.COM ", Ip);

        Assert.Equal(AccessAction.Login, keys.Action);
        Assert.Equal(IpKey(Ip), keys.IpKeyHash);
        Assert.Equal(AccountKey(Email), keys.AccountKeyHash);
    }

    [Fact]
    public async Task REQ032_BeforeAttempt_computes_the_account_key_for_an_email_that_has_no_account()
    {
        await _throttle.BeforeAttemptAsync(AccessAction.Login, "ninguem@example.com", Ip);

        Assert.Equal(1, await CountAccount("ninguem@example.com"));
    }

    [Fact]
    public async Task REQ032_BeforeAttempt_without_email_has_only_the_ip_key()
    {
        var keys = await _throttle.BeforeAttemptAsync(AccessAction.PasswordResetRequest, null, Ip);

        Assert.Null(keys.AccountKeyHash);
        Assert.Equal(IpKey(Ip), keys.IpKeyHash);
    }

    [Theory]
    [InlineData(AccessAction.Login)]
    [InlineData(AccessAction.GoogleLink)]
    [InlineData(AccessAction.AccountDeletion)]
    [InlineData(AccessAction.PasswordResetRequest)]
    public async Task SEC01_BeforeAttempt_reserves_the_provisional_event_under_both_the_account_and_the_ip_key(
        AccessAction action)
    {
        var keys = await _throttle.BeforeAttemptAsync(action, Email, Ip);

        Assert.NotNull(keys.ReservedAttemptId);
        Assert.Equal(1, await CountAccount(Email));
        Assert.Equal(1, await CountIp(Ip));
    }

    [Fact]
    public async Task SEC01_BeforeAttempt_stamps_the_provisional_event_with_the_current_time()
    {
        _clock.Advance(TimeSpan.FromMinutes(10));

        await _throttle.BeforeAttemptAsync(AccessAction.Login, Email, Ip);

        Assert.Equal(1, await _store.CountByAccountAsync(AccountKey(Email), Now.AddMinutes(10)));
        Assert.Equal(0, await _store.CountByAccountAsync(AccountKey(Email), Now.AddMinutes(10).AddTicks(1)));
    }

    [Fact]
    public async Task REQ041_Registration_refused_for_used_email_records_only_the_ip_key()
    {
        var keys = await _throttle.BeforeAttemptAsync(AccessAction.Registration, Email, Ip);

        await _throttle.RecordAsync(keys);

        Assert.Null(keys.AccountKeyHash);
        Assert.Equal(1, await CountIp(Ip));
        Assert.Equal(0, await CountAccount(Email));
    }

    [Fact]
    public async Task REQ041_Registration_delay_ignores_the_account_key_count_and_uses_only_the_ip_count()
    {
        await Seed(AccessAction.Login, Email, OtherIp, 7); // conta no teto, mas em outro IP

        await _throttle.BeforeAttemptAsync(AccessAction.Registration, Email, Ip);

        AssertNoDelay();
    }

    [Fact]
    public async Task REQ041_refused_registrations_count_toward_the_ip_delay_without_blocking()
    {
        await Seed(AccessAction.Registration, null, Ip, 10);

        await _throttle.BeforeAttemptAsync(AccessAction.Registration, "novo@example.com", Ip);

        Assert.Equal(TimeSpan.FromSeconds(1), Assert.Single(_delayer.Delays));
    }

    [Fact]
    public async Task REQ032_ClearAccountFailures_removes_only_the_account_events_after_commit()
    {
        await Seed(AccessAction.Login, Email, Ip, 3);
        await Seed(AccessAction.Login, "outra@example.com", Ip, 1);
        await Seed(AccessAction.Registration, null, Ip, 1);
        var keys = await _throttle.BeforeAttemptAsync(AccessAction.Login, Email, Ip);

        await _throttle.ClearAccountFailuresAsync(keys);
        await _uow.CommitAsync();

        Assert.Equal(0, await CountAccount(Email));
        Assert.Equal(1, await CountAccount("outra@example.com"));
        Assert.Equal(2, await CountIp(Ip));
    }

    [Fact]
    public async Task REQ032_ClearAccountFailures_inside_ExecuteAsync_is_applied_on_success_and_discarded_on_failure()
    {
        await Seed(AccessAction.Login, Email, Ip, 3);
        var keys = await _throttle.BeforeAttemptAsync(AccessAction.Login, Email, Ip);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _uow.ExecuteAsync(async ct =>
        {
            await _throttle.ClearAccountFailuresAsync(keys);
            throw new InvalidOperationException("falha na operacao");
        }));
        Assert.Equal(4, await CountAccount(Email)); // 3 semeadas + o provisorio

        await _uow.ExecuteAsync(ct => _throttle.ClearAccountFailuresAsync(keys));
        Assert.Equal(0, await CountAccount(Email));
    }

    [Fact]
    public async Task REQ032_BeforeAttempt_counts_every_action_in_a_single_counter_per_key()
    {
        await Seed(AccessAction.PasswordResetRequest, Email, OtherIp, 2);
        await Seed(AccessAction.GoogleLink, Email, OtherIp, 1);

        await _throttle.BeforeAttemptAsync(AccessAction.Login, Email, Ip);

        Assert.Equal(TimeSpan.FromSeconds(1), Assert.Single(_delayer.Delays));
    }

    [Fact]
    public async Task REQ032_ClearAccountFailures_without_account_key_is_a_no_op()
    {
        await Seed(AccessAction.Registration, null, Ip, 2);
        var keys = await _throttle.BeforeAttemptAsync(AccessAction.Registration, Email, Ip);

        await _throttle.ClearAccountFailuresAsync(keys);
        await _uow.CommitAsync();

        Assert.Equal(2, await CountIp(Ip));
    }

    [Fact]
    public async Task REQ032_BeforeAttempt_passes_the_cancellation_token_to_the_delayer()
    {
        await Seed(AccessAction.Login, Email, OtherIp, 3);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _throttle.BeforeAttemptAsync(AccessAction.Login, Email, Ip, cts.Token));
    }

    private AccessThrottle ThrottleWith(IDelayer delayer, int budget = 30) =>
        new(
            _store,
            _hasher,
            delayer,
            new ProgressiveDelayPolicy(new ProgressiveDelayOptions { AccountHourlyBudget = budget }),
            _clock);

    [Fact]
    public async Task SEC01_BeforeAttempt_writes_the_provisional_event_before_the_delay_and_counts_it()
    {
        await Seed(AccessAction.Login, Email, OtherIp, 3);
        var observing = new CountingDelayer(() => CountAccount(Email));

        await ThrottleWith(observing).BeforeAttemptAsync(AccessAction.Login, Email, Ip);

        Assert.Equal(4, observing.AccountCountAtDelay); // 3 anteriores + o proprio, ja gravado quando o atraso comeca
        Assert.Equal(TimeSpan.FromSeconds(1), Assert.Single(observing.Delays)); // tabela sobre contagem - 1
    }

    [Fact]
    public async Task SEC01_BeforeAttempt_for_registration_reserves_nothing()
    {
        var keys = await _throttle.BeforeAttemptAsync(AccessAction.Registration, Email, Ip);

        Assert.Null(keys.ReservedAttemptId);
        Assert.False(keys.BudgetExceeded);
        Assert.Equal(0, await CountIp(Ip));
    }

    [Fact]
    public async Task SEC01_attempt_number_thirty_is_admitted_and_number_thirty_one_is_over_the_budget()
    {
        await Seed(AccessAction.Login, Email, OtherIp, 29);

        var thirtieth = await _throttle.BeforeAttemptAsync(AccessAction.Login, Email, Ip);
        var thirtyFirst = await _throttle.BeforeAttemptAsync(AccessAction.Login, Email, Ip);

        Assert.False(thirtieth.BudgetExceeded);
        Assert.NotNull(thirtieth.ReservedAttemptId);
        Assert.True(thirtyFirst.BudgetExceeded);
        Assert.Null(thirtyFirst.ReservedAttemptId);
    }

    [Fact]
    public async Task SEC01_a_refused_attempt_deletes_its_own_event_so_it_does_not_extend_the_refusal()
    {
        await Seed(AccessAction.Login, Email, OtherIp, 30);

        for (var i = 0; i < 5; i++)
        {
            Assert.True((await _throttle.BeforeAttemptAsync(AccessAction.Login, Email, Ip)).BudgetExceeded);
        }

        Assert.Equal(30, await CountAccount(Email));
        Assert.Equal(0, await CountIp(Ip));
    }

    [Fact]
    public async Task SEC01_events_older_than_sixty_minutes_do_not_use_the_budget()
    {
        await Seed(AccessAction.Login, Email, OtherIp, 40, Now.AddMinutes(-61));
        await Seed(AccessAction.Login, Email, OtherIp, 29, Now.AddMinutes(-59));

        var keys = await _throttle.BeforeAttemptAsync(AccessAction.Login, Email, Ip);

        Assert.False(keys.BudgetExceeded);
    }

    [Fact]
    public async Task SEC01_the_budget_is_a_single_counter_for_every_action_of_the_account_key()
    {
        await Seed(AccessAction.PasswordResetRequest, Email, OtherIp, 15);
        await Seed(AccessAction.GoogleLink, Email, OtherIp, 15);

        var keys = await _throttle.BeforeAttemptAsync(AccessAction.AccountDeletion, Email, Ip);

        Assert.True(keys.BudgetExceeded);
    }

    [Fact]
    public async Task SEC01_the_budget_is_per_account_key_not_global()
    {
        await Seed(AccessAction.Login, Email, OtherIp, 40);

        var keys = await _throttle.BeforeAttemptAsync(AccessAction.Login, "outra@example.com", Ip);

        Assert.False(keys.BudgetExceeded);
    }

    [Fact]
    public async Task SEC01_the_budget_comes_from_the_configured_option()
    {
        await Seed(AccessAction.Login, Email, OtherIp, 5);

        var keys = await ThrottleWith(_delayer, budget: 5).BeforeAttemptAsync(AccessAction.Login, Email, Ip);

        Assert.True(keys.BudgetExceeded);
    }

    [Fact]
    public async Task SEC01_a_refused_attempt_still_waits_the_progressive_delay_capped_at_ten_seconds()
    {
        await Seed(AccessAction.Login, Email, OtherIp, 40);

        var keys = await _throttle.BeforeAttemptAsync(AccessAction.Login, Email, Ip);

        Assert.True(keys.BudgetExceeded);
        Assert.Equal(TimeSpan.FromSeconds(10), Assert.Single(_delayer.Delays));
    }

    [Fact]
    public async Task SEC01_ReleaseAsync_deletes_only_the_provisional_event_of_that_attempt()
    {
        await Seed(AccessAction.Login, Email, OtherIp, 2);
        var keys = await _throttle.BeforeAttemptAsync(AccessAction.Login, Email, Ip);

        await _throttle.ReleaseAsync(keys);

        Assert.Equal(2, await CountAccount(Email));
        Assert.Equal(0, await CountIp(Ip));
    }

    [Fact]
    public async Task SEC01_ReleaseAsync_without_a_reserved_id_is_a_no_op()
    {
        await Seed(AccessAction.Registration, null, Ip, 2);
        var registration = await _throttle.BeforeAttemptAsync(AccessAction.Registration, Email, Ip);
        await Seed(AccessAction.Login, Email, OtherIp, 30);
        var refused = await _throttle.BeforeAttemptAsync(AccessAction.Login, Email, Ip);

        await _throttle.ReleaseAsync(registration);
        await _throttle.ReleaseAsync(refused);

        Assert.Equal(2, await CountIp(Ip));
        Assert.Equal(30, await CountAccount(Email));
    }

    [Fact]
    public async Task SEC01_cancelling_during_the_delay_does_not_release_the_provisional_event()
    {
        await Seed(AccessAction.Login, Email, OtherIp, 3);
        using var cts = new CancellationTokenSource();
        var cancelling = new CancellingDelayer(cts);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ThrottleWith(cancelling).BeforeAttemptAsync(AccessAction.Login, Email, Ip, cts.Token));

        Assert.Equal(4, await CountAccount(Email)); // o provisorio fica e conta como falha
        Assert.Equal(1, await CountIp(Ip));
    }

    [Fact]
    public async Task SEC01_parallel_attempts_from_many_ips_on_one_account_never_admit_more_than_the_budget()
    {
        var delayer = new ConcurrentDelayer();
        var throttle = ThrottleWith(delayer);

        var results = await Task.WhenAll(Enumerable.Range(0, 100)
            .Select(i => Task.Run(() => throttle.BeforeAttemptAsync(AccessAction.Login, Email, $"198.51.100.{i}"))));

        Assert.True(results.Count(r => !r.BudgetExceeded) <= 30);
        Assert.True(await CountAccount(Email) <= 30);
        Assert.Equal(TimeSpan.FromSeconds(10), delayer.Snapshot().Max()); // a concorrencia eleva o atraso ate o teto
    }

    [Fact]
    public async Task SEC01_sequential_attempts_from_different_ips_raise_the_delay_and_stop_at_the_budget()
    {
        var delays = new List<TimeSpan>();
        var admitted = 0;
        for (var i = 0; i < 40; i++)
        {
            _delayer.Delays.Clear();
            var keys = await _throttle.BeforeAttemptAsync(AccessAction.Login, Email, $"198.51.100.{i}");
            delays.Add(_delayer.Delays.SingleOrDefault());
            admitted += keys.BudgetExceeded ? 0 : 1;
        }

        Assert.Equal(30, admitted);
        Assert.Equal([0, 0, 0, 1, 2, 4, 8, 10], delays.Take(8).Select(d => (int)d.TotalSeconds));
        Assert.All(delays.Skip(7), d => Assert.Equal(TimeSpan.FromSeconds(10), d));
    }

    private sealed class CountingDelayer(Func<Task<int>> accountCount) : IDelayer
    {
        public List<TimeSpan> Delays { get; } = [];

        public int AccountCountAtDelay { get; private set; }

        public async Task DelayAsync(TimeSpan delay, CancellationToken ct = default)
        {
            AccountCountAtDelay = await accountCount();
            Delays.Add(delay);
        }
    }

    private sealed class CancellingDelayer(CancellationTokenSource source) : IDelayer
    {
        public async Task DelayAsync(TimeSpan delay, CancellationToken ct = default)
        {
            await source.CancelAsync();
            ct.ThrowIfCancellationRequested();
        }
    }

    private sealed class ConcurrentDelayer : IDelayer
    {
        private readonly object _lock = new();
        private readonly List<TimeSpan> _delays = [];

        public IReadOnlyList<TimeSpan> Snapshot()
        {
            lock (_lock)
            {
                return [.. _delays];
            }
        }

        public Task DelayAsync(TimeSpan delay, CancellationToken ct = default)
        {
            lock (_lock)
            {
                _delays.Add(delay);
            }

            return Task.CompletedTask;
        }
    }
}
