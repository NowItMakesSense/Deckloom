namespace Deckloom.Accounts.Infrastructure;

public sealed class RateLimitHmacOptions
{
    public const string SectionName = "Auth";

    public string? RateLimitHmacKey { get; init; }

    public override string ToString() => nameof(RateLimitHmacOptions);
}
