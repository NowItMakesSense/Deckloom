using System.Reflection;

namespace Deckloom.Accounts.Infrastructure.Tests;

// ADR-0015 / T-P04 (APR-025): a consulta do inspetor de papel cobre todos os schemas do sistema, inclusive catalog.
// A consulta real so roda no PostgreSQL (verificada por humano); aqui se confere o texto da consulta, sem conexao.
public class NpgsqlDatabaseRoleInspectorQueryTests
{
    private static string Query() =>
        (string)typeof(NpgsqlDatabaseRoleInspector)
            .GetField("Query", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetRawConstantValue()!;

    [Theory]
    [InlineData("accounts")]
    [InlineData("deck_suggestion")]
    [InlineData("catalog")]
    public void DB7_query_checks_create_privilege_in_each_system_schema(string schema)
    {
        Assert.Contains($"has_schema_privilege(r.rolname, '{schema}', 'CREATE')", Query());
    }

    [Fact]
    public void DB7_query_checks_ownership_in_every_system_schema()
    {
        Assert.Equal(2, Query().Split("IN ('accounts', 'deck_suggestion', 'catalog')").Length - 1);
    }
}
