using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;

namespace Deckloom.Accounts.Infrastructure.Tests;

// T-064 (REQ-025; ADR-0006): NoDecksYetPurger e um no-op documentado ate a feature 9 (Decks).
public class NoDecksYetPurgerTests : PlayerDecksPurgerContract
{
    protected override IPlayerDecksPurger CreatePurger(InMemoryUnitOfWork unitOfWork) => new NoDecksYetPurger();

    [Fact]
    public void REQ_025_NoDecksYetPurger_implements_IPlayerDecksPurger()
    {
        Assert.IsAssignableFrom<IPlayerDecksPurger>(new NoDecksYetPurger());
    }

    [Fact]
    public void REQ_025_NoDecksYetPurger_depends_on_nothing_so_it_cannot_open_its_own_transaction()
    {
        var constructors = typeof(NoDecksYetPurger).GetConstructors();

        Assert.All(constructors, c => Assert.Empty(c.GetParameters()));
    }

    [Fact]
    public async Task REQ_025_NoDecksYetPurger_returns_a_completed_task_without_side_effects()
    {
        var task = new NoDecksYetPurger().PurgeAsync(new PlayerId(Guid.NewGuid()));

        await task;
        Assert.True(task.IsCompletedSuccessfully);
    }
}
