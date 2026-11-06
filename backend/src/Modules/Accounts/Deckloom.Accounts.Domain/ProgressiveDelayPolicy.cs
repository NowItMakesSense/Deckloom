namespace Deckloom.Accounts.Domain;

public sealed class ProgressiveDelayPolicy
{
    // Limite da ASVS 2.2.1 (cerca de 100 falhas por hora por conta): o orcamento nunca pode passar disso.
    private const int MaxAccountHourlyBudget = 100;

    private readonly ProgressiveDelayOptions _options;

    public ProgressiveDelayPolicy(ProgressiveDelayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.AccountThreshold < 1)
        {
            throw new ArgumentException("AccountThreshold must be at least 1.", nameof(options));
        }

        if (options.IpThreshold < 1)
        {
            throw new ArgumentException("IpThreshold must be at least 1.", nameof(options));
        }

        if (options.BaseDelay <= TimeSpan.Zero)
        {
            throw new ArgumentException("BaseDelay must be greater than zero.", nameof(options));
        }

        if (options.MaxDelay < options.BaseDelay)
        {
            throw new ArgumentException("MaxDelay must not be less than BaseDelay.", nameof(options));
        }

        if (options.AccountHourlyBudget < 1 || options.AccountHourlyBudget > MaxAccountHourlyBudget)
        {
            throw new ArgumentException(
                $"AccountHourlyBudget must be between 1 and {MaxAccountHourlyBudget}.", nameof(options));
        }

        _options = options;
    }

    public int AccountHourlyBudget => _options.AccountHourlyBudget;

    public TimeSpan DelayFor(int accountCount, int ipCount, AccessAction action)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(accountCount);
        ArgumentOutOfRangeException.ThrowIfNegative(ipCount);

        var ipDelay = DelayForKey(ipCount, _options.IpThreshold);

        if (action == AccessAction.Registration)
        {
            return ipDelay;
        }

        var accountDelay = DelayForKey(accountCount, _options.AccountThreshold);

        return accountDelay > ipDelay ? accountDelay : ipDelay;
    }

    private TimeSpan DelayForKey(int count, int threshold)
    {
        if (count < threshold)
        {
            return TimeSpan.Zero;
        }

        var steps = count - threshold;
        var delay = _options.BaseDelay;

        // Doubles until the cap is reached; never computes 2^steps, so it cannot overflow.
        for (var step = 0; step < steps; step++)
        {
            if (delay >= _options.MaxDelay - delay)
            {
                return _options.MaxDelay;
            }

            delay += delay;
        }

        return delay < _options.MaxDelay ? delay : _options.MaxDelay;
    }
}
