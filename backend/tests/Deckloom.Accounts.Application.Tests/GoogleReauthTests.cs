using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;
using Microsoft.Extensions.Time.Testing;
using static Deckloom.Accounts.Application.Tests.PlayerFixtures;

namespace Deckloom.Accounts.Application.Tests;

// T-039 (REQ-023, REQ-042): reautenticacao Google para excluir a conta. RED: os tipos/campos abaixo ainda nao existem.
// Fontes: design.md linha 1535 (`GET /auth/google/start?intent=reauth`: so para sessao valida; `returnTo` ignorado;
// `prompt=login` e `max_age=300`; callback exige `auth_time` nos ultimos 5 min e `subject` igual ao vinculado);
// linha 1511 (resultados `reautenticado` e `reautenticacao-falhou`); linha 1482 (cookie `__Host-dl_oauth` carrega
// intent e PlayerId); linhas 244 e 237 (S6: `Session.MarkReauthenticated` e `ReauthenticatedAt` preso a sessao);
// linha 226 (janela unica `Reauthentication.Window`); linha 447 (Intent e PlayerId entram no record na T-039).
//
// Forma esperada (aditiva; os testes de T-037/T-038 seguem compilando):
//   enum GoogleAuthIntent { SignIn, Reauth }
//   GoogleOAuthCorrelation(string State, string Nonce, string CodeVerifier,
//       GoogleAuthIntent Intent = GoogleAuthIntent.SignIn, PlayerId? PlayerId = null)
//   HandleGoogleCallbackCommand(string? Code, string? State, string? Error, GoogleOAuthCorrelation? Correlation,
//       string? SessionToken = null)          <- token bruto do cookie de sessao; so usado no intent Reauth
//   GoogleVerifiedIdentity(string Subject, Email Email, bool EmailVerified, string? Name, DateTimeOffset? AuthTime = null)
//       <- `auth_time` do ID token (linha 1535). Nulo = ausente = reautenticacao falha. O FakeGoogleIdentityVerifier
//          nao muda: ele devolve a identidade programada, que ja carrega AuthTime.
//   sealed record GoogleCallbackReauthenticated : GoogleCallbackOutcome   <- `reautenticado`, sem campos
//   sealed record GoogleCallbackReauthFailed    : GoogleCallbackOutcome   <- `reautenticacao-falhou`, sem campos
//       (ambos sem campos: a API so redireciona para ?resultado=...; igual ao padrao de GoogleCallbackFailed).
// Regras do caso de uso (intent Reauth), todas devolvem `Result` de sucesso com o ramo correspondente:
//   - sessao valida (token do comando -> Session.IsValid) cujo PlayerId == Correlation.PlayerId, senao reautenticacao-falhou,
//     sem chamar o Google;
//   - qualquer falha de callback (error, state, code, cookie ausente nao: ver abaixo, verificador nulo/excecao) ->
//     reautenticacao-falhou quando o intent do cookie e Reauth. Cookie ausente (Correlation nulo) nao revela o intent
//     e continua `falhou`;
//   - AuthTime ausente ou anterior a now - Reauthentication.Window (5:00 exatos aceito, como Player.Delete) -> falha;
//   - Subject diferente do ExternalIdentity do Jogador (ou conta sem Google) -> falha;
//   - sucesso: Session.MarkReauthenticated(linked, presented, now) dentro do unitOfWork + sessions.Save;
//     nenhuma sessao nova, nenhuma pendencia.
// Fica para a API (nao testavel na Application): StartGoogleAuth com intent=reauth montar a URL com prompt=login e
// max_age=300 (IGoogleIdentityVerifier.BuildAuthorizationUrlAsync), rejeitar visitante sem sessao no start, gravar o
// cookie __Host-dl_oauth (intent/PlayerId) e ignorar `returnTo` no start. Aqui so se verifica que o caso de uso
// nao recebe `returnTo`.
public class GoogleReauthTests
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
    private readonly SequentialTokenGenerator _tokens = new();
    private readonly Sha256TokenHasher _tokenHasher = new();
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly InMemoryAvatarPresetCatalog _catalog = new(
    [
        new AvatarPresetEntry("preset-01", "Dragao", "/avatars/preset-01.png", 1, false, true),
        new AvatarPresetEntry("preset-02", "Planeswalker", "/avatars/preset-02.png", 2, true, true),
    ]);

    public GoogleReauthTests()
    {
        _players = new InMemoryPlayerRepository(_uow);
        _sessions = new InMemorySessionRepository(_uow);
    }

    private HandleGoogleCallback NewUseCase() =>
        new(_players, _sessions, _uow, _google, _tokens, _tokenHasher, _catalog, new InMemoryPendingGoogleAuthStore(_clock, _uow), _clock);

    private static GoogleOAuthCorrelation ReauthCorrelation(PlayerId? playerId) =>
        new(State, Nonce, Verifier, GoogleAuthIntent.Reauth, playerId);

    private static HandleGoogleCallbackCommand Command(PlayerId? playerId, string? sessionToken = SessionToken) =>
        new(AuthCode, State, null, ReauthCorrelation(playerId), sessionToken);

    private static GoogleVerifiedIdentity Verified(string subject = Subject, DateTimeOffset? authTime = null, bool hasAuthTime = true) =>
        new(subject, AnEmail("gui@example.com"), true, "Gui Google", hasAuthTime ? authTime ?? Now : null);

    private async Task<(Player Player, Session Session)> SeedAsync()
    {
        var player = AGooglePlayer("gui@example.com", Subject);
        await _players.Add(player);
        var session = Session.Start(player.Id, _tokenHasher.Hash(SessionToken), false, Now);
        await _sessions.Add(session);
        await _uow.CommitAsync();
        return (player, session);
    }

    private async Task<bool> AnyNewSessionOpenedAsync() =>
        await _sessions.FindByTokenHash(_tokenHasher.Hash("tok-0001")) is not null;

    [Fact]
    public async Task REQ042_recent_login_by_the_linked_subject_marks_the_session_as_reauthenticated()
    {
        var (player, session) = await SeedAsync();
        _google.Returns(Verified(authTime: Now.AddMinutes(-1)));

        var result = await NewUseCase().ExecuteAsync(Command(player.Id));

        Assert.True(result.IsSuccess);
        Assert.IsType<GoogleCallbackReauthenticated>(result.Value);
        Assert.Equal(Now, session.ReauthenticatedAt);
    }

    [Fact]
    public async Task REQ042_reauthentication_opens_no_new_session_and_the_verifier_gets_the_cookie_values()
    {
        var (player, _) = await SeedAsync();
        _google.Returns(Verified());

        await NewUseCase().ExecuteAsync(Command(player.Id));

        Assert.False(await AnyNewSessionOpenedAsync());
        var call = Assert.Single(_google.CompleteCalls);
        Assert.Equal(new CompleteCall(AuthCode, Verifier, Nonce), call);
    }

    [Fact]
    public async Task REQ042_the_mark_is_persisted_inside_a_unit_of_work_block()
    {
        var (player, _) = await SeedAsync();
        _google.Returns(Verified());

        await NewUseCase().ExecuteAsync(Command(player.Id));

        Assert.True(_uow.TransactionOpenDuringLastCommit);
        Assert.False(_uow.IsTransactionOpen);
    }

    [Fact]
    public async Task REQ042_auth_time_exactly_five_minutes_old_is_accepted()
    {
        var (player, session) = await SeedAsync();
        _google.Returns(Verified(authTime: Now - Reauthentication.Window));

        var result = await NewUseCase().ExecuteAsync(Command(player.Id));

        Assert.IsType<GoogleCallbackReauthenticated>(result.Value);
        Assert.Equal(Now, session.ReauthenticatedAt);
    }

    [Fact]
    public async Task REQ042_auth_time_older_than_five_minutes_fails_without_marking()
    {
        var (player, session) = await SeedAsync();
        _google.Returns(Verified(authTime: Now - Reauthentication.Window - TimeSpan.FromSeconds(1)));

        var result = await NewUseCase().ExecuteAsync(Command(player.Id));

        Assert.True(result.IsSuccess);
        Assert.IsType<GoogleCallbackReauthFailed>(result.Value);
        Assert.Null(session.ReauthenticatedAt);
    }

    [Fact]
    public async Task REQ042_missing_auth_time_fails_without_marking()
    {
        var (player, session) = await SeedAsync();
        _google.Returns(Verified(hasAuthTime: false));

        var result = await NewUseCase().ExecuteAsync(Command(player.Id));

        Assert.IsType<GoogleCallbackReauthFailed>(result.Value);
        Assert.Null(session.ReauthenticatedAt);
    }

    [Fact]
    public async Task REQ042_a_different_google_subject_fails_without_marking_and_creates_nothing()
    {
        var (player, session) = await SeedAsync();
        _google.Returns(Verified(subject: "another-google-sub"));

        var result = await NewUseCase().ExecuteAsync(Command(player.Id));

        Assert.IsType<GoogleCallbackReauthFailed>(result.Value);
        Assert.Null(session.ReauthenticatedAt);
        Assert.False(await AnyNewSessionOpenedAsync());
        Assert.Null(await _players.FindByGoogleSubject("another-google-sub"));
    }

    [Fact]
    public async Task REQ042_the_subject_of_another_player_does_not_reauthenticate_this_session()
    {
        var (player, session) = await SeedAsync();
        await _players.Add(AGooglePlayer("other@example.com", "other-sub"));
        await _uow.CommitAsync();
        _google.Returns(Verified(subject: "other-sub"));

        var result = await NewUseCase().ExecuteAsync(Command(player.Id));

        Assert.IsType<GoogleCallbackReauthFailed>(result.Value);
        Assert.Null(session.ReauthenticatedAt);
        Assert.False(await AnyNewSessionOpenedAsync());
    }

    [Fact]
    public async Task REQ042_an_account_without_google_cannot_be_reauthenticated_through_google()
    {
        var player = APasswordPlayer();
        await _players.Add(player);
        var session = Session.Start(player.Id, _tokenHasher.Hash(SessionToken), false, Now);
        await _sessions.Add(session);
        await _uow.CommitAsync();
        _google.Returns(Verified());

        var result = await NewUseCase().ExecuteAsync(Command(player.Id));

        Assert.IsType<GoogleCallbackReauthFailed>(result.Value);
        Assert.Null(session.ReauthenticatedAt);
    }

    [Fact]
    public async Task REQ042_cancellation_error_state_and_verifier_failures_all_fail_without_marking()
    {
        var (player, session) = await SeedAsync();
        var outcomes = new List<GoogleCallbackOutcome>();

        _google.Returns(Verified());
        outcomes.Add((await NewUseCase().ExecuteAsync(
            new HandleGoogleCallbackCommand(null, State, "access_denied", ReauthCorrelation(player.Id), SessionToken))).Value);
        outcomes.Add((await NewUseCase().ExecuteAsync(
            new HandleGoogleCallbackCommand(AuthCode, "other-state", null, ReauthCorrelation(player.Id), SessionToken))).Value);
        outcomes.Add((await NewUseCase().ExecuteAsync(
            new HandleGoogleCallbackCommand(null, State, null, ReauthCorrelation(player.Id), SessionToken))).Value);
        _google.Cancel();
        outcomes.Add((await NewUseCase().ExecuteAsync(Command(player.Id))).Value);
        _google.Fail();
        outcomes.Add((await NewUseCase().ExecuteAsync(Command(player.Id))).Value);

        Assert.All(outcomes, o => Assert.Equal(new GoogleCallbackReauthFailed(), o));
        Assert.Null(session.ReauthenticatedAt);
        Assert.False(await AnyNewSessionOpenedAsync());
    }

    [Fact]
    public async Task REQ042_a_missing_or_unknown_session_token_fails_without_calling_google()
    {
        var (player, session) = await SeedAsync();
        _google.Returns(Verified());

        var missing = await NewUseCase().ExecuteAsync(Command(player.Id, sessionToken: null));
        var unknown = await NewUseCase().ExecuteAsync(Command(player.Id, sessionToken: "not-a-session"));

        Assert.IsType<GoogleCallbackReauthFailed>(missing.Value);
        Assert.IsType<GoogleCallbackReauthFailed>(unknown.Value);
        Assert.Empty(_google.CompleteCalls);
        Assert.Null(session.ReauthenticatedAt);
    }

    [Fact]
    public async Task REQ042_an_expired_session_cannot_be_reauthenticated()
    {
        var (player, session) = await SeedAsync();
        _clock.Advance(TimeSpan.FromMinutes(31));
        _google.Returns(Verified(authTime: _clock.GetUtcNow()));

        var result = await NewUseCase().ExecuteAsync(Command(player.Id));

        Assert.IsType<GoogleCallbackReauthFailed>(result.Value);
        Assert.Empty(_google.CompleteCalls);
        Assert.Null(session.ReauthenticatedAt);
    }

    [Fact]
    public async Task REQ042_a_revoked_session_cannot_be_reauthenticated()
    {
        var (player, session) = await SeedAsync();
        session.Revoke(SessionRevocationReason.Logout, Now);
        _google.Returns(Verified());

        var result = await NewUseCase().ExecuteAsync(Command(player.Id));

        Assert.IsType<GoogleCallbackReauthFailed>(result.Value);
        Assert.Null(session.ReauthenticatedAt);
    }

    [Fact]
    public async Task REQ042_the_cookie_player_must_be_the_owner_of_the_session()
    {
        var (_, session) = await SeedAsync();
        var other = AGooglePlayer("other@example.com", "other-sub");
        await _players.Add(other);
        await _uow.CommitAsync();
        _google.Returns(Verified());

        var wrongPlayer = await NewUseCase().ExecuteAsync(Command(other.Id));
        var noPlayer = await NewUseCase().ExecuteAsync(Command(null));

        Assert.IsType<GoogleCallbackReauthFailed>(wrongPlayer.Value);
        Assert.IsType<GoogleCallbackReauthFailed>(noPlayer.Value);
        Assert.Null(session.ReauthenticatedAt);
    }

    [Fact]
    public async Task REQ042_a_missing_correlation_cookie_is_the_generic_failure()
    {
        var (_, session) = await SeedAsync();
        _google.Returns(Verified());

        var result = await NewUseCase().ExecuteAsync(new HandleGoogleCallbackCommand(AuthCode, State, null, null, SessionToken));

        Assert.IsType<GoogleCallbackFailed>(result.Value);
        Assert.Null(session.ReauthenticatedAt);
    }

    [Fact]
    public async Task REQ023_a_sign_in_callback_never_marks_the_session_even_when_a_session_token_is_present()
    {
        var (_, session) = await SeedAsync();
        _google.Returns(Verified());

        var result = await NewUseCase().ExecuteAsync(
            new HandleGoogleCallbackCommand(AuthCode, State, null, new GoogleOAuthCorrelation(State, Nonce, Verifier), SessionToken));

        Assert.IsType<GoogleCallbackOk>(result.Value);
        Assert.Null(session.ReauthenticatedAt);
    }

    [Fact]
    public async Task REQ023_the_reauthenticated_session_allows_a_google_only_account_to_be_deleted()
    {
        var (player, session) = await SeedAsync();
        _google.Returns(Verified());

        await NewUseCase().ExecuteAsync(Command(player.Id));
        var deletion = player.Delete(true, session.ToRecentGoogleLogin(), Now);

        Assert.True(deletion.IsSuccess);
    }

    [Fact]
    public void REQ042_the_use_case_never_receives_a_return_to()
    {
        var types = new[]
        {
            typeof(HandleGoogleCallbackCommand),
            typeof(GoogleOAuthCorrelation),
            typeof(GoogleCallbackReauthenticated),
            typeof(GoogleCallbackReauthFailed),
        };

        Assert.All(types, t => Assert.DoesNotContain(
            t.GetProperties(), p => p.Name.Contains("ReturnTo", StringComparison.OrdinalIgnoreCase)));
    }
}
