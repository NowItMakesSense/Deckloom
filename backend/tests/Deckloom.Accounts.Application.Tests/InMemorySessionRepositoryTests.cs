using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;
using static Deckloom.Accounts.Application.Tests.PlayerFixtures;

namespace Deckloom.Accounts.Application.Tests;

// T-024 (REQ-002, REQ-027): fake em memoria de ISessionRepository.
// RevokeAllFor(PlayerId, SessionRevocationReason, now, ct) : Task<int> revoga toda sessao ainda nao revogada
// do Jogador (expiradas inclusive), devolve quantas foram revogadas agora; eventos ficam nas sessoes.
// T-027 (emenda): a revogacao e mudanca pendente; so tem efeito no CommitAsync/ExecuteAsync (testes de
// efeito abaixo confirmam com commit; os de pendencia/descarte estao em InMemoryUnitOfWorkTests).
public class InMemorySessionRepositoryTests
{
    private readonly InMemoryUnitOfWork _uow = new();
    private readonly InMemorySessionRepository _sessions;
    private readonly PlayerId _ana = new(Guid.NewGuid());
    private readonly PlayerId _bia = new(Guid.NewGuid());

    public InMemorySessionRepositoryTests() => _sessions = new InMemorySessionRepository(_uow);

    private async Task Persist(Session session)
    {
        await _sessions.Add(session);
        await _uow.CommitAsync();
    }

    [Fact]
    public async Task REQ027_FindByTokenHash_returns_the_session_with_that_hash_only()
    {
        var first = ASession(_ana, 1);
        var second = ASession(_ana, 2);
        await Persist(first);
        await Persist(second);

        Assert.Same(first, await _sessions.FindByTokenHash(AHash(1)));
        Assert.Same(second, await _sessions.FindByTokenHash(AHash(2)));
        Assert.Null(await _sessions.FindByTokenHash(AHash(3)));
    }

    [Fact]
    public async Task REQ027_FindByTokenHash_returns_revoked_and_expired_sessions()
    {
        var revoked = ASession(_ana, 1);
        revoked.Revoke(SessionRevocationReason.Logout, Now);
        var expired = ASession(_ana, 2, at: Now.AddDays(-1));
        await Persist(revoked);
        await Persist(expired);

        Assert.Same(revoked, await _sessions.FindByTokenHash(AHash(1)));
        Assert.Same(expired, await _sessions.FindByTokenHash(AHash(2)));
    }

    [Fact]
    public async Task REQ027_Save_of_a_session_never_added_is_a_noop()
    {
        var stranger = ASession(_ana, 7);

        var error = await Record.ExceptionAsync(() => _sessions.Save(stranger));
        await _uow.CommitAsync();

        Assert.Null(error);
        Assert.Null(await _sessions.FindByTokenHash(AHash(7)));
    }

    [Fact]
    public async Task REQ027_RevokeAllFor_revokes_every_session_of_the_player_and_counts_them()
    {
        var one = ASession(_ana, 1);
        var two = ASession(_ana, 2, rememberMe: true);
        var three = ASession(_ana, 3);
        foreach (var s in new[] { one, two, three })
        {
            await Persist(s);
        }

        var later = Now.AddMinutes(5);

        var revoked = await _sessions.RevokeAllFor(_ana, SessionRevocationReason.PasswordReset, later);
        await _uow.CommitAsync();

        Assert.Equal(3, revoked);
        Assert.All(new[] { one, two, three }, s => Assert.Equal(later, s.RevokedAt));
        Assert.All(new[] { one, two, three }, s => Assert.False(s.IsValid(later)));
    }

    [Fact]
    public async Task REQ027_RevokeAllFor_does_not_touch_sessions_of_other_players()
    {
        var anas = ASession(_ana, 1);
        var bias = ASession(_bia, 2);
        await Persist(anas);
        await Persist(bias);

        await _sessions.RevokeAllFor(_ana, SessionRevocationReason.PasswordReset, Now.AddMinutes(1));
        await _uow.CommitAsync();

        Assert.NotNull(anas.RevokedAt);
        Assert.Null(bias.RevokedAt);
    }

    [Fact]
    public async Task REQ027_RevokeAllFor_also_revokes_expired_sessions()
    {
        var expired = ASession(_ana, 1, at: Now.AddDays(-2));
        await Persist(expired);
        var later = Now.AddMinutes(1);

        var revoked = await _sessions.RevokeAllFor(_ana, SessionRevocationReason.PasswordReset, later);
        await _uow.CommitAsync();

        Assert.Equal(1, revoked);
        Assert.Equal(later, expired.RevokedAt);
    }

    [Fact]
    public async Task REQ027_RevokeAllFor_counts_only_sessions_revoked_now_and_keeps_the_original_RevokedAt()
    {
        var already = ASession(_ana, 1);
        already.Revoke(SessionRevocationReason.Logout, Now);
        var active = ASession(_ana, 2);
        await Persist(already);
        await Persist(active);
        var later = Now.AddMinutes(10);

        var revoked = await _sessions.RevokeAllFor(_ana, SessionRevocationReason.PasswordReset, later);
        await _uow.CommitAsync();

        Assert.Equal(1, revoked);
        Assert.Equal(Now, already.RevokedAt);
        Assert.Equal(later, active.RevokedAt);
    }

    [Fact]
    public async Task REQ027_RevokeAllFor_leaves_SessionRevoked_events_with_the_reason_on_each_session()
    {
        var session = ASession(_ana, 1);
        await Persist(session);
        session.ClearDomainEvents();

        await _sessions.RevokeAllFor(_ana, SessionRevocationReason.PasswordReset, Now.AddMinutes(1));
        await _uow.CommitAsync();

        var raised = Assert.IsType<SessionRevoked>(Assert.Single(session.DomainEvents));
        Assert.Equal(session.Id, raised.SessionId);
        Assert.Equal(_ana, raised.PlayerId);
        Assert.Equal(SessionRevocationReason.PasswordReset, raised.Reason);
    }

    [Fact]
    public async Task REQ027_RevokeAllFor_with_no_sessions_returns_zero()
    {
        Assert.Equal(0, await _sessions.RevokeAllFor(_ana, SessionRevocationReason.PasswordReset, Now));
    }

    [Fact]
    public async Task REQ027_RevokeAllFor_is_idempotent()
    {
        await Persist(ASession(_ana, 1));
        await _sessions.RevokeAllFor(_ana, SessionRevocationReason.PasswordReset, Now.AddMinutes(1));
        await _uow.CommitAsync();

        Assert.Equal(0, await _sessions.RevokeAllFor(_ana, SessionRevocationReason.PasswordReset, Now.AddMinutes(2)));
    }
}
