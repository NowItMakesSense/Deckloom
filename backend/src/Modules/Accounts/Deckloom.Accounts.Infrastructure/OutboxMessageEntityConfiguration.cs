using Deckloom.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Deckloom.Accounts.Infrastructure;

// Mapeamento de accounts.outbox_message (APR-017). Datas como DateTime UTC (timestamptz), como em access_attempt.
internal sealed class OutboxMessageEntityConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public const string TableName = "outbox_message";

    private static readonly ValueConverter<DateTimeOffset, DateTime> Utc =
        new(value => value.UtcDateTime, value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));

    private static readonly ValueConverter<DateTimeOffset?, DateTime?> NullableUtc =
        new(
            value => value == null ? null : value.Value.UtcDateTime,
            value => value == null ? null : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)));

    public void Configure(EntityTypeBuilder<OutboxMessage> message)
    {
        ArgumentNullException.ThrowIfNull(message);

        message.ToTable(TableName);
        message.HasKey(m => m.Id).HasName("pk_outbox_message");
        message.Property(m => m.Id).HasColumnName("id").ValueGeneratedNever();
        message.Property(m => m.Type).HasColumnName("type").IsRequired();
        message.Property(m => m.Payload).HasColumnName("payload").IsRequired();
        message.Property(m => m.OccurredAt).HasColumnName("occurred_at").HasConversion(Utc);
        message.Property(m => m.ProcessedAt).HasColumnName("processed_at").HasConversion(NullableUtc);

        message.HasIndex(m => m.OccurredAt)
            .HasDatabaseName("ix_outbox_message_pending")
            .HasFilter("processed_at IS NULL");
    }
}
