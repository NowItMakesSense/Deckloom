using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;
using Deckloom.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using static Deckloom.Accounts.Application.Tests.PlayerFixtures;

namespace Deckloom.Accounts.Application.Tests;

// T-049 (REQ-023, 024, 025, 026, 027, 032, 047, 048): exclusao de conta COM senha local (POST /me/deletion, design linha 1580).
// RED: os tipos abaixo ainda nao existem.
// Forma esperada (convencao do T-045..T-048: a identidade vem sempre do AuthenticatedPlayer, design J10 linha 184):
//   sealed record DeleteMyAccountCommand(AuthenticatedPlayer? Caller, string? Password, bool Confirmed, string Ip)
//   sealed class DeleteMyAccount(IPlayerRepository players, ISessionRepository sessions, IPlayerDecksPurger decksPurger,
//       IAvatarStorage avatarStorage, IUnitOfWork unitOfWork, IPasswordHasher passwordHasher, AccessThrottle throttle,
//       TimeProvider time)
//     Task<Result<AccountDeleted>> ExecuteAsync(DeleteMyAccountCommand, CancellationToken ct = default)
//   AccountDeleted: sealed record vazio (marcador do 204 sem corpo; o SharedKernel nao tem Result sem valor). Os testes so
//   leem IsSuccess/IsFailure/Errors/Error.
// Chave de conta (design linha 259, REQ-047): a MESMA do AccessThrottle. Reuso esperado: keys = throttle.BeforeAttemptAsync(
//   AccessAction.AccountDeletion, player.Email.Value, command.Ip) (o email vem do Jogador carregado pelo Caller; o corpo nao
//   traz email) e, no sucesso, throttle.ClearAccountFailuresAsync(keys) DENTRO do unitOfWork.ExecuteAsync. Falha de senha ->
//   throttle.RecordAsync(keys) FORA da unidade de trabalho (a falha precisa persistir, REQ-032).
// Ordem: Caller nulo/Jogador removido -> unauthenticated -> throttle (atraso) -> valida (confirm: required/confirm; senha:
//   required/password ou too_long/password; erros coletados, sem hasher, sem registrar falha) -> confere a senha com
//   CredentialVerifier (incorrect_password sem campo; RecordAsync) -> Player.Delete(confirmed, PasswordProof, now) ->
//   UoW { decksPurger.PurgeAsync, ClearAccountFailuresAsync, avatarStorage.RemoveAsync(custom), sessions.RevokeAllFor,
//   players.Remove }. Excecao do bloco (ex.: purger) propaga e o rollback e total.
public class DeleteMyAccountPasswordTests
{
    private const string RawEmail = "ana@example.com";
    private const string RawPassword = "correct horse battery";
    private const string Ip = "203.0.113.7";
    private const string CustomKey = "avatars/ana-1";
    private const string AnaToken = "ana-token";
    private const string GuiToken = "gui-token";

    private static readonly DomainError Unauthenticated = new("unauthenticated");

    private readonly InMemoryUnitOfWork _uow = new();
    private readonly InMemoryPlayerRepository _players;
    private readonly InMemorySessionRepository _sessions;
    private readonly RecordingSessionRepository _recordedSessions;
    private readonly SessionProbe _probe;
    private readonly InMemoryAccessAttemptStore _attempts;
    private readonly InMemoryAvatarStorage _storage;
    private readonly RecordingPlayerDecksPurger _purger;
    private readonly FakePasswordHasher _hasher = new(600_000);
    private readonly SequentialTokenGenerator _tokens = new();
    private readonly Sha256TokenHasher _tokenHasher = new();
    private readonly FakeKeyedHasher _keyedHasher = new();
    private readonly RecordingDelayer _delayer = new();
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly InMemoryAvatarPresetCatalog _catalog = new(
    [
        new AvatarPresetEntry("preset-01", "Dragao", "/avatars/preset-01.png", 1, false, true),
        new AvatarPresetEntry("preset-02", "Planeswalker", "/avatars/preset-02.png", 2, true, true),
    ]);

    public DeleteMyAccountPasswordTests()
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

    private async Task<Player> SeedPlayerAsync(string email = RawEmail, string? customKey = null)
    {
        var hash = await _hasher.HashAsync(Password.Create(RawPassword).Value);
        var player = Player.RegisterWithPassword(
            AnEmail(email), hash, DisplayName.Create("Ana").Value, "2026-10-01", "2026-10-01", true, true, Now, "preset-01").Value;
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

    private async Task<AuthenticatedPlayer> SignInAsync(Player player, string token = AnaToken)
    {
        await _sessions.Add(Session.Start(player.Id, _tokenHasher.Hash(token), false, Now));
        await _uow.CommitAsync();
        return (await NewAuthenticate().ExecuteAsync(new AuthenticateSessionCommand(token))).Value;
    }

    private async Task AddSessionAsync(Player player, string token, bool rememberMe)
    {
        await _sessions.Add(Session.Start(player.Id, _tokenHasher.Hash(token), rememberMe, Now));
        await _uow.CommitAsync();
    }

    private Task<Result<AccountDeleted>> DeleteAsync(
        AuthenticatedPlayer? caller, string? password = RawPassword, bool confirmed = true, string ip = Ip) =>
        NewUseCase().ExecuteAsync(new DeleteMyAccountCommand(caller, password, confirmed, ip));

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

    private Task<int> IpAttemptsAsync(string ip) =>
        _attempts.CountByIpAsync(_keyedHasher.Hash(ip), Now.AddHours(-1));

    private async Task AssertAccountAndDataIntactAsync(Player player)
    {
        Assert.Same(player, await _players.GetById(player.Id));
        Assert.Same(player, await _players.FindByEmail(AnEmail(RawEmail)));
        Assert.DoesNotContain(player.DomainEvents, e => e is PlayerDeleted);
        Assert.NotNull(player.PasswordHash);
        Assert.Empty(_purger.Purged);
        Assert.True(await _probe.IsValidAsync(AnaToken));
    }

    [Fact]
    public async Task REQ023_a_correct_password_with_confirmation_deletes_the_account()
    {
        var player = await SeedPlayerAsync();
        var caller = await SignInAsync(player);

        var result = await DeleteAsync(caller);

        Assert.True(result.IsSuccess);
        Assert.Null(await _players.GetById(player.Id));
        Assert.Null(await _players.FindByEmail(AnEmail(RawEmail)));
        Assert.False(await _players.ExistsByEmail(AnEmail(RawEmail)));
    }

    [Fact]
    public async Task REQ024_the_player_carries_the_acceptance_and_adult_declaration_so_they_leave_with_the_removal()
    {
        var player = await SeedPlayerAsync();
        var caller = await SignInAsync(player);
        Assert.NotNull(player.TermsAcceptance);

        await DeleteAsync(caller);

        // REQ-048: aceite e 18+ vivem na propria linha do Player, entao uma unica remocao do agregado os apaga.
        Assert.Null(await _players.GetById(player.Id));
    }

    [Fact]
    public async Task REQ025_the_decks_purger_is_called_for_the_player_in_the_same_unit_of_work()
    {
        var player = await SeedPlayerAsync();
        var caller = await SignInAsync(player);

        await DeleteAsync(caller);

        Assert.Equal([player.Id], _purger.Purged);
        Assert.False(_uow.IsTransactionOpen);
        Assert.True(_uow.TransactionOpenDuringLastCommit);
    }

    [Fact]
    public async Task REQ024_the_custom_avatar_image_is_removed_with_the_account_and_other_players_images_are_kept()
    {
        var player = await SeedPlayerAsync(customKey: CustomKey);
        var other = await SeedPlayerAsync("gui@example.com", "avatars/gui-1");
        var caller = await SignInAsync(player);

        await DeleteAsync(caller);

        Assert.Null(await _storage.ReadAsync(CustomKey));
        Assert.NotNull(await _storage.ReadAsync("avatars/gui-1"));
        Assert.Same(other, await _players.GetById(other.Id));
    }

    [Fact]
    public async Task REQ024_all_the_players_sessions_are_revoked_and_other_players_sessions_are_kept()
    {
        var player = await SeedPlayerAsync();
        var other = await SeedPlayerAsync("gui@example.com");
        var caller = await SignInAsync(player);
        await AddSessionAsync(player, "ana-remembered", true);
        await AddSessionAsync(other, GuiToken, false);

        await DeleteAsync(caller);

        Assert.False(await _probe.IsUsableAsync(AnaToken));
        Assert.False(await _probe.IsUsableAsync("ana-remembered"));
        Assert.True(await _probe.IsValidAsync(GuiToken));
    }

    [Fact]
    public async Task REQ024_the_deletion_calls_RevokeAllFor_once_for_the_deleted_player_with_AccountDeletion_inside_the_unit_of_work()
    {
        var player = await SeedPlayerAsync();
        var other = await SeedPlayerAsync("gui@example.com");
        var caller = await SignInAsync(player);
        await AddSessionAsync(other, GuiToken, false);

        await DeleteAsync(caller);

        var call = Assert.Single(_recordedSessions.RevokeAllForCalls);
        Assert.Equal(player.Id, call.PlayerId);
        Assert.Equal(SessionRevocationReason.AccountDeletion, call.Reason);
        Assert.Equal(Now, call.Now);
        Assert.True(call.InsideUnitOfWork);
        Assert.True(await _probe.IsValidAsync(GuiToken));
    }

    [Fact]
    public async Task REQ024_a_refused_deletion_does_not_call_RevokeAllFor()
    {
        var player = await SeedPlayerAsync();
        var caller = await SignInAsync(player);

        await DeleteAsync(caller, password: "wrong horse battery");
        await DeleteAsync(caller, confirmed: false);

        Assert.Empty(_recordedSessions.RevokeAllForCalls);
        Assert.True(await _probe.IsValidAsync(AnaToken));
    }

    [Fact]
    public async Task REQ024_the_deleted_players_session_token_no_longer_authenticates()
    {
        var player = await SeedPlayerAsync();
        var caller = await SignInAsync(player);

        await DeleteAsync(caller);

        var again = await NewAuthenticate().ExecuteAsync(new AuthenticateSessionCommand(AnaToken));
        Assert.Equal(Unauthenticated, again.Error);
    }

    [Fact]
    public async Task REQ047_only_the_attempts_keyed_by_the_players_email_hmac_are_deleted()
    {
        var player = await SeedPlayerAsync();
        await SeedPlayerAsync("gui@example.com");
        var caller = await SignInAsync(player);
        await RecordAttemptAsync(AccessAction.Login, RawEmail, "198.51.100.1");
        await RecordAttemptAsync(AccessAction.PasswordResetRequest, RawEmail, "198.51.100.1");
        await RecordAttemptAsync(AccessAction.Login, "gui@example.com", "198.51.100.2");
        await RecordAttemptAsync(AccessAction.Registration, null, "198.51.100.3");

        await DeleteAsync(caller, ip: "198.51.100.9");

        Assert.Equal(0, await AccountAttemptsAsync(RawEmail));
        Assert.Equal(0, await IpAttemptsAsync("198.51.100.1"));
        Assert.Equal(1, await AccountAttemptsAsync("gui@example.com"));
        Assert.Equal(1, await IpAttemptsAsync("198.51.100.2"));
        Assert.Equal(1, await IpAttemptsAsync("198.51.100.3"));
    }

    [Fact]
    public async Task REQ047_attempts_made_with_a_differently_formatted_email_share_the_same_account_key_and_are_deleted()
    {
        var player = await SeedPlayerAsync();
        var caller = await SignInAsync(player);
        await RecordAttemptAsync(AccessAction.Login, "  ANA@Example.com ", "198.51.100.1");

        await DeleteAsync(caller);

        Assert.Equal(0, await AccountAttemptsAsync(RawEmail));
        Assert.Equal(0, await IpAttemptsAsync("198.51.100.1"));
    }

    [Fact]
    public async Task REQ047_a_successful_deletion_is_not_itself_recorded_as_a_failed_attempt()
    {
        var player = await SeedPlayerAsync();
        var caller = await SignInAsync(player);

        await DeleteAsync(caller);

        Assert.Equal(0, await AccountAttemptsAsync(RawEmail));
        Assert.Equal(0, await IpAttemptsAsync(Ip));
        Assert.Empty(_delayer.Delays);
    }

    [Fact]
    public async Task REQ023_the_player_deleted_event_carries_only_the_player_id()
    {
        var player = await SeedPlayerAsync();
        var caller = await SignInAsync(player);

        await DeleteAsync(caller);

        var deleted = Assert.IsType<PlayerDeleted>(Assert.Single(player.DomainEvents.OfType<PlayerDeleted>()));
        Assert.Equal(player.Id, deleted.PlayerId);
        Assert.Equal(["PlayerId"], typeof(PlayerDeleted).GetProperties()
            .Select(p => p.Name)
            .Where(n => n is not nameof(DomainEvent.OccurredAt))
            .Order()
            .ToArray());
    }

    [Fact]
    public async Task REQ026_without_confirmation_it_is_required_on_confirm_and_nothing_changes()
    {
        var player = await SeedPlayerAsync(customKey: CustomKey);
        var caller = await SignInAsync(player);
        await RecordAttemptAsync(AccessAction.Login, RawEmail, "198.51.100.1");

        var result = await DeleteAsync(caller, confirmed: false);

        Assert.True(result.IsFailure);
        Assert.Equal(new DomainError("required", "confirm"), Assert.Single(result.Errors));
        await AssertAccountAndDataIntactAsync(player);
        Assert.NotNull(await _storage.ReadAsync(CustomKey));
        Assert.Equal(1, await AccountAttemptsAsync(RawEmail));
        Assert.Empty(_hasher.VerifyCalls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task REQ026_a_missing_password_is_required_on_the_password_field_and_nothing_changes(string? password)
    {
        var player = await SeedPlayerAsync();
        var caller = await SignInAsync(player);

        var result = await DeleteAsync(caller, password);

        Assert.Equal(new DomainError("required", "password"), Assert.Single(result.Errors));
        await AssertAccountAndDataIntactAsync(player);
        Assert.Empty(_hasher.VerifyCalls);
        Assert.Equal(0, await AccountAttemptsAsync(RawEmail));
    }

    [Fact]
    public async Task REQ026_missing_confirmation_and_password_report_both_fields()
    {
        var player = await SeedPlayerAsync();
        var caller = await SignInAsync(player);

        var result = await DeleteAsync(caller, null, confirmed: false);

        Assert.Contains(new DomainError("required", "confirm"), result.Errors);
        Assert.Contains(new DomainError("required", "password"), result.Errors);
        await AssertAccountAndDataIntactAsync(player);
    }

    [Fact]
    public async Task REQ038_a_129_character_password_is_too_long_on_the_password_field_and_nothing_changes()
    {
        var player = await SeedPlayerAsync();
        var caller = await SignInAsync(player);

        var result = await DeleteAsync(caller, new string('a', 129));

        Assert.Equal(new DomainError("too_long", "password"), Assert.Single(result.Errors));
        await AssertAccountAndDataIntactAsync(player);
        Assert.Empty(_hasher.VerifyCalls);
    }

    [Fact]
    public async Task REQ026_an_incorrect_password_is_incorrect_password_without_field_and_keeps_account_and_data()
    {
        var player = await SeedPlayerAsync(customKey: CustomKey);
        var caller = await SignInAsync(player);

        var result = await DeleteAsync(caller, "wrong password");

        Assert.True(result.IsFailure);
        Assert.Equal(new DomainError("incorrect_password"), Assert.Single(result.Errors));
        await AssertAccountAndDataIntactAsync(player);
        Assert.NotNull(await _storage.ReadAsync(CustomKey));
        Assert.Single(_hasher.VerifyCalls);
    }

    [Fact]
    public async Task REQ032_an_incorrect_password_records_a_failure_on_the_players_email_key_and_on_the_ip()
    {
        var player = await SeedPlayerAsync();
        var caller = await SignInAsync(player);

        await DeleteAsync(caller, "wrong password");

        Assert.Equal(1, await AccountAttemptsAsync(RawEmail));
        Assert.Equal(1, await IpAttemptsAsync(Ip));
    }

    [Fact]
    public async Task REQ032_an_incorrect_password_does_not_delete_the_existing_attempts_of_the_account()
    {
        var player = await SeedPlayerAsync();
        var caller = await SignInAsync(player);
        await RecordAttemptAsync(AccessAction.Login, RawEmail, "198.51.100.1");

        await DeleteAsync(caller, "wrong password");

        Assert.Equal(2, await AccountAttemptsAsync(RawEmail));
    }

    [Fact]
    public async Task REQ032_progressive_delay_is_applied_once_before_the_password_is_checked_after_repeated_failures()
    {
        var player = await SeedPlayerAsync();
        var caller = await SignInAsync(player);
        for (var i = 0; i < new ProgressiveDelayOptions().AccountThreshold; i++)
        {
            await RecordAttemptAsync(AccessAction.AccountDeletion, RawEmail, "198.51.100.1");
        }

        var delaysBefore = _delayer.Delays.Count;
        var result = await DeleteAsync(caller, "wrong password");

        Assert.Equal("incorrect_password", result.Error.Code);
        Assert.Equal(TimeSpan.FromSeconds(1), Assert.Single(_delayer.Delays.Skip(delaysBefore)));
        Assert.Equal(new ProgressiveDelayOptions().AccountThreshold + 1, await AccountAttemptsAsync(RawEmail));
        await AssertAccountAndDataIntactAsync(player);
    }

    [Fact]
    public async Task SEC01_over_the_account_budget_is_refused_with_too_many_attempts_without_verifying_the_password()
    {
        var player = await SeedPlayerAsync(customKey: CustomKey);
        var caller = await SignInAsync(player);
        for (var i = 0; i < 30; i++)
        {
            await RecordAttemptAsync(AccessAction.Login, RawEmail, "198.51.100.1");
        }

        var result = await DeleteAsync(caller);

        Assert.Equal(new DomainError("too_many_attempts"), Assert.Single(result.Errors));
        Assert.Empty(_hasher.VerifyCalls);
        await AssertAccountAndDataIntactAsync(player);
        Assert.Equal(30, await AccountAttemptsAsync(RawEmail)); // a recusa nao consome orcamento
    }

    [Fact]
    public async Task SEC01_validation_errors_release_the_provisional_event()
    {
        var player = await SeedPlayerAsync();
        var caller = await SignInAsync(player);

        await DeleteAsync(caller, confirmed: false);
        await DeleteAsync(caller, password: "");

        Assert.Equal(0, await AccountAttemptsAsync(RawEmail));
        Assert.Equal(0, await IpAttemptsAsync(Ip));
    }

    [Fact]
    public async Task REQ025_a_failing_decks_purger_rolls_everything_back_and_the_error_propagates()
    {
        var player = await SeedPlayerAsync(customKey: CustomKey);
        var caller = await SignInAsync(player);
        await RecordAttemptAsync(AccessAction.Login, RawEmail, "198.51.100.1");
        _purger.FailWith(new InvalidOperationException("decks unavailable"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => DeleteAsync(caller));

        await AssertAccountAndDataIntactAsync(player);
        Assert.NotNull(await _storage.ReadAsync(CustomKey));
        Assert.Equal(2, await AccountAttemptsAsync(RawEmail)); // SEC-01: excecao nao libera o provisorio
        Assert.Equal(1, await IpAttemptsAsync("198.51.100.1"));
        Assert.Equal(1, await IpAttemptsAsync(Ip));
        Assert.False(_uow.IsTransactionOpen);
    }

    [Fact]
    public async Task REQ024_repeating_the_deletion_with_the_same_caller_is_unauthenticated_and_purges_only_once()
    {
        var player = await SeedPlayerAsync();
        var caller = await SignInAsync(player);
        await DeleteAsync(caller);

        var second = await DeleteAsync(caller);

        Assert.True(second.IsFailure);
        Assert.Equal(Unauthenticated, Assert.Single(second.Errors));
        Assert.Equal([player.Id], _purger.Purged);
    }

    [Fact]
    public async Task REQ023_without_a_caller_it_is_unauthenticated_and_nothing_is_verified_or_changed()
    {
        var player = await SeedPlayerAsync();
        await SignInAsync(player);

        var result = await DeleteAsync(null);

        Assert.Equal(Unauthenticated, Assert.Single(result.Errors));
        await AssertAccountAndDataIntactAsync(player);
        Assert.Empty(_hasher.VerifyCalls);
        Assert.Equal(0, await IpAttemptsAsync(Ip));
    }

    [Fact]
    public async Task REQ027_after_deletion_a_password_login_with_the_old_credentials_is_invalid_credentials()
    {
        var player = await SeedPlayerAsync();
        var caller = await SignInAsync(player);
        await DeleteAsync(caller);
        var login = new LoginWithPassword(
            _players, _sessions, _uow, _hasher, _tokens, _tokenHasher, _catalog, NewThrottle(), _clock);

        var result = await login.ExecuteAsync(new LoginWithPasswordCommand(RawEmail, RawPassword, false, Ip));

        Assert.True(result.IsFailure);
        Assert.Equal(new DomainError("invalid_credentials"), Assert.Single(result.Errors));
    }
}
