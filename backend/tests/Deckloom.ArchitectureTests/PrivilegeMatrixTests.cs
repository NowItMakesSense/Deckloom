using System.Text.RegularExpressions;
using Deckloom.ArchitectureTests.Support;

namespace Deckloom.ArchitectureTests;

/// <summary>
/// T-132 (habilitadora, ADR-0015): a matriz de privilégios de deckloom_app é lida por um único parser
/// e conferida contra Database/tables/. Verificação estática de arquivos; nunca toca o banco.
/// Contrato esperado do helper (Support/PrivilegeMatrix.cs):
///   PrivilegeMatrix.Parse(string markdown) -> IReadOnlyList&lt;PrivilegeRow&gt; (ordem do arquivo);
///   PrivilegeMatrix.Load(string repoRoot) -> idem, lendo Database/roles/privilege-matrix.md;
///   PrivilegeRow(Table, Select, Insert, Delete, UpdateWholeTable, UpdateColumns, Line);
///   erros de formato: FormatException citando a linha da matriz e/ou o problema.
/// </summary>
public class PrivilegeMatrixTests
{
    private const string Header =
        "| Tabela | SELECT | INSERT | UPDATE | DELETE | Uso no design |\n|---|---|---|---|---|---|\n";

    private static readonly string[] UpdateForbidden =
    [
        "accounts.access_attempt", "accounts.avatar_image", "accounts.pending_google_auth",
        "accounts.player_external_identity", "accounts.player_preferred_format",
    ];

    private static readonly string[] SelectOnly = ["accounts.avatar_preset", "deck_suggestion.format"];

    private static readonly string[] ImmutablePlayerColumns =
        ["email", "id", "terms_version", "terms_accepted_at", "adult_declared", "created_at"];

    private static readonly string[] ExpectedUpdateColumns = ["col_a", "col_b"];

    // ---- Parser (tabelas em string) ----

    [Fact]
    public void ADR0015_parser_reads_a_valid_row()
    {
        var rows = PrivilegeMatrix.Parse(Header + "| `accounts.demo` | x | x | - | x | uso |\n");

        var row = Assert.Single(rows);
        Assert.Equal("accounts.demo", row.Table);
        Assert.True(row.Select);
        Assert.True(row.Insert);
        Assert.True(row.Delete);
        Assert.False(row.UpdateWholeTable);
        Assert.Empty(row.UpdateColumns);
    }

    [Fact]
    public void ADR0015_parser_reads_not_granted_cells_as_false()
    {
        var rows = PrivilegeMatrix.Parse(Header + "| `accounts.ro` | x | - | - | - | leitura |\n");

        var row = Assert.Single(rows);
        Assert.True(row.Select);
        Assert.False(row.Insert);
        Assert.False(row.Delete);
        Assert.False(row.UpdateWholeTable);
        Assert.Empty(row.UpdateColumns);
    }

    [Fact]
    public void ADR0015_parser_reads_update_column_list()
    {
        var rows = PrivilegeMatrix.Parse(Header + "| `accounts.demo` | x | x | (col_a, col_b) | x | uso |\n");

        var row = Assert.Single(rows);
        Assert.False(row.UpdateWholeTable);
        Assert.Equal(ExpectedUpdateColumns, row.UpdateColumns.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ADR0015_parser_treats_x_in_update_as_whole_table()
    {
        var rows = PrivilegeMatrix.Parse(Header + "| `accounts.demo` | x | x | x | x | uso |\n");

        var row = Assert.Single(rows);
        Assert.True(row.UpdateWholeTable);
    }

    [Fact]
    public void ADR0015_parser_keeps_duplicate_rows_so_coverage_can_detect_them()
    {
        var rows = PrivilegeMatrix.Parse(
            Header + "| `accounts.demo` | x | - | - | - | a |\n| `accounts.demo` | x | - | - | - | b |\n");

        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void ADR0015_parser_fails_clearly_on_invalid_cell()
    {
        var ex = Assert.Throws<FormatException>(() =>
            PrivilegeMatrix.Parse(Header + "| `accounts.demo` | sim | - | - | - | uso |\n"));

        Assert.Contains("accounts.demo", ex.Message);
        Assert.Contains("SELECT", ex.Message);
    }

    [Fact]
    public void ADR0015_parser_fails_clearly_on_invalid_update_cell()
    {
        var ex = Assert.Throws<FormatException>(() =>
            PrivilegeMatrix.Parse(Header + "| `accounts.demo` | x | - | col_a, col_b | - | uso |\n"));

        Assert.Contains("accounts.demo", ex.Message);
        Assert.Contains("UPDATE", ex.Message);
    }

    [Fact]
    public void ADR0015_parser_fails_clearly_when_header_is_missing()
    {
        var ex = Assert.Throws<FormatException>(() =>
            PrivilegeMatrix.Parse("# Matriz\n\n| Objeto | Privilégio |\n|---|---|\n| banco | CONNECT |\n"));

        Assert.Contains("Tabela", ex.Message);
        Assert.Contains("SELECT", ex.Message);
    }

    // ---- Matriz real x Database/tables/ ----

    [Fact]
    public void ADR0015_matrix_has_exactly_one_row_per_table_file()
    {
        var rows = PrivilegeMatrix.Load(RepoHygieneTests.RepoRoot());
        var files = TableFiles().Keys.ToHashSet(StringComparer.Ordinal);

        var missing = files.Except(rows.Select(r => r.Table)).Order(StringComparer.Ordinal).ToList();
        var orphan = rows.Select(r => r.Table).Where(t => !files.Contains(t)).Distinct().Order(StringComparer.Ordinal).ToList();
        var duplicated = rows.GroupBy(r => r.Table).Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} (linhas {string.Join(", ", g.Select(r => r.Line))})").ToList();

        Assert.True(missing.Count == 0, $"Tabelas sem linha na matriz: {string.Join(", ", missing)}");
        Assert.True(orphan.Count == 0, $"Linhas da matriz sem arquivo em Database/tables/: {string.Join(", ", orphan)}");
        Assert.True(duplicated.Count == 0, $"Linhas duplicadas na matriz: {string.Join("; ", duplicated)}");
    }

    [Fact]
    public void ADR0015_every_update_column_exists_in_the_table_file()
    {
        var rows = PrivilegeMatrix.Load(RepoHygieneTests.RepoRoot());
        var files = TableFiles();

        var unknown = new List<string>();
        foreach (var row in rows)
        {
            if (!files.TryGetValue(row.Table, out var columns)) continue; // cobertura é outro teste
            foreach (var column in row.UpdateColumns.Where(c => !columns.Contains(c)))
                unknown.Add($"{row.Table}.{column} (linha {row.Line} da matriz)");
        }

        Assert.True(unknown.Count == 0, $"Colunas de UPDATE inexistentes em Database/tables/: {string.Join("; ", unknown)}");
    }

    [Fact]
    public void ADR0015_append_only_and_key_only_tables_never_grant_update()
    {
        var rows = PrivilegeMatrix.Load(RepoHygieneTests.RepoRoot());

        foreach (var table in UpdateForbidden)
        {
            var row = Assert.Single(rows, r => r.Table == table);
            Assert.False(row.UpdateWholeTable, $"{table}: UPDATE na tabela inteira (linha {row.Line}).");
            Assert.True(row.UpdateColumns.Count == 0,
                $"{table}: UPDATE em ({string.Join(", ", row.UpdateColumns)}) (linha {row.Line}).");
        }
    }

    [Fact]
    public void ADR0015_catalog_tables_are_select_only()
    {
        var rows = PrivilegeMatrix.Load(RepoHygieneTests.RepoRoot());

        foreach (var table in SelectOnly)
        {
            var row = Assert.Single(rows, r => r.Table == table);
            Assert.True(row.Select, $"{table}: falta SELECT (linha {row.Line}).");
            Assert.False(row.Insert, $"{table}: INSERT concedido (linha {row.Line}).");
            Assert.False(row.Delete, $"{table}: DELETE concedido (linha {row.Line}).");
            Assert.False(row.UpdateWholeTable, $"{table}: UPDATE concedido (linha {row.Line}).");
            Assert.Empty(row.UpdateColumns);
        }
    }

    [Fact]
    public void ADR0015_player_immutable_columns_never_appear_in_update()
    {
        var rows = PrivilegeMatrix.Load(RepoHygieneTests.RepoRoot());
        var player = Assert.Single(rows, r => r.Table == "accounts.player");

        Assert.False(player.UpdateWholeTable, $"accounts.player: UPDATE na tabela inteira (linha {player.Line}).");
        var leaked = ImmutablePlayerColumns.Where(player.UpdateColumns.Contains).ToList();
        Assert.True(leaked.Count == 0,
            $"accounts.player: colunas imutáveis em UPDATE (linha {player.Line}): {string.Join(", ", leaked)}");
    }

    [Fact]
    public void ADR0015_migration_history_tables_and_identity_sequence_are_listed_as_none()
    {
        var text = File.ReadAllText(Path.Combine(RepoHygieneTests.RepoRoot(), "Database", "roles", "privilege-matrix.md"));
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

        string[] objects =
        [
            "accounts.__ef_migrations_history",
            "deck_suggestion.__ef_migrations_history",
            "sequência implícita de `accounts.access_attempt.id`",
        ];
        foreach (var obj in objects)
        {
            var line = lines.FirstOrDefault(l => l.StartsWith('|') && l.Contains(obj, StringComparison.Ordinal));
            Assert.True(line is not null, $"Matriz sem linha para '{obj}'.");
            var cells = line.Split('|').Select(c => c.Trim()).ToList();
            Assert.True(cells.Count > 2 && cells[2] == "nenhum", $"'{obj}' deve constar como 'nenhum': {line}");
        }
    }

    // ---- Database/tables/ ----

    private static Dictionary<string, HashSet<string>> TableFiles()
    {
        var dir = Path.Combine(RepoHygieneTests.RepoRoot(), "Database", "tables");
        Assert.True(Directory.Exists(dir), "Falta o diretório Database/tables.");
        return Directory.EnumerateFiles(dir, "*.md")
            .Where(f => !string.Equals(Path.GetFileName(f), "README.md", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                f => Path.GetFileNameWithoutExtension(f),
                f => Columns(File.ReadAllText(f)),
                StringComparer.Ordinal);
    }

    /// <summary>Primeira célula das linhas da tabela "| Column | ...".</summary>
    private static HashSet<string> Columns(string markdown)
    {
        var columns = new HashSet<string>(StringComparer.Ordinal);
        var inTable = false;
        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (Regex.IsMatch(line, @"^\|\s*Column\s*\|")) { inTable = true; continue; }
            if (!inTable) continue;
            if (!line.StartsWith('|')) break;
            var first = line.Split('|')[1].Trim().Trim('`');
            if (first.Length > 0 && !first.StartsWith("---", StringComparison.Ordinal)) columns.Add(first);
        }
        return columns;
    }
}
