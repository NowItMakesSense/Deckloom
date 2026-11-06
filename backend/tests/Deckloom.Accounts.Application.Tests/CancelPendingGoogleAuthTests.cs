using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;
using Microsoft.Extensions.Time.Testing;
using static Deckloom.Accounts.Application.Tests.PlayerFixtures;

namespace Deckloom.Accounts.Application.Tests;

// T-040 (REQ-006, REQ-008, REQ-039): cancelamento da pendencia Google. RED: os tipos abaixo ainda nao existem.
// Contrato (design.md linha 1533, DELETE /auth/google/pending): 204 + cookie expirado; sem erro (idempotente).
// Forma esperada (a spec nao fixa nomes; decisao desta tarefa, seguindo ResetPassword/PasswordResetCompleted):
//   sealed record CancelPendingGoogleAuthCommand(string? PendingToken)   <- token em claro do cookie __Host-dl_pending
//   sealed record PendingGoogleAuthCancelled                              <- marcador de sucesso
//   sealed class CancelPendingGoogleAuth(IPendingGoogleAuthStore, ITokenHasher, IUnitOfWork)
//       Task<Result<PendingGoogleAuthCancelled>> ExecuteAsync(CancelPendingGoogleAuthCommand, CancellationToken = default)
//   Sempre Success (idempotente: inexistente, expirada, ja cancelada ou token ausente). Descarta via ConsumeAsync.
public class CancelPendingGoogleAuthTests
{
    private const string Token = "tok-pending-1";

    private readonly InMemoryUnitOfWork _uow = new();
    private readonly Sha256TokenHasher _tokenHasher = new();
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly InMemoryPendingGoogleAuthStore _pending;

    public CancelPendingGoogleAuthTests() => _pending = new InMemoryPendingGoogleAuthStore(_clock, _uow);

    private CancelPendingGoogleAuth NewUseCase() => new(_pending, _tokenHasher, _uow);

    private async Task SeedSignupAsync(string token = Token) =>
        await _pending.AddAsync(PendingGoogleSignup.Create(
            _tokenHasher.Hash(token), "google-sub-1", AnEmail("bia@example.com"), "Beatriz", Now));

    private async Task SeedLinkAsync() =>
        await _pending.AddAsync(PendingGoogleLink.Create(
            _tokenHasher.Hash(Token), "google-sub-1", AnEmail("ana@example.com"), new PlayerId(Guid.NewGuid()), Now));

    private async Task<PendingGoogleAuth?> FindAsync(string token = Token) =>
        await _pending.FindAsync(_tokenHasher.Hash(token));

    [Fact]
    public async Task REQ006_cancelling_discards_a_pending_signup()
    {
        await SeedSignupAsync();

        var result = await NewUseCase().ExecuteAsync(new CancelPendingGoogleAuthCommand(Token));

        Assert.True(result.IsSuccess);
        Assert.Null(await FindAsync());
    }

    [Fact]
    public async Task REQ008_cancelling_discards_a_pending_link()
    {
        await SeedLinkAsync();

        var result = await NewUseCase().ExecuteAsync(new CancelPendingGoogleAuthCommand(Token));

        Assert.True(result.IsSuccess);
        Assert.Null(await FindAsync());
    }

    [Fact]
    public async Task REQ039_cancelling_twice_is_idempotent()
    {
        await SeedSignupAsync();
        var useCase = NewUseCase();

        var first = await useCase.ExecuteAsync(new CancelPendingGoogleAuthCommand(Token));
        var second = await useCase.ExecuteAsync(new CancelPendingGoogleAuthCommand(Token));

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Null(await FindAsync());
    }

    [Fact]
    public async Task REQ039_cancelling_an_unknown_token_succeeds_and_keeps_other_pendings()
    {
        await SeedSignupAsync();

        var result = await NewUseCase().ExecuteAsync(new CancelPendingGoogleAuthCommand("another-token"));

        Assert.True(result.IsSuccess);
        Assert.NotNull(await FindAsync());
    }

    [Fact]
    public async Task REQ039_cancelling_an_expired_pending_succeeds()
    {
        await SeedSignupAsync();
        _clock.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1));

        var result = await NewUseCase().ExecuteAsync(new CancelPendingGoogleAuthCommand(Token));

        Assert.True(result.IsSuccess);
        Assert.Null(await FindAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task REQ039_cancelling_without_a_cookie_token_succeeds(string? token)
    {
        await SeedSignupAsync();

        var result = await NewUseCase().ExecuteAsync(new CancelPendingGoogleAuthCommand(token));

        Assert.True(result.IsSuccess);
        Assert.NotNull(await FindAsync());
    }

    [Fact]
    public async Task REQ006_a_cancelled_pending_can_no_longer_be_read()
    {
        await SeedSignupAsync();
        await NewUseCase().ExecuteAsync(new CancelPendingGoogleAuthCommand(Token));

        var read = await new GetPendingGoogleAuth(_pending, _tokenHasher)
            .ExecuteAsync(new GetPendingGoogleAuthCommand(Token));

        Assert.True(read.IsFailure);
        Assert.Equal("pending_not_found", read.Error.Code);
    }
}
