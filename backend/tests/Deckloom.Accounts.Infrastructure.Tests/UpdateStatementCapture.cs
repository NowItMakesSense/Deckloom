using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Deckloom.Accounts.Infrastructure.Tests;

// T-133 (UA1, ADR-0012, ADR-0015). Um comando de escrita capturado: tipo, tabela (sem schema) e, para UPDATE, as
// colunas do SET (o WHERE, onde fica o token de concorrencia, nao conta).
internal enum WriteKind
{
    Insert,
    Update,
    Delete,
}

internal sealed record CapturedWrite(WriteKind Kind, string Table, IReadOnlyList<string> SetColumns, string Sql);

// Interceptor que registra cada INSERT/UPDATE/DELETE executado (SaveChanges e ExecuteDelete/ExecuteUpdate).
// REFACTOR de T-133: mover para Deckloom.Accounts.Testing (reutilizado por T-138).
internal sealed partial class UpdateStatementCapture : DbCommandInterceptor
{
    private readonly List<CapturedWrite> _writes = [];

    public IReadOnlyList<CapturedWrite> Writes => _writes;

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Record(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Record(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Record(command);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Record(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Record(command);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        Record(command);
        return ValueTask.FromResult(result);
    }

    // Um comando do EF pode agrupar varias instrucoes separadas por ';'.
    private void Record(DbCommand command)
    {
        foreach (var statement in command.CommandText.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var update = UpdateRegex().Match(statement);
            if (update.Success)
            {
                var columns = ColumnRegex().Matches(update.Groups["set"].Value).Select(m => m.Groups[1].Value).ToList();
                _writes.Add(new CapturedWrite(WriteKind.Update, update.Groups["table"].Value, columns, statement));
                continue;
            }

            var insert = InsertRegex().Match(statement);
            if (insert.Success)
            {
                _writes.Add(new CapturedWrite(WriteKind.Insert, insert.Groups["table"].Value, [], statement));
                continue;
            }

            var delete = DeleteRegex().Match(statement);
            if (delete.Success)
            {
                _writes.Add(new CapturedWrite(WriteKind.Delete, delete.Groups["table"].Value, [], statement));
            }
        }
    }

    [GeneratedRegex("""^UPDATE\s+(?:"\w+"\.)?"(?<table>\w+)"\s+SET\s+(?<set>.*?)(?:\s+WHERE\b.*)?$""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex UpdateRegex();

    [GeneratedRegex("""^INSERT\s+INTO\s+(?:"\w+"\.)?"(?<table>\w+)"\s""", RegexOptions.IgnoreCase)]
    private static partial Regex InsertRegex();

    [GeneratedRegex("""^DELETE\s+FROM\s+(?:"\w+"\.)?"(?<table>\w+)"(?:\s|$)""", RegexOptions.IgnoreCase)]
    private static partial Regex DeleteRegex();

    [GeneratedRegex("""(?:^|,\s*)"(\w+)"\s*=""")]
    private static partial Regex ColumnRegex();
}
