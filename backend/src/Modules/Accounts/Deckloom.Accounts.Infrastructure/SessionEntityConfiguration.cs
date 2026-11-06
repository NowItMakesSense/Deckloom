using Deckloom.Accounts.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Deckloom.Accounts.Infrastructure;

// Mapeamento de Database/tables/accounts.player_session.md. Sem default nem funcao de data do banco: o "agora"
// chega sempre pelo agregado. Datas gravadas em UTC (timestamptz). Sem IP, sem user agent, sem token em claro.
internal sealed class SessionEntityConfiguration : IEntityTypeConfiguration<Session>
{
    public const string TableName = "player_session";
    public const string TokenHashIndexName = "ux_player_session_token_hash";

    private static readonly ValueConverter<DateTimeOffset, DateTimeOffset> Utc =
        new(value => value.ToUniversalTime(), value => value);

    public void Configure(EntityTypeBuilder<Session> session)
    {
        ArgumentNullException.ThrowIfNull(session);

        session.ToTable(TableName);
        session.HasKey(s => s.Id).HasName("pk_player_session");
        session.Property(s => s.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new SessionId(value))
            .ValueGeneratedNever();

        session.Property(s => s.TokenHash)
            .HasColumnName("token_hash")
            .HasConversion(hash => hash.ToBytes(), bytes => TokenHash.Create(bytes).Value)
            .IsRequired();
        session.HasIndex(s => s.TokenHash).IsUnique().HasDatabaseName(TokenHashIndexName);

        session.Property(s => s.PlayerId)
            .HasColumnName("player_id")
            .HasConversion(id => id.Value, value => new PlayerId(value));
        session.HasIndex(s => s.PlayerId).HasDatabaseName("ix_player_session_player_id");
        session.HasOne<Player>()
            .WithMany()
            .HasForeignKey(s => s.PlayerId)
            .HasConstraintName("fk_player_session_player")
            .OnDelete(DeleteBehavior.Cascade);

        session.Property(s => s.RememberMe).HasColumnName("remember_me");
        session.Property(s => s.CreatedAt).HasColumnName("created_at").HasConversion(Utc);
        session.Property(s => s.LastSeenAt).HasColumnName("last_seen_at").HasConversion(Utc);
        session.Property(s => s.ExpiresAt).HasColumnName("expires_at").HasConversion(UtcComparableConverter.Instance);
        session.HasIndex(s => s.ExpiresAt).HasDatabaseName("ix_player_session_expires_at");
        session.Property(s => s.RevokedAt).HasColumnName("revoked_at").HasConversion(Utc);
        session.Property(s => s.ReauthenticatedAt).HasColumnName("reauthenticated_at").HasConversion(Utc);

        session.Ignore(s => s.DomainEvents);
    }
}
