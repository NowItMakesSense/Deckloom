using System.Diagnostics.Metrics;
using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Application;

// T-092: metricas OpenTelemetry (System.Diagnostics.Metrics) dos fluxos de conta. Os atributos sao sempre
// codigos fechados em snake_case: nunca email, IP, token ou id de conta.
public static class AccountsMetrics
{
    public const string MeterName = "Deckloom";

    public const string LoginName = "deckloom.auth.login";
    public const string GoogleName = "deckloom.auth.google";
    public const string ProgressiveDelayName = "deckloom.auth.progressive_delay";
    public const string PasswordResetName = "deckloom.auth.password_reset";
    public const string ResetEmailFailuresName = "deckloom.auth.reset_email_failures";
    public const string ProcessingDurationName = "deckloom.auth.processing_duration";
    public const string RegistrationsName = "deckloom.accounts.registrations";
    public const string DeletionsName = "deckloom.accounts.deletions";
    public const string AvatarRejectionsName = "deckloom.avatar.rejections";

    public const string DbCommandFailuresName = "deckloom.db.command_failures";

    public const string SqlStateTag = "sqlstate";
    public const string OutcomeTag = "outcome";
    public const string BudgetExceededOutcome = "budget_exceeded";
    public const string StageTag = "stage";
    public const string ActionTag = "action";
    public const string MethodTag = "method";
    public const string ReasonTag = "reason";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> Logins = Meter.CreateCounter<long>(LoginName);
    private static readonly Counter<long> GoogleAttempts = Meter.CreateCounter<long>(GoogleName);
    private static readonly Histogram<double> ProgressiveDelays =
        Meter.CreateHistogram<double>(ProgressiveDelayName, "ms");
    private static readonly Counter<long> PasswordResets = Meter.CreateCounter<long>(PasswordResetName);
    private static readonly Counter<long> ResetEmailFailures = Meter.CreateCounter<long>(ResetEmailFailuresName);
    private static readonly Histogram<double> ProcessingDurations =
        Meter.CreateHistogram<double>(ProcessingDurationName, "ms");
    private static readonly Counter<long> Registrations = Meter.CreateCounter<long>(RegistrationsName);
    private static readonly Counter<long> Deletions = Meter.CreateCounter<long>(DeletionsName);
    private static readonly Counter<long> AvatarRejections = Meter.CreateCounter<long>(AvatarRejectionsName);
    private static readonly Counter<long> DbCommandFailures = Meter.CreateCounter<long>(DbCommandFailuresName);

    public static void DatabaseCommandFailed(string sqlState) =>
        DbCommandFailures.Add(1, new KeyValuePair<string, object?>(SqlStateTag, sqlState));

    public static void LoginAttempted(bool succeeded) =>
        Logins.Add(1, new KeyValuePair<string, object?>(OutcomeTag, succeeded ? "success" : "failure"));

    public static void LoginBudgetExceeded() =>
        Logins.Add(1, new KeyValuePair<string, object?>(OutcomeTag, BudgetExceededOutcome));

    public static void GoogleBudgetExceeded() =>
        GoogleAttempts.Add(1, new KeyValuePair<string, object?>(OutcomeTag, BudgetExceededOutcome));

    public static void PasswordResetBudgetExceeded() => PasswordResetOutcome(BudgetExceededOutcome);

    public static void GoogleSucceeded(string outcome) =>
        GoogleAttempts.Add(1, new KeyValuePair<string, object?>(OutcomeTag, outcome));

    public static void GoogleFailed(string stage) =>
        GoogleAttempts.Add(
            1,
            new KeyValuePair<string, object?>(OutcomeTag, "failed"),
            new KeyValuePair<string, object?>(StageTag, stage));

    public static void ProgressiveDelayApplied(AccessAction action, TimeSpan delay) =>
        ProgressiveDelays.Record(
            delay.TotalMilliseconds, new KeyValuePair<string, object?>(ActionTag, ActionCode(action)));

    public static void PasswordResetOutcome(string outcome) =>
        PasswordResets.Add(1, new KeyValuePair<string, object?>(OutcomeTag, outcome));

    public static void ResetEmailFailed() => ResetEmailFailures.Add(1);

    public static void LoginProcessed(TimeSpan elapsed) => ProcessingDurations.Record(elapsed.TotalMilliseconds);

    public static void PlayerRegistered(string method) =>
        Registrations.Add(1, new KeyValuePair<string, object?>(MethodTag, method));

    public static void AccountDeleted() => Deletions.Add(1);

    public static void AvatarRejected(string reason) =>
        AvatarRejections.Add(1, new KeyValuePair<string, object?>(ReasonTag, reason));

    internal static string ActionCode(AccessAction action) => action switch
    {
        AccessAction.Login => "login",
        AccessAction.PasswordResetRequest => "password_reset_request",
        AccessAction.GoogleLink => "google_link",
        AccessAction.AccountDeletion => "account_deletion",
        AccessAction.Registration => "registration",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };
}
