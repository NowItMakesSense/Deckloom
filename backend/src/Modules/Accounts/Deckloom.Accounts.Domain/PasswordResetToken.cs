namespace Deckloom.Accounts.Domain;

public sealed class PasswordResetToken
{
    internal PasswordResetToken(TokenHash tokenHash, DateTimeOffset issuedAt, DateTimeOffset expiresAt)
    {
        TokenHash = tokenHash;
        IssuedAt = issuedAt;
        ExpiresAt = expiresAt;
    }

    public TokenHash TokenHash { get; }

    public DateTimeOffset IssuedAt { get; }

    public DateTimeOffset ExpiresAt { get; }
}
