using Deckloom.Accounts.Infrastructure;
using Deckloom.Api.Account;

namespace Deckloom.Api.Composition;

// Unico ponto da API que toca o catalogo de logs da Infrastructure (regra 5 de arquitetura).
public sealed class AccountsAuditLog(ILogger<AccountsAuditLog> logger) : IAccountsAuditLog
{
    public void AccountDeleted(Guid playerId) =>
        AccountsLog.AccountDeleted(logger, LogLevel.Information, playerId);

    public void CsrfRejected(string path) =>
        AccountsLog.CsrfRejected(logger, LogLevel.Warning, path);
}
