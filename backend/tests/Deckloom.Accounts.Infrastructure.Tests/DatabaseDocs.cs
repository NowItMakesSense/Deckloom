using System.Text.RegularExpressions;

namespace Deckloom.Accounts.Infrastructure.Tests;

// T-066 (ADR-0015). Leitor estatico dos artefatos de Database/ (tabelas, PKs, FKs, indices). Nunca toca banco.
internal sealed record DocColumn(string Name, bool? Required);

internal sealed record DocTable(string Schema, string Name, IReadOnlyList<DocColumn> Columns);

internal sealed record DocKey(string Name, string Schema, string Table, IReadOnlyList<string> Columns);

internal sealed record DocForeignKey(
    string Name, string SourceSchema, string SourceTable, string SourceColumn,
    string TargetSchema, string TargetTable, string TargetColumn, string OnDelete);

internal sealed record DocIndex(
    string Name, string Schema, string Table, IReadOnlyList<string> Columns, bool Unique, string? Filter);

internal static class DatabaseDocs
{
    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Raiz do repositorio (.git) nao encontrada.");
    }

    public static IReadOnlyList<DocTable> Tables(string schema) =>
        Files("tables", $"{schema}.*.md").Select(ParseTable).ToList();

    public static IReadOnlyList<DocKey> PrimaryKeys(string schema) =>
        Files("primary-keys", "pk_*.md").Select(ParseKey).Where(k => k.Schema == schema).ToList();

    public static IReadOnlyList<DocForeignKey> ForeignKeys(string schema) =>
        Files("foreign-keys", "fk_*.md").Select(ParseForeignKey).Where(k => k.SourceSchema == schema).ToList();

    public static IReadOnlyList<DocIndex> Indexes(string schema) =>
        Files("indexes", "*.md").Select(ParseIndex).Where(i => i.Schema == schema).ToList();

    private static string[] Files(string folder, string pattern)
    {
        var dir = Path.Combine(RepoRoot(), "Database", folder);
        Assert.True(Directory.Exists(dir), $"Pasta ausente: Database/{folder}.");
        return Directory.GetFiles(dir, pattern)
            .Where(f => !f.EndsWith("README.md", StringComparison.Ordinal))
            .Order()
            .ToArray();
    }

    private static (string Schema, string Name) Split(string qualified)
    {
        var parts = qualified.Trim().Trim('`').Split('.');
        Assert.True(parts.Length == 2, $"Nome qualificado invalido: '{qualified}'.");
        return (parts[0], parts[1]);
    }

    private static string Field(string text, string label)
    {
        var match = Regex.Match(text, $@"^{Regex.Escape(label)}:\s*(.+?)\s*$", RegexOptions.Multiline);
        Assert.True(match.Success, $"Campo '{label}:' ausente.");
        return match.Groups[1].Value;
    }

    private static DocTable ParseTable(string path)
    {
        var (schema, name) = Split(Path.GetFileNameWithoutExtension(path));
        var columns = new List<DocColumn>();
        var started = false;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.TrimEnd();
            if (!started)
            {
                started = line.StartsWith("| Column |", StringComparison.Ordinal);
                continue;
            }

            if (!line.StartsWith('|'))
            {
                break;
            }

            var cells = line.Split('|').Select(c => c.Trim()).ToList();
            if (cells.Count < 4 || cells[1].StartsWith("---", StringComparison.Ordinal))
            {
                continue;
            }

            var constraints = cells[3];
            bool? required = constraints.Contains("not null", StringComparison.Ordinal) ? true
                : constraints.StartsWith("null", StringComparison.Ordinal) ? false
                : null;
            columns.Add(new DocColumn(cells[1], required));
        }

        Assert.True(columns.Count > 0, $"{Path.GetFileName(path)}: tabela de colunas nao encontrada.");
        return new DocTable(schema, name, columns);
    }

    private static DocKey ParseKey(string path)
    {
        var text = File.ReadAllText(path);
        var (schema, table) = Split(Field(text, "Table"));
        var columns = Field(text, "Column(s)").Split(',').Select(c => c.Trim()).ToList();
        return new DocKey(Path.GetFileNameWithoutExtension(path), schema, table, columns);
    }

    private static DocForeignKey ParseForeignKey(string path)
    {
        var text = File.ReadAllText(path);
        var (sourceSchema, sourceTable, sourceColumn) = SplitColumn(Field(text, "Source table/column"));
        var (targetSchema, targetTable, targetColumn) = SplitColumn(Field(text, "Target table/column"));
        return new DocForeignKey(
            Path.GetFileNameWithoutExtension(path), sourceSchema, sourceTable, sourceColumn,
            targetSchema, targetTable, targetColumn, Field(text, "On delete").ToLowerInvariant());
    }

    private static (string Schema, string Table, string Column) SplitColumn(string value)
    {
        var parts = value.Trim().Trim('`').Split('.');
        Assert.True(parts.Length == 3, $"Coluna qualificada invalida: '{value}'.");
        return (parts[0], parts[1], parts[2]);
    }

    private static DocIndex ParseIndex(string path)
    {
        var text = File.ReadAllText(path);
        var (schema, table) = Split(Field(text, "Table"));
        var columnsLine = Field(text, "Columns");
        var filter = Regex.Match(columnsLine, @"WHERE ([^`]+)`");
        var columnList = Regex.Replace(columnsLine, @"\s*\(.*$", string.Empty);
        var columns = columnList.Split(',').Select(c => c.Trim()).ToList();
        var unique = Field(text, "Type").Contains("Unique: yes", StringComparison.Ordinal);
        return new DocIndex(
            Path.GetFileNameWithoutExtension(path), schema, table, columns, unique,
            filter.Success ? filter.Groups[1].Value.Trim() : null);
    }
}
