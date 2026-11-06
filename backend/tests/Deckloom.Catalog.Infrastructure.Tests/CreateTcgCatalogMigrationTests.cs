using Deckloom.Catalog.Infrastructure.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Deckloom.Catalog.Infrastructure.Tests;

// T-P04 (AC-P04a, ADR-0015). A migration gerada como arquivo cria catalog.tcg com os 5 TCGs, concede so SELECT
// a deckloom_app e tem rollback (Down). Inspecao das operacoes: nada e aplicado e nenhum banco e consultado.
public sealed class CreateTcgCatalogMigrationTests
{
    private static readonly string[] ExpectedIds = ["mtg", "pokemon", "yugioh", "onepiece", "lorcana"];

    private static readonly CreateTcgCatalog Migration = new();

    [Fact]
    public void AC_P04a_up_creates_the_catalog_schema_and_the_tcg_table_with_its_primary_key()
    {
        var table = Assert.Single(Migration.UpOperations.OfType<CreateTableOperation>());

        Assert.Equal("tcg", table.Name);
        Assert.Equal("catalog", table.Schema);
        Assert.Equal(["id", "name", "is_active", "sort_order"], table.Columns.Select(c => c.Name));
        Assert.All(table.Columns, c => Assert.False(c.IsNullable));
        Assert.Equal(["id"], table.PrimaryKey!.Columns);
        Assert.Contains(Migration.UpOperations.OfType<EnsureSchemaOperation>(), s => s.Name == "catalog");
    }

    [Fact]
    public void AC_P04a_up_loads_exactly_the_five_mvp_tcgs()
    {
        var insert = Assert.Single(Migration.UpOperations.OfType<InsertDataOperation>());

        Assert.Equal("catalog", insert.Schema);
        Assert.Equal("tcg", insert.Table);
        Assert.Equal(["id", "is_active", "name", "sort_order"], insert.Columns);
        var ids = Enumerable.Range(0, insert.Values.GetLength(0)).Select(i => (string)insert.Values[i, 0]!);
        Assert.Equal(ExpectedIds.Order(StringComparer.Ordinal), ids.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AC_P04a_up_grants_usage_and_select_only_to_the_application_role()
    {
        var sql = Migration.UpOperations.OfType<SqlOperation>().Select(o => o.Sql).ToList();

        Assert.Equal(
            ["GRANT USAGE ON SCHEMA catalog TO deckloom_app;", "GRANT SELECT ON catalog.tcg TO deckloom_app;"],
            sql);
    }

    [Fact]
    public void AC_P04a_down_rolls_back_by_dropping_the_tcg_table()
    {
        var drop = Assert.Single(Migration.DownOperations.OfType<DropTableOperation>());

        Assert.Equal("tcg", drop.Name);
        Assert.Equal("catalog", drop.Schema);
    }
}
