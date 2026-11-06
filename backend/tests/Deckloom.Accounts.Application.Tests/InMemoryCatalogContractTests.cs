using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Deckloom.Accounts.Application.Tests;

// T-145: os contratos dos catalogos contra os fakes em memoria.
public sealed class InMemoryAvatarPresetCatalogContractTests : AvatarPresetCatalogContract
{
    protected override CatalogHarness<IAvatarPresetCatalog> Create(IReadOnlyList<AvatarPresetEntry> seed)
    {
        var catalog = new InMemoryAvatarPresetCatalog(seed);
        return new CatalogHarness<IAvatarPresetCatalog>(
            catalog,
            new FakeTimeProvider(AccountsContractFixture.ContractNow),
            (id, isActive) =>
            {
                if (isActive)
                {
                    catalog.Activate(id);
                }
                else
                {
                    catalog.Deactivate(id);
                }

                return Task.CompletedTask;
            });
    }
}

public sealed class InMemoryFormatCatalogContractTests : FormatCatalogContract
{
    protected override CatalogHarness<IFormatCatalog> Create(IReadOnlyList<FormatEntry> seed)
    {
        var catalog = new InMemoryFormatCatalog(seed);
        return new CatalogHarness<IFormatCatalog>(
            catalog,
            new FakeTimeProvider(AccountsContractFixture.ContractNow),
            (id, isActive) =>
            {
                if (isActive)
                {
                    catalog.Activate(new FormatId(id));
                }
                else
                {
                    catalog.Deactivate(new FormatId(id));
                }

                return Task.CompletedTask;
            });
    }
}
