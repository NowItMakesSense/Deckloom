using Deckloom.Accounts.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Deckloom.Accounts.Infrastructure;

// Linha de accounts.player_preferred_format. O agregado guarda o conjunto em Player.Formats;
// AccountsDbContext sincroniza as linhas no SaveChanges e EfPlayerRepository as carrega na leitura.
// A FK format_id -> deck_suggestion.format (entre schemas, ADR-0001; RESTRICT, ADR-0007) e declarada contra
// ReferencedFormat: um alvo minimo so de modelo, sem codigo de acesso, que nunca e criado pelas migrations de Accounts.
internal sealed class ReferencedFormat
{
    public const string TableName = "format";
    public const string SchemaName = "deck_suggestion";

    public string Id { get; init; } = string.Empty;
}

internal sealed class ReferencedFormatConfiguration : IEntityTypeConfiguration<ReferencedFormat>
{
    public void Configure(EntityTypeBuilder<ReferencedFormat> row)
    {
        ArgumentNullException.ThrowIfNull(row);

        // A tabela pertence a Sugestao de Deck (que a cria e migra); aqui ela so ancora a FK.
        row.ToTable(ReferencedFormat.TableName, ReferencedFormat.SchemaName, t => t.ExcludeFromMigrations());
        row.HasKey(f => f.Id).HasName("pk_format");
        row.Property(f => f.Id).HasColumnName("id");
    }
}

internal sealed class PlayerPreferredFormat
{
    public const string TableName = "player_preferred_format";

    public PlayerId PlayerId { get; init; }

    public string FormatId { get; init; } = string.Empty;
}

internal sealed class PlayerPreferredFormatConfiguration : IEntityTypeConfiguration<PlayerPreferredFormat>
{
    public void Configure(EntityTypeBuilder<PlayerPreferredFormat> row)
    {
        ArgumentNullException.ThrowIfNull(row);

        row.ToTable(PlayerPreferredFormat.TableName);
        row.HasKey(r => new { r.PlayerId, r.FormatId }).HasName("pk_player_preferred_format");
        row.Property(r => r.PlayerId)
            .HasColumnName("player_id")
            .HasConversion(id => id.Value, value => new PlayerId(value));
        row.Property(r => r.FormatId).HasColumnName("format_id").IsRequired();
        row.HasIndex(r => r.FormatId).HasDatabaseName("ix_player_preferred_format_format_id");
        row.HasOne<ReferencedFormat>()
            .WithMany()
            .HasForeignKey(r => r.FormatId)
            .HasConstraintName("fk_player_preferred_format_format")
            .OnDelete(DeleteBehavior.Restrict);
        row.HasOne<Player>()
            .WithMany()
            .HasForeignKey(r => r.PlayerId)
            .HasConstraintName("fk_player_preferred_format_player")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
