using Deckloom.Accounts.Infrastructure;
using Deckloom.Api.Problems;

namespace Deckloom.Api.Composition;

// Ponto da API que toca o catalogo de logs da Infrastructure para o evento 1021 (regra 5 de arquitetura).
public sealed class DatabaseFailureLog(ILogger<DatabaseFailureLog> logger) : IDatabaseFailureLog
{
    public void CommandFailed(string sqlState, string operation) =>
        AccountsLog.DatabaseCommandFailed(logger, LogLevel.Error, sqlState, operation);
}
