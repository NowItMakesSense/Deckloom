using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;
using Microsoft.Extensions.Time.Testing;
using static Deckloom.Accounts.Application.Tests.PlayerFixtures;

namespace Deckloom.Accounts.Application.Tests;

// T-027 (REQ-036, REQ-039): PendingGoogleAuth (Signup/Link) e fake de IPendingGoogleAuthStore.
// Forma (design.md, "Forma das portas de estado"): identificada por TokenHash; ExpiresAt = CreatedAt + 10 min
// calculado na factory; expirada quando agora >= ExpiresAt (relogio = TimeProvider do fake); Find le sem
// consumir (nulo se desconhecida/expirada); Consume remove na hora, sob lock, e devolve nulo se
// desconhecida/consumida/expirada; "erro nao consome" = o caso de uso so chama Consume apos validar tudo.
public class InMemoryPendingGoogleAuthStoreTests
{
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly InMemoryPendingGoogleAuthStore _store;

    public InMemoryPendingGoogleAuthStoreTests() => _store = new InMemoryPendingGoogleAuthStore(_clock, new InMemoryUnitOfWork());

    private static PendingGoogleSignup Signup(byte seed = 1, DateTimeOffset? at = null) =>
        PendingGoogleSignup.Create(AHash(seed), "sub-" + seed, AnEmail(), "Gui Google", at ?? Now);

    [Fact]
    public void REQ039_Signup_factory_sets_the_ten_minute_expiry_and_carries_the_google_data()
    {
        var pending = PendingGoogleSignup.Create(AHash(1), "sub-1", AnEmail("gui@example.com"), "Gui Google", Now);

        Assert.Equal(TimeSpan.FromMinutes(10), PendingGoogleAuth.Ttl);
        Assert.Equal(Now, pending.CreatedAt);
        Assert.Equal(Now.AddMinutes(10), pending.ExpiresAt);
        Assert.Equal(AHash(1), pending.TokenHash);
        Assert.Equal("sub-1", pending.GoogleSubject);
        Assert.Equal(AnEmail("gui@example.com"), pending.Email);
        Assert.Equal("Gui Google", pending.SuggestedDisplayName);
        Assert.NotEqual(Guid.Empty, pending.Id);
    }

    [Fact]
    public void REQ036_Link_factory_sets_the_ten_minute_expiry_and_the_target_player()
    {
        var target = new PlayerId(Guid.NewGuid());

        var pending = PendingGoogleLink.Create(AHash(2), "sub-2", AnEmail(), target, Now);

        Assert.Equal(Now.AddMinutes(10), pending.ExpiresAt);
        Assert.Equal(target, pending.TargetPlayerId);
        Assert.Equal("sub-2", pending.GoogleSubject);
        Assert.NotEqual(Guid.Empty, pending.Id);
    }

    [Fact]
    public async Task REQ039_Find_returns_the_added_pending_without_consuming_it()
    {
        var pending = Signup();
        await _store.AddAsync(pending);

        Assert.Same(pending, await _store.FindAsync(AHash(1)));
        Assert.Same(pending, await _store.FindAsync(AHash(1)));
    }

    [Fact]
    public async Task REQ039_Find_of_an_unknown_token_returns_null()
    {
        await _store.AddAsync(Signup(1));

        Assert.Null(await _store.FindAsync(AHash(2)));
    }

    [Fact]
    public async Task REQ039_Find_is_valid_until_the_last_instant_and_null_from_ExpiresAt_on()
    {
        await _store.AddAsync(Signup());

        _clock.Advance(TimeSpan.FromMinutes(10) - TimeSpan.FromMilliseconds(1));
        Assert.NotNull(await _store.FindAsync(AHash(1)));

        _clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Null(await _store.FindAsync(AHash(1)));
    }

    [Fact]
    public async Task REQ039_Consume_returns_the_pending_and_removes_it()
    {
        var pending = Signup();
        await _store.AddAsync(pending);

        Assert.Same(pending, await _store.ConsumeAsync(AHash(1)));
        Assert.Null(await _store.FindAsync(AHash(1)));
        Assert.Null(await _store.ConsumeAsync(AHash(1)));
    }

    [Fact]
    public async Task REQ039_Consume_of_an_unknown_token_returns_null_and_keeps_the_others()
    {
        var pending = Signup(1);
        await _store.AddAsync(pending);

        Assert.Null(await _store.ConsumeAsync(AHash(2)));
        Assert.Same(pending, await _store.FindAsync(AHash(1)));
    }

    [Fact]
    public async Task REQ039_Consume_of_an_expired_pending_returns_null_at_the_boundary_and_drops_it()
    {
        await _store.AddAsync(Signup());
        _clock.Advance(PendingGoogleAuth.Ttl);

        Assert.Null(await _store.ConsumeAsync(AHash(1)));
        Assert.Null(await _store.FindAsync(AHash(1)));
    }

    [Fact]
    public async Task REQ036_Link_pending_follows_the_same_find_and_consume_rules()
    {
        var link = PendingGoogleLink.Create(AHash(3), "sub-3", AnEmail(), new PlayerId(Guid.NewGuid()), Now);
        await _store.AddAsync(link);

        Assert.Same(link, await _store.FindAsync(AHash(3)));
        Assert.Same(link, await _store.ConsumeAsync(AHash(3)));
        Assert.Null(await _store.ConsumeAsync(AHash(3)));
    }

    [Fact]
    public async Task REQ039_Concurrent_consumes_of_the_same_token_have_exactly_one_winner()
    {
        const int contenders = 20;
        await _store.AddAsync(Signup());
        using var barrier = new Barrier(contenders);

        var tasks = Enumerable.Range(0, contenders).Select(_ => Task.Run(async () =>
        {
            barrier.SignalAndWait();
            return await _store.ConsumeAsync(AHash(1));
        })).ToArray();
        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(r => r is not null));
    }

    [Fact]
    public async Task REQ039_DeleteExpired_removes_only_expired_pendings_and_counts_them()
    {
        await _store.AddAsync(Signup(1, Now.AddMinutes(-20)));
        await _store.AddAsync(Signup(2, Now.AddMinutes(-10)));
        var alive = Signup(3, Now.AddMinutes(-9));
        await _store.AddAsync(alive);

        var deleted = await _store.DeleteExpiredAsync(1000);

        Assert.Equal(2, deleted);
        Assert.Same(alive, await _store.FindAsync(AHash(3)));
    }

    [Fact]
    public async Task REQ039_DeleteExpired_with_nothing_expired_returns_zero()
    {
        await _store.AddAsync(Signup());

        Assert.Equal(0, await _store.DeleteExpiredAsync(1000));
    }
}
