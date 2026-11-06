using System.Text.RegularExpressions;

namespace Deckloom.ArchitectureTests;

/// <summary>
/// REQ-046 / ADR-0001 (emenda: tipo âncora de FK entre schemas). O tipo que ancora a FK
/// player_preferred_format.format_id -> deck_suggestion.format(id) contém SÓ a coluna referenciada (Id), é internal,
/// fica fora das migrations, não tem DbSet e nunca é consultado nem gravado pelo módulo Accounts.
/// Verificação estática das fontes de Deckloom.Accounts.Infrastructure.
/// </summary>
public class AnchorFormatRuleTests
{
    private const string AccountsInfrastructure = "Deckloom.Accounts.Infrastructure";

    // O tipo âncora é o alvo do HasOne<T>() cuja HasForeignKey usa FormatId.
    private static readonly Regex AnchorTarget = new(
        @"HasOne<(?<type>\w+)>\(\)\s*\.WithMany\(\)\s*\.HasForeignKey\(\s*\w+\s*=>\s*\w+\.FormatId\s*\)",
        RegexOptions.Singleline);

    private static readonly Regex DataProperty = new(
        @"^\s*(public|internal)\s+(?!const\b|static\b|class\b|sealed\b|void\b)[\w<>\?\[\],\.]+\s+(?<name>\w+)\s*\{\s*get\s*;",
        RegexOptions.Multiline);

    [Fact]
    public void REQ_046_anchor_format_type_has_only_the_Id_property()
    {
        var (anchor, source) = FindAnchorDeclaration();

        var properties = DataProperty.Matches(ClassBody(source, anchor))
            .Select(m => m.Groups["name"].Value)
            .ToList();

        Assert.True(
            properties.SequenceEqual(["Id"]),
            $"Tipo âncora '{anchor}' deve ter só a propriedade Id (ADR-0001, emenda), mas tem: {string.Join(", ", properties)}.");
    }

    [Fact]
    public void REQ_046_anchor_format_type_is_internal_and_excluded_from_migrations()
    {
        var (anchor, source) = FindAnchorDeclaration();

        Assert.Matches(new Regex($@"\binternal\s+(sealed\s+)?class\s+{anchor}\b"), source);
        Assert.Contains(".ExcludeFromMigrations()", source);
    }

    [Fact]
    public void REQ_046_anchor_format_type_has_no_DbSet_and_is_never_queried_or_written()
    {
        var (anchor, _) = FindAnchorDeclaration();
        var offenders = new List<string>();

        foreach (var file in HandWrittenSources())
        {
            var code = SolutionLayout.ReadCode(file);
            var name = Path.GetFileName(file);

            if (Regex.IsMatch(code, $@"DbSet<\s*{anchor}\s*>"))
                offenders.Add($"{name}: DbSet<{anchor}>");
            if (Regex.IsMatch(code, $@"\bSet<\s*{anchor}\s*>"))
                offenders.Add($"{name}: Set<{anchor}>()");
            if (Regex.IsMatch(code, $@"\bnew\s+{anchor}\b"))
                offenders.Add($"{name}: new {anchor}");
            if (Regex.IsMatch(code, $@"\b(Entries|Entry|Find|FindAsync|Add|AddRange|Remove|Update)\w*<\s*{anchor}\s*>"))
                offenders.Add($"{name}: acesso a {anchor}");

            // Fora da declaração do próprio tipo, só a configuração de FK e o AccountsDbContext (ApplyConfiguration) o citam.
            var allowed = name is "PlayerPreferredFormat.cs" or "AccountsDbContext.cs";
            if (!allowed && Regex.IsMatch(code, $@"\b{anchor}\b"))
                offenders.Add($"{name}: referencia {anchor}");
        }

        Assert.True(offenders.Count == 0,
            $"Tipo âncora '{anchor}' não pode ter DbSet nem ser consultado/gravado (ADR-0001, emenda): {string.Join("; ", offenders)}.");
    }

    // Migrations/ e codigo gerado (Designer.cs, ModelSnapshot.cs citam o tipo ancora por serem o modelo gerado): fora da regra.
    private static IEnumerable<string> HandWrittenSources() =>
        SolutionLayout.Named(AccountsInfrastructure).SourceFiles
            .Where(f => !f.Replace('\\', '/').Split('/').Contains("Migrations"));

    private static (string Anchor, string Source) FindAnchorDeclaration()
    {
        foreach (var file in HandWrittenSources())
        {
            var code = SolutionLayout.ReadCode(file);
            var match = AnchorTarget.Match(code);
            if (match.Success)
                return (match.Groups["type"].Value, code);
        }

        throw new InvalidOperationException("Tipo âncora da FK format_id não encontrado em Accounts.Infrastructure.");
    }

    private static string ClassBody(string source, string anchor)
    {
        var start = Regex.Match(source, $@"\bclass\s+{anchor}\b");
        Assert.True(start.Success, $"Declaração de '{anchor}' não encontrada.");
        var open = source.IndexOf('{', start.Index);
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0)
                return source[open..(i + 1)];
        }

        throw new InvalidOperationException($"Corpo de '{anchor}' não fechado.");
    }
}
