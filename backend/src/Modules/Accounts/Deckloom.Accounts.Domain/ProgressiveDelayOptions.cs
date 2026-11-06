namespace Deckloom.Accounts.Domain;

public sealed record ProgressiveDelayOptions
{
    public int AccountThreshold { get; init; } = 3;

    public int IpThreshold { get; init; } = 10;

    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(10);

    // Orcamento de tentativas contadas por chave de conta em 60 min (ADR-0005, emenda SEC-01, E4).
    public int AccountHourlyBudget { get; init; } = 30;
}
