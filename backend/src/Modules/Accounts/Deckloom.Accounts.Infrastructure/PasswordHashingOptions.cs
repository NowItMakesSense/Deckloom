namespace Deckloom.Accounts.Infrastructure;

public sealed class PasswordHashingOptions
{
    public const string SectionName = "Auth:PasswordHashing";

    public int Iterations { get; init; } = 600_000;

    public int MaxConcurrency { get; init; } = Environment.ProcessorCount;
}
