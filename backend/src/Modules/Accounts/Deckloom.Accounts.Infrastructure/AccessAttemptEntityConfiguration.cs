using Deckloom.Accounts.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Deckloom.Accounts.Infrastructure;

// Mapeamento de accounts.access_attempt. Sem FK para player (anti-enumeracao), sem default nem
// funcao de data do banco: o "agora" chega sempre como parametro. Datas gravadas como DateTime UTC (timestamptz),
// o que permite ao EF traduzir as comparacoes de janela tambem no SQLite dos testes.
internal sealed class AccessAttemptEntityConfiguration : IEntityTypeConfiguration<AccessAttemptRow>
{
    public const string TableName = "access_attempt";

    private static readonly ValueConverter<DateTimeOffset, DateTime> Utc =
        new(value => value.UtcDateTime, value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));

    public static string ToColumnValue(AccessAction action) => action switch
    {
        AccessAction.Login => "login",
        AccessAction.PasswordResetRequest => "password_reset_request",
        AccessAction.GoogleLink => "google_link",
        AccessAction.AccountDeletion => "account_deletion",
        AccessAction.Registration => "registration",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown access action."),
    };

    public void Configure(EntityTypeBuilder<AccessAttemptRow> attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        var allowedActions = string.Join(", ", Enum.GetValues<AccessAction>().Select(a => $"'{ToColumnValue(a)}'"));
        attempt.ToTable(TableName, table => table.HasCheckConstraint("ck_access_attempt_action", $"action IN ({allowedActions})"));
        attempt.HasKey(a => a.Id).HasName("pk_access_attempt");
        attempt.Property(a => a.Id).HasColumnName("id").ValueGeneratedOnAdd();
        attempt.Property(a => a.Action).HasColumnName("action").IsRequired();
        attempt.Property(a => a.IpKeyHash).HasColumnName("ip_key_hash").IsRequired();
        attempt.Property(a => a.AccountKeyHash).HasColumnName("account_key_hash");
        attempt.Property(a => a.AttemptedAt).HasColumnName("attempted_at").HasConversion(Utc);

        attempt.HasIndex(a => new { a.IpKeyHash, a.AttemptedAt }).HasDatabaseName("ix_access_attempt_ip_key");
        attempt.HasIndex(a => new { a.AccountKeyHash, a.AttemptedAt }).HasDatabaseName("ix_access_attempt_account_key");
        attempt.HasIndex(a => a.AttemptedAt).HasDatabaseName("ix_access_attempt_attempted_at");
    }
}
