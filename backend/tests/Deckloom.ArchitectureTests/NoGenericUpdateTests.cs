using System.Text.RegularExpressions;

namespace Deckloom.ArchitectureTests;

/// <summary>
/// T-133 (UA1, DB3; ADR-0012, ADR-0015): em backend/src/**/Infrastructure o EF so pode gerar UPDATE das colunas
/// realmente alteradas de entidades rastreadas. Proibidos: DbSet.Update/UpdateRange/Attach/AttachRange,
/// DbContext.Update(...) e Entry(...).State = EntityState.Modified / Entry(...).Property(...).IsModified = true
/// (todos geram SET de todas as colunas em grafos desanexados, inclusive email e evidencia do aceite).
/// Varredura sintatica (mesmo metodo de T-131, via RuntimeMigrationScanner); nada e executado.
/// </summary>
public class NoGenericUpdateTests
{
    private const string DesignRule = "UPDATE so das colunas alteradas de entidades rastreadas (UA1, ADR-0012)";

    private static readonly Regex AlwaysForbidden = new(
        @"\.\s*(UpdateRange|Attach|AttachRange)\s*(?:<[^<>;()]*>)?\s*\(");

    private static readonly Regex StateModified = new(@"\.\s*State\s*=\s*(?:\w+\.)*EntityState\s*\.\s*Modified\b");

    private static readonly Regex IsModifiedTrue = new(@"\.\s*IsModified\s*=\s*true\b");

    private static readonly Regex DbSetDeclaration = new(@"DbSet<[^<>]+>\s+(\w+)");

    // Receptores de Update(...) que sao o contexto ou um DbSet (evita falso positivo em FormatPreferences.Update etc.).
    private static readonly string[] ContextNames =
        ["context", "Context", "_context", "db", "_db", "dbContext", "ctx", "this", "base"];

    [Fact]
    public void T133_infrastructure_never_uses_generic_update_or_attach()
    {
        var files = InfrastructureFiles();
        var dbSets = files
            .SelectMany(f => DbSetDeclaration.Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value))
            .ToHashSet();

        var violations = files
            .SelectMany(f => Find(File.ReadAllText(f), dbSets).Select(v => $"{Relative(f)}:{v}"))
            .ToList();

        Assert.True(violations.Count == 0, $"{DesignRule}: {string.Join("; ", violations)}");
    }

    [Fact]
    public void T133_scan_covers_infrastructure_sources()
    {
        Assert.Contains(InfrastructureFiles(), f => f.EndsWith("EfPlayerRepository.cs", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("context.Players.Update(player);", "Update")]
    [InlineData("context.Set<Session>().Update(session);", "Update")]
    [InlineData("context.Update(player);", "Update")]
    [InlineData("Players.Update(player);", "Update")]
    [InlineData("context.Players.UpdateRange(players);", "UpdateRange")]
    [InlineData("context.Players.Attach(player);", "Attach")]
    [InlineData("context.AttachRange(players);", "AttachRange")]
    [InlineData("context.Entry(player).State = EntityState.Modified;", "State")]
    [InlineData("context.Entry(player).State = Microsoft.EntityFrameworkCore.EntityState.Modified;", "State")]
    [InlineData("context.Entry(player).Property(p => p.Email).IsModified = true;", "IsModified")]
    public void T133_detector_flags_generic_update_forms(string code, string member)
    {
        var found = Find(code, new HashSet<string> { "Players" });
        Assert.Contains(found, v => v.Contains(member, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("// context.Players.Update(player);")]
    [InlineData("var s = \"context.Players.Update(player)\";")]
    [InlineData("var r = FormatPreferences.Empty.Update(ids, ids);")]
    [InlineData("var r = Formats.Update(ids, active);")]
    [InlineData("context.Entry(player).State = EntityState.Detached;")]
    [InlineData("context.Entry(row).Property(p => p.X).IsModified = false;")]
    [InlineData("context.Players.Add(player);")]
    public void T133_detector_ignores_comments_strings_and_unrelated_updates(string code)
    {
        Assert.Empty(Find(code, new HashSet<string> { "Players" }));
    }

    private static List<string> Find(string source, ISet<string> dbSets)
    {
        var code = RuntimeMigrationScanner.StripCommentsAndLiterals(source);
        var found = new List<string>();

        Add(found, code, AlwaysForbidden, m => m.Groups[1].Value);
        Add(found, code, StateModified, _ => "State = EntityState.Modified");
        Add(found, code, IsModifiedTrue, _ => "IsModified = true");

        var receivers = string.Join('|', ContextNames.Concat(dbSets).Select(Regex.Escape));
        var update = new Regex($@"(?:\b(?:{receivers})|\bSet\s*<[^<>;()]*>\s*\(\s*\))\s*\.\s*Update\s*\(");
        Add(found, code, update, _ => "Update");
        return found;
    }

    private static void Add(List<string> found, string code, Regex pattern, Func<Match, string> label)
    {
        foreach (Match m in pattern.Matches(code))
            found.Add($"{LineOf(code, m.Index)} usa {label(m)}");
    }

    private static int LineOf(string text, int index) => text.Take(index).Count(c => c == '\n') + 1;

    private static List<string> InfrastructureFiles() =>
        SolutionLayout.WithSuffix(".Infrastructure").SelectMany(p => p.SourceFiles).ToList();

    private static string Relative(string file) =>
        Path.GetRelativePath(SolutionLayout.BackendDirectory, file).Replace('\\', '/');
}
