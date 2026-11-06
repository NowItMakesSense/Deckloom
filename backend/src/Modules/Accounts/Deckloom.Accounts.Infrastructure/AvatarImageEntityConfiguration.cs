using Deckloom.Accounts.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Deckloom.Accounts.Infrastructure;

// Mapeamento de Database/tables/accounts.avatar_image.md. Sem default de data do banco: o "agora" vem do TimeProvider.
// O CHECK octet_length(content) <= 262144 so existe no PostgreSQL e NAO e verificado pelo SQLite dos testes.
internal sealed class AvatarImageEntityConfiguration : IEntityTypeConfiguration<AvatarImageRow>
{
    public const string TableName = "avatar_image";
    public const string PlayerIndexName = "ux_avatar_image_player_id";
    public const string PrimaryKeyName = "pk_avatar_image";

    private static readonly ValueConverter<DateTimeOffset, DateTimeOffset> Utc =
        new(value => value.ToUniversalTime(), value => value);

    public void Configure(EntityTypeBuilder<AvatarImageRow> image)
    {
        ArgumentNullException.ThrowIfNull(image);

        image.ToTable(
            TableName,
            table => table.HasCheckConstraint(
                "ck_avatar_image_content_type",
                $"content_type IN ('{AvatarImageRow.WebpContentType}')"));
        image.HasKey(i => i.StorageKey).HasName(PrimaryKeyName);
        image.Property(i => i.StorageKey).HasColumnName("storage_key").ValueGeneratedNever();

        image.Property(i => i.PlayerId)
            .HasColumnName("player_id")
            .HasConversion(id => id.Value, value => new PlayerId(value))
            .IsRequired();
        image.HasIndex(i => i.PlayerId).IsUnique().HasDatabaseName(PlayerIndexName);
        image.HasOne<Player>()
            .WithMany()
            .HasForeignKey(i => i.PlayerId)
            .HasConstraintName("fk_avatar_image_player")
            .OnDelete(DeleteBehavior.Cascade);

        image.Property(i => i.ContentType).HasColumnName("content_type").IsRequired();
        image.Property(i => i.Content).HasColumnName("content").IsRequired();
        image.Property(i => i.CreatedAt).HasColumnName("created_at").HasConversion(Utc);
    }
}
