using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;
using Deckloom.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using static Deckloom.Accounts.Application.Tests.PlayerFixtures;

namespace Deckloom.Accounts.Application.Tests;

// T-043 (REQ-008, 032, 036, 038): vinculo do Google com a senha (S5). RED: os tipos abaixo ainda nao existem.
// Contrato (design.md linha 1535, POST /auth/google/link): body {password}; 200 SessionSummary + cookie de sessao;
//   400 validation_failed (password required/too_long, nada vinculado); 401 incorrect_password; 404 pending_not_found.
// Forma esperada (convencao do T-041; a spec nao fixa nomes de tipos):
//   sealed record LinkGoogleWithPasswordCommand(string? PendingToken, string? Password, string Ip)
//       <- token do cookie __Host-dl_pending EM CLARO (design.md linhas 266, 474); Ip como no login (T-033)
//   sealed class LinkGoogleWithPassword(IPlayerRepository, ISessionRepository, IUnitOfWork, IPasswordHasher,
//       ISecureTokenGenerator, ITokenHasher, IAvatarPresetCatalog, IPendingGoogleAuthStore, AccessThrottle, TimeProvider)
//     Task<Result<AuthenticatedSession>> ExecuteAsync(LinkGoogleWithPasswordCommand, CancellationToken ct = default)
// Throttle: AccessAction.GoogleLink ja existe (AccessAction.cs; design.md linha 254, ADR-0005). Chave de conta = email da
//   pendencia (o corpo nao traz email). Como o login: BeforeAttemptAsync (atraso) antes de conferir a senha; o evento
//   provisorio gravado por ele ja e a falha (SEC-01); validacao e sucesso o liberam (ReleaseAsync). Pendencia inexistente/expirada/de signup e resolvida antes (nao ha email para a chave).
// Ordem: FindAsync (nao consome; so PendingGoogleLink) -> throttle -> valida senha (required/too_long, Field "password",
//   sem hasher) -> confere senha com CredentialVerifier (incorrect_password sem campo) -> UoW { ConsumeAsync +
//   Player.LinkGoogle(identity, pending.Email, PasswordProof) + Save player + Add session }.
public class LinkGoogleWithPasswordTests
{
    private const string Ip = "203.0.113.7";
    private const string RawEmail = "ana@example.com";
    private const string RawPassword = "correct horse battery";
    private const string PendingToken = "pending-raw-token";
    private const string Subject = "google-sub-1";
    private const int CurrentIterations = 600_000;

    private readonly InMemoryUnitOfWork _uow = new();
    private readonly InMemoryPlayerRepository _players;
    private readonly InMemorySessionRepository _sessions;
    private readonly InMemoryAccessAttemptStore _attempts;
    private readonly InMemoryPendingGoogleAuthStore _pending;
    private readonly FakePasswordHasher _hasher = new(CurrentIterations);
    private readonly SequentialTokenGenerator _tokens = new();
    private readonly Sha256TokenHasher _tokenHasher = new();
    private readonly FakeKeyedHasher _keyedHasher = new();
    private readonly RecordingDelayer _delayer = new();
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly FakeGoogleIdentityVerifier _google = new();
    private readonly InMemoryAvatarPresetCatalog _catalog = new(
    [
        new AvatarPresetEntry("preset-01", "Dragao", "/avatars/preset-01.png", 1, false, true),
        new AvatarPresetEntry("preset-02", "Planeswalker", "/avatars/preset-02.png", 2, true, true),
    ]);

    public LinkGoogleWithPasswordTests()
    {
        _players = new InMemoryPlayerRepository(_uow);
        _sessions = new InMemorySessionRepository(_uow);
        _attempts = new InMemoryAccessAttemptStore(_uow);
        _pending = new InMemoryPendingGoogleAuthStore(_clock, _uow);
    }

    private AccessThrottle NewThrottle() =>
        new(_attempts, _keyedHasher, _delayer, new ProgressiveDelayPolicy(new ProgressiveDelayOptions()), _clock);

    private LinkGoogleWithPassword NewUseCase() =>
        new(_players, _sessions, _uow, _hasher, _tokens, _tokenHasher, _catalog, _pending, NewThrottle(), _clock);

    private static LinkGoogleWithPasswordCommand ValidCommand(string? password = RawPassword) =>
        new(PendingToken, password, Ip);

    private async Task<Player> SeedPasswordPlayerAsync()
    {
        var hash = await _hasher.HashAsync(Password.Create(RawPassword).Value);
        var player = Player.RegisterWithPassword(
            AnEmail(RawEmail), hash, DisplayName.Create("Ana").Value, "2026-10-01", "2026-10-01", true, true, Now, "preset-01").Value;
        await _players.Add(player);
        await _uow.CommitAsync();
        return player;
    }

    private async Task SeedLinkPendingAsync(Player player) =>
        await _pending.AddAsync(PendingGoogleLink.Create(
            _tokenHasher.Hash(PendingToken), Subject, AnEmail(RawEmail), player.Id, Now));

    private async Task<Player> SeedPlayerWithPendingAsync()
    {
        var player = await SeedPasswordPlayerAsync();
        await SeedLinkPendingAsync(player);
        return player;
    }

    private async Task<bool> PendingStillStoredAsync() =>
        await _pending.FindAsync(_tokenHasher.Hash(PendingToken)) is not null;

    private async Task<bool> AnySessionOpenedAsync() =>
        await _sessions.FindByTokenHash(_tokenHasher.Hash("tok-0001")) is not null;

    private Task<int> AccountFailuresAsync() =>
        _attempts.CountByAccountAsync(_keyedHasher.Hash(RawEmail), Now.AddHours(-1));

    private Task<int> IpFailuresAsync() =>
        _attempts.CountByIpAsync(_keyedHasher.Hash(Ip), Now.AddHours(-1));

    private async Task AssertNothingLinkedNoSessionPendingKeptAsync(Player player)
    {
        Assert.Null(player.ExternalIdentity);
        Assert.Null(await _players.FindByGoogleSubject(Subject));
        Assert.False(await AnySessionOpenedAsync());
        Assert.True(await PendingStillStoredAsync());
    }

    [Fact]
    public async Task REQ008_correct_password_links_the_google_identity_to_the_account()
    {
        var player = await SeedPlayerWithPendingAsync();

        var result = await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.True(result.IsSuccess);
        Assert.Equal("google", player.ExternalIdentity!.Provider);
        Assert.Equal(Subject, player.ExternalIdentity.Subject);
        Assert.Same(player, await _players.FindByGoogleSubject(Subject));
        Assert.NotNull(player.PasswordHash);
    }

    [Fact]
    public async Task REQ036_correct_password_opens_a_session_without_remember_me_and_returns_the_summary()
    {
        var player = await SeedPlayerWithPendingAsync();

        var result = await NewUseCase().ExecuteAsync(ValidCommand());

        var session = await _sessions.FindByTokenHash(_tokenHasher.Hash("tok-0001"));
        Assert.NotNull(session);
        Assert.Equal(player.Id, session!.PlayerId);
        Assert.False(session.RememberMe);
        Assert.Equal("tok-0001", result.Value.SessionToken);
        Assert.Equal(session.ExpiresAt, result.Value.ExpiresAt);
        Assert.Equal("Ana", result.Value.Summary.DisplayName);
        Assert.True(result.Value.Summary.HasPassword);
        Assert.True(result.Value.Summary.HasGoogle);
    }

    [Fact]
    public async Task REQ036_correct_password_consumes_the_pending_in_the_same_unit_of_work()
    {
        await SeedPlayerWithPendingAsync();

        await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.False(await PendingStillStoredAsync());
        Assert.False(_uow.IsTransactionOpen);
        Assert.True(_uow.TransactionOpenDuringLastCommit);
    }

    [Fact]
    public async Task REQ036_the_pending_is_single_use_a_second_submission_is_pending_not_found()
    {
        await SeedPlayerWithPendingAsync();
        await NewUseCase().ExecuteAsync(ValidCommand());

        var second = await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.True(second.IsFailure);
        Assert.Equal("pending_not_found", second.Error.Code);
    }

    [Fact]
    public async Task REQ008_a_future_google_login_enters_the_linked_account_without_a_new_pending()
    {
        var player = await SeedPlayerWithPendingAsync();
        await NewUseCase().ExecuteAsync(ValidCommand());
        _google.Returns(new GoogleVerifiedIdentity(Subject, AnEmail(RawEmail), true, "Ana Google"));
        var callback = new HandleGoogleCallback(
            _players, _sessions, _uow, _google, _tokens, _tokenHasher, _catalog, _pending, _clock);

        var result = await callback.ExecuteAsync(
            new HandleGoogleCallbackCommand("code", "state", null, new GoogleOAuthCorrelation("state", "nonce", "verifier")));

        var ok = Assert.IsType<GoogleCallbackOk>(result.Value);
        var session = await _sessions.FindByTokenHash(_tokenHasher.Hash(ok.Session.SessionToken));
        Assert.Equal(player.Id, session!.PlayerId);
    }

    [Fact]
    public async Task REQ032_incorrect_password_is_incorrect_password_without_field_and_links_nothing()
    {
        var player = await SeedPlayerWithPendingAsync();

        var result = await NewUseCase().ExecuteAsync(ValidCommand("wrong password"));

        Assert.True(result.IsFailure);
        Assert.Equal(new DomainError("incorrect_password"), Assert.Single(result.Errors));
        await AssertNothingLinkedNoSessionPendingKeptAsync(player);
    }

    [Fact]
    public async Task REQ032_incorrect_password_records_a_failure_on_the_pending_email_and_on_the_ip()
    {
        await SeedPlayerWithPendingAsync();

        await NewUseCase().ExecuteAsync(ValidCommand("wrong password"));

        Assert.Equal(1, await AccountFailuresAsync());
        Assert.Equal(1, await IpFailuresAsync());
    }

    [Fact]
    public async Task REQ032_progressive_delay_is_applied_once_before_the_password_is_checked_after_repeated_failures()
    {
        await SeedPlayerWithPendingAsync();
        var throttle = NewThrottle();
        for (var i = 0; i < new ProgressiveDelayOptions().AccountThreshold; i++)
        {
            // SEC-01: o evento provisorio gravado por BeforeAttemptAsync ja e a falha.
            await throttle.BeforeAttemptAsync(AccessAction.GoogleLink, RawEmail, "198.51.100.1");
        }

        var delaysBefore = _delayer.Delays.Count;
        var result = await NewUseCase().ExecuteAsync(ValidCommand("wrong password"));

        Assert.Equal("incorrect_password", result.Error.Code);
        Assert.Equal(TimeSpan.FromSeconds(1), Assert.Single(_delayer.Delays.Skip(delaysBefore)));
        Assert.Equal(new ProgressiveDelayOptions().AccountThreshold + 1, await AccountFailuresAsync());
        Assert.True(await PendingStillStoredAsync());
    }

    [Fact]
    public async Task REQ032_a_correct_password_without_prior_failures_is_not_delayed_nor_recorded()
    {
        await SeedPlayerWithPendingAsync();

        await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.Empty(_delayer.Delays);
        Assert.Equal(0, await AccountFailuresAsync());
        Assert.Equal(0, await IpFailuresAsync());
    }

    [Fact]
    public async Task SEC01_validation_errors_release_the_provisional_event()
    {
        await SeedPlayerWithPendingAsync();

        await NewUseCase().ExecuteAsync(ValidCommand(""));
        await NewUseCase().ExecuteAsync(ValidCommand(new string('a', 129)));

        Assert.Equal(0, await AccountFailuresAsync());
        Assert.Equal(0, await IpFailuresAsync());
    }

    [Fact]
    public async Task SEC01_a_successful_link_releases_only_its_own_provisional_event()
    {
        await SeedPlayerWithPendingAsync();
        await NewThrottle().BeforeAttemptAsync(AccessAction.GoogleLink, RawEmail, "198.51.100.1");

        var result = await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.True(result.IsSuccess);
        Assert.Equal(1, await AccountFailuresAsync());
        Assert.Equal(0, await IpFailuresAsync());
    }

    [Fact]
    public async Task SEC01_over_the_account_budget_is_refused_with_too_many_attempts_without_verifying_the_password()
    {
        var player = await SeedPlayerWithPendingAsync();
        for (var i = 0; i < 30; i++)
        {
            await NewThrottle().BeforeAttemptAsync(AccessAction.GoogleLink, RawEmail, "198.51.100.1");
        }

        var result = await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.Equal(new DomainError("too_many_attempts"), Assert.Single(result.Errors));
        Assert.Empty(_hasher.VerifyCalls);
        await AssertNothingLinkedNoSessionPendingKeptAsync(player);
        Assert.Equal(30, await AccountFailuresAsync());
    }

    [Fact]
    public async Task REQ038_a_129_character_password_is_too_long_on_the_password_field_and_nothing_is_linked_or_consumed()
    {
        var player = await SeedPlayerWithPendingAsync();

        var result = await NewUseCase().ExecuteAsync(ValidCommand(new string('a', 129)));

        Assert.True(result.IsFailure);
        Assert.Equal(new DomainError("too_long", "password"), Assert.Single(result.Errors));
        Assert.Empty(_hasher.VerifyCalls);
        await AssertNothingLinkedNoSessionPendingKeptAsync(player);
    }

    [Fact]
    public async Task REQ038_a_128_character_password_is_accepted_for_verification_and_is_just_incorrect()
    {
        await SeedPlayerWithPendingAsync();

        var result = await NewUseCase().ExecuteAsync(ValidCommand(new string('a', 128)));

        Assert.Equal("incorrect_password", result.Error.Code);
        Assert.Single(_hasher.VerifyCalls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task REQ036_a_missing_password_is_required_on_the_password_field_and_the_pending_is_kept(string? password)
    {
        var player = await SeedPlayerWithPendingAsync();

        var result = await NewUseCase().ExecuteAsync(ValidCommand(password));

        Assert.Equal(new DomainError("required", "password"), Assert.Single(result.Errors));
        await AssertNothingLinkedNoSessionPendingKeptAsync(player);
    }

    [Fact]
    public async Task REQ036_an_expired_pending_is_pending_not_found_and_links_nothing()
    {
        var player = await SeedPlayerWithPendingAsync();
        _clock.Advance(PendingGoogleAuth.Ttl + TimeSpan.FromSeconds(1));

        var result = await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.True(result.IsFailure);
        Assert.Equal("pending_not_found", result.Error.Code);
        Assert.Null(player.ExternalIdentity);
        Assert.Null(await _players.FindByGoogleSubject(Subject));
        Assert.False(await AnySessionOpenedAsync());
    }

    [Theory]
    [InlineData("another-token")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task REQ036_an_unknown_or_missing_pending_token_is_pending_not_found(string? token)
    {
        var player = await SeedPlayerWithPendingAsync();

        var result = await NewUseCase().ExecuteAsync(ValidCommand() with { PendingToken = token });

        Assert.Equal("pending_not_found", result.Error.Code);
        await AssertNothingLinkedNoSessionPendingKeptAsync(player);
    }

    [Fact]
    public async Task REQ036_a_pending_signup_cannot_be_used_to_link_and_is_not_consumed()
    {
        var player = await SeedPasswordPlayerAsync();
        await _pending.AddAsync(PendingGoogleSignup.Create(
            _tokenHasher.Hash(PendingToken), Subject, AnEmail(RawEmail), "Ana Google", Now));

        var result = await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.Equal("pending_not_found", result.Error.Code);
        await AssertNothingLinkedNoSessionPendingKeptAsync(player);
    }
}
