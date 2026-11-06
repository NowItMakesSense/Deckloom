using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;
using Microsoft.Extensions.Time.Testing;
using static Deckloom.Accounts.Application.Tests.PlayerFixtures;

namespace Deckloom.Accounts.Application.Tests;

// T-137 (habilitadora de DB9/UA3; REQ-001, 005, 006, 019, 023, 028, 030, 032, 042): nenhuma transacao fica aberta durante
// atraso, hash de senha, chamada ao Google, envio de e-mail e processamento de imagem. A transacao de IUnitOfWork so envolve
// a persistencia (design.md, "Resilience"). Mecanismo: cada duble (RecordingDelayer, FakePasswordHasher,
// FakeGoogleIdentityVerifier, RecordingPasswordResetNotifier, FakeAvatarImageProcessor, InMemoryAccessAttemptStore) registra
// InMemoryUnitOfWork.IsTransactionOpen a cada chamada (TransactionProbe). Cada teste exige (a) que a chamada tenha ocorrido
// e (b) que nenhuma tenha visto transacao aberta; e que o commit tenha ocorrido COM a transacao aberta.
public class NoOpenTransactionTests
{
    private const string Ip = "203.0.113.7";
    private const string RawEmail = "ana@example.com";
    private const string RawPassword = "correct horse battery";
    private const string NewPassword = "brand new password";
    private const string TermsVersion = "2026-10-01";
    private const string PendingToken = "pending-raw-token";
    private const string ResetToken = "reset-token-raw";
    private const string SessionToken = "ana-session-token";
    private const string Subject = "google-sub-1";
    private const string State = "state-abc";
    private const string Nonce = "nonce-xyz";
    private const string Verifier = "verifier-123";
    private const string AuthCode = "auth-code-1";
    private const int CurrentIterations = 600_000;
    private const int OldIterations = 100_000;

    private readonly InMemoryUnitOfWork _uow = new();
    private readonly InMemoryPlayerRepository _players;
    private readonly InMemorySessionRepository _sessions;
    private readonly InMemoryAccessAttemptStore _attempts;
    private readonly InMemoryPendingGoogleAuthStore _pending;
    private readonly InMemoryAvatarStorage _storage;
    private readonly RecordingPlayerDecksPurger _purger;
    private readonly FakePasswordHasher _hasher = new(CurrentIterations);
    private readonly SequentialTokenGenerator _tokens = new();
    private readonly Sha256TokenHasher _tokenHasher = new();
    private readonly FakeKeyedHasher _keyedHasher = new();
    private readonly RecordingDelayer _delayer = new();
    private readonly RecordingPasswordResetNotifier _notifier = new();
    private readonly FakeGoogleIdentityVerifier _google = new();
    private readonly FakeAvatarImageProcessor _processor = new();
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly InMemoryAvatarPresetCatalog _catalog = new(
    [
        new AvatarPresetEntry("preset-01", "Dragao", "/avatars/preset-01.png", 1, false, true),
        new AvatarPresetEntry("preset-02", "Planeswalker", "/avatars/preset-02.png", 2, true, true),
    ]);

    private readonly FixedTermsVersionProvider _terms = new(
        TermsVersion, new Uri("https://deckloom.test/termos"), new Uri("https://deckloom.test/privacidade"));

    public NoOpenTransactionTests()
    {
        _players = new InMemoryPlayerRepository(_uow);
        _sessions = new InMemorySessionRepository(_uow);
        _attempts = new InMemoryAccessAttemptStore(_uow);
        _pending = new InMemoryPendingGoogleAuthStore(_clock, _uow);
        _storage = new InMemoryAvatarStorage(_uow);
        _purger = new RecordingPlayerDecksPurger(_uow);
        _processor.AcceptWith([7, 7, 7]);
        foreach (var probe in Probes)
        {
            probe.Observe(_uow);
        }
    }

    private IEnumerable<TransactionProbe> Probes =>
    [
        _delayer.TransactionProbe,
        _hasher.TransactionProbe,
        _google.TransactionProbe,
        _notifier.TransactionProbe,
        _processor.TransactionProbe,
        _attempts.TransactionProbe,
    ];

    private AccessThrottle NewThrottle() =>
        new(_attempts, _keyedHasher, _delayer, new ProgressiveDelayPolicy(new ProgressiveDelayOptions()), _clock);

    private void AssertOutsideTransaction(params string[] expectedCalls)
    {
        var samples = Probes.SelectMany(p => p.Samples).ToList();
        foreach (var call in expectedCalls)
        {
            Assert.True(samples.Any(s => s.Call == call), $"A chamada '{call}' deveria ter ocorrido e nao ocorreu.");
        }

        var violations = samples.Where(s => s.TransactionOpen).Select(s => s.Call).ToList();
        Assert.True(
            violations.Count == 0,
            $"Chamadas com a transacao aberta: {string.Join(", ", violations)}.");
    }

    private void AssertPersistenceWasInsideTransaction() =>
        Assert.True(_uow.TransactionOpenDuringLastCommit, "O commit deveria ocorrer com a transacao aberta.");

    // ---- sementes ----

    private async Task SeedAttemptsAsync(AccessAction action, string? email, int count)
    {
        var accountKey = email is null || action == AccessAction.Registration
            ? null
            : _keyedHasher.Hash(email.Trim().ToLowerInvariant());
        for (var i = 0; i < count; i++)
        {
            await _attempts.RecordAsync(new AccessAttempt(action, _keyedHasher.Hash(Ip), accountKey, Now));
        }

        await _uow.CommitAsync();
    }

    private async Task<Player> SeedPasswordPlayerAsync(int iterations = CurrentIterations, string email = RawEmail)
    {
        var hash = await new FakePasswordHasher(iterations).HashAsync(Password.Create(RawPassword).Value);
        var player = Player.RegisterWithPassword(
            AnEmail(email), hash, DisplayName.Create("Ana").Value, TermsVersion, TermsVersion, true, true, Now, "preset-01").Value;
        await _players.Add(player);
        await _uow.CommitAsync();
        return player;
    }

    private async Task<AuthenticatedPlayer> SignInAsync(Player player)
    {
        await _sessions.Add(Session.Start(player.Id, _tokenHasher.Hash(SessionToken), false, Now));
        await _uow.CommitAsync();
        var authenticate = new AuthenticateSession(_sessions, _players, _uow, _tokenHasher, _catalog, _clock);
        return (await authenticate.ExecuteAsync(new AuthenticateSessionCommand(SessionToken))).Value;
    }

    private static GoogleVerifiedIdentity Verified(string email = "bia@example.com") =>
        new(Subject, AnEmail(email), true, "Beatriz Souza");

    private static HandleGoogleCallbackCommand GoogleCommand() =>
        new(AuthCode, State, null, new GoogleOAuthCorrelation(State, Nonce, Verifier));

    private HandleGoogleCallback NewGoogleCallback() =>
        new(_players, _sessions, _uow, _google, _tokens, _tokenHasher, _catalog, _pending, _clock);

    // ---- LoginWithPassword ----

    private LoginWithPassword NewLogin() =>
        new(_players, _sessions, _uow, _hasher, _tokens, _tokenHasher, _catalog, NewThrottle(), _clock);

    [Fact]
    public async Task REQ032_login_runs_the_throttle_reads_and_the_delay_outside_any_transaction()
    {
        await SeedPasswordPlayerAsync();
        await SeedAttemptsAsync(AccessAction.Login, RawEmail, 3);

        var result = await NewLogin().ExecuteAsync(new LoginWithPasswordCommand(RawEmail, RawPassword, false, Ip));

        Assert.True(result.IsSuccess);
        AssertOutsideTransaction("CountByIp", "CountByAccount", "Delay", "Verify");
        AssertPersistenceWasInsideTransaction();
    }

    [Fact]
    public async Task REQ005_login_rehash_after_verification_runs_outside_any_transaction()
    {
        await SeedPasswordPlayerAsync(OldIterations);

        var result = await NewLogin().ExecuteAsync(new LoginWithPasswordCommand(RawEmail, RawPassword, false, Ip));

        Assert.True(result.IsSuccess);
        AssertOutsideTransaction("Verify", "Hash");
        AssertPersistenceWasInsideTransaction();
    }

    // ---- RegisterWithPassword ----

    private RegisterWithPassword NewRegister() =>
        new(_players, _sessions, _uow, _hasher, _tokens, _tokenHasher, _terms, _catalog, NewThrottle(), _clock);

    [Fact]
    public async Task REQ032_registration_runs_the_throttle_read_and_the_delay_outside_any_transaction()
    {
        await SeedAttemptsAsync(AccessAction.Registration, null, 10);

        var result = await NewRegister().ExecuteAsync(
            new RegisterWithPasswordCommand(RawEmail, RawPassword, "Ana", true, TermsVersion, true, Ip));

        Assert.True(result.IsSuccess);
        AssertOutsideTransaction("CountByIp", "Delay");
        AssertPersistenceWasInsideTransaction();
    }

    [Fact]
    public async Task REQ001_registration_password_hash_runs_outside_any_transaction()
    {
        var result = await NewRegister().ExecuteAsync(
            new RegisterWithPasswordCommand(RawEmail, RawPassword, "Ana", true, TermsVersion, true, Ip));

        Assert.True(result.IsSuccess);
        AssertOutsideTransaction("Hash");
        AssertPersistenceWasInsideTransaction();
    }

    // ---- RequestPasswordReset ----

    private RequestPasswordReset NewRequestReset() =>
        new(
            _players,
            _uow,
            _tokens,
            _tokenHasher,
            _notifier,
            NewThrottle(),
            new PasswordResetOptions { PublicBaseUrl = "https://app.deckloom.test" },
            _clock);

    [Fact]
    public async Task REQ032_reset_request_runs_the_throttle_reads_and_the_delay_outside_any_transaction()
    {
        await SeedPasswordPlayerAsync();
        await SeedAttemptsAsync(AccessAction.PasswordResetRequest, RawEmail, 3);

        var result = await NewRequestReset().ExecuteAsync(new RequestPasswordResetCommand(RawEmail, Ip));

        Assert.True(result.IsSuccess);
        AssertOutsideTransaction("CountByIp", "CountByAccount", "Delay");
        AssertPersistenceWasInsideTransaction();
    }

    [Fact]
    public async Task REQ028_reset_request_sends_the_email_outside_any_transaction()
    {
        await SeedPasswordPlayerAsync();

        var result = await NewRequestReset().ExecuteAsync(new RequestPasswordResetCommand(RawEmail, Ip));

        Assert.True(result.IsSuccess);
        Assert.Single(_notifier.Sent);
        AssertOutsideTransaction("Notify");
        AssertPersistenceWasInsideTransaction();
    }

    // ---- ResetPassword ----

    [Fact]
    public async Task REQ030_reset_password_hash_runs_outside_any_transaction()
    {
        var player = APasswordPlayer();
        Assert.True(player.IssuePasswordReset(_tokenHasher.Hash(ResetToken), Now).IsSuccess);
        await _players.Add(player);
        await _uow.CommitAsync();
        var useCase = new ResetPassword(_players, _sessions, _uow, _hasher, _tokenHasher, _clock);

        var result = await useCase.ExecuteAsync(new ResetPasswordCommand(ResetToken, NewPassword));

        Assert.True(result.IsSuccess);
        AssertOutsideTransaction("Hash");
        AssertPersistenceWasInsideTransaction();
    }

    // ---- LinkGoogleWithPassword ----

    [Fact]
    public async Task REQ032_link_google_runs_the_throttle_read_the_delay_and_the_password_check_outside_any_transaction()
    {
        var player = await SeedPasswordPlayerAsync();
        await _pending.AddAsync(PendingGoogleLink.Create(
            _tokenHasher.Hash(PendingToken), Subject, AnEmail(RawEmail), player.Id, Now));
        await SeedAttemptsAsync(AccessAction.GoogleLink, RawEmail, 3);
        var useCase = new LinkGoogleWithPassword(
            _players, _sessions, _uow, _hasher, _tokens, _tokenHasher, _catalog, _pending, NewThrottle(), _clock);

        var result = await useCase.ExecuteAsync(new LinkGoogleWithPasswordCommand(PendingToken, RawPassword, Ip));

        Assert.True(result.IsSuccess);
        AssertOutsideTransaction("CountByIp", "CountByAccount", "Delay", "Verify");
        AssertPersistenceWasInsideTransaction();
    }

    // ---- DeleteMyAccount ----

    private DeleteMyAccount NewDelete() =>
        new(_players, _sessions, _purger, _storage, _uow, _hasher, NewThrottle(), _clock);

    [Fact]
    public async Task REQ032_account_deletion_runs_the_throttle_reads_the_delay_and_the_password_check_before_the_transaction()
    {
        var player = await SeedPasswordPlayerAsync();
        var caller = await SignInAsync(player);
        await SeedAttemptsAsync(AccessAction.AccountDeletion, RawEmail, 3);

        var result = await NewDelete().ExecuteAsync(new DeleteMyAccountCommand(caller, RawPassword, true, Ip));

        Assert.True(result.IsSuccess);
        AssertOutsideTransaction("CountByIp", "CountByAccount", "Delay", "Verify");
        AssertPersistenceWasInsideTransaction();
    }

    [Fact]
    public async Task REQ023_account_deletion_stays_atomic_with_the_purger_and_the_removals_in_the_same_transaction()
    {
        var player = await SeedPasswordPlayerAsync();
        var caller = await SignInAsync(player);

        var result = await NewDelete().ExecuteAsync(new DeleteMyAccountCommand(caller, RawPassword, true, Ip));

        Assert.True(result.IsSuccess);
        AssertPersistenceWasInsideTransaction();
        Assert.Equal([player.Id], _purger.Purged);
        Assert.Null(await _players.GetById(player.Id));
    }

    [Fact]
    public async Task REQ023_account_deletion_rolls_everything_back_when_the_purger_fails_inside_the_transaction()
    {
        var player = await SeedPasswordPlayerAsync();
        var caller = await SignInAsync(player);
        _purger.FailWith(new InvalidOperationException("purger down"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewDelete().ExecuteAsync(new DeleteMyAccountCommand(caller, RawPassword, true, Ip)));

        Assert.Empty(_purger.Purged);
        Assert.NotNull(await _players.GetById(player.Id));
        Assert.True((await _sessions.FindByTokenHash(_tokenHasher.Hash(SessionToken)))!.IsValid(Now));
    }

    // ---- CompleteGoogleSignup ----

    [Fact]
    public async Task REQ006_google_signup_completion_calls_no_password_hash_and_persists_inside_the_transaction()
    {
        await _pending.AddAsync(PendingGoogleSignup.Create(
            _tokenHasher.Hash(PendingToken), Subject, AnEmail("bia@example.com"), "Beatriz Souza", Now));
        var useCase = new CompleteGoogleSignup(
            _players, _sessions, _uow, _tokens, _tokenHasher, _terms, _catalog, _pending, _clock);

        var result = await useCase.ExecuteAsync(
            new CompleteGoogleSignupCommand(PendingToken, "Beatriz", true, TermsVersion, true));

        Assert.True(result.IsSuccess);
        Assert.Empty(_hasher.TransactionProbe.Samples);
        AssertOutsideTransaction();
        AssertPersistenceWasInsideTransaction();
    }

    // ---- HandleGoogleCallback ----

    [Fact]
    public async Task REQ006_google_callback_for_a_known_subject_verifies_with_google_outside_any_transaction()
    {
        await _players.Add(AGooglePlayer("bia@example.com", Subject));
        await _uow.CommitAsync();
        _google.Returns(Verified());

        var result = await NewGoogleCallback().ExecuteAsync(GoogleCommand());

        Assert.IsType<GoogleCallbackOk>(result.Value);
        AssertOutsideTransaction("Complete");
        AssertPersistenceWasInsideTransaction();
    }

    [Fact]
    public async Task REQ006_google_callback_for_an_unknown_subject_verifies_with_google_outside_any_transaction()
    {
        _google.Returns(Verified());

        var result = await NewGoogleCallback().ExecuteAsync(GoogleCommand());

        Assert.IsType<GoogleCallbackConclude>(result.Value);
        AssertOutsideTransaction("Complete");
        AssertPersistenceWasInsideTransaction();
    }

    [Fact]
    public async Task REQ042_google_reauthentication_verifies_with_google_outside_any_transaction()
    {
        var player = AGooglePlayer("gui@example.com", Subject);
        await _players.Add(player);
        await _sessions.Add(Session.Start(player.Id, _tokenHasher.Hash(SessionToken), false, Now));
        await _uow.CommitAsync();
        _google.Returns(new GoogleVerifiedIdentity(Subject, AnEmail("gui@example.com"), true, "Gui", Now));
        var command = new HandleGoogleCallbackCommand(
            AuthCode,
            State,
            null,
            new GoogleOAuthCorrelation(State, Nonce, Verifier, GoogleAuthIntent.Reauth, player.Id),
            SessionToken);

        var result = await NewGoogleCallback().ExecuteAsync(command);

        Assert.IsType<GoogleCallbackReauthenticated>(result.Value);
        AssertOutsideTransaction("Complete");
        AssertPersistenceWasInsideTransaction();
    }

    // ---- UploadCustomAvatar ----

    [Fact]
    public async Task REQ019_custom_avatar_image_processing_runs_outside_any_transaction()
    {
        var player = await SeedPasswordPlayerAsync();
        var caller = await SignInAsync(player);
        var useCase = new UploadCustomAvatar(_players, _catalog, _processor, _storage, _uow);

        var result = await useCase.ExecuteAsync(new UploadCustomAvatarCommand(caller, [10, 20, 30]));

        Assert.True(result.IsSuccess);
        AssertOutsideTransaction("Process");
        AssertPersistenceWasInsideTransaction();
    }
}
