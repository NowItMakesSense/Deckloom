using Deckloom.Accounts.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Deckloom.Accounts.Infrastructure;

// Mapeamento de Database/tables/accounts.pending_google_auth.md. Sem default nem funcao de data do banco: o "agora"
// chega sempre pelo TimeProvider. Datas gravadas em UTC (timestamptz). So o hash do token, nunca o token em claro.
internal sealed class PendingGoogleAuthEntityConfiguration : IEntityTypeConfiguration<PendingGoogleAuthRow>
{
    public const string TableName = "pending_google_auth";
    public const string TokenHashIndexName = "ux_pending_google_auth_token_hash";

    private static readonly ValueConverter<DateTimeOffset, DateTimeOffset> Utc =
        new(value => value.ToUniversalTime(), value => value);

    public void Configure(EntityTypeBuilder<PendingGoogleAuthRow> pending)
    {
        ArgumentNullException.ThrowIfNull(pending);

        pending.ToTable(
            TableName,
            table =>
            {
                table.HasCheckConstraint(
                    "ck_pending_google_auth_kind",
                    $"kind IN ('{PendingGoogleAuthRow.SignupKind}', '{PendingGoogleAuthRow.LinkKind}')");
                table.HasCheckConstraint(
                    "ck_pending_google_auth_kind_target",
                    $"(kind = '{PendingGoogleAuthRow.LinkKind}' AND target_player_id IS NOT NULL) " +
                    $"OR (kind = '{PendingGoogleAuthRow.SignupKind}' AND target_player_id IS NULL)");
            });
        pending.HasKey(p => p.Id).HasName("pk_pending_google_auth");
        pending.Property(p => p.Id).HasColumnName("id").ValueGeneratedNever();

        pending.Property(p => p.TokenHash)
            .HasColumnName("token_hash")
            .HasConversion(hash => hash.ToBytes(), bytes => TokenHash.Create(bytes).Value)
            .IsRequired();
        pending.HasIndex(p => p.TokenHash).IsUnique().HasDatabaseName(TokenHashIndexName);

        pending.Property(p => p.Kind).HasColumnName("kind").IsRequired();
        pending.Property(p => p.GoogleSubject).HasColumnName("google_subject").IsRequired();
        pending.Property(p => p.Email)
            .HasColumnName("email")
            .HasConversion(email => email.Value, value => Email.Create(value).Value)
            .IsRequired();
        pending.Property(p => p.SuggestedDisplayName).HasColumnName("suggested_display_name");

        pending.Property(p => p.TargetPlayerId)
            .HasColumnName("target_player_id")
            .HasConversion(id => id!.Value.Value, value => new PlayerId(value));
        pending.HasOne<Player>()
            .WithMany()
            .HasForeignKey(p => p.TargetPlayerId)
            .HasConstraintName("fk_pending_google_auth_target_player")
            .OnDelete(DeleteBehavior.Cascade);
        pending.HasIndex(p => p.TargetPlayerId).HasDatabaseName("ix_pending_google_auth_target_player_id");

        pending.Property(p => p.CreatedAt).HasColumnName("created_at").HasConversion(Utc);
        pending.Property(p => p.ExpiresAt).HasColumnName("expires_at").HasConversion(UtcComparableConverter.Instance);
        pending.HasIndex(p => p.ExpiresAt).HasDatabaseName("ix_pending_google_auth_expires_at");
    }
}
