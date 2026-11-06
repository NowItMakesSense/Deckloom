using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Testing;

// T-144 (retro item 1): contrato de IAccessAttemptStore. RecordAsync, DeleteByIdAsync e DeleteOlderThanAsync agem na hora,
// fora do IUnitOfWork (SEC-01, REQ-032): o evento gravado fica visivel a outro escopo sem ExecuteAsync e sem CommitAsync.
// Chamar RecordAsync/DeleteByIdAsync/DeleteOlderThanAsync com uma UoW aberta e uso proibido (InvalidOperationException, decisao do usuario).
// DeleteByAccountAsync so enfileira e as linhas somem no commit da UoW (REQ-047). Chaves sao sempre HMAC (bytes opacos).
// Cobertura da porta: RecordAsync, DeleteByIdAsync, CountByIpAsync, CountByAccountAsync, DeleteByAccountAsync,
// DeleteOlderThanAsync. (ReleaseAsync pertence ao AccessThrottle e chama DeleteByIdAsync.)
public abstract class AccessAttemptStoreContract : IAsyncDisposable
{
    private static readonly DateTimeOffset Now = AccountsContractFixture.ContractNow;
    private static readonly DateTimeOffset Since = Now.AddMinutes(-15);

    private AccountsContractFixture? _fixture;

    protected abstract AccountsContractFixture CreateFixture();

    private AccountsContractFixture Fixture => _fixture ??= CreateFixture();

    public async ValueTask DisposeAsync()
    {
        if (_fixture is not null)
        {
            await _fixture.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }

    private static byte[] Key(byte seed) => [.. Enumerable.Repeat(seed, 32)];

    private static AccessAttempt Attempt(byte ip, byte? account, DateTimeOffset at, AccessAction action = AccessAction.Login) =>
        new(action, Key(ip), account is null ? null : Key(account.Value), at);

    private async Task<long> Record(AccessAttempt attempt)
    {
        await using var scope = Fixture.CreateScope();
        return await scope.AccessAttempts.RecordAsync(attempt);
    }

    private async Task<int> CountIp(byte ip, DateTimeOffset since)
    {
        await using var reader = Fixture.CreateScope();
        return await reader.AccessAttempts.CountByIpAsync(Key(ip), since);
    }

    private async Task<int> CountAccount(byte account, DateTimeOffset since)
    {
        await using var reader = Fixture.CreateScope();
        return await reader.AccessAttempts.CountByAccountAsync(Key(account), since);
    }

    [Fact]
    public async Task Access_contract_RecordAsync_returns_an_id_and_the_event_is_visible_to_another_scope_without_a_commit()
    {
        var first = await Record(Attempt(1, 10, Now));
        var second = await Record(Attempt(1, 10, Now));

        Assert.NotEqual(first, second);
        Assert.Equal(2, await CountIp(1, Since));
        Assert.Equal(2, await CountAccount(10, Since));
    }

    // Decisao do usuario (T-144): as operacoes imediatas nao podem rodar com uma UoW aberta; falham rapido nos dois lados.
    [Fact]
    public async Task Access_contract_RecordAsync_inside_ExecuteAsync_throws_InvalidOperationException_and_records_nothing()
    {
        await using var scope = Fixture.CreateScope();

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.UnitOfWork.ExecuteAsync(
            ct => scope.AccessAttempts.RecordAsync(Attempt(1, 10, Now), ct)));

        Assert.Equal(0, await CountIp(1, Since));
        Assert.Equal(0, await CountAccount(10, Since));
    }

    [Fact]
    public async Task Access_contract_the_window_start_is_inclusive_and_the_event_itself_is_counted()
    {
        await Record(Attempt(1, 10, Since));
        await Record(Attempt(1, 10, Since.AddTicks(-1)));

        Assert.Equal(1, await CountIp(1, Since));
        Assert.Equal(1, await CountAccount(10, Since));
        Assert.Equal(2, await CountIp(1, Since.AddTicks(-1)));
        Assert.Equal(0, await CountIp(1, Since.AddTicks(1)));
    }

    [Fact]
    public async Task Access_contract_ip_and_account_counts_are_independent_and_by_key()
    {
        await Record(Attempt(1, 10, Now));
        await Record(Attempt(1, 11, Now));
        await Record(Attempt(2, 10, Now));
        await Record(Attempt(1, null, Now));

        Assert.Equal(3, await CountIp(1, Since));
        Assert.Equal(1, await CountIp(2, Since));
        Assert.Equal(2, await CountAccount(10, Since));
        Assert.Equal(1, await CountAccount(11, Since));
        Assert.Equal(0, await CountAccount(12, Since));
        Assert.Equal(0, await CountIp(3, Since));
    }

    [Fact]
    public async Task Access_contract_the_count_does_not_depend_on_the_action()
    {
        await Record(Attempt(1, 10, Now, AccessAction.Login));
        await Record(Attempt(1, 10, Now, AccessAction.PasswordResetRequest));
        await Record(Attempt(1, 10, Now, AccessAction.Registration));

        Assert.Equal(3, await CountIp(1, Since));
    }

    [Fact]
    public async Task Access_contract_DeleteByIdAsync_removes_only_the_event_of_that_id()
    {
        var keep = await Record(Attempt(1, 10, Now));
        var drop = await Record(Attempt(1, 10, Now));
        await using var scope = Fixture.CreateScope();

        var removed = await scope.AccessAttempts.DeleteByIdAsync(drop);
        var again = await scope.AccessAttempts.DeleteByIdAsync(drop);
        var unknown = await scope.AccessAttempts.DeleteByIdAsync(keep + 1000);

        Assert.Equal(1, removed);
        Assert.Equal(0, again);
        Assert.Equal(0, unknown);
        Assert.Equal(1, await CountAccount(10, Since));
    }

    [Fact]
    public async Task Access_contract_DeleteByIdAsync_inside_ExecuteAsync_throws_InvalidOperationException_and_deletes_nothing()
    {
        var id = await Record(Attempt(1, 10, Now));
        await using var scope = Fixture.CreateScope();

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.UnitOfWork.ExecuteAsync(
            ct => scope.AccessAttempts.DeleteByIdAsync(id, ct)));

        Assert.Equal(1, await CountAccount(10, Since));
    }

    [Fact]
    public async Task Access_contract_DeleteOlderThanAsync_inside_ExecuteAsync_throws_InvalidOperationException_and_deletes_nothing()
    {
        var cutoff = Now.AddDays(-1);
        await Record(Attempt(1, 10, cutoff.AddDays(-2)));
        await using var scope = Fixture.CreateScope();

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.UnitOfWork.ExecuteAsync(
            ct => scope.AccessAttempts.DeleteOlderThanAsync(cutoff, 10, ct)));

        Assert.Equal(1, await CountIp(1, Now.AddYears(-1)));
    }

    [Fact]
    public async Task Access_contract_DeleteByAccountAsync_returns_the_count_and_removes_the_rows_on_commit_only_for_that_account()
    {
        await Record(Attempt(1, 10, Now));
        await Record(Attempt(2, 10, Now));
        await Record(Attempt(1, 11, Now));
        await Record(Attempt(1, null, Now));
        await using var scope = Fixture.CreateScope();

        var count = await scope.UnitOfWork.ExecuteAsync(ct => scope.AccessAttempts.DeleteByAccountAsync(Key(10), ct));

        Assert.Equal(2, count);
        Assert.Equal(0, await CountAccount(10, Since));
        Assert.Equal(1, await CountAccount(11, Since));
        Assert.Equal(2, await CountIp(1, Since));
        Assert.Equal(0, await CountIp(2, Since));
    }

    [Fact]
    public async Task Access_contract_DeleteByAccountAsync_in_a_failed_block_removes_nothing()
    {
        await Record(Attempt(1, 10, Now));
        await using var scope = Fixture.CreateScope();

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.UnitOfWork.ExecuteAsync(async ct =>
        {
            await scope.AccessAttempts.DeleteByAccountAsync(Key(10), ct);
            throw new InvalidOperationException("boom");
        }));

        Assert.Equal(1, await CountAccount(10, Since));
    }

    [Fact]
    public async Task Access_contract_DeleteOlderThanAsync_respects_the_cutoff_boundary_and_the_batch_size_REQ_047()
    {
        var cutoff = Now.AddDays(-1);
        await Record(Attempt(1, 10, cutoff.AddTicks(-1)));
        await Record(Attempt(1, 10, cutoff.AddHours(-3)));
        await Record(Attempt(1, 10, cutoff.AddDays(-2)));
        await Record(Attempt(1, 10, cutoff));
        await Record(Attempt(1, 10, Now));
        await using var scope = Fixture.CreateScope();

        var first = await scope.AccessAttempts.DeleteOlderThanAsync(cutoff, 2);
        var second = await scope.AccessAttempts.DeleteOlderThanAsync(cutoff, 2);
        var third = await scope.AccessAttempts.DeleteOlderThanAsync(cutoff, 2);

        Assert.Equal([2, 1, 0], new[] { first, second, third });
        Assert.Equal(2, await CountIp(1, Now.AddYears(-1)));
    }
}
