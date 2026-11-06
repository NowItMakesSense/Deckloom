using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;
using static Deckloom.Accounts.Application.Tests.PlayerFixtures;

namespace Deckloom.Accounts.Application.Tests;

// T-027 (habilitadora; REQ-027, REQ-047; ADR-0015): IUnitOfWork.ExecuteAsync(bloco) abre a "transacao",
// confirma ao fim e descarta se o bloco ou o commit lancar (relancando a mesma excecao).
// IsTransactionOpen (so no InMemoryUnitOfWork): verdadeiro apenas dentro do bloco.
// Rollback = nada do que estava pendente e aplicado; mutacoes em agregados ja carregados nao sao revertidas.
public class InMemoryUnitOfWorkTests
{
    private readonly InMemoryUnitOfWork _uow = new();
    private readonly InMemoryPlayerRepository _players;
    private readonly InMemorySessionRepository _sessions;
    private readonly PlayerId _ana = new(Guid.NewGuid());

    public InMemoryUnitOfWorkTests()
    {
        _players = new InMemoryPlayerRepository(_uow);
        _sessions = new InMemorySessionRepository(_uow);
    }

    [Fact]
    public void IsTransactionOpen_is_false_before_any_block()
    {
        Assert.False(_uow.IsTransactionOpen);
        Assert.Null(_uow.TransactionOpenDuringLastCommit);
    }

    [Fact]
    public async Task IsTransactionOpen_is_true_only_inside_the_block()
    {
        var inside = false;

        await _uow.ExecuteAsync(_ =>
        {
            inside = _uow.IsTransactionOpen;
            return Task.CompletedTask;
        });

        Assert.True(inside);
        Assert.False(_uow.IsTransactionOpen);
    }

    [Fact]
    public async Task IsTransactionOpen_is_false_after_the_block_throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _uow.ExecuteAsync(_ => throw new InvalidOperationException()));

        Assert.False(_uow.IsTransactionOpen);
    }

    [Fact]
    public async Task IsTransactionOpen_is_false_after_the_commit_fails_on_uniqueness()
    {
        await _players.Add(APasswordPlayer());
        await _uow.CommitAsync();

        await Assert.ThrowsAsync<UniqueConstraintViolationException>(() =>
            _uow.ExecuteAsync(async ct => await _players.Add(APasswordPlayer(), ct)));

        Assert.False(_uow.IsTransactionOpen);
    }

    [Fact]
    public async Task The_generic_overload_is_transactional_and_returns_the_block_result()
    {
        var inside = false;

        var result = await _uow.ExecuteAsync(_ =>
        {
            inside = _uow.IsTransactionOpen;
            return Task.FromResult(42);
        });

        Assert.Equal(42, result);
        Assert.True(inside);
        Assert.False(_uow.IsTransactionOpen);
    }

    [Fact]
    public async Task Commit_applied_by_ExecuteAsync_happens_with_the_transaction_open()
    {
        await _uow.ExecuteAsync(async ct => await _players.Add(APasswordPlayer(), ct));

        Assert.True(_uow.TransactionOpenDuringLastCommit);
    }

    [Fact]
    public async Task CommitAsync_outside_a_block_is_recorded_as_not_in_a_transaction()
    {
        await _uow.CommitAsync();

        Assert.False(_uow.TransactionOpenDuringLastCommit);
    }

    [Fact]
    public async Task ExecuteAsync_commits_pending_changes_when_the_block_succeeds()
    {
        var player = APasswordPlayer();

        await _uow.ExecuteAsync(async ct => await _players.Add(player, ct));

        Assert.Same(player, await _players.FindByEmail(player.Email));
    }

    [Fact]
    public async Task ExecuteAsync_discards_pending_add_when_the_block_throws_and_rethrows_the_same_exception()
    {
        var boom = new InvalidOperationException("boom");
        var player = APasswordPlayer();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _uow.ExecuteAsync(async ct =>
            {
                await _players.Add(player, ct);
                throw boom;
            }));

        Assert.Same(boom, thrown);
        Assert.Null(await _players.FindByEmail(player.Email));
    }

    [Fact]
    public async Task ExecuteAsync_discards_pending_remove_when_the_block_throws()
    {
        var player = APasswordPlayer();
        await _players.Add(player);
        await _uow.CommitAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _uow.ExecuteAsync(async ct =>
            {
                await _players.Remove(player, ct);
                throw new InvalidOperationException();
            }));
        await _uow.CommitAsync();

        Assert.Same(player, await _players.FindByEmail(player.Email));
    }

    [Fact]
    public async Task A_discarded_block_leaves_nothing_behind_for_the_next_commit()
    {
        var player = APasswordPlayer();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _uow.ExecuteAsync(async ct =>
            {
                await _players.Add(player, ct);
                throw new InvalidOperationException();
            }));

        await _uow.CommitAsync();

        Assert.Null(await _players.FindByEmail(player.Email));
    }

    [Fact]
    public async Task Uniqueness_violation_inside_ExecuteAsync_propagates_and_applies_nothing()
    {
        await _players.Add(APasswordPlayer());
        await _uow.CommitAsync();
        var session = ASession(_ana, 1);

        var error = await Assert.ThrowsAsync<UniqueConstraintViolationException>(() =>
            _uow.ExecuteAsync(async ct =>
            {
                await _sessions.Add(session, ct);
                await _players.Add(APasswordPlayer(), ct);
            }));

        Assert.Equal("ux_player_email", error.ConstraintName);
        Assert.Null(await _sessions.FindByTokenHash(AHash(1)));
    }

    // T-027 (emenda): RevokeAllFor e mudanca pendente; a T-049 exige rollback das sessoes quando o purger falha.

    [Fact]
    public async Task REQ027_RevokeAllFor_returns_the_count_at_once_but_does_not_revoke_before_commit()
    {
        var one = ASession(_ana, 1);
        var two = ASession(_ana, 2);
        await _sessions.Add(one);
        await _sessions.Add(two);
        await _uow.CommitAsync();
        one.ClearDomainEvents();
        two.ClearDomainEvents();

        var count = await _sessions.RevokeAllFor(_ana, SessionRevocationReason.PasswordReset, Now.AddMinutes(5));

        Assert.Equal(2, count);
        Assert.Null(one.RevokedAt);
        Assert.Null(two.RevokedAt);
        Assert.Empty(one.DomainEvents);
    }

    [Fact]
    public async Task REQ027_RevokeAllFor_takes_effect_with_the_given_now_on_CommitAsync()
    {
        var session = ASession(_ana, 1);
        await _sessions.Add(session);
        await _uow.CommitAsync();
        var later = Now.AddMinutes(5);
        await _sessions.RevokeAllFor(_ana, SessionRevocationReason.PasswordReset, later);

        await _uow.CommitAsync();

        Assert.Equal(later, session.RevokedAt);
        Assert.Contains(session.DomainEvents, e => e is SessionRevoked { Reason: SessionRevocationReason.PasswordReset });
    }

    [Fact]
    public async Task REQ027_RevokeAllFor_takes_effect_when_ExecuteAsync_succeeds()
    {
        var session = ASession(_ana, 1);
        await _sessions.Add(session);
        await _uow.CommitAsync();
        var later = Now.AddMinutes(5);

        await _uow.ExecuteAsync(async ct =>
            await _sessions.RevokeAllFor(_ana, SessionRevocationReason.PasswordReset, later, ct));

        Assert.Equal(later, session.RevokedAt);
    }

    [Fact]
    public async Task REQ027_RevokeAllFor_is_cancelled_when_the_block_throws()
    {
        var session = ASession(_ana, 1);
        await _sessions.Add(session);
        await _uow.CommitAsync();
        session.ClearDomainEvents();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _uow.ExecuteAsync(async ct =>
            {
                await _sessions.RevokeAllFor(_ana, SessionRevocationReason.PasswordReset, Now.AddMinutes(5), ct);
                throw new InvalidOperationException("purger failed");
            }));
        await _uow.CommitAsync();

        Assert.Null(session.RevokedAt);
        Assert.Empty(session.DomainEvents);
    }

    [Fact]
    public async Task REQ027_RevokeAllFor_is_cancelled_when_the_commit_fails_on_uniqueness()
    {
        await _players.Add(APasswordPlayer());
        await _uow.CommitAsync();
        var session = ASession(_ana, 1);
        await _sessions.Add(session);
        await _uow.CommitAsync();

        await Assert.ThrowsAsync<UniqueConstraintViolationException>(() =>
            _uow.ExecuteAsync(async ct =>
            {
                await _sessions.RevokeAllFor(_ana, SessionRevocationReason.PasswordReset, Now.AddMinutes(5), ct);
                await _players.Add(APasswordPlayer(), ct);
            }));

        Assert.Null(session.RevokedAt);
    }
}
