using System.Text.RegularExpressions;

namespace Deckloom.ArchitectureTests;

/// <summary>Definição de um papel lida de Database/roles/&lt;papel&gt;.md (fonte de verdade, ADR-0015).</summary>
internal sealed record RoleDefinition(
    string Role,
    IReadOnlySet<string> Attributes,
    int? ConnectionLimit,
    IReadOnlySet<(string Parameter, string Value)> Settings);

/// <summary>Lê atributos, limites e parâmetros dos papéis a partir dos .md, para o teste não manter tabela própria.</summary>
internal static partial class RoleDocs
{
    public static readonly string[] AttributeKeywords =
    [
        "LOGIN", "NOLOGIN", "SUPERUSER", "NOSUPERUSER", "CREATEDB", "NOCREATEDB", "CREATEROLE", "NOCREATEROLE",
        "REPLICATION", "NOREPLICATION", "BYPASSRLS", "NOBYPASSRLS", "INHERIT", "NOINHERIT",
    ];

    public static IReadOnlyList<RoleDefinition> Load(string repoRoot)
    {
        var dir = Path.Combine(repoRoot, "Database", "roles");
        Assert.True(Directory.Exists(dir), "Falta o diretório Database/roles.");
        return Directory.EnumerateFiles(dir, "deckloom_*.md").Order(StringComparer.Ordinal)
            .Select(f => Parse(Path.GetFileNameWithoutExtension(f), File.ReadAllText(f)))
            .ToList();
    }

    public static RoleDefinition Parse(string role, string markdown)
    {
        var attributesSection = Section(markdown, "Attributes");
        var attributeSpan = BacktickSpanRegex().Matches(attributesSection)
            .Select(m => m.Groups[1].Value)
            .FirstOrDefault(s => s.Split(' ').All(t => AttributeKeywords.Contains(t)));
        Assert.True(attributeSpan is not null, $"{role}.md: seção Attributes sem lista de atributos entre crases.");
        var attributes = attributeSpan.Split(' ').ToHashSet(StringComparer.Ordinal);

        var limits = Section(markdown, "Limits");
        int? connectionLimit = null;
        var limitMatch = ConnectionLimitRegex().Match(limits);
        if (limitMatch.Success) connectionLimit = int.Parse(limitMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);

        var settings = new HashSet<(string, string)>();
        foreach (Match m in AlterRoleRegex().Matches(limits))
            if (m.Groups[1].Value == role) settings.Add((m.Groups[2].Value, m.Groups[3].Value));
        foreach (Match m in BareSettingRegex().Matches(limits))
            settings.Add((m.Groups[1].Value, m.Groups[2].Value));
        foreach (Match m in TableSettingRegex().Matches(limits))
            if (m.Groups[1].Value != "CONNECTION LIMIT") settings.Add((m.Groups[1].Value, m.Groups[2].Value));

        return new RoleDefinition(role, attributes, connectionLimit, settings);
    }

    /// <summary>Corpo da seção "## nome" até o próximo "## ".</summary>
    private static string Section(string markdown, string name)
    {
        var m = Regex.Match(markdown, $@"(?ms)^##\s+{Regex.Escape(name)}\s*$(.*?)(?=^##\s|\z)");
        Assert.True(m.Success, $"Seção '## {name}' ausente.");
        return m.Groups[1].Value;
    }

    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex BacktickSpanRegex();

    // "`CONNECTION LIMIT 2`" ou linha de tabela "| `CONNECTION LIMIT` | 30 |".
    [GeneratedRegex(@"CONNECTION LIMIT`?\s*\|?\s*(\d+)")]
    private static partial Regex ConnectionLimitRegex();

    // "`ALTER ROLE r IN DATABASE deckloom SET param = 'valor'`" (aspas opcionais).
    [GeneratedRegex(@"ALTER ROLE (\w+) IN DATABASE deckloom SET (\w+) = '?([^'`\s]+)'?")]
    private static partial Regex AlterRoleRegex();

    // "`statement_timeout = '5min'`".
    [GeneratedRegex(@"`(\w+) = '([^']+)'")]
    private static partial Regex BareSettingRegex();

    // "| `statement_timeout` | `5s` | motivo |".
    [GeneratedRegex(@"^\|\s*`([^`]+)`\s*\|\s*`([^`]+)`\s*\|", RegexOptions.Multiline)]
    private static partial Regex TableSettingRegex();
}
