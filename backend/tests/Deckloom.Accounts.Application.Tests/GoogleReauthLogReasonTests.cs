using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;
using Microsoft.Extensions.Time.Testing;
using static Deckloom.Accounts.Application.Tests.PlayerFixtures;

namespace Deckloom.Accounts.Application.Tests;

// T-151 (RED): R-41 / REQ-042, REQ-026 (design, catalogo do evento 1010 e "Forma da reautenticacao Google", passo 1).
// Contrato do evento 1010 (GoogleAuthFailed(stage, reason)) na reautenticacao (intent Reauth):
//   - callback ACEITAVEL e sessao ausente / nao encontrada / expirada / revogada, ou PlayerId do cookie diferente do
//     da sessao -> stage "callback", reason "session_invalid" (hoje registra "subject_mismatch");
//   - callback NAO aceitavel (error, state divergente, code ausente) -> motivo do callback, como hoje, mesmo com sessao
//     invalida (cancelled/provider_error/state_mismatch);
//   - passo 4 (Jogador inexistente, conta sem identidade Google, subject invalido) e subject diferente do vinculado ->
//     stage "id_token", reason "subject_mismatch" (nunca session_invalid).
// O desfecho para o usuario (GoogleCallbackReauthFailed, nada marcado, REQ-026) nao muda.
// Lacunas assumidas: (1) PlayerId ausente (nulo) no cookie conta como "diferente do da sessao" (design, passo 1:
// "presente e igual"); (2) a exigencia "IsAcceptable avaliado uma vez por callback" nao e observavel por um teste de
// unidade (o criterio e uma funcao estatica privada, sem verificador/colaborador que o conte), fica para a revisao
// do GREEN; (3) o coletor registra so o evento 1010 (nao ha dubles prontos de IAccountsEventLog no repositorio).
public class GoogleReauthLogReasonTests
{
    private const string Subject = "google-sub-1";
    private const string State = "state-abc";
    private const string Nonce = "nonce-xyz";
    private const string Verifier = "verifier-123";
    private const string AuthCode = "auth-code-1";
    private const string SessionToken = "existing-session-token";

    private readonly InMemoryUnitOfWork _uow = new();
    private readonly InMemoryPlayerRepository _players;
    private readonly InMemorySessionRepository _sessions;
    private readonly FakeGoogleIdentityVerifier _google = new();
    private readonly Sha256TokenHasher _tokenHasher = new();
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly RecordingEventLog _events = new();
    private readonly InMemoryAvatarPresetCatalog _catalog = new(
    [
        new AvatarPresetEntry("preset-01", "Dragao", "/avatars/preset-01.png", 1, true, true),
    ]);

    public GoogleReauthLogReasonTests()
    {
        _players = new InMemoryPlayerRepository(_uow);
        _sessions = new InMemorySessionRepository(_uow);
    }

    private HandleGoogleCallback NewUseCase() =>
        new(
            _players,
            _sessions,
            _uow,
            _google,
            new SequentialTokenGenerator(),
            _tokenHasher,
            _catalog,
            new InMemoryPendingGoogleAuthStore(_clock, _uow),
            _clock,
            _events);

    private static HandleGoogleCallbackCommand Command(
        PlayerId? cookiePlayer,
        string? sessionToken = SessionToken,
        string? code = AuthCode,
        string? state = State,
        string? error = null) =>
        new(code, state, error, new GoogleOAuthCorrelation(State, Nonce, Verifier, GoogleAuthIntent.Reauth, cookiePlayer), sessionToken);

    private static GoogleVerifiedIdentity Verified(string subject = Subject) =>
        new(subject, AnEmail("gui@example.com"), true, "Gui Google", Now);

    private async Task<(Player Player, Session Session)> SeedGoogleAsync()
    {
        var player = AGooglePlayer("gui@example.com", Subject);
        await _players.Add(player);
        var session = Session.Start(player.Id, _tokenHasher.Hash(SessionToken), false, Now);
        await _sessions.Add(session);
        await _uow.CommitAsync();
        return (player, session);
    }

    private void AssertSingleFailure(string stage, string reason)
    {
        var failure = Assert.Single(_events.Failures);
        Assert.Equal((stage, reason), failure);
    }

    [Fact]
    public async Task R41_missing_session_token_logs_callback_session_invalid()
    {
        var (player, session) = await SeedGoogleAsync();
        _google.Returns(Verified());

        var result = await NewUseCase().ExecuteAsync(Command(player.Id, sessionToken: null));

        Assert.IsType<GoogleCallbackReauthFailed>(result.Value);
        AssertSingleFailure("callback", "session_invalid");
        Assert.Null(session.ReauthenticatedAt);
    }

    [Fact]
    public async Task R41_unknown_session_token_logs_callback_session_invalid()
    {
        var (player, _) = await SeedGoogleAsync();
        _google.Returns(Verified());

        var result = await NewUseCase().ExecuteAsync(Command(player.Id, sessionToken: "not-a-session"));

        Assert.IsType<GoogleCallbackReauthFailed>(result.Value);
        AssertSingleFailure("callback", "session_invalid");
    }

    [Fact]
    public async Task R41_expired_session_logs_callback_session_invalid()
    {
        var (player, session) = await SeedGoogleAsync();
        _clock.Advance(TimeSpan.FromMinutes(31));
        _google.Returns(Verified());

        var result = await NewUseCase().ExecuteAsync(Command(player.Id));

        Assert.IsType<GoogleCallbackReauthFailed>(result.Value);
        AssertSingleFailure("callback", "session_invalid");
        Assert.Null(session.ReauthenticatedAt);
    }

    [Fact]
    public async Task R41_revoked_session_logs_callback_session_invalid()
    {
        var (player, session) = await SeedGoogleAsync();
        session.Revoke(SessionRevocationReason.Logout, Now);
        _google.Returns(Verified());

        var result = await NewUseCase().ExecuteAsync(Command(player.Id));

        Assert.IsType<GoogleCallbackReauthFailed>(result.Value);
        AssertSingleFailure("callback", "session_invalid");
    }

    [Fact]
    public async Task R41_cookie_player_different_from_the_session_owner_logs_callback_session_invalid()
    {
        var (_, session) = await SeedGoogleAsync();
        var other = AGooglePlayer("other@example.com", "other-sub");
        await _players.Add(other);
        await _uow.CommitAsync();
        _google.Returns(Verified());

        var result = await NewUseCase().ExecuteAsync(Command(other.Id));

        Assert.IsType<GoogleCallbackReauthFailed>(result.Value);
        AssertSingleFailure("callback", "session_invalid");
        Assert.Null(session.ReauthenticatedAt);
    }

    [Fact]
    public async Task R41_cookie_without_player_id_logs_callback_session_invalid()
    {
        await SeedGoogleAsync();
        _google.Returns(Verified());

        var result = await NewUseCase().ExecuteAsync(Command(cookiePlayer: null));

        Assert.IsType<GoogleCallbackReauthFailed>(result.Value);
        AssertSingleFailure("callback", "session_invalid");
    }

    [Fact]
    public async Task R41_session_failures_never_call_google()
    {
        var (player, _) = await SeedGoogleAsync();
        _google.Returns(Verified());

        await NewUseCase().ExecuteAsync(Command(player.Id, sessionToken: null));

        Assert.Empty(_google.CompleteCalls);
    }

    [Fact]
    public async Task R41_unacceptable_state_with_an_invalid_session_keeps_the_callback_reason()
    {
        var (player, _) = await SeedGoogleAsync();

        await NewUseCase().ExecuteAsync(Command(player.Id, sessionToken: null, state: "other-state"));

        AssertSingleFailure("callback", "state_mismatch");
    }

    [Fact]
    public async Task R41_missing_code_with_an_invalid_session_keeps_the_callback_reason()
    {
        var (player, _) = await SeedGoogleAsync();

        await NewUseCase().ExecuteAsync(Command(player.Id, sessionToken: null, code: null));

        AssertSingleFailure("callback", "provider_error");
    }

    [Fact]
    public async Task R41_provider_cancellation_with_an_invalid_session_keeps_the_authorize_reason()
    {
        var (player, _) = await SeedGoogleAsync();

        await NewUseCase().ExecuteAsync(Command(player.Id, sessionToken: null, code: null, error: "access_denied"));

        AssertSingleFailure("authorize", "cancelled");
    }

    [Fact]
    public async Task R41_a_different_google_subject_stays_subject_mismatch_at_id_token()
    {
        var (player, session) = await SeedGoogleAsync();
        _google.Returns(Verified("another-sub"));

        var result = await NewUseCase().ExecuteAsync(Command(player.Id));

        Assert.IsType<GoogleCallbackReauthFailed>(result.Value);
        AssertSingleFailure("id_token", "subject_mismatch");
        Assert.Null(session.ReauthenticatedAt);
    }

    [Fact]
    public async Task R41_step_4_player_that_does_not_exist_stays_subject_mismatch_at_id_token()
    {
        var ghost = new PlayerId(Guid.NewGuid());
        await _sessions.Add(Session.Start(ghost, _tokenHasher.Hash(SessionToken), false, Now));
        await _uow.CommitAsync();
        _google.Returns(Verified());

        var result = await NewUseCase().ExecuteAsync(Command(ghost));

        Assert.IsType<GoogleCallbackReauthFailed>(result.Value);
        AssertSingleFailure("id_token", "subject_mismatch");
    }

    [Fact]
    public async Task R41_step_4_account_without_google_identity_stays_subject_mismatch_at_id_token()
    {
        var player = APasswordPlayer();
        await _players.Add(player);
        await _sessions.Add(Session.Start(player.Id, _tokenHasher.Hash(SessionToken), false, Now));
        await _uow.CommitAsync();
        _google.Returns(Verified());

        var result = await NewUseCase().ExecuteAsync(Command(player.Id));

        Assert.IsType<GoogleCallbackReauthFailed>(result.Value);
        AssertSingleFailure("id_token", "subject_mismatch");
    }

    [Fact]
    public async Task R41_step_4_invalid_subject_stays_subject_mismatch_at_id_token()
    {
        var (player, session) = await SeedGoogleAsync();
        _google.Returns(Verified(subject: string.Empty));

        var result = await NewUseCase().ExecuteAsync(Command(player.Id));

        Assert.IsType<GoogleCallbackReauthFailed>(result.Value);
        AssertSingleFailure("id_token", "subject_mismatch");
        Assert.Null(session.ReauthenticatedAt);
    }

    [Fact]
    public async Task R41_successful_reauthentication_logs_no_failure()
    {
        var (player, session) = await SeedGoogleAsync();
        _google.Returns(Verified());

        var result = await NewUseCase().ExecuteAsync(Command(player.Id));

        Assert.IsType<GoogleCallbackReauthenticated>(result.Value);
        Assert.Empty(_events.Failures);
        Assert.Equal(Now, session.ReauthenticatedAt);
    }

    private sealed class RecordingEventLog : IAccountsEventLog
    {
        public List<(string Stage, string Reason)> Failures { get; } = [];

        public void GoogleAuthFailed(string stage, string reason) => Failures.Add((stage, reason));

        public void PlayerRegistered(PlayerId playerId, string method)
        {
        }

        public void LoginSucceeded(PlayerId playerId, SessionId sessionId, bool rememberMe)
        {
        }

        public void LoginFailed(string reason, PlayerId? playerId)
        {
        }

        public void LoggedOut(PlayerId playerId, SessionId sessionId)
        {
        }

        public void PasswordResetRequested(string outcome, PlayerId? playerId)
        {
        }

        public void PasswordResetCompleted(PlayerId playerId, int revokedSessions)
        {
        }

        public void PasswordResetRejected(string reason)
        {
        }

        public void PasswordResetEmailFailed(PlayerId playerId, int attempt)
        {
        }

        public void GoogleAuthCompleted(string outcome, PlayerId? playerId)
        {
        }

        public void GoogleLinked(PlayerId playerId)
        {
        }

        public void GoogleLinkRejected(PlayerId playerId, string reason)
        {
        }

        public void ProfileUpdated(PlayerId playerId, IReadOnlyList<string> changedFields)
        {
        }

        public void AvatarChanged(PlayerId playerId, string kind)
        {
        }

        public void AvatarRejected(PlayerId playerId, string reason, string? detail)
        {
        }

        public void ProgressiveDelayApplied(string action, int delayMs, string scope)
        {
        }

        public void AccessBudgetExceeded(string action)
        {
        }

        public void AccountDeletionRejected(PlayerId playerId, string reason)
        {
        }

        public void SessionRevoked(PlayerId playerId, SessionId sessionId, string reason)
        {
        }
    }
}
