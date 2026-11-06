using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional
#pragma warning disable CA1861 // Dados e índices gerados pelo EF: arrays constantes são esperados em migrations

namespace Deckloom.DeckSuggestion.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CreateFormatCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "deck_suggestion");

            migrationBuilder.CreateTable(
                name: "format",
                schema: "deck_suggestion",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_format", x => x.id);
                });

            migrationBuilder.Sql("GRANT USAGE ON SCHEMA deck_suggestion TO deckloom_app;");

            migrationBuilder.Sql("GRANT SELECT ON deck_suggestion.format TO deckloom_app;");

            migrationBuilder.InsertData(
                schema: "deck_suggestion",
                table: "format",
                columns: new[] { "id", "is_active", "name", "sort_order" },
                values: new object[,]
                {
                    { "commander", true, "Commander", 4 },
                    { "modern", true, "Modern", 2 },
                    { "pauper", true, "Pauper", 5 },
                    { "pioneer", true, "Pioneer", 3 },
                    { "standard", true, "Standard", 1 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "format",
                schema: "deck_suggestion");
        }
    }
}
