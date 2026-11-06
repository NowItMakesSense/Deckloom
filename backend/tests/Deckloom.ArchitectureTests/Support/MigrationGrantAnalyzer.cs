using System.Text;
using System.Text.RegularExpressions;

namespace Deckloom.ArchitectureTests.Support;

/// <summary>Resultado da leitura estática dos GRANT de uma ou mais migrações (ADR-0015). Nunca executa SQL.</summary>
public sealed class GrantAnalysis
{
    /// <summary>"schema.tabela" -> privilégios de tabela inteira concedidos (SELECT, INSERT, DELETE).</summary>
    public Dictionary<string, HashSet<string>> TablePrivileges { get; } = new(StringComparer.Ordinal);

    /// <summary>"schema.tabela" -> colunas de GRANT UPDATE (colunas).</summary>
    public Dictionary<string, HashSet<string>> UpdateColumns { get; } = new(StringComparer.Ordinal);

    /// <summary>"schema.tabela" com GRANT UPDATE na tabela inteira (a matriz de hoje não usa).</summary>
    public HashSet<string> UpdateWholeTable { get; } = new(StringComparer.Ordinal);

    public HashSet<string> SchemaUsage { get; } = new(StringComparer.Ordinal);

    public List<string> Violations { get; } = [];

    public IEnumerable<string> GrantedTables =>
        TablePrivileges.Keys.Concat(UpdateColumns.Keys).Concat(UpdateWholeTable).Distinct(StringComparer.Ordinal);
}

/// <summary>Extrai os literais de migrationBuilder.Sql e classifica cada GRANT contra o formato permitido.</summary>
public static partial class MigrationGrantAnalyzer
{
    private const string Role = "deckloom_app";
    private const string Ident = "\"?([A-Za-z0-9_]+)\"?";
    private const string QualifiedTable = "\"?(?<schema>[A-Za-z0-9_]+)\"?\\.\"?(?<table>[A-Za-z0-9_]+)\"?";

    [GeneratedRegex($@"^GRANT USAGE ON SCHEMA {Ident} TO ""?{Role}""?$", RegexOptions.IgnoreCase)]
    private static partial Regex UsagePattern();

    [GeneratedRegex(
        $@"^GRANT (?<privs>(?:SELECT|INSERT|DELETE)(?: *, *(?:SELECT|INSERT|DELETE))*) ON (?:TABLE )?{QualifiedTable} TO ""?{Role}""?$",
        RegexOptions.IgnoreCase)]
    private static partial Regex TablePattern();

    [GeneratedRegex(
        $@"^GRANT UPDATE \((?<cols>[^)]*)\) ON (?:TABLE )?{QualifiedTable} TO ""?{Role}""?$", RegexOptions.IgnoreCase)]
    private static partial Regex UpdateColumnsPattern();

    [GeneratedRegex($@"^GRANT UPDATE ON (?:TABLE )?{QualifiedTable} TO ""?{Role}""?$", RegexOptions.IgnoreCase)]
    private static partial Regex UpdateWholePattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"CREATE\s+(ROLE|USER)|ALTER\s+(ROLE|USER)|\bPASSWORD\b", RegexOptions.IgnoreCase)]
    private static partial Regex RoleOrPasswordPattern();

    [GeneratedRegex(@"ALTER\s+DEFAULT\s+PRIVILEGES", RegexOptions.IgnoreCase)]
    private static partial Regex DefaultPrivilegesPattern();

    /// <summary>
    /// Devolve os textos passados a migrationBuilder.Sql. Qualquer chamada cujo argumento não seja um único
    /// literal (interpolação, concatenação, variável) vira um item em <paramref name="problems"/>.
    /// </summary>
    public static List<string> ExtractSqlLiterals(string source, List<string> problems)
    {
        const string marker = "migrationBuilder.Sql(";
        var literals = new List<string>();
        var from = 0;
        while (true)
        {
            var at = source.IndexOf(marker, from, StringComparison.Ordinal);
            if (at < 0)
            {
                return literals;
            }

            var pos = SkipSpace(source, at + marker.Length);
            from = pos;
            var line = source.Take(at).Count(c => c == '\n') + 1;
            var text = ReadLiteral(source, ref pos, out var reason);
            if (text is null)
            {
                problems.Add($"linha {line}: migrationBuilder.Sql com argumento nao literal ({reason}).");
                continue;
            }

            pos = SkipSpace(source, pos);
            if (pos >= source.Length || (source[pos] != ')' && source[pos] != ','))
            {
                problems.Add($"linha {line}: migrationBuilder.Sql com concatenacao ou expressao apos o literal.");
                continue;
            }

            literals.Add(text);
            from = pos;
        }
    }

    public static GrantAnalysis Analyze(IEnumerable<string> sqlLiterals)
    {
        var result = new GrantAnalysis();
        foreach (var literal in sqlLiterals)
        {
            if (RoleOrPasswordPattern().IsMatch(literal))
            {
                result.Violations.Add($"CREATE/ALTER ROLE ou senha em migracao (papeis sao do bootstrap, T-005): {Short(literal)}");
            }

            if (DefaultPrivilegesPattern().IsMatch(literal))
            {
                result.Violations.Add($"ALTER DEFAULT PRIVILEGES proibido: {Short(literal)}");
            }

            foreach (var raw in literal.Split(';'))
            {
                var statement = Whitespace().Replace(raw, " ").Trim();
                if (statement.Contains("GRANT", StringComparison.OrdinalIgnoreCase)
                    && !DefaultPrivilegesPattern().IsMatch(statement))
                {
                    Classify(statement, result);
                }
            }
        }

        return result;
    }

    private static void Classify(string statement, GrantAnalysis result)
    {
        if (statement.Contains("__ef_migrations_history", StringComparison.OrdinalIgnoreCase))
        {
            result.Violations.Add($"{Reason(statement)}: {Short(statement)}");
            return;
        }

        if (UsagePattern().Match(statement) is { Success: true } usage)
        {
            result.SchemaUsage.Add(usage.Groups[1].Value);
            return;
        }

        if (TablePattern().Match(statement) is { Success: true } table)
        {
            var key = $"{table.Groups["schema"].Value}.{table.Groups["table"].Value}";
            if (!result.TablePrivileges.TryGetValue(key, out var set))
            {
                result.TablePrivileges[key] = set = new HashSet<string>(StringComparer.Ordinal);
            }

            foreach (var priv in table.Groups["privs"].Value.Split(','))
            {
                set.Add(priv.Trim().ToUpperInvariant());
            }

            return;
        }

        if (UpdateColumnsPattern().Match(statement) is { Success: true } update)
        {
            var key = $"{update.Groups["schema"].Value}.{update.Groups["table"].Value}";
            if (!result.UpdateColumns.TryGetValue(key, out var set))
            {
                result.UpdateColumns[key] = set = new HashSet<string>(StringComparer.Ordinal);
            }

            foreach (var column in update.Groups["cols"].Value.Split(','))
            {
                set.Add(column.Trim().Trim('"'));
            }

            return;
        }

        if (UpdateWholePattern().Match(statement) is { Success: true } whole)
        {
            result.UpdateWholeTable.Add($"{whole.Groups["schema"].Value}.{whole.Groups["table"].Value}");
            return;
        }

        result.Violations.Add($"{Reason(statement)}: {Short(statement)}");
    }

    private static string Reason(string statement)
    {
        if (Regex.IsMatch(statement, @"\bPUBLIC\b", RegexOptions.IgnoreCase)) return "GRANT a PUBLIC";
        if (Regex.IsMatch(statement, @"ALL\s+PRIVILEGES|\bGRANT\s+ALL\b", RegexOptions.IgnoreCase)) return "GRANT ALL PRIVILEGES";
        if (Regex.IsMatch(statement, @"\b(TRUNCATE|REFERENCES|TRIGGER)\b", RegexOptions.IgnoreCase)) return "GRANT de TRUNCATE/REFERENCES/TRIGGER";
        if (Regex.IsMatch(statement, @"__ef_migrations_history", RegexOptions.IgnoreCase)) return "GRANT em __ef_migrations_history";
        if (Regex.IsMatch(statement, @"\bSEQUENCE\b", RegexOptions.IgnoreCase)) return "GRANT em sequencia";
        if (Regex.IsMatch(statement, @"GRANT\s+(USAGE\s*,\s*)?CREATE\b|\bCREATE\s+ON\b", RegexOptions.IgnoreCase)) return "GRANT CREATE";
        if (!Regex.IsMatch(statement, $@"\bTO\s+""?{Role}""?$", RegexOptions.IgnoreCase)) return $"GRANT a papel diferente de {Role}";
        return "GRANT em forma nao reconhecida";
    }

    private static string Short(string text)
    {
        var flat = Whitespace().Replace(text, " ").Trim();
        return flat.Length <= 160 ? flat : flat[..160] + "...";
    }

    private static int SkipSpace(string s, int pos)
    {
        while (pos < s.Length && char.IsWhiteSpace(s[pos]))
        {
            pos++;
        }

        return pos;
    }

    private static string? ReadLiteral(string s, ref int pos, out string reason)
    {
        reason = string.Empty;
        if (pos >= s.Length)
        {
            reason = "fim do arquivo";
            return null;
        }

        if (s[pos] == '$')
        {
            reason = "interpolacao";
            return null;
        }

        if (s[pos] == '@')
        {
            if (pos + 1 < s.Length && s[pos + 1] == '"')
            {
                return ReadVerbatim(s, ref pos);
            }

            reason = "interpolacao ou forma desconhecida";
            return null;
        }

        if (s[pos] != '"')
        {
            reason = "nao e um literal de texto";
            return null;
        }

        if (s.AsSpan(pos).StartsWith("\"\"\"", StringComparison.Ordinal))
        {
            return ReadRaw(s, ref pos);
        }

        return ReadRegular(s, ref pos);
    }

    private static string ReadVerbatim(string s, ref int pos)
    {
        var sb = new StringBuilder();
        pos += 2;
        while (pos < s.Length)
        {
            if (s[pos] == '"')
            {
                if (pos + 1 < s.Length && s[pos + 1] == '"')
                {
                    sb.Append('"');
                    pos += 2;
                    continue;
                }

                pos++;
                return sb.ToString();
            }

            sb.Append(s[pos++]);
        }

        return sb.ToString();
    }

    private static string ReadRaw(string s, ref int pos)
    {
        var quotes = 0;
        while (pos + quotes < s.Length && s[pos + quotes] == '"')
        {
            quotes++;
        }

        var delimiter = new string('"', quotes);
        var start = pos + quotes;
        var end = s.IndexOf(delimiter, start, StringComparison.Ordinal);
        if (end < 0)
        {
            end = s.Length;
        }

        pos = Math.Min(end + quotes, s.Length);
        return s[start..end];
    }

    private static string ReadRegular(string s, ref int pos)
    {
        var sb = new StringBuilder();
        pos++;
        while (pos < s.Length)
        {
            var c = s[pos++];
            if (c == '"')
            {
                return sb.ToString();
            }

            if (c == '\\' && pos < s.Length)
            {
                var next = s[pos++];
                sb.Append(next switch { 'n' => '\n', 'r' => '\r', 't' => '\t', _ => next });
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }
}
