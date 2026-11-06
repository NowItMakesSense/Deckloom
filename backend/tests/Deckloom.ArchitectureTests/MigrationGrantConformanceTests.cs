using System.Text.RegularExpressions;
using Deckloom.ArchitectureTests.Support;

namespace Deckloom.ArchitectureTests;

/// <summary>
/// T-066 (RED, ADR-0015). Os GRANT emitidos pelas migracoes geradas (Accounts e DeckSuggestion) sao exatamente os da
/// matriz Database/roles/privilege-matrix.md, lida pelo parser unico de T-132. Verificacao estatica de arquivos .cs:
/// nada e executado e nenhum banco e consultado.
/// Layout (orquestrador): as migracoes ficam em src/Modules/Accounts/Deckloom.Accounts.Infrastructure/Migrations e
/// src/Modules/DeckSuggestion/Deckloom.DeckSuggestion.Infrastructure/Migrations.
/// </summary>
public sealed partial class MigrationGrantConformanceTests
{
    private const string NotGenerated = "migrações ainda não geradas (T-066 GREEN)";

    private static readonly string[] ModuleMigrationDirs =
    [
        "backend/src/Modules/Accounts/Deckloom.Accounts.Infrastructure/Migrations",
        "backend/src/Modules/DeckSuggestion/Deckloom.DeckSuggestion.Infrastructure/Migrations",
    ];

    [GeneratedRegex(@"CreateTable\(\s*name:\s*""(?<name>\w+)"",\s*schema:\s*""(?<schema>\w+)""")]
    private static partial Regex CreateTablePattern();

    [GeneratedRegex(@"^(?<stamp>\d{14})_")]
    private static partial Regex StampPattern();

    private static List<string> MigrationFiles(string relativeDir)
    {
        var dir = Path.Combine(RepoHygieneTests.RepoRoot(), relativeDir);
        if (!Directory.Exists(dir))
        {
            Assert.Fail($"{NotGenerated}: pasta ausente {relativeDir}.");
        }

        var files = Directory.GetFiles(dir, "*.cs")
            .Where(f => !f.EndsWith(".Designer.cs", StringComparison.Ordinal)
                && !f.EndsWith("ModelSnapshot.cs", StringComparison.Ordinal))
            .Order().ToList();
        if (files.Count == 0)
        {
            Assert.Fail($"{NotGenerated}: nenhum arquivo de migracao em {relativeDir}.");
        }

        return files;
    }

    private static List<string> AllMigrationFiles() => ModuleMigrationDirs.SelectMany(MigrationFiles).ToList();

    private static List<string> LiteralsOf(string file)
    {
        var problems = new List<string>();
        var literals = MigrationGrantAnalyzer.ExtractSqlLiterals(File.ReadAllText(file), problems);
        Assert.True(problems.Count == 0, $"{Path.GetFileName(file)}: {string.Join("; ", problems)}");
        return literals;
    }

    private static GrantAnalysis AnalyzeAll() =>
        MigrationGrantAnalyzer.Analyze(AllMigrationFiles().SelectMany(LiteralsOf));

    private static IReadOnlyList<PrivilegeRow> Matrix() => PrivilegeMatrix.Load(RepoHygieneTests.RepoRoot());

    // ---- Migracoes geradas contra a matriz ----

    [Fact]
    public void T066_migrations_grant_exactly_the_privileges_marked_in_the_matrix_to_deckloom_app()
    {
        var analysis = AnalyzeAll();
        var matrix = Matrix();
        var problems = new List<string>(analysis.Violations);

        foreach (var row in matrix)
        {
            var expected = new[] { (row.Select, "SELECT"), (row.Insert, "INSERT"), (row.Delete, "DELETE") }
                .Where(p => p.Item1).Select(p => p.Item2).Order().ToList();
            var actual = analysis.TablePrivileges.GetValueOrDefault(row.Table, []).Order().ToList();
            if (!expected.SequenceEqual(actual))
            {
                problems.Add($"{row.Table}: GRANT [{string.Join(",", actual)}] != matriz [{string.Join(",", expected)}].");
            }

            var actualColumns = analysis.UpdateColumns.GetValueOrDefault(row.Table, []).Order().ToList();
            var expectedColumns = row.UpdateColumns.Order().ToList();
            if (!expectedColumns.SequenceEqual(actualColumns))
            {
                problems.Add($"{row.Table}: GRANT UPDATE ({string.Join(",", actualColumns)}) != matriz ({string.Join(",", expectedColumns)}).");
            }

            if (analysis.UpdateWholeTable.Contains(row.Table) != row.UpdateWholeTable)
            {
                problems.Add($"{row.Table}: GRANT UPDATE na tabela inteira diverge da matriz (x={row.UpdateWholeTable}).");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void T066_migrations_grant_only_on_tables_present_in_the_matrix()
    {
        var analysis = AnalyzeAll();
        var known = Matrix().Select(r => r.Table).ToHashSet(StringComparer.Ordinal);

        var unknown = analysis.GrantedTables.Where(t => !known.Contains(t)).Order().ToList();

        Assert.True(unknown.Count == 0, $"GRANT em tabela ausente da matriz: {string.Join(", ", unknown)}.");
    }

    [Fact]
    public void T066_migrations_grant_usage_on_exactly_the_accounts_and_deck_suggestion_schemas()
    {
        var analysis = AnalyzeAll();

        Assert.Equal(["accounts", "deck_suggestion"], analysis.SchemaUsage.Order().ToList());
    }

    [Fact]
    public void T066_migrations_have_no_forbidden_grant_role_or_password_statement()
    {
        var analysis = AnalyzeAll();

        Assert.True(analysis.Violations.Count == 0, string.Join(Environment.NewLine, analysis.Violations));
    }

    [Fact]
    public void T066_every_migrationBuilder_Sql_argument_is_a_plain_literal()
    {
        var problems = new List<string>();
        foreach (var file in AllMigrationFiles())
        {
            var found = new List<string>();
            MigrationGrantAnalyzer.ExtractSqlLiterals(File.ReadAllText(file), found);
            problems.AddRange(found.Select(p => $"{Path.GetFileName(file)}: {p}"));
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void T066_each_migration_grants_the_matrix_privileges_of_the_tables_it_creates()
    {
        var matrix = Matrix().ToDictionary(r => r.Table, StringComparer.Ordinal);
        var problems = new List<string>();

        foreach (var file in AllMigrationFiles())
        {
            var created = CreateTablePattern().Matches(File.ReadAllText(file))
                .Select(m => $"{m.Groups["schema"].Value}.{m.Groups["name"].Value}").Distinct().ToList();
            var analysis = MigrationGrantAnalyzer.Analyze(LiteralsOf(file));

            foreach (var table in created)
            {
                if (!matrix.TryGetValue(table, out var row))
                {
                    problems.Add($"{Path.GetFileName(file)}: tabela {table} criada sem linha na matriz.");
                    continue;
                }

                var expected = new[] { (row.Select, "SELECT"), (row.Insert, "INSERT"), (row.Delete, "DELETE") }
                    .Where(p => p.Item1).Select(p => p.Item2).Order().ToList();
                var actual = analysis.TablePrivileges.GetValueOrDefault(table, []).Order().ToList();
                var columns = analysis.UpdateColumns.GetValueOrDefault(table, []).Order().ToList();
                if (!expected.SequenceEqual(actual) || !row.UpdateColumns.Order().SequenceEqual(columns))
                {
                    problems.Add($"{Path.GetFileName(file)}: GRANT de {table} nao esta na mesma migracao que a cria.");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void T066_deck_suggestion_migration_is_ordered_before_the_accounts_migration()
    {
        // Nota T-062: fk_player_preferred_format_format referencia deck_suggestion.format, que precisa existir antes.
        static List<string> Stamps(string dir) => MigrationFiles(dir)
            .Select(f => StampPattern().Match(Path.GetFileName(f)))
            .Where(m => m.Success).Select(m => m.Groups["stamp"].Value).ToList();

        var accounts = Stamps(ModuleMigrationDirs[0]);
        var deckSuggestion = Stamps(ModuleMigrationDirs[1]);

        Assert.True(accounts.Count > 0 && deckSuggestion.Count > 0, "Migracoes sem prefixo de data (yyyyMMddHHmmss_).");
        Assert.True(
            string.CompareOrdinal(deckSuggestion.Max(), accounts.Min()) < 0,
            "A migracao de DeckSuggestion deve ter data anterior a de Accounts (aplicada antes).");
    }

    // ---- O detector em si (strings em memoria; garante que a violacao temporaria seria apanhada) ----

    [Theory]
    [InlineData("GRANT SELECT ON accounts.player TO PUBLIC", "PUBLIC")]
    [InlineData("GRANT SELECT ON accounts.player TO deckloom_owner", "papel diferente")]
    [InlineData("GRANT ALL PRIVILEGES ON accounts.player TO deckloom_app", "ALL PRIVILEGES")]
    [InlineData("GRANT TRUNCATE ON accounts.player TO deckloom_app", "TRUNCATE")]
    [InlineData("GRANT REFERENCES ON accounts.player TO deckloom_app", "REFERENCES")]
    [InlineData("GRANT TRIGGER ON accounts.player TO deckloom_app", "TRIGGER")]
    [InlineData("GRANT CREATE ON SCHEMA accounts TO deckloom_app", "CREATE")]
    [InlineData("GRANT SELECT ON accounts.__ef_migrations_history TO deckloom_app", "__ef_migrations_history")]
    [InlineData("GRANT USAGE ON SEQUENCE accounts.access_attempt_id_seq TO deckloom_app", "sequencia")]
    [InlineData("ALTER DEFAULT PRIVILEGES IN SCHEMA accounts GRANT SELECT ON TABLES TO deckloom_app", "DEFAULT PRIVILEGES")]
    [InlineData("CREATE ROLE deckloom_app LOGIN", "ROLE")]
    [InlineData("ALTER ROLE deckloom_app WITH PASSWORD 'x'", "ROLE")]
    public void T066_detector_flags_forbidden_statement(string sql, string expectedFragment)
    {
        var analysis = MigrationGrantAnalyzer.Analyze([sql]);

        Assert.Contains(analysis.Violations, v => v.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("GRANT USAGE ON SCHEMA accounts TO deckloom_app")]
    [InlineData("GRANT SELECT, INSERT, DELETE ON accounts.player TO deckloom_app")]
    [InlineData("GRANT UPDATE (password_hash, display_name) ON accounts.player TO deckloom_app")]
    public void T066_detector_accepts_the_permitted_forms(string sql)
    {
        var analysis = MigrationGrantAnalyzer.Analyze([sql]);

        Assert.Empty(analysis.Violations);
    }

    [Fact]
    public void T066_detector_reads_update_columns_and_privileges()
    {
        var analysis = MigrationGrantAnalyzer.Analyze(
        [
            "GRANT SELECT, INSERT ON accounts.player TO deckloom_app;\nGRANT UPDATE (bio, display_name) ON accounts.player TO deckloom_app;",
        ]);

        Assert.Equal(["INSERT", "SELECT"], analysis.TablePrivileges["accounts.player"].Order().ToList());
        Assert.Equal(["bio", "display_name"], analysis.UpdateColumns["accounts.player"].Order().ToList());
    }

    [Theory]
    [InlineData("migrationBuilder.Sql($\"GRANT SELECT ON accounts.{t} TO deckloom_app\");")]
    [InlineData("migrationBuilder.Sql(\"GRANT SELECT ON accounts.\" + t + \" TO deckloom_app\");")]
    [InlineData("migrationBuilder.Sql(sql);")]
    public void T066_detector_flags_non_literal_sql(string source)
    {
        var problems = new List<string>();

        MigrationGrantAnalyzer.ExtractSqlLiterals(source, problems);

        Assert.Single(problems);
    }

    [Theory]
    [InlineData("migrationBuilder.Sql(\"GRANT SELECT ON accounts.player TO deckloom_app\");")]
    [InlineData("migrationBuilder.Sql(@\"GRANT SELECT\nON accounts.player TO deckloom_app\", suppressTransaction: false);")]
    [InlineData("migrationBuilder.Sql(\"\"\"\n    GRANT SELECT ON accounts.player TO deckloom_app\n    \"\"\");")]
    public void T066_detector_extracts_plain_literals(string source)
    {
        var problems = new List<string>();

        var literals = MigrationGrantAnalyzer.ExtractSqlLiterals(source, problems);

        Assert.Empty(problems);
        Assert.Contains("GRANT SELECT", Assert.Single(literals), StringComparison.Ordinal);
    }
}
