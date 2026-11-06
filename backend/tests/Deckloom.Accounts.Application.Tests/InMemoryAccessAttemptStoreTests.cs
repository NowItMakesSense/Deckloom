using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;
using static Deckloom.Accounts.Application.Tests.PlayerFixtures;

namespace Deckloom.Accounts.Application.Tests;

// T-027 (REQ-032, REQ-041, REQ-047): fake de IAccessAttemptStore.
// Forma (design.md, "Forma das portas de estado"): chaves byte[] comparadas por conteudo; now e parametro
// (since/cutoff); janela inclusiva no inicio (>= since); retencao apaga so < cutoff; RecordAsync, contagens e
// DeleteOlderThanAsync sao imediatos; DeleteByAccountAsync e pendente ate o commit (exclusao transacional).
public class InMemoryAccessAttemptStoreTests
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly InMemoryUnitOfWork _uow = new();
    private readonly InMemoryAccessAttemptStore _store;

    public InMemoryAccessAttemptStoreTests() => _store = new InMemoryAccessAttemptStore(_uow);

    private static byte[] Key(byte seed) => Enumerable.Repeat(seed, 32).ToArray();

    private static AccessAttempt Attempt(
        byte ip,
        byte? account,
        DateTimeOffset at,
        AccessAction action = AccessAction.Login) =>
        new(action, Key(ip), account is null ? null : Key(account.Value), at);

    [Fact]
    public async Task SEC01_RecordAsync_returns_a_distinct_id_per_event()
    {
        var first = await _store.RecordAsync(Attempt(1, 9, Now));
        var second = await _store.RecordAsync(Attempt(1, 9, Now));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task SEC01_DeleteByIdAsync_deletes_only_that_event_at_once_and_reports_the_count()
    {
        var first = await _store.RecordAsync(Attempt(1, 9, Now));
        await _store.RecordAsync(Attempt(1, 9, Now));

        Assert.Equal(1, await _store.DeleteByIdAsync(first));
        Assert.Equal(0, await _store.DeleteByIdAsync(first));
        Assert.Equal(1, await _store.CountByAccountAsync(Key(9), Now - Window));
    }

    [Fact]
    public async Task SEC01_concurrent_RecordAsync_calls_lose_no_event_and_repeat_no_id()
    {
        var ids = await Task.WhenAll(Enumerable.Range(0, 200).Select(_ => Task.Run(() => _store.RecordAsync(Attempt(1, 9, Now)))));

        Assert.Equal(200, ids.Distinct().Count());
        Assert.Equal(200, await _store.CountByAccountAsync(Key(9), Now - Window));
    }

    [Fact]
    public async Task REQ032_CountByIp_counts_events_of_that_ip_key_inside_the_window()
    {
        await _store.RecordAsync(Attempt(1, 9, Now.AddMinutes(-1)));
        await _store.RecordAsync(Attempt(1, 8, Now.AddMinutes(-5)));
        await _store.RecordAsync(Attempt(2, 9, Now.AddMinutes(-1)));

        Assert.Equal(2, await _store.CountByIpAsync(Key(1), Now - Window));
        Assert.Equal(1, await _store.CountByIpAsync(Key(2), Now - Window));
        Assert.Equal(0, await _store.CountByIpAsync(Key(3), Now - Window));
    }

    [Fact]
    public async Task REQ032_CountByAccount_counts_events_of_that_account_key_inside_the_window()
    {
        await _store.RecordAsync(Attempt(1, 9, Now.AddMinutes(-1)));
        await _store.RecordAsync(Attempt(2, 9, Now.AddMinutes(-2)));
        await _store.RecordAsync(Attempt(1, 8, Now.AddMinutes(-2)));

        Assert.Equal(2, await _store.CountByAccountAsync(Key(9), Now - Window));
        Assert.Equal(1, await _store.CountByAccountAsync(Key(8), Now - Window));
    }

    [Fact]
    public async Task REQ032_Window_start_is_inclusive_and_older_events_do_not_count()
    {
        await _store.RecordAsync(Attempt(1, 9, Now - Window));
        await _store.RecordAsync(Attempt(1, 9, Now - Window - TimeSpan.FromSeconds(1)));
        await _store.RecordAsync(Attempt(1, 9, Now - Window + TimeSpan.FromSeconds(1)));

        Assert.Equal(2, await _store.CountByIpAsync(Key(1), Now - Window));
        Assert.Equal(2, await _store.CountByAccountAsync(Key(9), Now - Window));
    }

    [Fact]
    public async Task REQ032_Keys_are_compared_by_content_not_by_reference()
    {
        await _store.RecordAsync(Attempt(1, 9, Now));

        Assert.Equal(1, await _store.CountByIpAsync(Key(1), Now - Window));
        Assert.Equal(1, await _store.CountByAccountAsync(Key(9), Now - Window));
    }

    [Fact]
    public async Task REQ041_Registration_event_has_only_the_ip_key_and_counts_for_the_ip_of_every_action()
    {
        await _store.RecordAsync(Attempt(1, null, Now.AddMinutes(-1), AccessAction.Registration));
        await _store.RecordAsync(Attempt(1, 9, Now.AddMinutes(-1), AccessAction.Login));
        await _store.RecordAsync(Attempt(1, 9, Now.AddMinutes(-1), AccessAction.PasswordResetRequest));

        Assert.Equal(3, await _store.CountByIpAsync(Key(1), Now - Window));
        Assert.Equal(2, await _store.CountByAccountAsync(Key(9), Now - Window));
    }

    [Fact]
    public async Task REQ032_RecordAsync_is_immediate_and_survives_a_discarded_block()
    {
        // T-144: gravar de dentro do bloco agora e proibido; a gravacao imediata vem antes dele.
        await _store.RecordAsync(Attempt(1, 9, Now));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _uow.ExecuteAsync(ct => throw new InvalidOperationException()));

        Assert.Equal(1, await _store.CountByIpAsync(Key(1), Now - Window));
    }

    [Fact]
    public async Task REQ047_DeleteByAccount_returns_the_count_and_removes_only_that_account_on_commit()
    {
        await _store.RecordAsync(Attempt(1, 9, Now));
        await _store.RecordAsync(Attempt(2, 9, Now));
        await _store.RecordAsync(Attempt(1, 8, Now));
        await _store.RecordAsync(Attempt(1, null, Now, AccessAction.Registration));

        var deleted = await _store.DeleteByAccountAsync(Key(9));
        await _uow.CommitAsync();

        Assert.Equal(2, deleted);
        Assert.Equal(0, await _store.CountByAccountAsync(Key(9), Now - Window));
        Assert.Equal(1, await _store.CountByAccountAsync(Key(8), Now - Window));
        Assert.Equal(2, await _store.CountByIpAsync(Key(1), Now - Window));
        Assert.Equal(0, await _store.CountByIpAsync(Key(2), Now - Window));
    }

    [Fact]
    public async Task REQ047_DeleteByAccount_does_not_apply_before_commit()
    {
        await _store.RecordAsync(Attempt(1, 9, Now));

        await _store.DeleteByAccountAsync(Key(9));

        Assert.Equal(1, await _store.CountByAccountAsync(Key(9), Now - Window));
    }

    [Fact]
    public async Task REQ047_DeleteByAccount_applies_when_ExecuteAsync_succeeds()
    {
        await _store.RecordAsync(Attempt(1, 9, Now));

        await _uow.ExecuteAsync(async ct => await _store.DeleteByAccountAsync(Key(9), ct));

        Assert.Equal(0, await _store.CountByAccountAsync(Key(9), Now - Window));
    }

    [Fact]
    public async Task REQ047_DeleteByAccount_is_undone_when_the_block_throws()
    {
        await _store.RecordAsync(Attempt(1, 9, Now));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _uow.ExecuteAsync(async ct =>
            {
                await _store.DeleteByAccountAsync(Key(9), ct);
                throw new InvalidOperationException("purger failed");
            }));
        await _uow.CommitAsync();

        Assert.Equal(1, await _store.CountByAccountAsync(Key(9), Now - Window));
    }

    [Fact]
    public async Task REQ047_DeleteByAccount_of_an_unknown_key_returns_zero()
    {
        Assert.Equal(0, await _store.DeleteByAccountAsync(Key(7)));
    }

    [Fact]
    public async Task REQ047_DeleteOlderThan_removes_only_events_before_the_cutoff_at_once_and_counts_them()
    {
        var cutoff = Now.AddHours(-24);
        await _store.RecordAsync(Attempt(1, 9, cutoff.AddSeconds(-1)));
        await _store.RecordAsync(Attempt(1, 9, cutoff.AddDays(-3)));
        await _store.RecordAsync(Attempt(1, 9, cutoff));
        await _store.RecordAsync(Attempt(1, 9, Now));

        var deleted = await _store.DeleteOlderThanAsync(cutoff, 1000);

        Assert.Equal(2, deleted);
        Assert.Equal(2, await _store.CountByIpAsync(Key(1), cutoff.AddDays(-10)));
    }
}
