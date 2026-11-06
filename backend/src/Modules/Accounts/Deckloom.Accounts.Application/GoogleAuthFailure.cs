namespace Deckloom.Accounts.Application;

public enum GoogleAuthFailureStage
{
    Authorize,
    Callback,
    Token,
    IdToken,
}

public enum GoogleAuthFailureReason
{
    Cancelled,
    StateMismatch,
    UnverifiedEmail,
    ProviderError,
    Timeout,
    SubjectMismatch,
}

public sealed record GoogleAuthFailed(GoogleAuthFailureStage Stage, GoogleAuthFailureReason Reason);

public sealed record GoogleOidcOutcome(GoogleVerifiedIdentity? Identity, GoogleAuthFailed? Failure);
