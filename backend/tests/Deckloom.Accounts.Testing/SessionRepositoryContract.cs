using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Testing;

// T-142 (retro item 1): contrato de ISessionRepository escrito como os casos de uso chamam a porta: gravar por
// IUnitOfWork.ExecuteAsync SEM CommitAsync e ler de volta em um escopo novo. Roda contra o fake e contra EfSessionRepository.
// Cobertura da porta: Add, FindById, FindByTokenHash, Save, RevokeAllFor, DeleteExpiredOrRevoked.
public abstract class SessionRepositoryContract : IAsyncDisposable
{
    private const string Terms = "2026-01";
    private const string Stored =
        "v1$pbkdf2-sha256$600000$c2FsdHNhbHRzYWx0c2FsdA==$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g=";

    private static readonly DateTimeOffset Now = AccountsContractFixture.ContractNow;
    private static readonly TimeSpan Inactivity = TimeSpan.FromMinutes(30);

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

    private static Player NewPlayer(string email) =>
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

    private static TokenHash Token(byte seed) => TokenHash.Create([.. Enumerable.Repeat(seed, TokenHash.Length)]).Value;

    private async Task<Player> PersistPlayer(string email = "ana@example.com")
    {
        var player = NewPlayer(email);
        await using var scope = Fixture.CreateScope();
        await scope.UnitOfWork.ExecuteAsync(ct => scope.Players.Add(player, ct));
        return player;
    }

    private async Task<Session> PersistSession(
        Player player,
        byte seed,
        DateTimeOffset startedAt,
        bool rememberMe = false)
    {
        var session = Session.Start(player.Id, Token(seed), rememberMe, startedAt);
        await using var scope = Fixture.CreateScope();
        await scope.UnitOfWork.ExecuteAsync(ct => scope.Sessions.Add(session, ct));
        return session;
    }

    // Carrega a sessao em um escopo proprio, aplica a mudanca e grava como os casos de uso: Save dentro de ExecuteAsync.
    private async Task Change(SessionId id, Action<Session> change)
    {
        await using var scope = Fixture.CreateScope();
        var session = await scope.Sessions.FindById(id);
        Assert.NotNull(session);
        await scope.UnitOfWork.ExecuteAsync(ct =>
        {
            change(session);
            return scope.Sessions.Save(session, ct);
        });
    }

    private async Task<Session?> Reload(SessionId id)
    {
        await using var reader = Fixture.CreateScope();
        return await reader.Sessions.FindById(id);
    }

    [Fact]
    public async Task Session_contract_Add_then_FindByTokenHash_and_FindById_in_a_new_scope_return_the_session()
    {
        var player = await PersistPlayer();
        var session = await PersistSession(player, 1, Now, rememberMe: true);
        await using var reader = Fixture.CreateScope();

        var byToken = await reader.Sessions.FindByTokenHash(Token(1));
        var byId = await reader.Sessions.FindById(session.Id);

        Assert.NotNull(byToken);
        Assert.NotNull(byId);
        Assert.Equal(session.Id, byToken.Id);
        Assert.Equal(session.Id, byId.Id);
        Assert.Equal(player.Id, byId.PlayerId);
        Assert.True(byId.RememberMe);
        Assert.Equal(session.CreatedAt, byId.CreatedAt);
        Assert.Equal(session.LastSeenAt, byId.LastSeenAt);
        Assert.Equal(session.ExpiresAt, byId.ExpiresAt);
        Assert.Null(byId.RevokedAt);
    }

    [Fact]
    public async Task Session_contract_an_unknown_token_or_id_is_absent_not_an_exception()
    {
        var player = await PersistPlayer();
        await PersistSession(player, 1, Now);
        await using var reader = Fixture.CreateScope();

        Assert.Null(await reader.Sessions.FindByTokenHash(Token(2)));
        Assert.Null(await reader.Sessions.FindById(new SessionId(Guid.NewGuid())));
    }

    [Fact]
    public async Task Session_contract_Touch_persists_LastSeenAt_and_slides_the_expiry_of_a_session_without_remember_me()
    {
        var player = await PersistPlayer();
        var session = await PersistSession(player, 1, Now);
        var later = Now.AddMinutes(10);

        await Change(session.Id, s => Assert.True(s.Touch(later)));

        var loaded = await Reload(session.Id);
        Assert.NotNull(loaded);
        Assert.Equal(later, loaded.LastSeenAt);
        Assert.Equal(later + Inactivity, loaded.ExpiresAt);
    }

    [Fact]
    public async Task Session_contract_Touch_persists_LastSeenAt_but_keeps_the_expiry_of_a_remember_me_session()
    {
        var player = await PersistPlayer();
        var session = await PersistSession(player, 1, Now, rememberMe: true);
        var later = Now.AddDays(1);

        await Change(session.Id, s => Assert.True(s.Touch(later)));

        var loaded = await Reload(session.Id);
        Assert.NotNull(loaded);
        Assert.Equal(later, loaded.LastSeenAt);
        Assert.Equal(session.ExpiresAt, loaded.ExpiresAt);
    }

    [Fact]
    public async Task Session_contract_a_revoked_session_stays_readable_but_invalid()
    {
        var player = await PersistPlayer();
        var session = await PersistSession(player, 1, Now);
        var revokedAt = Now.AddMinutes(5);

        await Change(session.Id, s => s.Revoke(SessionRevocationReason.Logout, revokedAt));

        var loaded = await Reload(session.Id);
        Assert.NotNull(loaded);
        Assert.Equal(revokedAt, loaded.RevokedAt);
        Assert.False(loaded.IsValid(revokedAt));
        Assert.Equal(session.Id, (await ReadByToken(1))?.Id);
    }

    [Fact]
    public async Task Session_contract_an_expired_session_stays_readable_but_invalid()
    {
        var player = await PersistPlayer();
        var session = await PersistSession(player, 1, Now);
        var afterExpiry = session.ExpiresAt;

        var loaded = await Reload(session.Id);

        Assert.NotNull(loaded);
        Assert.False(loaded.IsValid(afterExpiry));
        Assert.True(loaded.IsValid(afterExpiry.AddTicks(-1)));
    }

    [Fact]
    public async Task Session_contract_RevokeAllFor_revokes_every_active_session_of_the_player_and_only_theirs_REQ_030()
    {
        var ana = await PersistPlayer("ana@example.com");
        var bia = await PersistPlayer("bia@example.com");
        var first = await PersistSession(ana, 1, Now);
        var second = await PersistSession(ana, 2, Now, rememberMe: true);
        var others = await PersistSession(bia, 3, Now);
        var at = Now.AddMinutes(3);
        int count;
        await using (var scope = Fixture.CreateScope())
        {
            count = await scope.UnitOfWork.ExecuteAsync(
                ct => scope.Sessions.RevokeAllFor(ana.Id, SessionRevocationReason.PasswordReset, at, ct));
        }

        Assert.Equal(2, count);
        Assert.Equal(at, (await Reload(first.Id))?.RevokedAt);
        Assert.Equal(at, (await Reload(second.Id))?.RevokedAt);
        Assert.Null((await Reload(others.Id))?.RevokedAt);
    }

    [Fact]
    public async Task Session_contract_RevokeAllFor_keeps_the_first_revocation_and_counts_only_the_active_ones()
    {
        var player = await PersistPlayer();
        var early = await PersistSession(player, 1, Now);
        var active = await PersistSession(player, 2, Now);
        var firstRevocation = Now.AddMinutes(1);
        var later = Now.AddMinutes(9);
        await Change(early.Id, s => s.Revoke(SessionRevocationReason.Logout, firstRevocation));
        int count;
        await using (var scope = Fixture.CreateScope())
        {
            count = await scope.UnitOfWork.ExecuteAsync(
                ct => scope.Sessions.RevokeAllFor(player.Id, SessionRevocationReason.AccountDeletion, later, ct));
        }

        Assert.Equal(1, count);
        Assert.Equal(firstRevocation, (await Reload(early.Id))?.RevokedAt);
        Assert.Equal(later, (await Reload(active.Id))?.RevokedAt);
    }

    [Fact]
    public async Task Session_contract_RevokeAllFor_of_a_player_without_sessions_returns_zero()
    {
        var player = await PersistPlayer();
        await using var scope = Fixture.CreateScope();

        var count = await scope.UnitOfWork.ExecuteAsync(
            ct => scope.Sessions.RevokeAllFor(player.Id, SessionRevocationReason.Logout, Now, ct));

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Session_contract_RevokeAllFor_inside_a_failed_block_revokes_nothing()
    {
        var player = await PersistPlayer();
        var session = await PersistSession(player, 1, Now);
        await using var scope = Fixture.CreateScope();

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.UnitOfWork.ExecuteAsync(async ct =>
        {
            await scope.Sessions.RevokeAllFor(player.Id, SessionRevocationReason.Logout, Now, ct);
            throw new InvalidOperationException("boom");
        }));

        Assert.Null((await Reload(session.Id))?.RevokedAt);
    }

    [Fact]
    public async Task Session_contract_DeleteExpiredOrRevoked_respects_the_expires_at_boundary()
    {
        var player = await PersistPlayer();
        var atBoundary = await PersistSession(player, 1, Now - Inactivity);
        var justAfter = await PersistSession(player, 2, Now - Inactivity + TimeSpan.FromSeconds(1));
        await using var scope = Fixture.CreateScope();

        var deleted = await scope.Sessions.DeleteExpiredOrRevoked(Now, 10);

        Assert.Equal(1, deleted);
        Assert.Null(await Reload(atBoundary.Id));
        Assert.NotNull(await Reload(justAfter.Id));
    }

    [Fact]
    public async Task Session_contract_DeleteExpiredOrRevoked_removes_a_session_revoked_before_it_expired_R_18()
    {
        var player = await PersistPlayer();
        var revoked = await PersistSession(player, 1, Now, rememberMe: true);
        var active = await PersistSession(player, 2, Now, rememberMe: true);
        await Change(revoked.Id, s => s.Revoke(SessionRevocationReason.Logout, Now));
        await using var scope = Fixture.CreateScope();

        var deleted = await scope.Sessions.DeleteExpiredOrRevoked(Now, 10);

        Assert.Equal(1, deleted);
        Assert.Null(await Reload(revoked.Id));
        Assert.Null(await ReadByToken(1));
        Assert.NotNull(await Reload(active.Id));
    }

    [Fact]
    public async Task Session_contract_DeleteExpiredOrRevoked_respects_the_batch_size_and_is_idempotent()
    {
        var player = await PersistPlayer();
        var removable = new List<Session>();
        for (byte seed = 1; seed <= 3; seed++)
        {
            removable.Add(await PersistSession(player, seed, Now - TimeSpan.FromHours(1)));
        }

        var live = await PersistSession(player, 9, Now);
        await using var scope = Fixture.CreateScope();

        var first = await scope.Sessions.DeleteExpiredOrRevoked(Now, 2);
        var second = await scope.Sessions.DeleteExpiredOrRevoked(Now, 2);
        var third = await scope.Sessions.DeleteExpiredOrRevoked(Now, 2);

        Assert.Equal([2, 1, 0], new[] { first, second, third });
        foreach (var session in removable)
        {
            Assert.Null(await Reload(session.Id));
        }

        Assert.NotNull(await Reload(live.Id));
    }

    private async Task<Session?> ReadByToken(byte seed)
    {
        await using var reader = Fixture.CreateScope();
        return await reader.Sessions.FindByTokenHash(Token(seed));
    }
}
