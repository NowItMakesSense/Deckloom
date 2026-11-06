namespace Deckloom.Accounts.Infrastructure.Tests;

// T-133 (UA1). O detector de escrita precisa enxergar as colunas do SET de um UPDATE (anti-vacuo do interceptor
// reutilizado por CleanupJobBatchingTests).
public sealed class UpdateStatementCaptureTests
{
    [Theory]
    [InlineData("UPDATE \"player\" SET \"email\" = @p0, \"display_name\" = @p1 WHERE \"id\" = @p2", "player", "email,display_name")]
    [InlineData("UPDATE \"accounts\".\"player_session\" SET \"revoked_at\" = @p0 WHERE \"id\" = @p1 RETURNING 1", "player_session", "revoked_at")]
    public void UA1_capture_parses_the_set_columns_of_an_update(string sql, string table, string columns)
    {
        var capture = new UpdateStatementCapture();
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        capture.NonQueryExecuting(command, null!, default);

        var write = Assert.Single(capture.Writes);
        Assert.Equal(WriteKind.Update, write.Kind);
        Assert.Equal(table, write.Table);
        Assert.Equal(columns.Split(','), write.SetColumns);
    }
}
