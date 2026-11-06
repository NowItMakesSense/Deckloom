namespace Deckloom.Accounts.Application;

// PublicBaseUrl e preenchida pela composicao a partir de "Auth:PublicOrigin" (fonte unica); deve ser uma URL absoluta.
public sealed record PasswordResetOptions
{
    public string PublicBaseUrl { get; init; } = "";
}
