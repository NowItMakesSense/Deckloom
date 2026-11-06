using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Domain.Tests;

// T-022 (REQ-032, REQ-041): ProgressiveDelayPolicy, funcao pura. Tabela da ADR-0005 (item 3).
// Design (emenda 2026-10-23, "Forma da politica"):
//   atraso de uma chave = 0 abaixo do limiar; senao BaseDelay x 2^(contagem - limiar), limitado a MaxDelay.
//   DelayFor = maximo entre conta e IP; Registration ignora a conta; contagem negativa lanca
//   ArgumentOutOfRangeException; opcoes invalidas lancam ArgumentException; nao existe resultado "bloqueado".
public class ProgressiveDelayPolicyTests
{
    private static readonly string[] ExpectedActionNames =
        ["AccountDeletion", "GoogleLink", "Login", "PasswordResetRequest", "Registration"];

    private static ProgressiveDelayPolicy ADefaultPolicy() => new(new ProgressiveDelayOptions());

    private static TimeSpan Seconds(double value) => TimeSpan.FromSeconds(value);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(3, 1)]
    [InlineData(4, 2)]
    [InlineData(5, 4)]
    [InlineData(6, 8)]
    [InlineData(7, 10)]
    [InlineData(8, 10)]
    public void REQ_032_account_count_follows_adr_table_with_default_options(int accountCount, double expectedSeconds)
    {
        var delay = ADefaultPolicy().DelayFor(accountCount, 0, AccessAction.Login);

        Assert.Equal(Seconds(expectedSeconds), delay);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(5, 0)]
    [InlineData(9, 0)]
    [InlineData(10, 1)]
    [InlineData(11, 2)]
    [InlineData(12, 4)]
    [InlineData(13, 8)]
    [InlineData(14, 10)]
    [InlineData(15, 10)]
    public void REQ_032_ip_count_follows_adr_table_with_default_options(int ipCount, double expectedSeconds)
    {
        var delay = ADefaultPolicy().DelayFor(0, ipCount, AccessAction.Login);

        Assert.Equal(Seconds(expectedSeconds), delay);
    }

    [Theory]
    [InlineData(3, 12, 4)]
    [InlineData(6, 10, 8)]
    [InlineData(5, 11, 4)]
    [InlineData(4, 4, 2)]
    [InlineData(2, 9, 0)]
    [InlineData(7, 14, 10)]
    [InlineData(0, 13, 8)]
    [InlineData(6, 0, 8)]
    public void REQ_032_delay_is_the_maximum_of_the_account_and_ip_delays(int accountCount, int ipCount, double expectedSeconds)
    {
        var delay = ADefaultPolicy().DelayFor(accountCount, ipCount, AccessAction.Login);

        Assert.Equal(Seconds(expectedSeconds), delay);
    }

    [Theory]
    [InlineData(100, 0)]
    [InlineData(0, 100)]
    [InlineData(1000, 1000)]
    [InlineData(int.MaxValue, 0)]
    [InlineData(0, int.MaxValue)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void REQ_032_delay_is_capped_at_ten_seconds_for_huge_counts_without_overflow(int accountCount, int ipCount)
    {
        var delay = ADefaultPolicy().DelayFor(accountCount, ipCount, AccessAction.Login);

        Assert.Equal(Seconds(10), delay);
    }

    [Theory]
    [InlineData(AccessAction.Login)]
    [InlineData(AccessAction.PasswordResetRequest)]
    [InlineData(AccessAction.GoogleLink)]
    [InlineData(AccessAction.AccountDeletion)]
    public void REQ_032_non_registration_actions_use_both_account_and_ip_keys(AccessAction action)
    {
        var policy = ADefaultPolicy();

        Assert.Equal(Seconds(8), policy.DelayFor(6, 0, action));
        Assert.Equal(Seconds(4), policy.DelayFor(0, 12, action));
        Assert.Equal(Seconds(8), policy.DelayFor(6, 12, action));
        Assert.Equal(TimeSpan.Zero, policy.DelayFor(2, 9, action));
    }

    [Fact]
    public void REQ_041_registration_ignores_the_account_count()
    {
        var delay = ADefaultPolicy().DelayFor(100, 0, AccessAction.Registration);

        Assert.Equal(TimeSpan.Zero, delay);
    }

    [Fact]
    public void REQ_041_registration_ignores_even_a_maxed_out_account_count()
    {
        var delay = ADefaultPolicy().DelayFor(int.MaxValue, 0, AccessAction.Registration);

        Assert.Equal(TimeSpan.Zero, delay);
    }

    [Theory]
    [InlineData(9, 0)]
    [InlineData(10, 1)]
    [InlineData(11, 2)]
    [InlineData(12, 4)]
    [InlineData(13, 8)]
    [InlineData(14, 10)]
    [InlineData(int.MaxValue, 10)]
    public void REQ_041_registration_applies_the_ip_delay_regardless_of_the_account_count(int ipCount, double expectedSeconds)
    {
        var policy = ADefaultPolicy();

        Assert.Equal(Seconds(expectedSeconds), policy.DelayFor(0, ipCount, AccessAction.Registration));
        Assert.Equal(Seconds(expectedSeconds), policy.DelayFor(100, ipCount, AccessAction.Registration));
    }

    [Fact]
    public void REQ_032_delay_never_blocks_and_always_returns_a_time_span()
    {
        var method = typeof(ProgressiveDelayPolicy).GetMethod(nameof(ProgressiveDelayPolicy.DelayFor));

        Assert.NotNull(method);
        Assert.Equal(typeof(TimeSpan), method.ReturnType);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(-5, -5)]
    [InlineData(int.MinValue, 0)]
    [InlineData(0, int.MinValue)]
    public void REQ_032_negative_counts_throw_argument_out_of_range(int accountCount, int ipCount)
    {
        var policy = ADefaultPolicy();

        Assert.Throws<ArgumentOutOfRangeException>(() => policy.DelayFor(accountCount, ipCount, AccessAction.Login));
    }

    [Fact]
    public void REQ_041_negative_ip_count_throws_for_registration()
    {
        var policy = ADefaultPolicy();

        Assert.Throws<ArgumentOutOfRangeException>(() => policy.DelayFor(0, -1, AccessAction.Registration));
    }

    [Fact]
    public void REQ_032_null_options_throw_argument_exception()
    {
        Assert.ThrowsAny<ArgumentException>(() => new ProgressiveDelayPolicy(null!));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(3, 0)]
    [InlineData(3, -1)]
    public void REQ_032_thresholds_below_one_throw_argument_exception(int accountThreshold, int ipThreshold)
    {
        var options = new ProgressiveDelayOptions { AccountThreshold = accountThreshold, IpThreshold = ipThreshold };

        Assert.ThrowsAny<ArgumentException>(() => new ProgressiveDelayPolicy(options));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void REQ_032_base_delay_zero_or_negative_throws_argument_exception(double baseSeconds)
    {
        var options = new ProgressiveDelayOptions { BaseDelay = Seconds(baseSeconds) };

        Assert.ThrowsAny<ArgumentException>(() => new ProgressiveDelayPolicy(options));
    }

    [Fact]
    public void SEC01_account_hourly_budget_defaults_to_thirty_and_is_exposed_by_the_policy()
    {
        Assert.Equal(30, new ProgressiveDelayOptions().AccountHourlyBudget);
        Assert.Equal(30, ADefaultPolicy().AccountHourlyBudget);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(100)]
    public void SEC01_account_hourly_budget_inside_one_to_one_hundred_is_accepted(int budget)
    {
        var policy = new ProgressiveDelayPolicy(new ProgressiveDelayOptions { AccountHourlyBudget = budget });

        Assert.Equal(budget, policy.AccountHourlyBudget);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public void SEC01_account_hourly_budget_below_one_or_above_one_hundred_throws_argument_exception(int budget)
    {
        var options = new ProgressiveDelayOptions { AccountHourlyBudget = budget };

        Assert.ThrowsAny<ArgumentException>(() => new ProgressiveDelayPolicy(options));
    }

    [Fact]
    public void REQ_032_max_delay_below_base_delay_throws_argument_exception()
    {
        var options = new ProgressiveDelayOptions { BaseDelay = Seconds(2), MaxDelay = Seconds(1) };

        Assert.ThrowsAny<ArgumentException>(() => new ProgressiveDelayPolicy(options));
    }

    [Fact]
    public void REQ_032_max_delay_equal_to_base_delay_is_valid_and_flat()
    {
        var policy = new ProgressiveDelayPolicy(new ProgressiveDelayOptions { BaseDelay = Seconds(2), MaxDelay = Seconds(2) });

        Assert.Equal(Seconds(2), policy.DelayFor(3, 0, AccessAction.Login));
        Assert.Equal(Seconds(2), policy.DelayFor(9, 0, AccessAction.Login));
    }

    [Fact]
    public void REQ_032_minimum_valid_thresholds_of_one_are_accepted()
    {
        var policy = new ProgressiveDelayPolicy(new ProgressiveDelayOptions { AccountThreshold = 1, IpThreshold = 1 });

        Assert.Equal(TimeSpan.Zero, policy.DelayFor(0, 0, AccessAction.Login));
        Assert.Equal(Seconds(1), policy.DelayFor(1, 0, AccessAction.Login));
        Assert.Equal(Seconds(2), policy.DelayFor(0, 2, AccessAction.Login));
    }

    [Fact]
    public void REQ_032_custom_account_threshold_moves_the_account_steps_only()
    {
        var policy = new ProgressiveDelayPolicy(new ProgressiveDelayOptions { AccountThreshold = 5 });

        Assert.Equal(TimeSpan.Zero, policy.DelayFor(4, 0, AccessAction.Login));
        Assert.Equal(Seconds(1), policy.DelayFor(5, 0, AccessAction.Login));
        Assert.Equal(Seconds(2), policy.DelayFor(6, 0, AccessAction.Login));
        Assert.Equal(Seconds(10), policy.DelayFor(9, 0, AccessAction.Login));
        Assert.Equal(TimeSpan.Zero, policy.DelayFor(0, 9, AccessAction.Login));
        Assert.Equal(Seconds(1), policy.DelayFor(0, 10, AccessAction.Login));
    }

    [Fact]
    public void REQ_032_custom_ip_threshold_moves_the_ip_steps_only()
    {
        var policy = new ProgressiveDelayPolicy(new ProgressiveDelayOptions { IpThreshold = 5 });

        Assert.Equal(TimeSpan.Zero, policy.DelayFor(0, 4, AccessAction.Login));
        Assert.Equal(Seconds(1), policy.DelayFor(0, 5, AccessAction.Login));
        Assert.Equal(Seconds(4), policy.DelayFor(0, 7, AccessAction.Login));
        Assert.Equal(Seconds(1), policy.DelayFor(3, 0, AccessAction.Login));
    }

    [Fact]
    public void REQ_041_custom_ip_threshold_applies_to_registration()
    {
        var policy = new ProgressiveDelayPolicy(new ProgressiveDelayOptions { IpThreshold = 5 });

        Assert.Equal(TimeSpan.Zero, policy.DelayFor(100, 4, AccessAction.Registration));
        Assert.Equal(Seconds(1), policy.DelayFor(100, 5, AccessAction.Registration));
    }

    [Fact]
    public void REQ_032_custom_max_delay_of_three_seconds_caps_the_delay()
    {
        var policy = new ProgressiveDelayPolicy(new ProgressiveDelayOptions { MaxDelay = Seconds(3) });

        Assert.Equal(Seconds(1), policy.DelayFor(3, 0, AccessAction.Login));
        Assert.Equal(Seconds(2), policy.DelayFor(4, 0, AccessAction.Login));
        Assert.Equal(Seconds(3), policy.DelayFor(5, 0, AccessAction.Login));
        Assert.Equal(Seconds(3), policy.DelayFor(0, 14, AccessAction.Login));
        Assert.Equal(Seconds(3), policy.DelayFor(int.MaxValue, int.MaxValue, AccessAction.Login));
    }

    [Fact]
    public void REQ_032_custom_base_delay_scales_the_steps()
    {
        var policy = new ProgressiveDelayPolicy(new ProgressiveDelayOptions { BaseDelay = Seconds(2) });

        Assert.Equal(Seconds(2), policy.DelayFor(3, 0, AccessAction.Login));
        Assert.Equal(Seconds(4), policy.DelayFor(4, 0, AccessAction.Login));
        Assert.Equal(Seconds(8), policy.DelayFor(5, 0, AccessAction.Login));
        Assert.Equal(Seconds(10), policy.DelayFor(6, 0, AccessAction.Login));
    }

    [Fact]
    public void REQ_032_access_action_has_exactly_the_five_adr_values()
    {
        var names = Enum.GetNames<AccessAction>().OrderBy(name => name).ToArray();

        Assert.Equal(ExpectedActionNames, names);
    }

    [Theory]
    [InlineData(AccessAction.Login)]
    [InlineData(AccessAction.PasswordResetRequest)]
    [InlineData(AccessAction.GoogleLink)]
    [InlineData(AccessAction.AccountDeletion)]
    public void REQ_032_delay_is_monotonic_and_capped_for_counts_zero_to_one_thousand(AccessAction action)
    {
        var policy = ADefaultPolicy();
        var cap = Seconds(10);

        for (var account = 0; account <= 1000; account++)
        {
            for (var ip = 0; ip <= 1000; ip++)
            {
                var current = policy.DelayFor(account, ip, action);

                Assert.InRange(current, TimeSpan.Zero, cap);
                Assert.True(
                    policy.DelayFor(account + 1, ip, action) >= current,
                    $"delay decreased when account count went {account} -> {account + 1} (ip {ip})");
                Assert.True(
                    policy.DelayFor(account, ip + 1, action) >= current,
                    $"delay decreased when ip count went {ip} -> {ip + 1} (account {account})");
            }
        }
    }

    [Fact]
    public void REQ_041_registration_delay_is_monotonic_in_ip_and_flat_in_account_for_counts_zero_to_one_thousand()
    {
        var policy = ADefaultPolicy();

        for (var ip = 0; ip <= 1000; ip++)
        {
            var current = policy.DelayFor(0, ip, AccessAction.Registration);

            Assert.InRange(current, TimeSpan.Zero, Seconds(10));
            Assert.True(policy.DelayFor(0, ip + 1, AccessAction.Registration) >= current);
            Assert.Equal(current, policy.DelayFor(ip, ip, AccessAction.Registration));
        }
    }
}
