using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;
using Microsoft.Extensions.Time.Testing;
using static Deckloom.Accounts.Application.Tests.PlayerFixtures;

namespace Deckloom.Accounts.Application.Tests;

// T-038 (REQ-006, REQ-008, REQ-039): retorno do Google com subject desconhecido. RED: os tipos/ctor abaixo ainda nao existem.
// Forma esperada (design.md linhas 446-453: "O IPendingGoogleAuthStore entra como dependencia na T-038";
// "Os records desses ramos sao definidos nas respectivas tarefas"; pendencia em cookie `__Host-dl_pending` com o token opaco, design.md linha 1466):
//   sealed class HandleGoogleCallback(IPlayerRepository, ISessionRepository, IUnitOfWork, IGoogleIdentityVerifier,
//       ISecureTokenGenerator, ITokenHasher, IAvatarPresetCatalog, IPendingGoogleAuthStore, TimeProvider)
//     (o store entra logo antes do TimeProvider; ordem nao fixada pela spec, decisao desta tarefa)
//   sealed record GoogleCallbackConclude(string PendingToken) : GoogleCallbackOutcome   <- `concluir`
//   sealed record GoogleCallbackLink(string PendingToken)     : GoogleCallbackOutcome   <- `vincular`
//     Campo unico: o token opaco EM CLARO gerado por ISecureTokenGenerator, que a API grava no cookie `__Host-dl_pending`
//     (design.md linhas 266 e 1466); so o hash (ITokenHasher) e persistido no store (S2). A API le o tipo/email/nome via
//     GetPendingGoogleAuth (T-040), por isso o resultado nao carrega email nem nome.
//   Conta Google (sem senha) com outro subject e o mesmo email: nao e vinculada nem recebe pendencia; devolve
//     GoogleCallbackFailed (REQ-009/D3: vinculo so com prova da senha; a spec nao define ramo proprio, decisao desta tarefa).
public class HandleGoogleCallbackNewTests
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
    private readonly InMemoryPendingGoogleAuthStore _pending;
    private readonly InMemoryAvatarPresetCatalog _catalog = new(
    [
        new AvatarPresetEntry("preset-01", "Dragao", "/avatars/preset-01.png", 1, false, true),
        new AvatarPresetEntry("preset-02", "Planeswalker", "/avatars/preset-02.png", 2, true, true),
    ]);

    public HandleGoogleCallbackNewTests()
    {
        _players = new InMemoryPlayerRepository(_uow);
        _sessions = new InMemorySessionRepository(_uow);
        _pending = new InMemoryPendingGoogleAuthStore(_clock, _uow);
    }

    private HandleGoogleCallback NewUseCase() =>
        new(_players, _sessions, _uow, _google, _tokens, _tokenHasher, _catalog, _pending, _clock);

    private static HandleGoogleCallbackCommand ValidCommand() =>
        new(AuthCode, State, null, new GoogleOAuthCorrelation(State, Nonce, Verifier));

    private static GoogleVerifiedIdentity Verified(string email = "bia@example.com", string? name = "Beatriz Souza") =>
        new(Subject, AnEmail(email), true, name);

    private async Task SeedAsync(Player player)
    {
        await _players.Add(player);
        await _uow.CommitAsync();
    }

    private async Task<bool> AnySessionOpenedAsync() =>
        await _sessions.FindByTokenHash(_tokenHasher.Hash("tok-0001")) is not null;

    private async Task<PendingGoogleAuth?> StoredPendingAsync(string token) =>
        await _pending.FindAsync(_tokenHasher.Hash(token));

    [Fact]
    public async Task REQ006_verified_email_without_account_creates_a_pending_signup_and_returns_concluir()
    {
        _google.Returns(Verified());

        var result = await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.True(result.IsSuccess);
        var conclude = Assert.IsType<GoogleCallbackConclude>(result.Value);
        Assert.Equal("tok-0001", conclude.PendingToken);
        var signup = Assert.IsType<PendingGoogleSignup>(await StoredPendingAsync(conclude.PendingToken));
        Assert.Equal(Subject, signup.GoogleSubject);
        Assert.Equal(AnEmail("bia@example.com"), signup.Email);
        Assert.Equal("Beatriz Souza", signup.SuggestedDisplayName);
    }

    [Fact]
    public async Task REQ006_no_account_and_no_session_are_created_before_the_signup_is_completed()
    {
        _google.Returns(Verified());

        await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.Null(await _players.FindByGoogleSubject(Subject));
        Assert.Null(await _players.FindByEmail(AnEmail("bia@example.com")));
        Assert.False(await AnySessionOpenedAsync());
    }

    [Fact]
    public async Task REQ039_the_google_name_is_kept_whole_even_beyond_the_display_name_limit()
    {
        var longName = new string('B', 40);
        _google.Returns(Verified(name: longName));

        var result = await NewUseCase().ExecuteAsync(ValidCommand());

        var conclude = Assert.IsType<GoogleCallbackConclude>(result.Value);
        var signup = Assert.IsType<PendingGoogleSignup>(await StoredPendingAsync(conclude.PendingToken));
        Assert.Equal(longName, signup.SuggestedDisplayName);
    }

    [Fact]
    public async Task REQ039_a_missing_google_name_leaves_the_suggestion_null()
    {
        _google.Returns(Verified(name: null));

        var result = await NewUseCase().ExecuteAsync(ValidCommand());

        var conclude = Assert.IsType<GoogleCallbackConclude>(result.Value);
        var signup = Assert.IsType<PendingGoogleSignup>(await StoredPendingAsync(conclude.PendingToken));
        Assert.Null(signup.SuggestedDisplayName);
    }

    [Fact]
    public async Task REQ039_the_pending_signup_lives_for_ten_minutes_and_only_the_token_hash_is_stored()
    {
        _google.Returns(Verified());

        var result = await NewUseCase().ExecuteAsync(ValidCommand());

        var conclude = Assert.IsType<GoogleCallbackConclude>(result.Value);
        var signup = (await StoredPendingAsync(conclude.PendingToken))!;
        Assert.Equal(Now, signup.CreatedAt);
        Assert.Equal(Now.AddMinutes(10), signup.ExpiresAt);
        Assert.True(_tokenHasher.Hash(conclude.PendingToken).Equals(signup.TokenHash));
        _clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Null(await StoredPendingAsync(conclude.PendingToken));
    }

    [Fact]
    public async Task REQ008_email_of_a_password_account_without_google_creates_a_pending_link_to_that_player()
    {
        var ana = APasswordPlayer("ana@example.com");
        await SeedAsync(ana);
        _google.Returns(Verified(email: "ana@example.com"));

        var result = await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.True(result.IsSuccess);
        var link = Assert.IsType<GoogleCallbackLink>(result.Value);
        Assert.Equal("tok-0001", link.PendingToken);
        var pending = Assert.IsType<PendingGoogleLink>(await StoredPendingAsync(link.PendingToken));
        Assert.Equal(ana.Id, pending.TargetPlayerId);
        Assert.Equal(Subject, pending.GoogleSubject);
        Assert.Equal(AnEmail("ana@example.com"), pending.Email);
    }

    [Fact]
    public async Task REQ008_the_pending_link_opens_no_session_creates_no_second_account_and_does_not_link_yet()
    {
        var ana = APasswordPlayer("ana@example.com");
        await SeedAsync(ana);
        _google.Returns(Verified(email: "ana@example.com"));

        await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.False(await AnySessionOpenedAsync());
        Assert.Same(ana, await _players.FindByEmail(AnEmail("ana@example.com")));
        Assert.Null(await _players.FindByGoogleSubject(Subject));
        Assert.Null(ana.ExternalIdentity);
    }

    [Fact]
    public async Task REQ008_the_pending_link_also_lives_for_ten_minutes()
    {
        await SeedAsync(APasswordPlayer("ana@example.com"));
        _google.Returns(Verified(email: "ana@example.com"));

        var result = await NewUseCase().ExecuteAsync(ValidCommand());

        var link = Assert.IsType<GoogleCallbackLink>(result.Value);
        var pending = (await StoredPendingAsync(link.PendingToken))!;
        Assert.Equal(Now.AddMinutes(10), pending.ExpiresAt);
        _clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Null(await StoredPendingAsync(link.PendingToken));
    }

    [Fact]
    public async Task REQ008_a_google_only_account_with_another_subject_is_not_linked_and_gets_no_pending_or_session()
    {
        var gui = AGooglePlayer("bia@example.com", "other-subject");
        await SeedAsync(gui);
        _google.Returns(Verified(email: "bia@example.com"));

        var result = await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.True(result.IsSuccess);
        Assert.IsType<GoogleCallbackFailed>(result.Value);
        Assert.Null(await StoredPendingAsync("tok-0001"));
        Assert.False(await AnySessionOpenedAsync());
        Assert.Equal("other-subject", gui.ExternalIdentity!.Subject);
        Assert.Null(await _players.FindByGoogleSubject(Subject));
    }

    [Fact]
    public async Task REQ009_unverified_email_creates_no_pending_even_when_there_is_no_account()
    {
        _google.Returns(new GoogleVerifiedIdentity(Subject, AnEmail("bia@example.com"), false, "Beatriz"));

        var result = await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.IsType<GoogleCallbackFailed>(result.Value);
        Assert.Null(await StoredPendingAsync("tok-0001"));
    }
}
