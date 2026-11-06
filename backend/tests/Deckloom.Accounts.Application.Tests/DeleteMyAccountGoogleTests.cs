using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;
using Deckloom.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using static Deckloom.Accounts.Application.Tests.PlayerFixtures;

namespace Deckloom.Accounts.Application.Tests;

// T-050 (REQ-023, 026, 042): exclusao de conta SEM senha local (Google) por DeleteMyAccount (design linha 1598:
// `session.reauthenticatedAt >= now - 5 min`; sem reautenticacao -> reauth_required; mais antiga -> reauth_expired;
// confirm obrigatorio, linha 1587). RED: o ramo Google ainda nao existe; hoje o caso de uso exige senha.
// Forma (NENHUM tipo novo; mesmo comando e mesmo caso de uso do T-049, design linha 536-538):
//   DeleteMyAccountCommand(AuthenticatedPlayer? Caller, string? Password, bool Confirmed, string Ip)  <- Password = null
//   DeleteMyAccount(...mesmas dependencias do T-049...).ExecuteAsync(command, ct) : Task<Result<AccountDeleted>>
// Contrato exercitado: conta sem PasswordHash nao e validada por senha; o caso de uso le a Session do Caller (pelo
// Caller.SessionId, no momento da exclusao, nao um instantaneo anterior), monta session.ToRecentGoogleLogin() e chama
// Player.Delete(confirmed, recentGoogleLogin, now). Falhas do dominio (reauth_required, reauth_expired, required/confirm)
// voltam como estao, sem gravar nada. O sucesso reusa a MESMA unidade de trabalho do caminho com senha (decks, limpeza
// de tentativas, avatar customizado, revogacao de sessoes, remocao do Jogador).
// A reautenticacao e simulada com Session.MarkReauthenticated (fluxo real do T-039, coberto em GoogleReauthTests).
public class DeleteMyAccountGoogleTests
{
    private const string RawEmail = "gui@example.com";
    private const string Subject = "google-sub-1";
    private const string Ip = "203.0.113.7";
    private const string CustomKey = "avatars/gui-1";
    private const string GuiToken = "gui-token";
    private const string GuiOtherToken = "gui-other-token";
    private const string AnaToken = "ana-token";

    private static readonly DomainError Unauthenticated = new("unauthenticated");
    private static readonly ExternalIdentity Linked = ExternalIdentity.Create("google", Subject).Value;

    private readonly InMemoryUnitOfWork _uow = new();
    private readonly InMemoryPlayerRepository _players;
    private readonly InMemorySessionRepository _sessions;
    private readonly RecordingSessionRepository _recordedSessions;
    private readonly SessionProbe _probe;
    private readonly InMemoryAccessAttemptStore _attempts;
    private readonly InMemoryAvatarStorage _storage;
    private readonly RecordingPlayerDecksPurger _purger;
    private readonly FakePasswordHasher _hasher = new(600_000);
    private readonly Sha256TokenHasher _tokenHasher = new();
    private readonly FakeKeyedHasher _keyedHasher = new();
    private readonly RecordingDelayer _delayer = new();
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly InMemoryAvatarPresetCatalog _catalog = new(
    [
        new AvatarPresetEntry("preset-01", "Dragao", "/avatars/preset-01.png", 1, false, true),
        new AvatarPresetEntry("preset-02", "Planeswalker", "/avatars/preset-02.png", 2, true, true),
    ]);

    public DeleteMyAccountGoogleTests()
    {
        _players = new InMemoryPlayerRepository(_uow);
        _sessions = new InMemorySessionRepository(_uow);
        _recordedSessions = new RecordingSessionRepository(_sessions, _uow);
        _probe = new SessionProbe(_sessions, _tokenHasher, _clock);
        _attempts = new InMemoryAccessAttemptStore(_uow);
        _storage = new InMemoryAvatarStorage(_uow);
        _purger = new RecordingPlayerDecksPurger(_uow);
    }

    private AccessThrottle NewThrottle() =>
        new(_attempts, _keyedHasher, _delayer, new ProgressiveDelayPolicy(new ProgressiveDelayOptions()), _clock);

    private DeleteMyAccount NewUseCase() =>
        new(_players, _recordedSessions, _purger, _storage, _uow, _hasher, NewThrottle(), _clock);

    private AuthenticateSession NewAuthenticate() =>
        new(_sessions, _players, _uow, _tokenHasher, _catalog, _clock);

    private async Task<Player> SeedGooglePlayerAsync(string email = RawEmail, string subject = Subject, string? customKey = null)
    {
        var player = AGooglePlayer(email, subject);
        if (customKey is not null)
        {
            Assert.True(player.SetCustomAvatar(customKey).IsSuccess);
            _storage.SeedCommitted(customKey, [1, 2, 3]);
        }

        player.ClearDomainEvents();
        await _players.Add(player);
        await _uow.CommitAsync();
        return player;
    }

    private async Task<(Session Session, AuthenticatedPlayer Caller)> SignInAsync(Player player, string token = GuiToken)
    {
        var session = Session.Start(player.Id, _tokenHasher.Hash(token), false, _clock.GetUtcNow());
        await _sessions.Add(session);
        await _uow.CommitAsync();
        var caller = (await NewAuthenticate().ExecuteAsync(new AuthenticateSessionCommand(token))).Value;
        return (session, caller);
    }

    private void Reauthenticate(Session session) =>
        Assert.True(session.MarkReauthenticated(Linked, Linked, _clock.GetUtcNow()).IsSuccess);

    private Task<Result<AccountDeleted>> DeleteAsync(AuthenticatedPlayer? caller, bool confirmed = true, string ip = Ip) =>
        NewUseCase().ExecuteAsync(new DeleteMyAccountCommand(caller, null, confirmed, ip));

    private async Task RecordAttemptAsync(AccessAction action, string? email, string ip)
    {
        var throttle = NewThrottle();
        var keys = await throttle.BeforeAttemptAsync(action, email, ip);

        // SEC-01: o evento provisorio de BeforeAttemptAsync ja conta como falha; so o cadastro (sem chave de conta) grava a parte.
        if (action == AccessAction.Registration)
        {
            await throttle.RecordAsync(keys);
        }
    }

    private Task<int> AccountAttemptsAsync(string email) =>
        _attempts.CountByAccountAsync(_keyedHasher.Hash(email), Now.AddHours(-1));

    private async Task AssertAccountAndDataIntactAsync(Player player, string token = GuiToken)
    {
        Assert.Same(player, await _players.GetById(player.Id));
        Assert.Same(player, await _players.FindByGoogleSubject(Subject));
        Assert.DoesNotContain(player.DomainEvents, e => e is PlayerDeleted);
        Assert.Empty(_purger.Purged);
        Assert.True(await _probe.IsValidAsync(token));
    }

    [Fact]
    public async Task REQ023_a_google_reauthentication_two_minutes_old_with_confirmation_deletes_the_account()
    {
        var player = await SeedGooglePlayerAsync();
        var (session, caller) = await SignInAsync(player);
        Reauthenticate(session);
        _clock.Advance(TimeSpan.FromMinutes(2));

        var result = await DeleteAsync(caller);

        Assert.True(result.IsSuccess);
        Assert.Null(await _players.GetById(player.Id));
        Assert.Null(await _players.FindByEmail(AnEmail(RawEmail)));
        Assert.Null(await _players.FindByGoogleSubject(Subject));
        Assert.Empty(_hasher.VerifyCalls);
    }

    [Fact]
    public async Task REQ042_a_reauthentication_four_minutes_fifty_nine_seconds_old_is_accepted()
    {
        var player = await SeedGooglePlayerAsync();
        var (session, caller) = await SignInAsync(player);
        Reauthenticate(session);
        _clock.Advance(Reauthentication.Window - TimeSpan.FromSeconds(1));

        var result = await DeleteAsync(caller);

        Assert.True(result.IsSuccess);
        Assert.Null(await _players.GetById(player.Id));
    }

    [Fact]
    public async Task REQ042_a_reauthentication_exactly_five_minutes_old_is_still_accepted()
    {
        var player = await SeedGooglePlayerAsync();
        var (session, caller) = await SignInAsync(player);
        Reauthenticate(session);
        _clock.Advance(Reauthentication.Window);

        var result = await DeleteAsync(caller);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task REQ042_a_reauthentication_five_minutes_and_one_second_old_is_reauth_expired_and_nothing_is_deleted()
    {
        var player = await SeedGooglePlayerAsync(customKey: CustomKey);
        var (session, caller) = await SignInAsync(player);
        Reauthenticate(session);
        await RecordAttemptAsync(AccessAction.Login, RawEmail, "198.51.100.1");
        _clock.Advance(Reauthentication.Window + TimeSpan.FromSeconds(1));

        var result = await DeleteAsync(caller);

        Assert.True(result.IsFailure);
        Assert.Equal(new DomainError("reauth_expired"), Assert.Single(result.Errors));
        await AssertAccountAndDataIntactAsync(player);
        Assert.NotNull(await _storage.ReadAsync(CustomKey));
        Assert.Equal(1, await AccountAttemptsAsync(RawEmail));
    }

    [Fact]
    public async Task SEC01_the_google_deletion_is_outside_the_account_budget_and_never_returns_too_many_attempts()
    {
        var player = await SeedGooglePlayerAsync(customKey: CustomKey);
        var (session, caller) = await SignInAsync(player);
        Reauthenticate(session);
        for (var i = 0; i < 31; i++)
        {
            await RecordAttemptAsync(AccessAction.Login, RawEmail, "198.51.100.1");
        }

        var result = await DeleteAsync(caller);

        Assert.True(result.IsSuccess);
        Assert.Null(await _players.GetById(player.Id));
    }

    [Fact]
    public async Task SEC01_the_google_deletion_over_the_budget_without_reauthentication_is_reauth_required_not_429()
    {
        var player = await SeedGooglePlayerAsync();
        var (_, caller) = await SignInAsync(player);
        for (var i = 0; i < 30; i++)
        {
            await RecordAttemptAsync(AccessAction.Login, RawEmail, "198.51.100.1");
        }

        var result = await DeleteAsync(caller);

        Assert.Equal(new DomainError("reauth_required"), Assert.Single(result.Errors));
        Assert.Equal(30, await AccountAttemptsAsync(RawEmail)); // nao reserva evento provisorio nem consome orcamento
    }

    [Fact]
    public async Task SEC01_a_refused_reauthentication_releases_the_provisional_event()
    {
        var player = await SeedGooglePlayerAsync();
        var (_, caller) = await SignInAsync(player);

        await DeleteAsync(caller);

        Assert.Equal(0, await AccountAttemptsAsync(RawEmail));
    }

    [Fact]
    public async Task REQ042_a_session_that_never_reauthenticated_is_reauth_required_and_nothing_is_deleted()
    {
        var player = await SeedGooglePlayerAsync(customKey: CustomKey);
        var (_, caller) = await SignInAsync(player);

        var result = await DeleteAsync(caller);

        Assert.True(result.IsFailure);
        Assert.Equal(new DomainError("reauth_required"), Assert.Single(result.Errors));
        await AssertAccountAndDataIntactAsync(player);
        Assert.NotNull(await _storage.ReadAsync(CustomKey));
    }

    [Fact]
    public async Task REQ026_a_recent_reauthentication_without_confirmation_is_required_on_confirm_and_keeps_the_account()
    {
        var player = await SeedGooglePlayerAsync(customKey: CustomKey);
        var (session, caller) = await SignInAsync(player);
        Reauthenticate(session);
        await RecordAttemptAsync(AccessAction.Login, RawEmail, "198.51.100.1");

        var result = await DeleteAsync(caller, confirmed: false);

        Assert.True(result.IsFailure);
        Assert.Equal(new DomainError("required", "confirm"), Assert.Single(result.Errors));
        await AssertAccountAndDataIntactAsync(player);
        Assert.NotNull(await _storage.ReadAsync(CustomKey));
        Assert.Equal(1, await AccountAttemptsAsync(RawEmail));
    }

    [Fact]
    public async Task REQ042_the_reauthentication_is_read_when_deleting_so_a_caller_built_before_it_still_works()
    {
        var player = await SeedGooglePlayerAsync();
        var (session, caller) = await SignInAsync(player);
        var before = await DeleteAsync(caller);
        Reauthenticate(session);

        var after = await DeleteAsync(caller);

        Assert.Equal("reauth_required", before.Error.Code);
        Assert.True(after.IsSuccess);
    }

    [Fact]
    public async Task REQ042_a_reauthentication_in_one_session_does_not_transfer_to_another_session_of_the_same_player()
    {
        var player = await SeedGooglePlayerAsync();
        var (reauthenticated, _) = await SignInAsync(player);
        var (_, otherCaller) = await SignInAsync(player, GuiOtherToken);
        Reauthenticate(reauthenticated);

        var result = await DeleteAsync(otherCaller);

        Assert.Equal(new DomainError("reauth_required"), Assert.Single(result.Errors));
        await AssertAccountAndDataIntactAsync(player);
        Assert.True(await _probe.IsValidAsync(GuiOtherToken));
    }

    [Fact]
    public async Task REQ042_a_reauthentication_of_another_players_session_does_not_authorize_this_deletion()
    {
        var gui = await SeedGooglePlayerAsync();
        var ana = await SeedGooglePlayerAsync("ana@example.com", "google-sub-2");
        var (_, guiCaller) = await SignInAsync(gui);
        var (anaSession, _) = await SignInAsync(ana, AnaToken);
        Assert.True(anaSession.MarkReauthenticated(
            ExternalIdentity.Create("google", "google-sub-2").Value,
            ExternalIdentity.Create("google", "google-sub-2").Value,
            _clock.GetUtcNow()).IsSuccess);

        var result = await DeleteAsync(guiCaller);

        Assert.Equal("reauth_required", result.Error.Code);
        await AssertAccountAndDataIntactAsync(gui);
        Assert.Same(ana, await _players.GetById(ana.Id));
    }

    [Fact]
    public async Task REQ023_the_google_deletion_runs_in_the_same_unit_of_work_as_the_password_path()
    {
        var player = await SeedGooglePlayerAsync(customKey: CustomKey);
        var other = await SeedGooglePlayerAsync("ana@example.com", "google-sub-2", "avatars/ana-1");
        var (session, caller) = await SignInAsync(player);
        await SignInAsync(player, GuiOtherToken);
        await SignInAsync(other, AnaToken);
        Reauthenticate(session);
        await RecordAttemptAsync(AccessAction.Login, RawEmail, "198.51.100.1");
        await RecordAttemptAsync(AccessAction.Login, "ana@example.com", "198.51.100.2");

        await DeleteAsync(caller, ip: "198.51.100.9");

        Assert.Equal([player.Id], _purger.Purged);
        Assert.False(_uow.IsTransactionOpen);
        Assert.True(_uow.TransactionOpenDuringLastCommit);
        Assert.Null(await _storage.ReadAsync(CustomKey));
        Assert.NotNull(await _storage.ReadAsync("avatars/ana-1"));
        Assert.False(await _probe.IsUsableAsync(GuiToken));
        Assert.False(await _probe.IsUsableAsync(GuiOtherToken));
        Assert.True(await _probe.IsValidAsync(AnaToken));
        Assert.Equal(0, await AccountAttemptsAsync(RawEmail));
        Assert.Equal(1, await AccountAttemptsAsync("ana@example.com"));
        var deleted = Assert.Single(player.DomainEvents.OfType<PlayerDeleted>());
        Assert.Equal(player.Id, deleted.PlayerId);
    }

    [Fact]
    public async Task REQ023_the_google_deletion_calls_RevokeAllFor_once_for_the_deleted_player_with_AccountDeletion_inside_the_unit_of_work()
    {
        var player = await SeedGooglePlayerAsync();
        var other = await SeedGooglePlayerAsync("ana@example.com", "google-sub-2");
        var (session, caller) = await SignInAsync(player);
        await SignInAsync(other, AnaToken);
        Reauthenticate(session);

        await DeleteAsync(caller);

        var call = Assert.Single(_recordedSessions.RevokeAllForCalls);
        Assert.Equal(player.Id, call.PlayerId);
        Assert.Equal(SessionRevocationReason.AccountDeletion, call.Reason);
        Assert.Equal(_clock.GetUtcNow(), call.Now);
        Assert.True(call.InsideUnitOfWork);
        Assert.True(await _probe.IsValidAsync(AnaToken));
    }

    [Fact]
    public async Task REQ042_a_refused_google_deletion_does_not_call_RevokeAllFor()
    {
        var player = await SeedGooglePlayerAsync();
        var (session, caller) = await SignInAsync(player);
        await DeleteAsync(caller); // sem reautenticacao: reauth_required
        Reauthenticate(session);
        await DeleteAsync(caller, confirmed: false); // sem confirmacao: required/confirm
        _clock.Advance(Reauthentication.Window + TimeSpan.FromSeconds(1));
        await DeleteAsync(caller); // reautenticacao expirada: reauth_expired

        Assert.Empty(_recordedSessions.RevokeAllForCalls);
        Assert.True(await _probe.IsValidAsync(GuiToken));
    }

    [Fact]
    public async Task REQ023_a_failing_decks_purger_rolls_the_google_deletion_back_and_the_error_propagates()
    {
        var player = await SeedGooglePlayerAsync(customKey: CustomKey);
        var (session, caller) = await SignInAsync(player);
        Reauthenticate(session);
        await RecordAttemptAsync(AccessAction.Login, RawEmail, "198.51.100.1");
        _purger.FailWith(new InvalidOperationException("decks unavailable"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => DeleteAsync(caller));

        await AssertAccountAndDataIntactAsync(player);
        Assert.NotNull(await _storage.ReadAsync(CustomKey));
        Assert.Equal(1, await AccountAttemptsAsync(RawEmail)); // o ramo Google nao reserva evento provisorio
        Assert.False(_uow.IsTransactionOpen);
    }

    [Fact]
    public async Task REQ023_repeating_the_google_deletion_is_unauthenticated_and_purges_only_once()
    {
        var player = await SeedGooglePlayerAsync();
        var (session, caller) = await SignInAsync(player);
        Reauthenticate(session);
        await DeleteAsync(caller);

        var second = await DeleteAsync(caller);

        Assert.Equal(Unauthenticated, Assert.Single(second.Errors));
        Assert.Equal([player.Id], _purger.Purged);
    }

    [Fact]
    public async Task REQ023_without_a_caller_it_is_unauthenticated_even_if_a_session_was_reauthenticated()
    {
        var player = await SeedGooglePlayerAsync();
        var (session, _) = await SignInAsync(player);
        Reauthenticate(session);

        var result = await DeleteAsync(null);

        Assert.Equal(Unauthenticated, Assert.Single(result.Errors));
        await AssertAccountAndDataIntactAsync(player);
    }
}
