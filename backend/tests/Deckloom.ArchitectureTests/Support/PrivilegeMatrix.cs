namespace Deckloom.ArchitectureTests.Support;

/// <summary>Uma linha da tabela "Tabelas" de Database/roles/privilege-matrix.md (ADR-0015).</summary>
public sealed record PrivilegeRow(
    string Table,
    bool Select,
    bool Insert,
    bool Delete,
    bool UpdateWholeTable,
    IReadOnlySet<string> UpdateColumns,
    int Line);

/// <summary>
/// Único leitor da matriz de privilégios de deckloom_app. Formato estrito descrito no próprio arquivo.
/// </summary>
public static class PrivilegeMatrix
{
    private const string MatrixPath = "Database/roles/privilege-matrix.md";

    public static IReadOnlyList<PrivilegeRow> Load(string repoRoot) =>
        Parse(File.ReadAllText(Path.Combine(repoRoot, "Database", "roles", "privilege-matrix.md")));

    public static IReadOnlyList<PrivilegeRow> Parse(string markdown)
    {
        var lines = markdown.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var headerIndex = lines.FindIndex(IsHeader);
        if (headerIndex < 0)
            throw new FormatException(
                $"{MatrixPath}: cabeçalho da tabela não encontrado (esperado '| Tabela | SELECT | INSERT | UPDATE | DELETE | ...').");

        var rows = new List<PrivilegeRow>();
        for (var i = headerIndex + 2; i < lines.Count && lines[i].StartsWith('|'); i++)
            rows.Add(ParseRow(lines[i], i + 1));
        return rows;
    }

    private static bool IsHeader(string line)
    {
        if (!line.StartsWith('|')) return false;
        var cells = Cells(line);
        return cells.Count >= 5
            && cells[0] == "Tabela" && cells[1] == "SELECT" && cells[2] == "INSERT"
            && cells[3] == "UPDATE" && cells[4] == "DELETE";
    }

    private static PrivilegeRow ParseRow(string line, int number)
    {
        var cells = Cells(line);
        if (cells.Count < 5)
            throw new FormatException($"{MatrixPath} linha {number}: esperadas ao menos 5 colunas, encontradas {cells.Count}.");

        var table = cells[0].Trim('`');
        var (wholeTable, columns) = ParseUpdate(cells[3], table, number);
        return new PrivilegeRow(
            table,
            ParseFlag(cells[1], table, "SELECT", number),
            ParseFlag(cells[2], table, "INSERT", number),
            ParseFlag(cells[4], table, "DELETE", number),
            wholeTable,
            columns,
            number);
    }

    private static bool ParseFlag(string cell, string table, string column, int number) => cell switch
    {
        "x" => true,
        "-" => false,
        _ => throw new FormatException(
            $"{MatrixPath} linha {number}: tabela '{table}', coluna {column}: célula '{cell}' inválida (use 'x' ou '-')."),
    };

    private static (bool WholeTable, IReadOnlySet<string> Columns) ParseUpdate(string cell, string table, int number)
    {
        var none = new HashSet<string>(StringComparer.Ordinal);
        if (cell == "-") return (false, none);
        if (cell == "x") return (true, none);

        if (cell.Length > 2 && cell[0] == '(' && cell[^1] == ')')
        {
            var names = cell[1..^1].Split(',').Select(c => c.Trim()).ToList();
            if (names.All(n => n.Length > 0 && n.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_')))
                return (false, new HashSet<string>(names, StringComparer.Ordinal));
        }

        throw new FormatException(
            $"{MatrixPath} linha {number}: tabela '{table}', coluna UPDATE: célula '{cell}' inválida (use '-', 'x' ou '(col_a, col_b)').");
    }

    private static List<string> Cells(string line)
    {
        var parts = line.Split('|').Select(c => c.Trim()).ToList();
        parts.RemoveAt(0); // texto antes do primeiro '|'
        return parts;
    }
}
