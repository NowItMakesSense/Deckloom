namespace Deckloom.Accounts.Infrastructure;

// Linha de Database/tables/accounts.access_attempt.md. So os HMACs de IP e de email normalizado, nunca o valor em claro.
internal sealed class AccessAttemptRow
{
    public long Id { get; set; }

    public required string Action { get; set; }

    public required byte[] IpKeyHash { get; set; }

    public byte[]? AccountKeyHash { get; set; }

    public DateTimeOffset AttemptedAt { get; set; }
}
