namespace Deckloom.Accounts.Infrastructure.Tests;

// T-134 (habilitadora; DB7/UA2; ADR-0015): decisao pura sobre o papel de conexao, sem conexao a banco.
// Contrato esperado (a ser criado pelo implementer):
//   record RoleSnapshot { RoleName, DatabaseName, IsSuperuser, CanCreateRole, CanCreateDatabase, BypassesRowLevelSecurity,
//                         CanCreateInDatabase, CanCreateInAccountsSchema, CanCreateInDeckSuggestionSchema, OwnsObjects }
//   DatabaseRolePolicy.Evaluate(RoleSnapshot) -> DatabaseRoleDecision { IsAccepted, Reasons }
//   Reasons: IReadOnlyCollection<DatabaseRoleRejection> com Code (string estavel) e Message.
public class DatabaseRolePolicyTests
{
    private const string FakeRoleName = "papel-teste";
    private const string FakeDatabaseName = "banco-teste";

    private static RoleSnapshot Compliant() => new()
    {
        RoleName = FakeRoleName,
        DatabaseName = FakeDatabaseName,
        IsSuperuser = false,
        CanCreateRole = false,
        CanCreateDatabase = false,
        BypassesRowLevelSecurity = false,
        CanCreateInDatabase = false,
        CanCreateInAccountsSchema = false,
        CanCreateInDeckSuggestionSchema = false,
        OwnsObjects = false,
    };

    public static TheoryData<string, RoleSnapshot> SingleViolations => new()
    {
        { "superuser", Compliant() with { IsSuperuser = true } },
        { "createrole", Compliant() with { CanCreateRole = true } },
        { "createdb", Compliant() with { CanCreateDatabase = true } },
        { "bypassrls", Compliant() with { BypassesRowLevelSecurity = true } },
        { "create_database", Compliant() with { CanCreateInDatabase = true } },
        { "create_schema_accounts", Compliant() with { CanCreateInAccountsSchema = true } },
        { "create_schema_deck_suggestion", Compliant() with { CanCreateInDeckSuggestionSchema = true } },
        { "owns_objects", Compliant() with { OwnsObjects = true } },
    };

    [Fact]
    public void DB7_compliant_role_is_accepted_without_reasons()
    {
        var decision = DatabaseRolePolicy.Evaluate(Compliant());

        Assert.True(decision.IsAccepted);
        Assert.Empty(decision.Reasons);
    }

    [Theory]
    [MemberData(nameof(SingleViolations))]
    public void DB7_each_condition_alone_rejects_with_exactly_one_reason(string condition, RoleSnapshot snapshot)
    {
        var decision = DatabaseRolePolicy.Evaluate(snapshot);

        Assert.False(decision.IsAccepted, condition);
        Assert.Single(decision.Reasons);
    }

    [Fact]
    public void DB7_each_condition_has_a_distinct_stable_code()
    {
        var codes = SingleViolations
            .Select(row => DatabaseRolePolicy.Evaluate((RoleSnapshot)row[1]).Reasons.Single().Code)
            .ToList();

        Assert.Equal(8, codes.Distinct().Count());
        Assert.All(codes, code => Assert.False(string.IsNullOrWhiteSpace(code)));
    }

    [Fact]
    public void DB7_codes_are_stable_across_evaluations()
    {
        var first = DatabaseRolePolicy.Evaluate(Compliant() with { IsSuperuser = true }).Reasons.Single().Code;
        var second = DatabaseRolePolicy.Evaluate(Compliant() with { IsSuperuser = true }).Reasons.Single().Code;

        Assert.Equal(first, second);
    }

    [Fact]
    public void DB7_combination_returns_all_reasons()
    {
        var snapshot = Compliant() with
        {
            IsSuperuser = true,
            CanCreateDatabase = true,
            CanCreateInAccountsSchema = true,
            OwnsObjects = true,
        };

        var decision = DatabaseRolePolicy.Evaluate(snapshot);

        Assert.False(decision.IsAccepted);
        Assert.Equal(4, decision.Reasons.Count);
        Assert.Equal(4, decision.Reasons.Select(r => r.Code).Distinct().Count());
    }

    [Fact]
    public void DB7_all_conditions_together_return_eight_reasons()
    {
        var snapshot = Compliant() with
        {
            IsSuperuser = true,
            CanCreateRole = true,
            CanCreateDatabase = true,
            BypassesRowLevelSecurity = true,
            CanCreateInDatabase = true,
            CanCreateInAccountsSchema = true,
            CanCreateInDeckSuggestionSchema = true,
            OwnsObjects = true,
        };

        var decision = DatabaseRolePolicy.Evaluate(snapshot);

        Assert.Equal(8, decision.Reasons.Count);
    }

    [Fact]
    public void DB7_reason_messages_never_contain_role_name_database_name_host_or_connection_string_values()
    {
        var snapshot = Compliant() with
        {
            IsSuperuser = true,
            CanCreateRole = true,
            CanCreateDatabase = true,
            BypassesRowLevelSecurity = true,
            CanCreateInDatabase = true,
            CanCreateInAccountsSchema = true,
            CanCreateInDeckSuggestionSchema = true,
            OwnsObjects = true,
        };
        string[] forbidden =
        [
            FakeRoleName, FakeDatabaseName, "Host=", "Server=", "Database=", "Username=", "User ID=", "Password=", "localhost",
        ];

        var decision = DatabaseRolePolicy.Evaluate(snapshot);

        Assert.All(decision.Reasons, reason =>
        {
            foreach (var value in forbidden)
            {
                Assert.DoesNotContain(value, reason.Message, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(value, reason.Code, StringComparison.OrdinalIgnoreCase);
            }
        });
    }
}
