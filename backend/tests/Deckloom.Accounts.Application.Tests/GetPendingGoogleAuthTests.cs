using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;
using Microsoft.Extensions.Time.Testing;
using static Deckloom.Accounts.Application.Tests.PlayerFixtures;

namespace Deckloom.Accounts.Application.Tests;

// T-040 (REQ-006, REQ-008, REQ-039): leitura da pendencia Google. RED: os tipos abaixo ainda nao existem.
// Contrato (design.md linha 1532, GET /auth/google/pending): 200 {kind: "signup"|"link", email, suggestedDisplayName}
//   (suggestedDisplayName = nome do Google sem truncar, ou null); 404 pending_not_found (expirada/inexistente).
// Forma esperada (a spec nao fixa nomes de tipos; decisao desta tarefa, seguindo ResetPassword/PasswordResetCompleted):
//   sealed record GetPendingGoogleAuthCommand(string? PendingToken)   <- token do cookie __Host-dl_pending EM CLARO (design.md linhas 266, 474)
//   enum PendingGoogleAuthKind { Signup, Link }                        <- "signup|link" da linha 1532 (concluir/vincular)
//   sealed record PendingGoogleAuthView(PendingGoogleAuthKind Kind, string Email, string? SuggestedDisplayName)  <- linha 1532, sem tokens do Google
//   sealed class GetPendingGoogleAuth(IPendingGoogleAuthStore, ITokenHasher)
//       Task<Result<PendingGoogleAuthView>> ExecuteAsync(GetPendingGoogleAuthCommand, CancellationToken = default)
//   Falha: DomainError("pending_not_found") (linha 1532). Leitura usa FindAsync (nao consome).
//   Pendencia de vinculo: suggestedDisplayName = null (campo so existe em PendingGoogleSignup, design.md linha 482).
public class GetPendingGoogleAuthTests
{
    private const string Token = "tok-pending-1";

    private readonly Sha256TokenHasher _tokenHasher = new();
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly InMemoryPendingGoogleAuthStore _pending;

    public GetPendingGoogleAuthTests() => _pending = new InMemoryPendingGoogleAuthStore(_clock, new InMemoryUnitOfWork());

    private GetPendingGoogleAuth NewUseCase() => new(_pending, _tokenHasher);

    private async Task SeedSignupAsync(string? name = "Beatriz Souza", string email = "bia@example.com") =>
        await _pending.AddAsync(PendingGoogleSignup.Create(
            _tokenHasher.Hash(Token), "google-sub-1", AnEmail(email), name, Now));

    private async Task SeedLinkAsync(string email = "ana@example.com") =>
        await _pending.AddAsync(PendingGoogleLink.Create(
            _tokenHasher.Hash(Token), "google-sub-1", AnEmail(email), new PlayerId(Guid.NewGuid()), Now));

    [Fact]
    public async Task REQ006_a_pending_signup_is_read_as_signup_with_email_and_suggested_name()
    {
        await SeedSignupAsync();

        var result = await NewUseCase().ExecuteAsync(new GetPendingGoogleAuthCommand(Token));

        Assert.True(result.IsSuccess);
        Assert.Equal(PendingGoogleAuthKind.Signup, result.Value.Kind);
        Assert.Equal("bia@example.com", result.Value.Email);
        Assert.Equal("Beatriz Souza", result.Value.SuggestedDisplayName);
    }

    [Fact]
    public async Task REQ008_a_pending_link_is_read_as_link_with_the_email_and_no_suggested_name()
    {
        await SeedLinkAsync();

        var result = await NewUseCase().ExecuteAsync(new GetPendingGoogleAuthCommand(Token));

        Assert.True(result.IsSuccess);
        Assert.Equal(PendingGoogleAuthKind.Link, result.Value.Kind);
        Assert.Equal("ana@example.com", result.Value.Email);
        Assert.Null(result.Value.SuggestedDisplayName);
    }

    [Fact]
    public async Task REQ039_a_forty_character_google_name_is_returned_whole_without_truncation()
    {
        var longName = new string('B', 40);
        await SeedSignupAsync(longName);

        var result = await NewUseCase().ExecuteAsync(new GetPendingGoogleAuthCommand(Token));

        Assert.Equal(longName, result.Value.SuggestedDisplayName);
    }

    [Fact]
    public async Task REQ039_a_missing_google_name_is_returned_as_null()
    {
        await SeedSignupAsync(name: null);

        var result = await NewUseCase().ExecuteAsync(new GetPendingGoogleAuthCommand(Token));

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.SuggestedDisplayName);
    }

    [Fact]
    public async Task REQ039_reading_does_not_consume_the_pending()
    {
        await SeedSignupAsync();
        var useCase = NewUseCase();

        var first = await useCase.ExecuteAsync(new GetPendingGoogleAuthCommand(Token));
        var second = await useCase.ExecuteAsync(new GetPendingGoogleAuthCommand(Token));

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.NotNull(await _pending.FindAsync(_tokenHasher.Hash(Token)));
    }

    [Fact]
    public async Task REQ039_a_pending_is_still_readable_at_nine_minutes_fifty_nine_seconds()
    {
        await SeedSignupAsync();
        _clock.Advance(TimeSpan.FromMinutes(9) + TimeSpan.FromSeconds(59));

        var result = await NewUseCase().ExecuteAsync(new GetPendingGoogleAuthCommand(Token));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task REQ039_an_expired_pending_at_ten_minutes_one_second_is_pending_not_found()
    {
        await SeedSignupAsync();
        _clock.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1));

        var result = await NewUseCase().ExecuteAsync(new GetPendingGoogleAuthCommand(Token));

        Assert.True(result.IsFailure);
        Assert.Equal("pending_not_found", result.Error.Code);
    }

    [Fact]
    public async Task REQ006_an_unknown_token_is_pending_not_found()
    {
        await SeedSignupAsync();

        var result = await NewUseCase().ExecuteAsync(new GetPendingGoogleAuthCommand("another-token"));

        Assert.True(result.IsFailure);
        Assert.Equal("pending_not_found", result.Error.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task REQ006_a_missing_cookie_token_is_pending_not_found(string? token)
    {
        await SeedSignupAsync();

        var result = await NewUseCase().ExecuteAsync(new GetPendingGoogleAuthCommand(token));

        Assert.True(result.IsFailure);
        Assert.Equal("pending_not_found", result.Error.Code);
    }
}
