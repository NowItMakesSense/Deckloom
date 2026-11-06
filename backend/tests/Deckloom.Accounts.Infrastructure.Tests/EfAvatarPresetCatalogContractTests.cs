using Deckloom.Accounts.Application;
using Deckloom.Accounts.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Deckloom.Accounts.Infrastructure.Tests;

// T-145: o contrato de IAvatarPresetCatalog contra EfAvatarPresetCatalog (SQLite em memoria, sem banco real).
public sealed class EfAvatarPresetCatalogContractTests : AvatarPresetCatalogContract
{
    protected override CatalogHarness<IAvatarPresetCatalog> Create(IReadOnlyList<AvatarPresetEntry> seed)
    {
        var db = new SqliteInMemoryFixture();
        Execute(db, "DELETE FROM avatar_preset");
        foreach (var p in seed)
        {
            Execute(
                db,
                "INSERT INTO avatar_preset (id, label, image_path, is_default, is_active, sort_order) VALUES " +
                $"('{p.Id}', '{p.Label}', '{p.ImagePath}', {(p.IsDefault ? 1 : 0)}, {(p.IsActive ? 1 : 0)}, {p.SortOrder})");
        }

        var clock = new FakeTimeProvider(AccountsContractFixture.ContractNow);
        return new CatalogHarness<IAvatarPresetCatalog>(
            new EfAvatarPresetCatalog(new Factory(db), clock),
            clock,
            (id, isActive) =>
            {
                Execute(db, $"UPDATE avatar_preset SET is_active = {(isActive ? 1 : 0)} WHERE id = '{id}'");
                return Task.CompletedTask;
            },
            db);
    }

    private static void Execute(SqliteInMemoryFixture db, string sql)
    {
        using var command = db.Connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private sealed class Factory(SqliteInMemoryFixture db) : IDbContextFactory<AccountsDbContext>
    {
        public AccountsDbContext CreateDbContext() => db.CreateContext();
    }
}
