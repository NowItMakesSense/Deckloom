using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Application;

// Motivos do evento 1010 (GoogleAuthFailed) emitidos pela Application. Os valores sao os do catalogo da Infrastructure
// (GoogleAuthFailureLogNames); a equivalencia e garantida por teste, pois a Application nao referencia a Infrastructure.
internal static class GoogleAuthFailureReasons
{
    public const string SessionInvalid = Session.SessionInvalidCode;
    public const string SubjectMismatch = Session.SubjectMismatchCode;
    public const string ProviderError = "provider_error";
    public const string UnverifiedEmail = "unverified_email";
    public const string Cancelled = "cancelled";
    public const string StateMismatch = "state_mismatch";
}
