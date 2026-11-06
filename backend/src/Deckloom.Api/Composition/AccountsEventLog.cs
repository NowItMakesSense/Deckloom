using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Infrastructure;

namespace Deckloom.Api.Composition;

// Adapter da porta IAccountsEventLog sobre o catalogo AccountsLog (Infrastructure); ponto unico da API que o toca
// para os eventos dos casos de uso (regra 5 de arquitetura). Niveis: fluxo normal Information, recusas Warning.
public sealed class AccountsEventLog(ILogger<AccountsEventLog> logger) : IAccountsEventLog
{
    public void PlayerRegistered(PlayerId playerId, string method) =>
        AccountsLog.PlayerRegistered(logger, LogLevel.Information, playerId.Value, method);

    public void LoginSucceeded(PlayerId playerId, SessionId sessionId, bool rememberMe) =>
        AccountsLog.LoginSucceeded(logger, LogLevel.Information, playerId.Value, sessionId.Value, rememberMe);

    public void LoginFailed(string reason, PlayerId? playerId) =>
        AccountsLog.LoginFailed(logger, LogLevel.Warning, reason, playerId?.Value);

    public void LoggedOut(PlayerId playerId, SessionId sessionId) =>
        AccountsLog.LoggedOut(logger, LogLevel.Information, playerId.Value, sessionId.Value);

    public void PasswordResetRequested(string outcome, PlayerId? playerId) =>
        AccountsLog.PasswordResetRequested(logger, LogLevel.Information, outcome, playerId?.Value);

    public void PasswordResetCompleted(PlayerId playerId, int revokedSessions) =>
        AccountsLog.PasswordResetCompleted(logger, LogLevel.Information, playerId.Value, revokedSessions);

    public void PasswordResetRejected(string reason) =>
        AccountsLog.PasswordResetRejected(logger, LogLevel.Warning, reason);

    // Falha de enfileirar, sem causa SMTP: a causa fica "other" (a causa SMTP vem do SmtpPasswordResetSender).
    public void PasswordResetEmailFailed(PlayerId playerId, int attempt) =>
        AccountsLog.PasswordResetEmailFailed(logger, LogLevel.Error, playerId.Value, attempt, "other");

    public void GoogleAuthCompleted(string outcome, PlayerId? playerId) =>
        AccountsLog.GoogleAuthCompleted(logger, LogLevel.Information, outcome, playerId?.Value);

    public void GoogleAuthFailed(string stage, string reason) =>
        AccountsLog.GoogleAuthFailed(logger, LogLevel.Warning, stage, reason);

    public void GoogleLinked(PlayerId playerId) =>
        AccountsLog.GoogleLinked(logger, LogLevel.Information, playerId.Value);

    public void GoogleLinkRejected(PlayerId playerId, string reason) =>
        AccountsLog.GoogleLinkRejected(logger, LogLevel.Warning, playerId.Value, reason);

    public void ProfileUpdated(PlayerId playerId, IReadOnlyList<string> changedFields)
    {
        var fields = string.Join(',', changedFields);
        AccountsLog.ProfileUpdated(logger, LogLevel.Information, playerId.Value, fields);
    }

    public void AvatarChanged(PlayerId playerId, string kind) =>
        AccountsLog.AvatarChanged(logger, LogLevel.Information, playerId.Value, kind);

    public void AvatarRejected(PlayerId playerId, string reason, string? detail) =>
        AccountsLog.AvatarRejected(logger, LogLevel.Warning, playerId.Value, reason, detail);

    public void ProgressiveDelayApplied(string action, int delayMs, string scope) =>
        AccountsLog.ProgressiveDelayApplied(logger, LogLevel.Warning, action, delayMs, scope);

    public void AccessBudgetExceeded(string action) =>
        AccountsLog.AccessBudgetExceeded(logger, LogLevel.Warning, action);

    public void AccountDeletionRejected(PlayerId playerId, string reason) =>
        AccountsLog.AccountDeletionRejected(logger, LogLevel.Warning, playerId.Value, reason);

    public void SessionRevoked(PlayerId playerId, SessionId sessionId, string reason) =>
        AccountsLog.SessionRevoked(logger, LogLevel.Information, playerId.Value, sessionId.Value, reason);
}
