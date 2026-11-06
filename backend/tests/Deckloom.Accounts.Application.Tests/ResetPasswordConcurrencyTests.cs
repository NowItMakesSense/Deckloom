using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;
using Microsoft.Extensions.Time.Testing;
using static Deckloom.Accounts.Application.Tests.PlayerFixtures;

namespace Deckloom.Accounts.Application.Tests;

// T-058 (REQ-030): quem perde a corrida pelo token recebe o mesmo erro do link invalido, nunca a excecao de
// concorrencia da persistencia. O InMemoryUnitOfWork simula o conflito no commit da transacao.
public class ResetPasswordConcurrencyTests
{
    private const string RawToken = "reset-token-raw";

    private readonly InMemoryUnitOfWork _inner = new();
    private readonly Sha256TokenHasher _tokenHasher = new();

    [Fact]
    public async Task REQ030_a_concurrency_conflict_on_commit_becomes_reset_link_invalid_without_field()
    {
        var players = new InMemoryPlayerRepository(_inner);
        var player = APasswordPlayer();
        Assert.True(player.IssuePasswordReset(_tokenHasher.Hash(RawToken), Now).IsSuccess);
        await players.Add(player);
        await _inner.CommitAsync();
        var useCase = new ResetPassword(
            players,
            new InMemorySessionRepository(_inner),
            new ConflictingUnitOfWork(_inner),
            new FakePasswordHasher(600_000),
            _tokenHasher,
            new FakeTimeProvider(Now));

        var result = await useCase.ExecuteAsync(new ResetPasswordCommand(RawToken, "brand new password"));

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal(Player.ResetLinkInvalidCode, error.Code);
        Assert.Null(error.Field);
    }

    private sealed class ConflictingUnitOfWork(InMemoryUnitOfWork inner) : IUnitOfWork
    {
        public Task CommitAsync(CancellationToken ct = default) => inner.CommitAsync(ct);

        public Task ExecuteAsync(Func<CancellationToken, Task> work, CancellationToken ct = default) =>
            throw new ConcurrencyConflictException();

        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default) =>
            throw new ConcurrencyConflictException();
    }
}
