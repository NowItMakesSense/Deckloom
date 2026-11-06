using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;
using Deckloom.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using static Deckloom.Accounts.Application.Tests.PlayerFixtures;

namespace Deckloom.Accounts.Application.Tests;

// T-034 (REQ-010, 011): logout revoga a sessao no servidor. RED: os tipos abaixo ainda nao existem.
// Forma esperada (convencao do T-030, ver relatorio do test agent):
//   record LogoutCommand(string? SessionToken)
//   sealed class Logout(ISessionRepository, IUnitOfWork, ITokenHasher, TimeProvider)
//     Task ExecuteAsync(LogoutCommand, CancellationToken ct = default)
// Nao ha erro possivel: POST /auth/logout responde 204 sempre, idempotente, "sessao (ou nenhuma)"
// (design.md linha 1427), por isso o retorno e Task simples (nao existe Result sem valor no SharedKernel).
// Revoke(Logout, now) e idempotente e vale ate para sessao expirada (design.md linha 243).
public class LogoutTests
{
    private const string RawToken = "raw-session-token";

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

    public LogoutTests()
    {
        _players = new InMemoryPlayerRepository(_uow);
        _sessions = new InMemorySessionRepository(_uow);
    }

    private Logout NewLogout() => new(_sessions, _uow, _tokenHasher, _clock);

    private AuthenticateSession NewAuthenticate() => new(_sessions, _players, _uow, _tokenHasher, _catalog, _clock);

    private async Task<Player> SeedPlayerAsync()
    {
        var player = APasswordPlayer();
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

    private Task LogoutAsync(string? token = RawToken) =>
        NewLogout().ExecuteAsync(new LogoutCommand(token));

    [Fact]
    public async Task REQ010_logout_revokes_the_session_on_the_server()
    {
        var session = await SeedSessionAsync(await SeedPlayerAsync());
        _clock.Advance(TimeSpan.FromMinutes(5));

        await LogoutAsync();

        Assert.Equal(Now.AddMinutes(5), session.RevokedAt);
        var revoked = Assert.Single(session.DomainEvents.OfType<SessionRevoked>());
        Assert.Equal(SessionRevocationReason.Logout, revoked.Reason);
        Assert.Equal(session.Id, revoked.SessionId);
    }

    [Fact]
    public async Task REQ010_a_token_used_after_logout_is_no_longer_authenticated()
    {
        await SeedSessionAsync(await SeedPlayerAsync());
        Assert.True((await NewAuthenticate().ExecuteAsync(new AuthenticateSessionCommand(RawToken))).IsSuccess);

        await LogoutAsync();

        var result = await NewAuthenticate().ExecuteAsync(new AuthenticateSessionCommand(RawToken));
        Assert.True(result.IsFailure);
        Assert.Equal(new DomainError("unauthenticated"), Assert.Single(result.Errors));
    }

    [Fact]
    public async Task REQ035_logout_also_ends_a_session_with_remember_me_before_its_30_days()
    {
        var session = await SeedSessionAsync(await SeedPlayerAsync(), rememberMe: true);
        _clock.Advance(TimeSpan.FromDays(2));

        await LogoutAsync();

        Assert.NotNull(session.RevokedAt);
        var result = await NewAuthenticate().ExecuteAsync(new AuthenticateSessionCommand(RawToken));
        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task REQ010_logout_revokes_only_the_presented_session_and_keeps_the_other_sessions_of_the_player()
    {
        var player = await SeedPlayerAsync();
        var current = await SeedSessionAsync(player);
        var other = await SeedSessionAsync(player, token: "other-device-token");

        await LogoutAsync();

        Assert.NotNull(current.RevokedAt);
        Assert.Null(other.RevokedAt);
        var result = await NewAuthenticate().ExecuteAsync(new AuthenticateSessionCommand("other-device-token"));
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task REQ010_logout_is_idempotent_and_keeps_the_first_revocation_and_a_single_event()
    {
        var session = await SeedSessionAsync(await SeedPlayerAsync());
        _clock.Advance(TimeSpan.FromMinutes(1));
        await LogoutAsync();
        _clock.Advance(TimeSpan.FromMinutes(10));

        await LogoutAsync();

        Assert.Equal(Now.AddMinutes(1), session.RevokedAt);
        Assert.Single(session.DomainEvents.OfType<SessionRevoked>());
    }

    [Fact]
    public async Task REQ010_logout_of_an_already_expired_session_still_revokes_it_without_failing()
    {
        var session = await SeedSessionAsync(await SeedPlayerAsync());
        _clock.Advance(TimeSpan.FromMinutes(45));

        await LogoutAsync();

        Assert.Equal(Now.AddMinutes(45), session.RevokedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task REQ010_logout_without_a_token_completes_and_changes_nothing(string? token)
    {
        var session = await SeedSessionAsync(await SeedPlayerAsync());

        await LogoutAsync(token);

        Assert.Null(session.RevokedAt);
    }

    [Fact]
    public async Task REQ010_logout_with_an_unknown_token_completes_and_changes_nothing()
    {
        var session = await SeedSessionAsync(await SeedPlayerAsync());

        await LogoutAsync("some-other-token");

        Assert.Null(session.RevokedAt);
        Assert.Empty(session.DomainEvents.OfType<SessionRevoked>());
    }

    [Fact]
    public async Task REQ010_revocation_is_persisted_inside_a_unit_of_work_block()
    {
        await SeedSessionAsync(await SeedPlayerAsync());

        await LogoutAsync();

        Assert.True(_uow.TransactionOpenDuringLastCommit);
        Assert.False(_uow.IsTransactionOpen);
    }
}
