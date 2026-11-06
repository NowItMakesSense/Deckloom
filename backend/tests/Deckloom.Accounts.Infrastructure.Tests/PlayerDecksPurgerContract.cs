using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;

namespace Deckloom.Accounts.Infrastructure.Tests;

// T-064 (REQ-025; ADR-0006): suite de contrato compartilhada por toda implementacao de IPlayerDecksPurger
// (NoDecksYetPurger hoje, a implementacao real de Decks na feature 9, RecordingPlayerDecksPurger nos testes).
public abstract class PlayerDecksPurgerContract
{
    protected abstract IPlayerDecksPurger CreatePurger(InMemoryUnitOfWork unitOfWork);

    [Fact]
    public async Task REQ_025_Contract_PurgeAsync_does_not_throw_for_a_player()
    {
        var purger = CreatePurger(new InMemoryUnitOfWork());

        await purger.PurgeAsync(new PlayerId(Guid.NewGuid()));
    }

    [Fact]
    public async Task REQ_025_Contract_PurgeAsync_inside_the_unit_of_work_commits_without_error()
    {
        var uow = new InMemoryUnitOfWork();
        var purger = CreatePurger(uow);

        await uow.ExecuteAsync(ct => purger.PurgeAsync(new PlayerId(Guid.NewGuid()), ct));

        Assert.False(uow.IsTransactionOpen);
    }

    [Fact]
    public async Task REQ_025_Contract_PurgeAsync_joins_the_ambient_transaction_instead_of_committing_its_own()
    {
        var uow = new InMemoryUnitOfWork();
        var purger = CreatePurger(uow);

        await uow.ExecuteAsync(async ct =>
        {
            await purger.PurgeAsync(new PlayerId(Guid.NewGuid()), ct);

            // O purger nao confirma nem encerra a transacao do chamador: ela segue aberta ate o fim do bloco.
            Assert.True(uow.IsTransactionOpen);
            Assert.Null(uow.TransactionOpenDuringLastCommit);
        });

        Assert.True(uow.TransactionOpenDuringLastCommit);
    }
}
