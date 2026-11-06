using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional
#pragma warning disable CA1861 // Dados e índices gerados pelo EF: arrays constantes são esperados em migrations

namespace Deckloom.Accounts.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CreateAccountsSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "accounts");

            migrationBuilder.CreateTable(
                name: "access_attempt",
                schema: "accounts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    action = table.Column<string>(type: "text", nullable: false),
                    ip_key_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    account_key_hash = table.Column<byte[]>(type: "bytea", nullable: true),
                    attempted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_access_attempt", x => x.id);
                    table.CheckConstraint("ck_access_attempt_action", "action IN ('login', 'password_reset_request', 'google_link', 'account_deletion', 'registration')");
                });

            migrationBuilder.CreateTable(
                name: "avatar_preset",
                schema: "accounts",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    label = table.Column<string>(type: "text", nullable: false),
                    image_path = table.Column<string>(type: "text", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_avatar_preset", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "player",
                schema: "accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    bio = table.Column<string>(type: "text", nullable: true),
                    favorite_tcg = table.Column<string>(type: "text", nullable: false, defaultValueSql: "'mtg'"),
                    terms_version = table.Column<string>(type: "text", nullable: false),
                    terms_accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    adult_declared = table.Column<bool>(type: "boolean", nullable: false),
                    avatar_kind = table.Column<string>(type: "text", nullable: false),
                    avatar_preset_id = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    custom_avatar_key = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_player", x => x.id);
                    table.CheckConstraint("ck_player_adult_declared", "adult_declared = true");
                    table.CheckConstraint("ck_player_avatar_consistency", "(avatar_kind = 'preset' AND avatar_preset_id IS NOT NULL AND custom_avatar_key IS NULL) OR (avatar_kind = 'custom' AND custom_avatar_key IS NOT NULL AND avatar_preset_id IS NULL)");
                    table.CheckConstraint("ck_player_avatar_kind", "avatar_kind IN ('preset', 'custom')");
                    table.CheckConstraint("ck_player_bio_length", "bio IS NULL OR length(bio) <= 1200");
                    table.CheckConstraint("ck_player_email_length", "length(email) <= 254");
                    table.ForeignKey(
                        name: "fk_player_avatar_preset",
                        column: x => x.avatar_preset_id,
                        principalSchema: "accounts",
                        principalTable: "avatar_preset",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "avatar_image",
                schema: "accounts",
                columns: table => new
                {
                    storage_key = table.Column<string>(type: "text", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_avatar_image", x => x.storage_key);
                    table.CheckConstraint("ck_avatar_image_content_type", "content_type IN ('image/webp')");
                    table.ForeignKey(
                        name: "fk_avatar_image_player",
                        column: x => x.player_id,
                        principalSchema: "accounts",
                        principalTable: "player",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "password_reset_token",
                schema: "accounts",
                columns: table => new
                {
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_password_reset_token", x => x.player_id);
                    table.ForeignKey(
                        name: "fk_password_reset_token_player",
                        column: x => x.player_id,
                        principalSchema: "accounts",
                        principalTable: "player",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pending_google_auth",
                schema: "accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    google_subject = table.Column<string>(type: "text", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    suggested_display_name = table.Column<string>(type: "text", nullable: true),
                    target_player_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pending_google_auth", x => x.id);
                    table.CheckConstraint("ck_pending_google_auth_kind", "kind IN ('signup', 'link')");
                    table.CheckConstraint("ck_pending_google_auth_kind_target", "(kind = 'link' AND target_player_id IS NOT NULL) OR (kind = 'signup' AND target_player_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_pending_google_auth_target_player",
                        column: x => x.target_player_id,
                        principalSchema: "accounts",
                        principalTable: "player",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "player_external_identity",
                schema: "accounts",
                columns: table => new
                {
                    provider = table.Column<string>(type: "text", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject = table.Column<string>(type: "text", nullable: false),
                    linked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_player_external_identity", x => new { x.player_id, x.provider });
                    table.CheckConstraint("ck_player_external_identity_provider", "provider IN ('google')");
                    table.ForeignKey(
                        name: "fk_player_external_identity_player",
                        column: x => x.player_id,
                        principalSchema: "accounts",
                        principalTable: "player",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "player_preferred_format",
                schema: "accounts",
                columns: table => new
                {
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    format_id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_player_preferred_format", x => new { x.player_id, x.format_id });
                    table.ForeignKey(
                        name: "fk_player_preferred_format_format",
                        column: x => x.format_id,
                        principalSchema: "deck_suggestion",
                        principalTable: "format",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_player_preferred_format_player",
                        column: x => x.player_id,
                        principalSchema: "accounts",
                        principalTable: "player",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "player_session",
                schema: "accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    remember_me = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reauthenticated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_player_session", x => x.id);
                    table.ForeignKey(
                        name: "fk_player_session_player",
                        column: x => x.player_id,
                        principalSchema: "accounts",
                        principalTable: "player",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("GRANT USAGE ON SCHEMA accounts TO deckloom_app;");

            migrationBuilder.Sql("GRANT SELECT, INSERT, DELETE ON accounts.access_attempt TO deckloom_app;");

            migrationBuilder.Sql("GRANT SELECT, INSERT, DELETE ON accounts.player TO deckloom_app;");

            migrationBuilder.Sql("GRANT UPDATE (password_hash, display_name, bio, favorite_tcg, avatar_kind, avatar_preset_id, custom_avatar_key, updated_at) ON accounts.player TO deckloom_app;");

            migrationBuilder.Sql("GRANT SELECT, INSERT, DELETE ON accounts.player_external_identity TO deckloom_app;");

            migrationBuilder.Sql("GRANT SELECT, INSERT, DELETE ON accounts.player_preferred_format TO deckloom_app;");

            migrationBuilder.Sql("GRANT SELECT, INSERT, DELETE ON accounts.pending_google_auth TO deckloom_app;");

            migrationBuilder.Sql("GRANT SELECT, INSERT, DELETE ON accounts.avatar_image TO deckloom_app;");

            migrationBuilder.Sql("GRANT SELECT, INSERT, DELETE ON accounts.password_reset_token TO deckloom_app;");

            migrationBuilder.Sql("GRANT UPDATE (token_hash, issued_at, expires_at) ON accounts.password_reset_token TO deckloom_app;");

            migrationBuilder.Sql("GRANT SELECT, INSERT, DELETE ON accounts.player_session TO deckloom_app;");

            migrationBuilder.Sql("GRANT UPDATE (last_seen_at, expires_at, revoked_at, reauthenticated_at) ON accounts.player_session TO deckloom_app;");

            migrationBuilder.Sql("GRANT SELECT ON accounts.avatar_preset TO deckloom_app;");

            migrationBuilder.InsertData(
                schema: "accounts",
                table: "avatar_preset",
                columns: new[] { "id", "image_path", "is_active", "is_default", "label", "sort_order" },
                values: new object[,]
                {
                    { "chama-amarela", "/avatars/presets/chama-amarela.svg", true, false, "Chama amarela", 8 },
                    { "circulo-azul", "/avatars/presets/circulo-azul.svg", true, true, "Círculo azul", 1 },
                    { "estrela-roxa", "/avatars/presets/estrela-roxa.svg", true, false, "Estrela roxa", 4 },
                    { "folha-verde", "/avatars/presets/folha-verde.svg", true, false, "Folha verde", 7 },
                    { "losango-laranja", "/avatars/presets/losango-laranja.svg", true, false, "Losango laranja", 3 },
                    { "onda-ciano", "/avatars/presets/onda-ciano.svg", true, false, "Onda ciano", 6 },
                    { "quadrado-verde", "/avatars/presets/quadrado-verde.svg", true, false, "Quadrado verde", 2 },
                    { "triangulo-vermelho", "/avatars/presets/triangulo-vermelho.svg", true, false, "Triângulo vermelho", 5 }
                });

            migrationBuilder.CreateIndex(
                name: "ix_access_attempt_account_key",
                schema: "accounts",
                table: "access_attempt",
                columns: new[] { "account_key_hash", "attempted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_access_attempt_attempted_at",
                schema: "accounts",
                table: "access_attempt",
                column: "attempted_at");

            migrationBuilder.CreateIndex(
                name: "ix_access_attempt_ip_key",
                schema: "accounts",
                table: "access_attempt",
                columns: new[] { "ip_key_hash", "attempted_at" });

            migrationBuilder.CreateIndex(
                name: "ux_avatar_image_player_id",
                schema: "accounts",
                table: "avatar_image",
                column: "player_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_avatar_preset_single_default",
                schema: "accounts",
                table: "avatar_preset",
                column: "is_default",
                unique: true,
                filter: "is_default = true");

            migrationBuilder.CreateIndex(
                name: "ux_password_reset_token_token_hash",
                schema: "accounts",
                table: "password_reset_token",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pending_google_auth_expires_at",
                schema: "accounts",
                table: "pending_google_auth",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_pending_google_auth_target_player_id",
                schema: "accounts",
                table: "pending_google_auth",
                column: "target_player_id");

            migrationBuilder.CreateIndex(
                name: "ux_pending_google_auth_token_hash",
                schema: "accounts",
                table: "pending_google_auth",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_player_avatar_preset_id",
                schema: "accounts",
                table: "player",
                column: "avatar_preset_id");

            migrationBuilder.CreateIndex(
                name: "ux_player_email",
                schema: "accounts",
                table: "player",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_player_external_identity_player_id",
                schema: "accounts",
                table: "player_external_identity",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "ux_player_external_identity_provider_subject",
                schema: "accounts",
                table: "player_external_identity",
                columns: new[] { "provider", "subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_player_preferred_format_format_id",
                schema: "accounts",
                table: "player_preferred_format",
                column: "format_id");

            migrationBuilder.CreateIndex(
                name: "ix_player_session_expires_at",
                schema: "accounts",
                table: "player_session",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_player_session_player_id",
                schema: "accounts",
                table: "player_session",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "ux_player_session_token_hash",
                schema: "accounts",
                table: "player_session",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "access_attempt",
                schema: "accounts");

            migrationBuilder.DropTable(
                name: "avatar_image",
                schema: "accounts");

            migrationBuilder.DropTable(
                name: "password_reset_token",
                schema: "accounts");

            migrationBuilder.DropTable(
                name: "pending_google_auth",
                schema: "accounts");

            migrationBuilder.DropTable(
                name: "player_external_identity",
                schema: "accounts");

            migrationBuilder.DropTable(
                name: "player_preferred_format",
                schema: "accounts");

            migrationBuilder.DropTable(
                name: "player_session",
                schema: "accounts");

            migrationBuilder.DropTable(
                name: "player",
                schema: "accounts");

            migrationBuilder.DropTable(
                name: "avatar_preset",
                schema: "accounts");
        }
    }
}
