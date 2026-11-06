using Deckloom.Accounts.Application;
using Deckloom.Accounts.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Deckloom.DeckSuggestion.Infrastructure.Tests;

// T-145: o contrato de IFormatCatalog contra EfFormatCatalog (SQLite em memoria, sem banco real).
public sealed class EfFormatCatalogContractTests : FormatCatalogContract
{
    protected override CatalogHarness<IFormatCatalog> Create(IReadOnlyList<FormatEntry> seed)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<FormatCatalogDbContext>().UseSqlite(connection).Options;
        using (var setup = new FormatCatalogDbContext(options))
        {
            setup.Database.EnsureCreated();
        }

        // O modelo ja traz a carga inicial (HasData); o contrato controla o catalogo por completo.
        Execute(connection, "DELETE FROM format");
        foreach (var f in seed)
        {
            Execute(
                connection,
                "INSERT INTO format (id, name, is_active, sort_order) VALUES " +
                $"('{f.Id.Value}', '{f.Name}', {(f.IsActive ? 1 : 0)}, {f.SortOrder})");
        }

        return new CatalogHarness<IFormatCatalog>(
            new EfFormatCatalog(new FormatCatalogDbContext(options)),
            new FakeTimeProvider(AccountsContractFixture.ContractNow),
            (id, isActive) =>
            {
                Execute(connection, $"UPDATE format SET is_active = {(isActive ? 1 : 0)} WHERE id = '{id}'");
                return Task.CompletedTask;
            },
            connection);
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
