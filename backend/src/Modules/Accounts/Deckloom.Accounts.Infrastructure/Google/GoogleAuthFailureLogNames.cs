using Deckloom.Accounts.Application;

namespace Deckloom.Accounts.Infrastructure;

// Valores snake_case do catalogo de eventos (design.md, evento 1010 GoogleAuthFailed).
internal static class GoogleAuthFailureLogNames
{
    public static string For(GoogleAuthFailureStage stage) => stage switch
    {
        GoogleAuthFailureStage.Authorize => "authorize",
        GoogleAuthFailureStage.Callback => "callback",
        GoogleAuthFailureStage.Token => "token",
        GoogleAuthFailureStage.IdToken => "id_token",
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null),
    };

    public static string For(GoogleAuthFailureReason reason) => reason switch
    {
        GoogleAuthFailureReason.Cancelled => "cancelled",
        GoogleAuthFailureReason.StateMismatch => "state_mismatch",
        GoogleAuthFailureReason.UnverifiedEmail => "unverified_email",
        GoogleAuthFailureReason.ProviderError => "provider_error",
        GoogleAuthFailureReason.Timeout => "timeout",
        GoogleAuthFailureReason.SubjectMismatch => "subject_mismatch",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };
}
