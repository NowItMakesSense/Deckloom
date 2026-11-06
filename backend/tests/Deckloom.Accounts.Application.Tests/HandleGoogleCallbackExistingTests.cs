using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;
using Deckloom.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using static Deckloom.Accounts.Application.Tests.PlayerFixtures;

namespace Deckloom.Accounts.Application.Tests;

// T-037 (REQ-007, REQ-009): retorno do Google para conta existente e falhas. RED: os tipos abaixo ainda nao existem.
// Forma esperada (convencao do T-030, design.md linhas 359-367; fluxo OIDC em ADR-0004 e design.md linhas 1259-1262):
//   sealed record GoogleOAuthCorrelation(string State, string Nonce, string CodeVerifier)
//     = conteudo do cookie `__Host-dl_oauth` (design.md linha 1439) ja decifrado pela API (Data Protection e infraestrutura).
//       O `state` NAO e guardado em nenhum store: vive no cookie, que a API decifra e entrega ao caso de uso
//       (design.md linha 1141: "codigo, state, cookie de correlacao"). Cookie ausente/invalido/expirado -> null.
//       Intent/PlayerId (reauth) entram no T-039; ReturnTo e do cookie e fica com a API (nao entra no resultado).
//   sealed record HandleGoogleCallbackCommand(string? Code, string? State, string? Error, GoogleOAuthCorrelation? Correlation)
//   sealed class HandleGoogleCallback(IPlayerRepository, ISessionRepository, IUnitOfWork, IGoogleIdentityVerifier,
//       ISecureTokenGenerator, ITokenHasher, IAvatarPresetCatalog, TimeProvider)
//     Task<Result<GoogleCallbackOutcome>> ExecuteAsync(HandleGoogleCallbackCommand, CancellationToken ct = default)
//     (T-038 acrescenta IPendingGoogleAuthStore ao construtor.)
//   Uniao fechada (design.md linha 1468: ok | concluir | vincular | reautenticado | falhou | reautenticacao-falhou):
//     abstract record GoogleCallbackOutcome, com derivados sealed record:
//       GoogleCallbackOk(AuthenticatedSession Session)   <- `ok`      (exercitado aqui)
//       GoogleCallbackFailed()                           <- `falhou`  (exercitado aqui; sem campos: mesma mensagem, REQ-009)
//       previstos e NAO exercitados aqui: concluir e vincular (T-038), reautenticado e reautenticacao-falhou (T-039).
//   `falhou` e ramo de sucesso do Result (a API responde 302 ...?resultado=falhou, nao um erro HTTP; design.md linha 1468).
//   Sessao do Google nasce sem "lembrar-me" (nao ha o campo no callback; REQ-012: 30 min).
public class HandleGoogleCallbackExistingTests
{
    private const string Subject = "google-sub-1";
    private const string State = "state-abc";
    private const string Nonce = "nonce-xyz";
    private const string Verifier = "verifier-123";
    private const string AuthCode = "auth-code-1";

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

    public HandleGoogleCallbackExistingTests()
    {
        _players = new InMemoryPlayerRepository(_uow);
        _sessions = new InMemorySessionRepository(_uow);
    }

    private HandleGoogleCallback NewUseCase() =>
        new(_players, _sessions, _uow, _google, _tokens, _tokenHasher, _catalog, new InMemoryPendingGoogleAuthStore(_clock, _uow), _clock);

    private static GoogleOAuthCorrelation Correlation() => new(State, Nonce, Verifier);

    private static HandleGoogleCallbackCommand ValidCommand() => new(AuthCode, State, null, Correlation());

    private static GoogleVerifiedIdentity Verified(string email = "gui@example.com", bool emailVerified = true) =>
        new(Subject, AnEmail(email), emailVerified, "Gui Google");

    private async Task<Player> SeedGooglePlayerAsync(string email = "gui@example.com")
    {
        var player = AGooglePlayer(email, Subject);
        await _players.Add(player);
        await _uow.CommitAsync();
        return player;
    }

    private async Task<bool> AnySessionOpenedAsync() =>
        await _sessions.FindByTokenHash(_tokenHasher.Hash("tok-0001")) is not null;

    [Fact]
    public async Task REQ007_linked_subject_opens_a_session_on_the_existing_account()
    {
        var player = await SeedGooglePlayerAsync();
        _google.Returns(Verified());

        var result = await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.True(result.IsSuccess);
        var ok = Assert.IsType<GoogleCallbackOk>(result.Value);
        Assert.Equal("tok-0001", ok.Session.SessionToken);
        var session = await _sessions.FindByTokenHash(_tokenHasher.Hash("tok-0001"));
        Assert.NotNull(session);
        Assert.Equal(player.Id, session!.PlayerId);
        Assert.Equal(session.ExpiresAt, ok.Session.ExpiresAt);
        Assert.Equal("Gui", ok.Session.Summary.DisplayName);
        Assert.True(ok.Session.Summary.HasGoogle);
        Assert.False(ok.Session.Summary.HasPassword);
    }

    [Fact]
    public async Task REQ007_no_duplicate_account_is_created_for_a_returning_google_player()
    {
        var player = await SeedGooglePlayerAsync();
        _google.Returns(Verified());

        await NewUseCase().ExecuteAsync(ValidCommand());
        await NewUseCase().ExecuteAsync(ValidCommand());

        // Uma segunda conta com o mesmo email estouraria a unicidade do fake no commit.
        var found = await _players.FindByGoogleSubject(Subject);
        Assert.Equal(player.Id, found!.Id);
        Assert.Same(player, await _players.FindByEmail(AnEmail("gui@example.com")));
    }

    [Fact]
    public async Task REQ007_the_account_is_found_by_subject_first_even_if_the_google_email_changed()
    {
        var player = await SeedGooglePlayerAsync("gui@example.com");
        _google.Returns(Verified(email: "new-address@example.com"));

        var result = await NewUseCase().ExecuteAsync(ValidCommand());

        var ok = Assert.IsType<GoogleCallbackOk>(result.Value);
        var session = await _sessions.FindByTokenHash(_tokenHasher.Hash(ok.Session.SessionToken));
        Assert.Equal(player.Id, session!.PlayerId);
        Assert.Null(await _players.FindByEmail(AnEmail("new-address@example.com")));
    }

    [Fact]
    public async Task REQ007_the_session_is_the_default_one_without_remember_me_and_stores_only_the_token_hash()
    {
        await SeedGooglePlayerAsync();
        _google.Returns(Verified());

        var result = await NewUseCase().ExecuteAsync(ValidCommand());

        var ok = Assert.IsType<GoogleCallbackOk>(result.Value);
        var session = await _sessions.FindByTokenHash(_tokenHasher.Hash(ok.Session.SessionToken));
        Assert.False(session!.RememberMe);
        Assert.Equal(Now.AddMinutes(30), session.ExpiresAt);
        Assert.True(_tokenHasher.Hash(ok.Session.SessionToken).Equals(session.TokenHash));
    }

    [Fact]
    public async Task REQ007_the_session_is_persisted_inside_a_unit_of_work_block()
    {
        await SeedGooglePlayerAsync();
        _google.Returns(Verified());

        await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.True(_uow.TransactionOpenDuringLastCommit);
        Assert.False(_uow.IsTransactionOpen);
    }

    [Fact]
    public async Task REQ007_each_return_issues_a_fresh_token()
    {
        await SeedGooglePlayerAsync();
        _google.Returns(Verified());

        var first = Assert.IsType<GoogleCallbackOk>((await NewUseCase().ExecuteAsync(ValidCommand())).Value);
        var second = Assert.IsType<GoogleCallbackOk>((await NewUseCase().ExecuteAsync(ValidCommand())).Value);

        Assert.NotEqual(first.Session.SessionToken, second.Session.SessionToken);
    }

    [Fact]
    public async Task REQ007_the_verifier_receives_the_code_verifier_and_nonce_from_the_correlation_cookie()
    {
        await SeedGooglePlayerAsync();
        _google.Returns(Verified());

        await NewUseCase().ExecuteAsync(ValidCommand());

        var call = Assert.Single(_google.CompleteCalls);
        Assert.Equal(new CompleteCall(AuthCode, Verifier, Nonce), call);
    }

    [Fact]
    public async Task REQ009_user_cancellation_with_access_denied_fails_without_account_or_session()
    {
        await SeedGooglePlayerAsync();
        _google.Returns(Verified());

        var result = await NewUseCase().ExecuteAsync(new HandleGoogleCallbackCommand(null, State, "access_denied", Correlation()));

        Assert.True(result.IsSuccess);
        Assert.IsType<GoogleCallbackFailed>(result.Value);
        Assert.Empty(_google.CompleteCalls);
        Assert.False(await AnySessionOpenedAsync());
    }

    [Fact]
    public async Task REQ009_cancellation_reported_by_the_verifier_as_null_fails_without_session()
    {
        await SeedGooglePlayerAsync();
        _google.Cancel();

        var result = await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.IsType<GoogleCallbackFailed>(result.Value);
        Assert.False(await AnySessionOpenedAsync());
    }

    [Fact]
    public async Task REQ009_verifier_failure_fails_without_account_or_session_and_does_not_throw()
    {
        await SeedGooglePlayerAsync();
        _google.Fail();

        var result = await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.True(result.IsSuccess);
        Assert.IsType<GoogleCallbackFailed>(result.Value);
        Assert.False(await AnySessionOpenedAsync());
    }

    [Fact]
    public async Task REQ009_unverified_email_fails_without_account_or_session_even_when_the_subject_is_linked()
    {
        await SeedGooglePlayerAsync();
        _google.Returns(Verified(emailVerified: false));

        var result = await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.IsType<GoogleCallbackFailed>(result.Value);
        Assert.False(await AnySessionOpenedAsync());
    }

    [Fact]
    public async Task REQ009_unverified_email_with_no_account_creates_no_account()
    {
        _google.Returns(Verified(emailVerified: false));

        await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.Null(await _players.FindByGoogleSubject(Subject));
        Assert.Null(await _players.FindByEmail(AnEmail("gui@example.com")));
        Assert.False(await AnySessionOpenedAsync());
    }

    [Fact]
    public async Task REQ009_state_different_from_the_cookie_fails_without_calling_google()
    {
        await SeedGooglePlayerAsync();
        _google.Returns(Verified());

        var result = await NewUseCase().ExecuteAsync(new HandleGoogleCallbackCommand(AuthCode, "other-state", null, Correlation()));

        Assert.IsType<GoogleCallbackFailed>(result.Value);
        Assert.Empty(_google.CompleteCalls);
        Assert.False(await AnySessionOpenedAsync());
    }

    [Fact]
    public async Task REQ009_missing_correlation_cookie_fails_without_calling_google()
    {
        await SeedGooglePlayerAsync();
        _google.Returns(Verified());

        var result = await NewUseCase().ExecuteAsync(new HandleGoogleCallbackCommand(AuthCode, State, null, null));

        Assert.IsType<GoogleCallbackFailed>(result.Value);
        Assert.Empty(_google.CompleteCalls);
        Assert.False(await AnySessionOpenedAsync());
    }

    [Fact]
    public async Task REQ009_missing_code_without_error_fails_without_calling_google()
    {
        await SeedGooglePlayerAsync();
        _google.Returns(Verified());

        var result = await NewUseCase().ExecuteAsync(new HandleGoogleCallbackCommand(null, State, null, Correlation()));

        Assert.IsType<GoogleCallbackFailed>(result.Value);
        Assert.Empty(_google.CompleteCalls);
    }

    [Fact]
    public async Task REQ009_every_failure_cause_gives_the_very_same_outcome()
    {
        await SeedGooglePlayerAsync();
        var outcomes = new List<GoogleCallbackOutcome>();

        _google.Returns(Verified());
        outcomes.Add((await NewUseCase().ExecuteAsync(
            new HandleGoogleCallbackCommand(null, State, "access_denied", Correlation()))).Value);
        _google.Cancel();
        outcomes.Add((await NewUseCase().ExecuteAsync(ValidCommand())).Value);
        _google.Fail();
        outcomes.Add((await NewUseCase().ExecuteAsync(ValidCommand())).Value);
        _google.Returns(Verified(emailVerified: false));
        outcomes.Add((await NewUseCase().ExecuteAsync(ValidCommand())).Value);
        outcomes.Add((await NewUseCase().ExecuteAsync(
            new HandleGoogleCallbackCommand(AuthCode, "other-state", null, Correlation()))).Value);

        Assert.All(outcomes, o => Assert.Equal(new GoogleCallbackFailed(), o));
    }

    [Fact]
    public async Task REQ009_google_outage_does_not_affect_password_login()
    {
        _google.Fail();
        var hasher = new FakePasswordHasher(600_000);
        var login = new LoginWithPassword(
            _players, _sessions, _uow, hasher, _tokens, _tokenHasher, _catalog,
            new AccessThrottle(
                new InMemoryAccessAttemptStore(_uow),
                new FakeKeyedHasher(),
                new RecordingDelayer(),
                new ProgressiveDelayPolicy(new ProgressiveDelayOptions()),
                _clock),
            _clock);
        var withPassword = Player.RegisterWithPassword(
            AnEmail(),
            await hasher.HashAsync(Password.Create("correct horse battery").Value),
            DisplayName.Create("Ana").Value,
            "2026-10-01",
            "2026-10-01",
            true,
            true,
            Now,
            "preset-01").Value;
        await _players.Add(withPassword);
        await _uow.CommitAsync();

        var callback = await NewUseCase().ExecuteAsync(ValidCommand());
        var loginResult = await login.ExecuteAsync(
            new LoginWithPasswordCommand("ana@example.com", "correct horse battery", false, "203.0.113.7"));

        Assert.IsType<GoogleCallbackFailed>(callback.Value);
        Assert.True(loginResult.IsSuccess);
        Assert.Equal("Ana", loginResult.Value.Summary.DisplayName);
    }
}
