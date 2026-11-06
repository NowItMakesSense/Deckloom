namespace Deckloom.Accounts.Infrastructure;

public sealed class GoogleOidcOptions
{
    public required Uri Authority { get; init; }

    public required string ClientId { get; init; }

    public required string ClientSecret { get; init; }

    public required string PublicOrigin { get; init; }
}
