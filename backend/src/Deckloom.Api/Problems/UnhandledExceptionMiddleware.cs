using Deckloom.Accounts.Application;

namespace Deckloom.Api.Problems;

public interface IDatabaseFailureLog
{
    void CommandFailed(string sqlState, string operation);
}

// Exceção não tratada vira 500 internal_error genérico; detalhes só no log (design: Tratamento de erros).
// T-139: falha de comando de banco com SQLSTATE conhecido vira o evento 1021 + métrica, sem stack nem mensagem do servidor.
public sealed partial class UnhandledExceptionMiddleware(RequestDelegate next, ILogger<UnhandledExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, IDatabaseFailureLog databaseFailureLog)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception) when (!context.Response.HasStarted)
        {
            if (DatabaseFailureClassifier.TryGetKnownSqlState(exception, out var sqlState))
            {
                AccountsMetrics.DatabaseCommandFailed(sqlState);
                databaseFailureLog.CommandFailed(sqlState, DatabaseFailureClassifier.OperationOf(exception));
            }
            else
            {
                LogUnhandled(logger, exception.GetType().Name);
            }

            context.Response.Clear();
            await DomainErrorProblemMapper.InternalErrorResult().ExecuteAsync(context);
        }
    }

    // So o nome do tipo: a mensagem e o stack trace de bibliotecas podem carregar dados pessoais ou SQL.
    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception while processing request: {exceptionType}")]
    private static partial void LogUnhandled(ILogger logger, string exceptionType);
}
