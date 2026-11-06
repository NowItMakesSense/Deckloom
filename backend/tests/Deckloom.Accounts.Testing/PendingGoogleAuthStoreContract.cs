using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Testing;

// T-143 (retro item 1): contrato de IPendingGoogleAuthStore escrito como os casos de uso chamam a porta: gravar e
// consumir por IUnitOfWork.ExecuteAsync SEM CommitAsync, lendo em um escopo novo. Roda contra o fake e contra
// EfPendingGoogleAuthStore. O "agora" vem do Clock do fixture (FakeTimeProvider).
// Cobertura da porta: AddAsync, FindAsync, ConsumeAsync, DeleteExpiredAsync.
public abstract class PendingGoogleAuthStoreContract : IAsyncDisposable
{
    private const string Terms = "2026-01";
    private const string Stored =
        "v1$pbkdf2-sha256$600000$c2FsdHNhbHRzYWx0c2FsdA==$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g=";

    private static readonly DateTimeOffset Now = AccountsContractFixture.ContractNow;

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

    private static TokenHash Token(byte seed) => TokenHash.Create([.. Enumerable.Repeat(seed, TokenHash.Length)]).Value;

    private static Email EmailOf(string value) => Email.Create(value).Value;

    private static PendingGoogleSignup NewSignup(byte seed, DateTimeOffset? at = null) =>
        PendingGoogleSignup.Create(Token(seed), $"sub-{seed}", EmailOf($"signup{seed}@example.com"), "Gui Lima", at ?? Now);

    private async Task<Player> PersistPlayer()
    {
        var player = Player.RegisterWithPassword(
            EmailOf("ana@example.com"),
            PasswordHash.Parse(Stored).Value,
            DisplayName.Create("Ana Souza").Value,
            Terms,
            Terms,
            true,
            true,
            Now,
            "preset-1").Value;
        await using var scope = Fixture.CreateScope();
        await scope.UnitOfWork.ExecuteAsync(ct => scope.Players.Add(player, ct));
        return player;
    }

    private async Task Persist(PendingGoogleAuth pending)
    {
        await using var scope = Fixture.CreateScope();
        await scope.UnitOfWork.ExecuteAsync(ct => scope.Pendings.AddAsync(pending, ct));
    }

    private async Task<PendingGoogleAuth?> Find(byte seed)
    {
        await using var reader = Fixture.CreateScope();
        return await reader.Pendings.FindAsync(Token(seed));
    }

    private async Task<PendingGoogleAuth?> Consume(byte seed)
    {
        await using var scope = Fixture.CreateScope();
        return await scope.UnitOfWork.ExecuteAsync(ct => scope.Pendings.ConsumeAsync(Token(seed), ct));
    }

    [Fact]
    public async Task Pending_contract_a_signup_is_saved_and_read_back_by_token_hash_in_a_new_scope()
    {
        var signup = NewSignup(1);
        await Persist(signup);

        var found = await Find(1);

        var loaded = Assert.IsType<PendingGoogleSignup>(found);
        Assert.Equal(signup.Id, loaded.Id);
        Assert.Equal(signup.GoogleSubject, loaded.GoogleSubject);
        Assert.Equal(signup.Email, loaded.Email);
        Assert.Equal("Gui Lima", loaded.SuggestedDisplayName);
        Assert.Equal(signup.CreatedAt, loaded.CreatedAt);
        Assert.Equal(signup.ExpiresAt, loaded.ExpiresAt);
    }

    [Fact]
    public async Task Pending_contract_a_link_is_saved_and_read_back_as_a_link_with_its_target_player()
    {
        var player = await PersistPlayer();
        var link = PendingGoogleLink.Create(Token(2), "sub-link", player.Email, player.Id, Now);
        await Persist(link);

        var found = await Find(2);

        var loaded = Assert.IsType<PendingGoogleLink>(found);
        Assert.Equal(link.Id, loaded.Id);
        Assert.Equal(player.Id, loaded.TargetPlayerId);
        Assert.Equal("sub-link", loaded.GoogleSubject);
    }

    [Fact]
    public async Task Pending_contract_an_unknown_token_is_absent_for_Find_and_Consume()
    {
        await Persist(NewSignup(1));
        await using var scope = Fixture.CreateScope();

        Assert.Null(await scope.Pendings.FindAsync(Token(9)));
        Assert.Null(await scope.UnitOfWork.ExecuteAsync(ct => scope.Pendings.ConsumeAsync(Token(9), ct)));
    }

    [Fact]
    public async Task Pending_contract_a_pending_added_in_a_failed_block_is_not_saved()
    {
        await using var scope = Fixture.CreateScope();

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.UnitOfWork.ExecuteAsync(async ct =>
        {
            await scope.Pendings.AddAsync(NewSignup(1), ct);
            throw new InvalidOperationException("boom");
        }));

        Assert.Null(await Find(1));
    }

    [Fact]
    public async Task Pending_contract_Consume_returns_the_pending_once_and_the_second_attempt_is_absent()
    {
        var signup = NewSignup(1);
        await Persist(signup);

        var first = await Consume(1);
        var second = await Consume(1);

        Assert.Equal(signup.Id, first?.Id);
        Assert.Null(second);
        Assert.Null(await Find(1));
    }

    [Fact]
    public async Task Pending_contract_two_scopes_that_both_found_the_pending_only_one_consumes_it_T_042()
    {
        await Persist(NewSignup(1));
        await using var a = Fixture.CreateScope();
        await using var b = Fixture.CreateScope();
        Assert.NotNull(await a.Pendings.FindAsync(Token(1)));
        Assert.NotNull(await b.Pendings.FindAsync(Token(1)));

        var fromA = await a.UnitOfWork.ExecuteAsync(ct => a.Pendings.ConsumeAsync(Token(1), ct));
        var fromB = await b.UnitOfWork.ExecuteAsync(ct => b.Pendings.ConsumeAsync(Token(1), ct));

        Assert.NotNull(fromA);
        Assert.Null(fromB);
    }

    [Fact]
    public async Task Pending_contract_a_consume_inside_a_failed_block_is_undone_and_the_pending_can_be_consumed_later()
    {
        await Persist(NewSignup(1));
        await using var scope = Fixture.CreateScope();

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.UnitOfWork.ExecuteAsync(async ct =>
        {
            Assert.NotNull(await scope.Pendings.ConsumeAsync(Token(1), ct));
            throw new InvalidOperationException("boom");
        }));

        Assert.NotNull(await Find(1));
        Assert.NotNull(await Consume(1));
    }

    [Fact]
    public async Task Pending_contract_a_pending_is_live_until_exactly_ten_minutes_and_absent_at_the_boundary()
    {
        var signup = NewSignup(1);
        await Persist(signup);
        Assert.Equal(Now + PendingGoogleAuth.Ttl, signup.ExpiresAt);

        Fixture.Clock.SetUtcNow(signup.ExpiresAt.AddTicks(-1));
        Assert.NotNull(await Find(1));

        Fixture.Clock.SetUtcNow(signup.ExpiresAt);
        Assert.Null(await Find(1));
    }

    [Fact]
    public async Task Pending_contract_Consume_of_an_expired_pending_is_absent()
    {
        var signup = NewSignup(1);
        await Persist(signup);
        Fixture.Clock.SetUtcNow(signup.ExpiresAt);

        Assert.Null(await Consume(1));
    }

    [Fact]
    public async Task Pending_contract_DeleteExpiredAsync_respects_the_boundary_and_the_batch_size()
    {
        foreach (var seed in new byte[] { 1, 2, 3 })
        {
            await Persist(NewSignup(seed));
        }

        var liveAt = Now.AddMinutes(5);
        await Persist(NewSignup(9, liveAt));
        Fixture.Clock.SetUtcNow(Now + PendingGoogleAuth.Ttl);
        await using var scope = Fixture.CreateScope();

        var first = await scope.Pendings.DeleteExpiredAsync(2);
        var second = await scope.Pendings.DeleteExpiredAsync(2);
        var third = await scope.Pendings.DeleteExpiredAsync(2);

        Assert.Equal([2, 1, 0], new[] { first, second, third });
        Assert.NotNull(await Find(9));
    }
}
