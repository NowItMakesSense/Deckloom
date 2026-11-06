using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;
using Microsoft.Extensions.Time.Testing;
using static Deckloom.Accounts.Application.Tests.PlayerFixtures;

namespace Deckloom.Accounts.Application.Tests;

// T-051 (REQ-047): job de limpeza de retencao. RED: o tipo e os metodos de porta abaixo ainda nao existem.
// Forma esperada (design.md linhas 1257 e 1399; convencao da T-044: job sem erro possivel nao devolve Result):
//   sealed class PurgeExpiredData(ISessionRepository, IPlayerRepository, IPendingGoogleAuthStore, IAccessAttemptStore, TimeProvider)
//       Task<PurgeExpiredDataSummary> ExecuteAsync(CancellationToken ct = default)
//   sealed record PurgeExpiredDataSummary(int Sessions, int ResetTokens, int PendingGoogleAuths, int AccessAttempts)
// Portas novas (o "agora" e parametro, como em RevokeAllFor e DeleteOlderThanAsync; design linha 325; REFACTOR da tarefa:
// um metodo por tipo de dado; o job apaga sessoes expiradas/revogadas e tokens expirados):
//   ISessionRepository.DeleteExpiredOrRevoked(DateTimeOffset now, int batchSize, CancellationToken ct = default) : Task<int>
//       apaga sessoes revogadas ou com now >= ExpiresAt (Session.IsValid e falso); devolve a contagem.
//   IPlayerRepository.DeleteExpiredResetTokens(DateTimeOffset now, CancellationToken ct = default) : Task<int>
//       zera PasswordResetToken com now >= ExpiresAt (mesma regra de Player.ResetPassword, 60:00 ja e expirado);
//       o token vive no agregado Player (tabela password_reset_token, 1:0..1), nao ha repositorio proprio.
// Existentes: IPendingGoogleAuthStore.DeleteExpiredAsync() (relogio do store) e IAccessAttemptStore.DeleteOlderThanAsync(cutoff),
// com cutoff = agora - 24 h (apaga so AttemptedAt < cutoff; exatamente 24 h e mantido, design linha 325).
// Os apagamentos sao imediatos nos fakes (uma instrucao por tipo, como DeleteOlderThanAsync); sem IUnitOfWork no caso de uso.
public class PurgeExpiredDataTests
{
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly InMemoryUnitOfWork _uow = new();
    private readonly InMemorySessionRepository _sessions;
    private readonly InMemoryPlayerRepository _players;
    private readonly InMemoryPendingGoogleAuthStore _pendings;
    private readonly InMemoryAccessAttemptStore _attempts;

    public PurgeExpiredDataTests()
    {
        _sessions = new InMemorySessionRepository(_uow);
        _players = new InMemoryPlayerRepository(_uow);
        _pendings = new InMemoryPendingGoogleAuthStore(_clock, _uow);
        _attempts = new InMemoryAccessAttemptStore(_uow);
    }

    private PurgeExpiredData UseCase() => new(_sessions, _players, _pendings, _attempts, _clock, 1000);

    private static byte[] Key(byte seed) => Enumerable.Repeat(seed, 32).ToArray();

    private static AccessAttempt Attempt(DateTimeOffset at) => new(AccessAction.Login, Key(1), Key(9), at);

    private Task<int> AttemptsKept() => _attempts.CountByIpAsync(Key(1), Now.AddYears(-1));

    private async Task<PlayerId> APlayerWithSessions(params Session[] sessions)
    {
        foreach (var session in sessions)
        {
            await _sessions.Add(session);
        }

        await _uow.CommitAsync();
        return sessions[0].PlayerId;
    }

    private async Task<Player> APlayerWithResetToken(string email, byte seed, DateTimeOffset issuedAt)
    {
        var player = APasswordPlayer(email);
        Assert.True(player.IssuePasswordReset(AHash(seed), issuedAt).IsSuccess);
        await _players.Add(player);
        await _uow.CommitAsync();
        return player;
    }

    [Fact]
    public async Task REQ047_access_events_older_than_24h_are_deleted_and_newer_ones_kept_with_exactly_24h_kept()
    {
        await _attempts.RecordAsync(Attempt(Now.AddHours(-24).AddSeconds(-1)));
        await _attempts.RecordAsync(Attempt(Now.AddDays(-3)));
        await _attempts.RecordAsync(Attempt(Now.AddHours(-24)));
        await _attempts.RecordAsync(Attempt(Now.AddHours(-1)));
        await _attempts.RecordAsync(Attempt(Now));

        var summary = await UseCase().ExecuteAsync();

        Assert.Equal(2, summary.AccessAttempts);
        Assert.Equal(3, await AttemptsKept());
    }

    [Fact]
    public async Task REQ047_the_retention_follows_the_clock_so_an_event_kept_today_goes_after_it_passes_24h()
    {
        await _attempts.RecordAsync(Attempt(Now));
        await UseCase().ExecuteAsync();
        Assert.Equal(1, await AttemptsKept());

        _clock.Advance(TimeSpan.FromHours(24) + TimeSpan.FromSeconds(1));
        var summary = await UseCase().ExecuteAsync();

        Assert.Equal(1, summary.AccessAttempts);
        Assert.Equal(0, await AttemptsKept());
    }

    [Fact]
    public async Task REQ047_expired_and_revoked_sessions_are_deleted_and_valid_ones_kept()
    {
        var playerId = new PlayerId(Guid.NewGuid());
        var expiredAtBoundary = ASession(playerId, 1);                   // sem lembrar: expira em Now + 30 min
        var revoked = ASession(playerId, 2, rememberMe: true);           // 30 dias, mas revogada
        var rememberedValid = ASession(playerId, 3, rememberMe: true);
        var recent = ASession(playerId, 4, at: Now.AddMinutes(10));      // expira em Now + 40 min
        revoked.Revoke(SessionRevocationReason.Logout, Now);
        await APlayerWithSessions(expiredAtBoundary, revoked, rememberedValid, recent);
        _clock.Advance(TimeSpan.FromMinutes(30));                        // exatamente ExpiresAt da primeira

        var summary = await UseCase().ExecuteAsync();

        Assert.Equal(2, summary.Sessions);
        Assert.Null(await _sessions.FindByTokenHash(AHash(1)));
        Assert.Null(await _sessions.FindByTokenHash(AHash(2)));
        Assert.NotNull(await _sessions.FindByTokenHash(AHash(3)));
        Assert.NotNull(await _sessions.FindByTokenHash(AHash(4)));
    }

    [Fact]
    public async Task REQ047_expired_reset_tokens_are_deleted_and_active_ones_kept()
    {
        var expired = await APlayerWithResetToken("a@example.com", 1, Now);                   // expira em Now + 60 min
        var active = await APlayerWithResetToken("b@example.com", 2, Now.AddMinutes(10));     // expira em Now + 70 min
        _clock.Advance(TimeSpan.FromMinutes(60));                                              // 60:00 ja e expirado

        var summary = await UseCase().ExecuteAsync();

        Assert.Equal(1, summary.ResetTokens);
        Assert.Null(expired.PasswordResetToken);
        Assert.Null(await _players.FindByResetTokenHash(AHash(1)));
        Assert.NotNull(active.PasswordResetToken);
        Assert.Same(active, await _players.FindByResetTokenHash(AHash(2)));
    }

    [Fact]
    public async Task REQ047_the_player_stays_when_only_the_expired_reset_token_is_removed()
    {
        var player = await APlayerWithResetToken("a@example.com", 1, Now);
        _clock.Advance(TimeSpan.FromHours(2));

        await UseCase().ExecuteAsync();

        Assert.Same(player, await _players.GetById(player.Id));
    }

    [Fact]
    public async Task REQ047_expired_pending_google_auths_are_deleted_and_valid_ones_kept()
    {
        var expired = PendingGoogleSignup.Create(AHash(1), "sub-1", AnEmail("a@example.com"), null, Now.AddMinutes(-11));
        var expiredLink = PendingGoogleLink.Create(AHash(2), "sub-2", AnEmail("b@example.com"), new PlayerId(Guid.NewGuid()), Now.AddMinutes(-10));
        var valid = PendingGoogleSignup.Create(AHash(3), "sub-3", AnEmail("c@example.com"), null, Now);
        await _pendings.AddAsync(expired);
        await _pendings.AddAsync(expiredLink);
        await _pendings.AddAsync(valid);

        var summary = await UseCase().ExecuteAsync();

        Assert.Equal(2, summary.PendingGoogleAuths);
        Assert.Null(await _pendings.FindAsync(AHash(1)));
        Assert.Null(await _pendings.FindAsync(AHash(2)));
        Assert.Same(valid, await _pendings.FindAsync(AHash(3)));
    }

    [Fact]
    public async Task REQ047_the_summary_counts_each_kind_of_data_removed()
    {
        await APlayerWithSessions(ASession(new PlayerId(Guid.NewGuid()), 1));
        await APlayerWithResetToken("a@example.com", 5, Now);
        await _pendings.AddAsync(PendingGoogleSignup.Create(AHash(7), "sub-7", AnEmail("d@example.com"), null, Now));
        await _attempts.RecordAsync(Attempt(Now));
        await _attempts.RecordAsync(Attempt(Now.AddMinutes(1)));
        _clock.Advance(TimeSpan.FromHours(25));

        var summary = await UseCase().ExecuteAsync();

        Assert.Equal(new PurgeExpiredDataSummary(Sessions: 1, ResetTokens: 1, PendingGoogleAuths: 1, AccessAttempts: 2), summary);
    }

    [Fact]
    public async Task REQ047_nothing_to_purge_yields_an_empty_summary_and_changes_nothing()
    {
        await APlayerWithSessions(ASession(new PlayerId(Guid.NewGuid()), 1));
        await _attempts.RecordAsync(Attempt(Now));

        var summary = await UseCase().ExecuteAsync();

        Assert.Equal(new PurgeExpiredDataSummary(0, 0, 0, 0), summary);
        Assert.NotNull(await _sessions.FindByTokenHash(AHash(1)));
        Assert.Equal(1, await AttemptsKept());
    }

    [Fact]
    public async Task REQ047_running_twice_is_idempotent_the_second_run_removes_nothing_more()
    {
        await APlayerWithSessions(ASession(new PlayerId(Guid.NewGuid()), 1), ASession(new PlayerId(Guid.NewGuid()), 2, rememberMe: true));
        var player = await APlayerWithResetToken("a@example.com", 5, Now);
        await _pendings.AddAsync(PendingGoogleSignup.Create(AHash(7), "sub-7", AnEmail("d@example.com"), null, Now));
        await _attempts.RecordAsync(Attempt(Now));
        _clock.Advance(TimeSpan.FromHours(25));
        var first = await UseCase().ExecuteAsync();

        var second = await UseCase().ExecuteAsync();

        Assert.Equal(new PurgeExpiredDataSummary(1, 1, 1, 1), first);
        Assert.Equal(new PurgeExpiredDataSummary(0, 0, 0, 0), second);
        Assert.NotNull(await _sessions.FindByTokenHash(AHash(2)));
        Assert.Null(player.PasswordResetToken);
    }

    [Fact]
    public async Task REQ047_two_concurrent_runs_remove_each_row_exactly_once_and_do_not_fail()
    {
        await APlayerWithSessions(
            ASession(new PlayerId(Guid.NewGuid()), 1),
            ASession(new PlayerId(Guid.NewGuid()), 2),
            ASession(new PlayerId(Guid.NewGuid()), 3, rememberMe: true));
        await APlayerWithResetToken("a@example.com", 5, Now);
        await _pendings.AddAsync(PendingGoogleSignup.Create(AHash(7), "sub-7", AnEmail("d@example.com"), null, Now));
        await _attempts.RecordAsync(Attempt(Now));
        await _attempts.RecordAsync(Attempt(Now.AddMinutes(1)));
        _clock.Advance(TimeSpan.FromHours(25));
        var gated = new GateSessionRepository(_sessions, contenders: 2);
        var job = new PurgeExpiredData(gated, _players, _pendings, _attempts, _clock, 1000);

        var summaries = await Task.WhenAll(job.ExecuteAsync(), job.ExecuteAsync());

        Assert.Equal(2, gated.Arrived);
        Assert.Equal(2, summaries.Sum(s => s.Sessions));
        Assert.Equal(1, summaries.Sum(s => s.ResetTokens));
        Assert.Equal(1, summaries.Sum(s => s.PendingGoogleAuths));
        Assert.Equal(2, summaries.Sum(s => s.AccessAttempts));
        Assert.Null(await _sessions.FindByTokenHash(AHash(1)));
        Assert.NotNull(await _sessions.FindByTokenHash(AHash(3)));
        Assert.Equal(0, await AttemptsKept());
    }

    // Decorator: segura cada execucao dentro de DeleteExpiredOrRevoked ate as duas chegarem (ou ate o limite de tempo),
    // forcando a sobreposicao. As continuacoes rodam em linha, uma por vez (sem paralelismo real: deterministico).
    private sealed class GateSessionRepository(ISessionRepository inner, int contenders) : ISessionRepository
    {
        private readonly TaskCompletionSource _released = new();
        private int _arrived;

        public int Arrived => _arrived;

        public async Task<int> DeleteExpiredOrRevoked(DateTimeOffset now, int batchSize, CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref _arrived) >= contenders)
            {
                _released.TrySetResult();
            }
            else
            {
                await Task.WhenAny(_released.Task, Task.Delay(TimeSpan.FromSeconds(2), ct));
            }

            return await inner.DeleteExpiredOrRevoked(now, batchSize, ct);
        }

        public Task Add(Session session, CancellationToken ct = default) => inner.Add(session, ct);

        public Task<Session?> FindById(SessionId id, CancellationToken ct = default) => inner.FindById(id, ct);

        public Task<Session?> FindByTokenHash(TokenHash tokenHash, CancellationToken ct = default) =>
            inner.FindByTokenHash(tokenHash, ct);

        public Task Save(Session session, CancellationToken ct = default) => inner.Save(session, ct);

        public Task<int> RevokeAllFor(
            PlayerId playerId, SessionRevocationReason reason, DateTimeOffset now, CancellationToken ct = default) =>
            inner.RevokeAllFor(playerId, reason, now, ct);
    }
}
