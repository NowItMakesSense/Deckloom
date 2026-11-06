using Deckloom.Accounts.Application;
using Deckloom.Accounts.Testing;

namespace Deckloom.Accounts.Infrastructure.Tests;

// T-064 (REQ-025): o fake RecordingPlayerDecksPurger cumpre o mesmo contrato, sendo substituivel pelo adapter real.
public class RecordingPlayerDecksPurgerContractTests : PlayerDecksPurgerContract
{
    protected override IPlayerDecksPurger CreatePurger(InMemoryUnitOfWork unitOfWork) =>
        new RecordingPlayerDecksPurger(unitOfWork);
}
