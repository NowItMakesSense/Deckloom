using System.Globalization;
using Deckloom.Accounts.Domain;
using Microsoft.EntityFrameworkCore.Storage;

namespace Deckloom.Accounts.Infrastructure.Tests;

// T-063 (REQ-024, 025, 047; emenda ADR-0015). Contrato para o implementer (GREEN):
//   EfUnitOfWork.ExecuteAsync(work[, ct]) abre UMA transacao (Database.BeginTransactionAsync) so em volta da
//     persistencia, executa o bloco e confirma ao fim; CommitAsync chamado dentro do bloco (SaveChanges) participa da
//     mesma transacao. Se o bloco ou o commit lancar, faz rollback total e relanca a MESMA excecao.
//   Database.CurrentTransaction e nulo antes do bloco, nao nulo dentro e nulo depois (commit ou rollback).
public sealed class EfUnitOfWorkTests : IDisposable
{
    private const string Terms = "2026-01";
    private const string Stored =
        "v1$pbkdf2-sha256$600000$c2FsdHNhbHRzYWx0c2FsdA==$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g=";

    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteInMemoryFixture _db = new();

    public void Dispose() => _db.Dispose();

    private static Player NewPlayer(string email = "ana@example.com") =>
        Player.RegisterWithPassword(
            Email.Create(email).Value,
            PasswordHash.Parse(Stored).Value,
            DisplayName.Create("Ana Souza").Value,
            Terms,
            Terms,
            true,
            true,
            Now,
            "preset-1").Value;

    private int PlayerCount()
    {
        using var command = _db.Connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM player";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task REQ_047_no_transaction_is_open_before_the_persistence_block_starts()
    {
        await using var context = _db.CreateContext();
        _ = new EfUnitOfWork(context);

        Assert.Null(context.Database.CurrentTransaction);
    }

    [Fact]
    public async Task REQ_047_the_transaction_exists_only_inside_the_block_and_is_gone_after_commit()
    {
        await using var context = _db.CreateContext();
        var unitOfWork = new EfUnitOfWork(context);
        IDbContextTransaction? inside = null;

        await unitOfWork.ExecuteAsync(async ct =>
        {
            inside = context.Database.CurrentTransaction;
            await new EfPlayerRepository(context).Add(NewPlayer(), ct);
            await unitOfWork.CommitAsync(ct);
        });

        Assert.NotNull(inside);
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Equal(1, PlayerCount());
    }

    [Fact]
    public async Task REQ_047_the_generic_overload_also_opens_the_transaction_only_inside_the_block_and_returns_the_result()
    {
        await using var context = _db.CreateContext();
        var unitOfWork = new EfUnitOfWork(context);
        var openInside = false;

        var result = await unitOfWork.ExecuteAsync(async ct =>
        {
            openInside = context.Database.CurrentTransaction is not null;
            await new EfPlayerRepository(context).Add(NewPlayer(), ct);
            await unitOfWork.CommitAsync(ct);
            return 42;
        });

        Assert.Equal(42, result);
        Assert.True(openInside);
        Assert.Null(context.Database.CurrentTransaction);
    }

    // Os casos de uso so enfileiram dentro do bloco (nao chamam CommitAsync), como o InMemoryUnitOfWork espera: o bloco
    // que termina sem erro grava o que ficou pendente no contexto.
    [Fact]
    public async Task REQ_047_changes_only_queued_inside_the_block_are_saved_when_the_block_completes()
    {
        await using var context = _db.CreateContext();
        var unitOfWork = new EfUnitOfWork(context);

        await unitOfWork.ExecuteAsync(ct => new EfPlayerRepository(context).Add(NewPlayer(), ct));

        Assert.Equal(1, PlayerCount());
    }

    [Fact]
    public async Task REQ_047_the_generic_overload_also_saves_changes_only_queued_inside_the_block()
    {
        await using var context = _db.CreateContext();
        var unitOfWork = new EfUnitOfWork(context);

        await unitOfWork.ExecuteAsync(async ct =>
        {
            await new EfPlayerRepository(context).Add(NewPlayer(), ct);
            return 42;
        });

        Assert.Equal(1, PlayerCount());
    }

    [Fact]
    public async Task REQ_047_a_unique_violation_of_changes_only_queued_inside_the_block_rolls_back_and_rethrows_the_translated_exception()
    {
        await using (var seed = _db.CreateContext())
        {
            await new EfPlayerRepository(seed).Add(NewPlayer());
            await new EfUnitOfWork(seed).CommitAsync();
        }

        await using var context = _db.CreateContext();
        var unitOfWork = new EfUnitOfWork(context);

        var thrown = await Assert.ThrowsAsync<UniqueConstraintViolationException>(
            () => unitOfWork.ExecuteAsync(ct => new EfPlayerRepository(context).Add(NewPlayer(), ct)));

        Assert.Equal("ux_player_email", thrown.ConstraintName);
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Equal(1, PlayerCount());
    }

    [Fact]
    public async Task REQ_047_a_failure_in_the_middle_of_the_block_rolls_everything_back_and_closes_the_transaction()
    {
        await using var context = _db.CreateContext();
        var unitOfWork = new EfUnitOfWork(context);
        var failure = new InvalidOperationException("boom");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.ExecuteAsync(async ct =>
        {
            await new EfPlayerRepository(context).Add(NewPlayer(), ct);
            await unitOfWork.CommitAsync(ct); // ja gravado dentro da transacao...
            throw failure; // ...e o bloco falha depois
        }));

        Assert.Same(failure, thrown);
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Equal(0, PlayerCount());
    }

    [Fact]
    public async Task REQ_047_a_unique_violation_at_commit_inside_the_block_rolls_back_closes_the_transaction_and_rethrows_the_translated_exception()
    {
        await using (var seed = _db.CreateContext())
        {
            await new EfPlayerRepository(seed).Add(NewPlayer());
            await new EfUnitOfWork(seed).CommitAsync();
        }

        await using var context = _db.CreateContext();
        var unitOfWork = new EfUnitOfWork(context);

        var thrown = await Assert.ThrowsAsync<UniqueConstraintViolationException>(() => unitOfWork.ExecuteAsync(async ct =>
        {
            await new EfPlayerRepository(context).Add(NewPlayer(), ct);
            await unitOfWork.CommitAsync(ct);
        }));

        Assert.Equal("ux_player_email", thrown.ConstraintName);
        Assert.Null(context.Database.CurrentTransaction);
        Assert.Equal(1, PlayerCount());
    }
}
