using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional
#pragma warning disable CA1861 // Dados e índices gerados pelo EF: arrays constantes são esperados em migrations

namespace Deckloom.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CreateTcgCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.CreateTable(
                name: "tcg",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tcg", x => x.id);
                });

            migrationBuilder.Sql("GRANT USAGE ON SCHEMA catalog TO deckloom_app;");

            migrationBuilder.Sql("GRANT SELECT ON catalog.tcg TO deckloom_app;");

            migrationBuilder.InsertData(
                schema: "catalog",
                table: "tcg",
                columns: new[] { "id", "is_active", "name", "sort_order" },
                values: new object[,]
                {
                    { "lorcana", true, "Lorcana", 5 },
                    { "mtg", true, "Magic: The Gathering", 1 },
                    { "onepiece", true, "One Piece", 4 },
                    { "pokemon", true, "Pokemon TCG", 2 },
                    { "yugioh", true, "Yu-Gi-Oh!", 3 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tcg",
                schema: "catalog");
        }
    }
}
