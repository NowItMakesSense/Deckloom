using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;
using Deckloom.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using static Deckloom.Accounts.Application.Tests.PlayerFixtures;

namespace Deckloom.Accounts.Application.Tests;

// T-034 (REQ-011, 012, 035): autenticacao da sessao por token. RED: os tipos abaixo ainda nao existem.
// Forma esperada (convencao do T-030, ver relatorio do test agent):
//   record AuthenticateSessionCommand(string? SessionToken)
//   sealed class AuthenticateSession(ISessionRepository, IPlayerRepository, IUnitOfWork, ITokenHasher,
//       IAvatarPresetCatalog, TimeProvider)
//     Task<Result<AuthenticatedPlayer>> ExecuteAsync(AuthenticateSessionCommand, CancellationToken ct = default)
// Falha unica: DomainError("unauthenticated") sem campo (design.md linha 1428, S4), sem dados do Jogador.
// Sucesso: record AuthenticatedPlayer(PlayerId PlayerId, SessionId SessionId, SessionSummary Summary) do dono da sessao;
// a API projeta so o Summary em GET /auth/session (design.md linha 1428). Decisao do usuario.
// Janela deslizante (design.md linhas 234, 242): Session.Touch renova LastSeenAt e, sem lembrar-me,
// ExpiresAt = now + 30 min; com lembrar-me o ExpiresAt nao muda.
public class AuthenticateSessionTests
{
    private const string RawToken = "raw-session-token";

    private static readonly DomainError Unauthenticated = new("unauthenticated");

    private readonly InMemoryUnitOfWork _uow = new();
    private readonly InMemoryPlayerRepository _players;
    private readonly InMemorySessionRepository _sessions;
    private readonly Sha256TokenHasher _tokenHasher = new();
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly InMemoryAvatarPresetCatalog _catalog = new(
    [
        new AvatarPresetEntry("preset-01", "Dragao", "/avatars/preset-01.png", 1, false, true),
        new AvatarPresetEntry("preset-02", "Planeswalker", "/avatars/preset-02.png", 2, true, true),
    ]);

    public AuthenticateSessionTests()
    {
        _players = new InMemoryPlayerRepository(_uow);
        _sessions = new InMemorySessionRepository(_uow);
    }

    private AuthenticateSession NewUseCase() => new(_sessions, _players, _uow, _tokenHasher, _catalog, _clock);

    private async Task<Player> SeedPlayerAsync(string email = "ana@example.com")
    {
        var player = APasswordPlayer(email);
        await _players.Add(player);
        await _uow.CommitAsync();
        return player;
    }

    private async Task<Session> SeedSessionAsync(Player player, bool rememberMe = false, string token = RawToken)
    {
        var session = Session.Start(player.Id, _tokenHasher.Hash(token), rememberMe, Now);
        await _sessions.Add(session);
        await _uow.CommitAsync();
        return session;
    }

    private Task<Result<AuthenticatedPlayer>> AuthenticateAsync(string? token = RawToken) =>
        NewUseCase().ExecuteAsync(new AuthenticateSessionCommand(token));

    private static void AssertUnauthenticated(Result<AuthenticatedPlayer> result)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(Unauthenticated, Assert.Single(result.Errors));
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task REQ011_request_without_a_token_is_unauthenticated(string? token)
    {
        await SeedSessionAsync(await SeedPlayerAsync());

        var result = await AuthenticateAsync(token);

        AssertUnauthenticated(result);
    }

    [Fact]
    public async Task REQ011_unknown_token_is_unauthenticated()
    {
        await SeedSessionAsync(await SeedPlayerAsync());

        var result = await AuthenticateAsync("some-other-token");

        AssertUnauthenticated(result);
    }

    [Fact]
    public async Task REQ011_the_token_is_looked_up_by_its_hash_so_the_stored_hash_is_not_a_valid_token()
    {
        var session = await SeedSessionAsync(await SeedPlayerAsync());

        var result = await AuthenticateAsync(Convert.ToHexString(session.TokenHash.ToBytes()));

        AssertUnauthenticated(result);
    }

    [Fact]
    public async Task REQ012_session_without_remember_me_idle_for_31_minutes_is_unauthenticated()
    {
        await SeedSessionAsync(await SeedPlayerAsync());
        _clock.Advance(TimeSpan.FromMinutes(31));

        var result = await AuthenticateAsync();

        AssertUnauthenticated(result);
    }

    [Fact]
    public async Task REQ012_session_without_remember_me_is_expired_at_exactly_30_minutes()
    {
        await SeedSessionAsync(await SeedPlayerAsync());
        _clock.Advance(TimeSpan.FromMinutes(30));

        var result = await AuthenticateAsync();

        AssertUnauthenticated(result);
    }

    [Fact]
    public async Task REQ035_session_with_remember_me_older_than_31_days_is_unauthenticated()
    {
        await SeedSessionAsync(await SeedPlayerAsync(), rememberMe: true);
        _clock.Advance(TimeSpan.FromDays(31));

        var result = await AuthenticateAsync();

        AssertUnauthenticated(result);
    }

    [Fact]
    public async Task REQ011_revoked_session_is_unauthenticated_even_within_its_lifetime()
    {
        var session = await SeedSessionAsync(await SeedPlayerAsync());
        session.Revoke(SessionRevocationReason.Logout, Now);
        _clock.Advance(TimeSpan.FromMinutes(1));

        var result = await AuthenticateAsync();

        AssertUnauthenticated(result);
    }

    [Fact]
    public async Task REQ011_revoked_session_with_remember_me_is_unauthenticated()
    {
        var session = await SeedSessionAsync(await SeedPlayerAsync(), rememberMe: true);
        session.Revoke(SessionRevocationReason.PasswordReset, Now);
        _clock.Advance(TimeSpan.FromDays(1));

        var result = await AuthenticateAsync();

        AssertUnauthenticated(result);
    }

    [Fact]
    public async Task REQ011_session_whose_player_no_longer_exists_is_unauthenticated()
    {
        var player = await SeedPlayerAsync();
        await SeedSessionAsync(player);
        await _players.Remove(player);
        await _uow.CommitAsync();

        var result = await AuthenticateAsync();

        AssertUnauthenticated(result);
    }

    [Fact]
    public async Task REQ012_session_without_remember_me_idle_for_29_minutes_is_valid_and_returns_the_summary()
    {
        var player = await SeedPlayerAsync();
        var session = await SeedSessionAsync(player);
        _clock.Advance(TimeSpan.FromMinutes(29));

        var result = await AuthenticateAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(player.Id, result.Value.PlayerId);
        Assert.Equal(session.Id, result.Value.SessionId);
        Assert.Equal("Ana", result.Value.Summary.DisplayName);
        Assert.Equal("preset-01", result.Value.Summary.Avatar.PresetId);
        Assert.True(result.Value.Summary.HasPassword);
        Assert.False(result.Value.Summary.HasGoogle);
    }

    [Fact]
    public async Task REQ011_the_summary_belongs_to_the_owner_of_the_session()
    {
        var ana = await SeedPlayerAsync("ana@example.com");
        await SeedSessionAsync(ana);
        var other = AGooglePlayer("gui@example.com", "sub-1");
        await _players.Add(other);
        await _uow.CommitAsync();
        var otherSession = await SeedSessionAsync(other, token: "other-token");

        var result = await AuthenticateAsync("other-token");

        Assert.Equal(other.Id, result.Value.PlayerId);
        Assert.NotEqual(ana.Id, result.Value.PlayerId);
        Assert.Equal(otherSession.Id, result.Value.SessionId);
        Assert.Equal("Gui", result.Value.Summary.DisplayName);
        Assert.False(result.Value.Summary.HasPassword);
        Assert.True(result.Value.Summary.HasGoogle);
    }

    [Fact]
    public async Task REQ012_valid_request_without_remember_me_renews_last_seen_and_slides_the_expiry()
    {
        var session = await SeedSessionAsync(await SeedPlayerAsync());
        _clock.Advance(TimeSpan.FromMinutes(29));

        await AuthenticateAsync();

        var seenAt = Now.AddMinutes(29);
        Assert.Equal(seenAt, session.LastSeenAt);
        Assert.Equal(seenAt.AddMinutes(30), session.ExpiresAt);
    }

    [Fact]
    public async Task REQ012_activity_keeps_the_session_alive_beyond_30_minutes_since_it_was_created()
    {
        await SeedSessionAsync(await SeedPlayerAsync());
        _clock.Advance(TimeSpan.FromMinutes(29));
        await AuthenticateAsync();
        _clock.Advance(TimeSpan.FromMinutes(29));

        var result = await AuthenticateAsync();

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task REQ012_without_activity_for_more_than_30_minutes_after_a_renewal_the_session_expires()
    {
        await SeedSessionAsync(await SeedPlayerAsync());
        _clock.Advance(TimeSpan.FromMinutes(29));
        await AuthenticateAsync();
        _clock.Advance(TimeSpan.FromMinutes(31));

        var result = await AuthenticateAsync();

        AssertUnauthenticated(result);
    }

    [Fact]
    public async Task REQ035_session_with_remember_me_at_29_days_is_valid()
    {
        await SeedSessionAsync(await SeedPlayerAsync(), rememberMe: true);
        _clock.Advance(TimeSpan.FromDays(29));

        var result = await AuthenticateAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal("Ana", result.Value.Summary.DisplayName);
    }

    [Fact]
    public async Task REQ035_with_remember_me_the_expiry_does_not_slide_so_it_ends_30_days_after_creation()
    {
        var session = await SeedSessionAsync(await SeedPlayerAsync(), rememberMe: true);
        _clock.Advance(TimeSpan.FromDays(29));
        await AuthenticateAsync();
        _clock.Advance(TimeSpan.FromDays(2));

        var result = await AuthenticateAsync();

        Assert.Equal(Now.AddDays(30), session.ExpiresAt);
        AssertUnauthenticated(result);
    }

    [Fact]
    public async Task REQ035_with_remember_me_a_few_hours_of_inactivity_never_expires_the_session()
    {
        await SeedSessionAsync(await SeedPlayerAsync(), rememberMe: true);
        _clock.Advance(TimeSpan.FromHours(5));

        var result = await AuthenticateAsync();

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task REQ011_refused_request_does_not_renew_an_expired_session()
    {
        var session = await SeedSessionAsync(await SeedPlayerAsync());
        var expiresAt = session.ExpiresAt;
        _clock.Advance(TimeSpan.FromMinutes(31));

        var result = await AuthenticateAsync();

        AssertUnauthenticated(result);
        Assert.Equal(Now, session.LastSeenAt);
        Assert.Equal(expiresAt, session.ExpiresAt);
    }

    [Fact]
    public async Task REQ012_renewal_is_persisted_inside_a_unit_of_work_block()
    {
        await SeedSessionAsync(await SeedPlayerAsync());
        _clock.Advance(TimeSpan.FromMinutes(5));

        await AuthenticateAsync();

        Assert.True(_uow.TransactionOpenDuringLastCommit);
        Assert.False(_uow.IsTransactionOpen);
    }
}
