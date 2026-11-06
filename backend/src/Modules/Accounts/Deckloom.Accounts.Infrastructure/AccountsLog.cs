using Microsoft.Extensions.Logging;

namespace Deckloom.Accounts.Infrastructure;

/// <summary>Definicao de um evento do catalogo de logs (design.md, catalogo 1001-1022).</summary>
public sealed record AccountsLogEvent(int Id, string Name, IReadOnlyList<string> Fields);

/// <summary>
/// Ponto unico de acesso ao catalogo de eventos de log (T-079). Cada evento tem um metodo tipado
/// (LoggerMessage) que so aceita os campos permitidos, sem PII. O nivel e escolhido por quem loga.
/// </summary>
public static partial class AccountsLog
{
    public static IReadOnlyList<AccountsLogEvent> Catalog { get; } =
    [
        new(1001, "PlayerRegistered", ["playerId", "method"]),
        new(1002, "LoginSucceeded", ["playerId", "sessionId", "rememberMe"]),
        new(1003, "LoginFailed", ["reason", "playerId"]),
        new(1004, "LoggedOut", ["playerId", "sessionId"]),
        new(1005, "PasswordResetRequested", ["outcome", "playerId"]),
        new(1006, "PasswordResetCompleted", ["playerId", "revokedSessions"]),
        new(1007, "PasswordResetRejected", ["reason"]),
        new(1008, "PasswordResetEmailFailed", ["playerId", "attempt", "cause"]),
        new(1009, "GoogleAuthCompleted", ["outcome", "playerId"]),
        new(1010, "GoogleAuthFailed", ["stage", "reason"]),
        new(1011, "GoogleLinked", ["playerId"]),
        new(1012, "GoogleLinkRejected", ["playerId", "reason"]),
        new(1013, "ProfileUpdated", ["playerId", "changedFields"]),
        new(1014, "AvatarChanged", ["playerId", "kind"]),
        new(1015, "AvatarRejected", ["playerId", "reason", "detail"]),
        new(1016, "ProgressiveDelayApplied", ["action", "delayMs", "scope"]),
        new(1017, "AccountDeleted", ["playerId"]),
        new(1018, "AccountDeletionRejected", ["playerId", "reason"]),
        new(1019, "SessionRevoked", ["playerId", "sessionId", "reason"]),
        new(1020, "CsrfRejected", ["path"]),
        new(1021, "DatabaseCommandFailed", ["sqlState", "operation"]),
        new(1022, "AccessBudgetExceeded", ["action"]),
    ];

    [LoggerMessage(EventId = 1001, EventName = "PlayerRegistered", Message = "Player registered {playerId} via {method}")]
    public static partial void PlayerRegistered(ILogger logger, LogLevel level, Guid playerId, string method);

    [LoggerMessage(EventId = 1002, EventName = "LoginSucceeded", Message = "Login succeeded for player {playerId} session {sessionId} rememberMe {rememberMe}")]
    public static partial void LoginSucceeded(ILogger logger, LogLevel level, Guid playerId, Guid sessionId, bool rememberMe);

    [LoggerMessage(EventId = 1003, EventName = "LoginFailed", Message = "Login failed: {reason} (player {playerId})")]
    public static partial void LoginFailed(ILogger logger, LogLevel level, string reason, Guid? playerId);

    [LoggerMessage(EventId = 1004, EventName = "LoggedOut", Message = "Player {playerId} logged out session {sessionId}")]
    public static partial void LoggedOut(ILogger logger, LogLevel level, Guid playerId, Guid sessionId);

    [LoggerMessage(EventId = 1005, EventName = "PasswordResetRequested", Message = "Password reset requested: {outcome} (player {playerId})")]
    public static partial void PasswordResetRequested(ILogger logger, LogLevel level, string outcome, Guid? playerId);

    [LoggerMessage(EventId = 1006, EventName = "PasswordResetCompleted", Message = "Password reset completed for player {playerId}; revoked sessions {revokedSessions}")]
    public static partial void PasswordResetCompleted(ILogger logger, LogLevel level, Guid playerId, int revokedSessions);

    [LoggerMessage(EventId = 1007, EventName = "PasswordResetRejected", Message = "Password reset rejected: {reason}")]
    public static partial void PasswordResetRejected(ILogger logger, LogLevel level, string reason);

    [LoggerMessage(EventId = 1008, EventName = "PasswordResetEmailFailed", Message = "Password reset e-mail failed for player {playerId} after attempt {attempt}: {cause}")]
    public static partial void PasswordResetEmailFailed(ILogger logger, LogLevel level, Guid playerId, int attempt, string cause);

    [LoggerMessage(EventId = 1009, EventName = "GoogleAuthCompleted", Message = "Google auth completed: {outcome} (player {playerId})")]
    public static partial void GoogleAuthCompleted(ILogger logger, LogLevel level, string outcome, Guid? playerId);

    [LoggerMessage(EventId = 1010, EventName = "GoogleAuthFailed", Message = "Falha no login com Google: estagio {stage}, motivo {reason}.")]
    public static partial void GoogleAuthFailed(ILogger logger, LogLevel level, string stage, string reason);

    [LoggerMessage(EventId = 1011, EventName = "GoogleLinked", Message = "Google linked to player {playerId}")]
    public static partial void GoogleLinked(ILogger logger, LogLevel level, Guid playerId);

    [LoggerMessage(EventId = 1012, EventName = "GoogleLinkRejected", Message = "Google link rejected for player {playerId}: {reason}")]
    public static partial void GoogleLinkRejected(ILogger logger, LogLevel level, Guid playerId, string reason);

    [LoggerMessage(EventId = 1013, EventName = "ProfileUpdated", Message = "Profile updated for player {playerId}: {changedFields}")]
    public static partial void ProfileUpdated(ILogger logger, LogLevel level, Guid playerId, string changedFields);

    [LoggerMessage(EventId = 1014, EventName = "AvatarChanged", Message = "Avatar changed for player {playerId}: {kind}")]
    public static partial void AvatarChanged(ILogger logger, LogLevel level, Guid playerId, string kind);

    [LoggerMessage(EventId = 1015, EventName = "AvatarRejected", Message = "Avatar rejected for player {playerId}: {reason} ({detail})")]
    public static partial void AvatarRejected(ILogger logger, LogLevel level, Guid playerId, string reason, string? detail);

    [LoggerMessage(EventId = 1016, EventName = "ProgressiveDelayApplied", Message = "Progressive delay applied to {action}: {delayMs} ms ({scope})")]
    public static partial void ProgressiveDelayApplied(ILogger logger, LogLevel level, string action, int delayMs, string scope);

    [LoggerMessage(EventId = 1017, EventName = "AccountDeleted", Message = "Account deleted: {playerId}")]
    public static partial void AccountDeleted(ILogger logger, LogLevel level, Guid playerId);

    [LoggerMessage(EventId = 1018, EventName = "AccountDeletionRejected", Message = "Account deletion rejected for player {playerId}: {reason}")]
    public static partial void AccountDeletionRejected(ILogger logger, LogLevel level, Guid playerId, string reason);

    [LoggerMessage(EventId = 1019, EventName = "SessionRevoked", Message = "Session {sessionId} of player {playerId} revoked: {reason}")]
    public static partial void SessionRevoked(ILogger logger, LogLevel level, Guid playerId, Guid sessionId, string reason);

    [LoggerMessage(EventId = 1020, EventName = "CsrfRejected", Message = "CSRF rejected for {path}")]
    public static partial void CsrfRejected(ILogger logger, LogLevel level, string path);

    [LoggerMessage(EventId = 1021, EventName = "DatabaseCommandFailed", Message = "Database command failed: SQLSTATE {sqlState} in {operation}")]
    public static partial void DatabaseCommandFailed(ILogger logger, LogLevel level, string sqlState, string operation);

    [LoggerMessage(EventId = 1022, EventName = "AccessBudgetExceeded", Message = "Account attempt budget exceeded for {action}")]
    public static partial void AccessBudgetExceeded(ILogger logger, LogLevel level, string action);
}
