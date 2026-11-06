using System.Data.Common;
using System.Diagnostics;

namespace Deckloom.Accounts.Application;

// T-139 (ADR-0015, DB9): reconhece falhas de comando de banco por SQLSTATE conhecido. Os demais SQLSTATEs seguem o
// tratamento generico de excecao nao tratada. O SqlState vem de DbException.SqlState (PostgresException deriva dela).
public static class DatabaseFailureClassifier
{
    // 42501 permissao faltando, 57014 timeout/cancelamento, 55P03 lock indisponivel, 25P03 idle-in-transaction,
    // 53300 conexoes demais.
    private static readonly HashSet<string> KnownSqlStates = new(StringComparer.Ordinal)
    {
        "42501", "57014", "55P03", "25P03", "53300",
    };

    private const string UseCaseNamespace = "Deckloom.Accounts.Application";

    public static bool TryGetKnownSqlState(Exception exception, out string sqlState)
    {
        ArgumentNullException.ThrowIfNull(exception);
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbException { SqlState: { } state } && KnownSqlStates.Contains(state))
            {
                sqlState = state;
                return true;
            }
        }

        sqlState = string.Empty;
        return false;
    }

    // Nome do caso de uso: a classe mais externa do namespace de Application na pilha da excecao (a pilha lista o
    // quadro mais interno primeiro).
    public static string OperationOf(Exception exception, string fallback = "Unknown")
    {
        ArgumentNullException.ThrowIfNull(exception);
        string? name = null;
        foreach (var frame in new StackTrace(exception, false).GetFrames())
        {
            var type = frame.GetMethod()?.DeclaringType;
            while (type?.DeclaringType is not null)
            {
                type = type.DeclaringType;
            }

            if (type?.Namespace == UseCaseNamespace)
            {
                name = type.Name;
            }
        }

        return name ?? fallback;
    }
}
