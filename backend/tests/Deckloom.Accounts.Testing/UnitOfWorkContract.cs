using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Testing;

// T-140 (retro item 1; regressao de 96e0919): contrato de IUnitOfWork escrito como os casos de uso chamam a porta, isto e,
// enfileirando dentro de ExecuteAsync e SEM chamar CommitAsync. Roda contra InMemoryUnitOfWork e contra EfUnitOfWork.
public abstract class UnitOfWorkContract : IAsyncDisposable
{
    private const string Terms = "2026-01";
    private const string Stored =
        "v1$pbkdf2-sha256$600000$c2FsdHNhbHRzYWx0c2FsdA==$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g=";
    private const string AvatarKey = "key-a-0000000000000000000";

    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Image = [0x52, 0x49, 0x46, 0x46, 0x01, 0x02, 0x03];

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

    private static Player NewPlayer(string email = "ana@example.com") =>
        Player.RegisterWithPassword(
            Email.Create(email).Value,
            PasswordHash.Parse(Stored).Value,
            DisplayName.Create("Ana Souza").Value,
            Terms,
            Terms,
            true,
            true,
            Now,
            "preset-1").Value;

    private async Task<bool> IsPersisted(PlayerId id)
    {
        await using var reader = Fixture.CreateScope();
        return await reader.Players.GetById(id) is not null;
    }

    [Fact]
    public async Task UoW_contract_ExecuteAsync_saves_what_the_block_only_queued_without_calling_CommitAsync()
    {
        await using var scope = Fixture.CreateScope();
        var player = NewPlayer();

        await scope.UnitOfWork.ExecuteAsync(ct => scope.Players.Add(player, ct));

        Assert.True(await IsPersisted(player.Id));
    }

    [Fact]
    public async Task UoW_contract_ExecuteAsync_of_T_returns_the_block_value_and_saves_the_same_way()
    {
        await using var scope = Fixture.CreateScope();
        var player = NewPlayer();

        var result = await scope.UnitOfWork.ExecuteAsync(async ct =>
        {
            await scope.Players.Add(player, ct);
            return 42;
        });

        Assert.Equal(42, result);
        Assert.True(await IsPersisted(player.Id));
    }

    [Fact]
    public async Task UoW_contract_an_exception_in_the_block_saves_nothing_and_propagates()
    {
        await using var scope = Fixture.CreateScope();
        var player = NewPlayer();
        var failure = new InvalidOperationException("boom");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => scope.UnitOfWork.ExecuteAsync(async ct =>
        {
            await scope.Players.Add(player, ct);
            throw failure;
        }));

        Assert.Same(failure, thrown);
        Assert.False(await IsPersisted(player.Id));
    }

    [Fact]
    public async Task UoW_contract_a_cancelled_token_before_the_block_saves_nothing()
    {
        await using var scope = Fixture.CreateScope();
        var player = NewPlayer();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var blockCalled = false;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.UnitOfWork.ExecuteAsync(
            ct =>
            {
                blockCalled = true;
                return scope.Players.Add(player, ct);
            },
            cts.Token));

        Assert.False(blockCalled);
        Assert.False(await IsPersisted(player.Id));
    }

    [Fact]
    public async Task UoW_contract_IsTransactionOpen_is_true_inside_the_block_and_false_outside()
    {
        await using var scope = Fixture.CreateScope();
        var before = scope.IsTransactionOpen;
        var inside = false;

        await scope.UnitOfWork.ExecuteAsync(ct =>
        {
            inside = scope.IsTransactionOpen;
            return Task.CompletedTask;
        });

        Assert.False(before);
        Assert.True(inside);
        Assert.False(scope.IsTransactionOpen);
    }

    [Fact]
    public async Task UoW_contract_a_failed_execution_does_not_leak_into_the_next_one()
    {
        await using var scope = Fixture.CreateScope();
        var lost = NewPlayer("lost@example.com");
        var kept = NewPlayer("kept@example.com");

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.UnitOfWork.ExecuteAsync(async ct =>
        {
            await scope.Players.Add(lost, ct);
            throw new InvalidOperationException("boom");
        }));
        await scope.UnitOfWork.ExecuteAsync(ct => scope.Players.Add(kept, ct));

        Assert.False(await IsPersisted(lost.Id));
        Assert.True(await IsPersisted(kept.Id));
        Assert.False(scope.IsTransactionOpen);
    }

    [Fact]
    public async Task UoW_contract_nested_execution_throws_InvalidOperationException()
    {
        await using var scope = Fixture.CreateScope();

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.UnitOfWork.ExecuteAsync(
            ct => scope.UnitOfWork.ExecuteAsync(_ => Task.CompletedTask, ct)));
    }

    [Fact]
    public async Task UoW_contract_two_executions_in_a_row_each_save_their_own_changes()
    {
        await using var scope = Fixture.CreateScope();
        var first = NewPlayer("first@example.com");
        var second = NewPlayer("second@example.com");

        await scope.UnitOfWork.ExecuteAsync(ct => scope.Players.Add(first, ct));
        await scope.UnitOfWork.ExecuteAsync(ct => scope.Players.Add(second, ct));

        Assert.True(await IsPersisted(first.Id));
        Assert.True(await IsPersisted(second.Id));
    }

    [Fact]
    public async Task UoW_contract_commit_participants_such_as_avatar_storage_are_confirmed_with_ExecuteAsync()
    {
        await using var scope = Fixture.CreateScope();
        var player = NewPlayer();
        player.SetCustomAvatar(AvatarKey);

        await scope.UnitOfWork.ExecuteAsync(async ct =>
        {
            await scope.Players.Add(player, ct);
            await scope.Avatars.SaveAsync(AvatarKey, Image, ct);
        });

        await using var reader = Fixture.CreateScope();
        Assert.Equal(Image, await reader.Avatars.ReadAsync(AvatarKey));
    }

    [Fact]
    public async Task UoW_contract_commit_participants_are_discarded_when_the_block_fails()
    {
        await using var scope = Fixture.CreateScope();
        var player = NewPlayer();
        player.SetCustomAvatar(AvatarKey);

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.UnitOfWork.ExecuteAsync(async ct =>
        {
            await scope.Players.Add(player, ct);
            await scope.Avatars.SaveAsync(AvatarKey, Image, ct);
            throw new InvalidOperationException("boom");
        }));

        await using var reader = Fixture.CreateScope();
        Assert.Null(await reader.Avatars.ReadAsync(AvatarKey));
    }

    // T-166 (R-50): o EfUnitOfWork repassa o ct ao SaveChangesAsync/transaction.CommitAsync; o fake tem de observa-lo igual.
    [Fact]
    public async Task UoW_contract_R_50_CommitAsync_with_a_cancelled_token_throws_and_saves_nothing()
    {
        await using var scope = Fixture.CreateScope();
        var player = NewPlayer();
        await scope.Players.Add(player);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.UnitOfWork.CommitAsync(cts.Token));

        Assert.False(await IsPersisted(player.Id));
    }

    [Fact]
    public async Task UoW_contract_R_50_ExecuteAsync_cancelled_after_the_work_and_before_the_commit_throws_and_saves_nothing()
    {
        await using var scope = Fixture.CreateScope();
        var player = NewPlayer();
        player.SetCustomAvatar(AvatarKey);
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.UnitOfWork.ExecuteAsync(
            async ct =>
            {
                await scope.Players.Add(player, CancellationToken.None);
                await scope.Avatars.SaveAsync(AvatarKey, Image, CancellationToken.None);
                await cts.CancelAsync();
            },
            cts.Token));

        Assert.False(await IsPersisted(player.Id));
        await using var reader = Fixture.CreateScope();
        Assert.Null(await reader.Avatars.ReadAsync(AvatarKey));
        Assert.False(scope.IsTransactionOpen);
    }

    [Fact]
    public async Task UoW_contract_R_50_ExecuteAsync_of_T_cancelled_after_the_work_throws_and_saves_nothing()
    {
        await using var scope = Fixture.CreateScope();
        var player = NewPlayer();
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.UnitOfWork.ExecuteAsync(
            async ct =>
            {
                await scope.Players.Add(player, CancellationToken.None);
                await cts.CancelAsync();
                return 42;
            },
            cts.Token));

        Assert.False(await IsPersisted(player.Id));
    }
}
