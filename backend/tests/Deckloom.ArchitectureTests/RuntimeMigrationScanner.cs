using System.Text;
using System.Text.RegularExpressions;

namespace Deckloom.ArchitectureTests;

internal sealed record MemberUse(string Member, int Line);

/// <summary>
/// Detector sintático de T-131: acha invocações de membros de migração/DDL ignorando comentários e literais.
/// Fonte única das listas proibidas.
/// </summary>
internal static class RuntimeMigrationScanner
{
    public static readonly IReadOnlyList<string> PendingMigrationMembers =
        ["GetPendingMigrations", "GetPendingMigrationsAsync"];

    public static readonly IReadOnlyList<string> ForbiddenMembers =
    [
        "Migrate", "MigrateAsync",
        "EnsureCreated", "EnsureCreatedAsync",
        "EnsureDeleted", "EnsureDeletedAsync",
        .. PendingMigrationMembers,
    ];

    /// <summary>Invocações <c>.Nome(</c> (ou <c>.Nome&lt;T&gt;(</c>) de qualquer membro proibido.</summary>
    public static IReadOnlyList<MemberUse> FindInvocations(string source) =>
        Find(source, ForbiddenMembers, invocationOnly: true);

    /// <summary>Qualquer acesso <c>.Nome</c> (inclusive grupo de métodos) aos membros indicados.</summary>
    public static IReadOnlyList<MemberUse> FindReferences(string source, IReadOnlyList<string> members) =>
        Find(source, members, invocationOnly: false);

    private static List<MemberUse> Find(string source, IReadOnlyList<string> members, bool invocationOnly)
    {
        var code = StripCommentsAndLiterals(source);
        var names = string.Join('|', members);
        var tail = invocationOnly ? @"\s*(?:<[^<>;()]*>)?\s*\(" : @"\b";
        var pattern = new Regex($@"\.\s*({names}){tail}");
        return pattern.Matches(code)
            .Select(m => new MemberUse(m.Groups[1].Value, LineOf(code, m.Groups[1].Index)))
            .ToList();
    }

    private static int LineOf(string text, int index)
    {
        var line = 1;
        for (var i = 0; i < index; i++)
            if (text[i] == '\n') line++;
        return line;
    }

    /// <summary>
    /// Troca comentários e conteúdo de literais por espaços (quebras de linha preservadas, então as linhas não
    /// mudam). Buracos de interpolação ($"{...}") continuam sendo código.
    /// </summary>
    internal static string StripCommentsAndLiterals(string s)
    {
        var o = new StringBuilder(s.Length);
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            var next = i + 1 < s.Length ? s[i + 1] : '\0';
            if (c == '/' && next == '/') i = Blank(s, o, i, UntilEndOfLine(s, i));
            else if (c == '/' && next == '*') i = Blank(s, o, i, UntilBlockCommentEnd(s, i));
            else if (c == '\'') i = SkipChar(s, o, i);
            else if (IsStringStart(s, i, out var info)) i = ScanString(s, o, i, info);
            else { o.Append(c); i++; }
        }
        return o.ToString();
    }

    private readonly record struct StringInfo(int PrefixLength, bool Verbatim, bool Interpolated, int RawQuotes);

    private static bool IsStringStart(string s, int i, out StringInfo info)
    {
        info = default;
        if (i > 0 && (char.IsLetterOrDigit(s[i - 1]) || s[i - 1] == '_')) return false;
        var j = i;
        var interpolated = false;
        var verbatim = false;
        while (j < s.Length && (s[j] == '$' || s[j] == '@'))
        {
            if (s[j] == '$') interpolated = true; else verbatim = true;
            j++;
        }
        if (j >= s.Length || s[j] != '"') return false;
        var quotes = 0;
        while (j + quotes < s.Length && s[j + quotes] == '"') quotes++;
        var raw = quotes >= 3 && !verbatim ? quotes : 0;
        info = new StringInfo(j - i, verbatim, interpolated, raw);
        return true;
    }

    private static int ScanString(string s, StringBuilder o, int start, StringInfo info)
    {
        var i = start;
        for (var k = 0; k < info.PrefixLength; k++) o.Append(' ');
        i += info.PrefixLength;

        if (info.RawQuotes > 0)
        {
            var closing = new string('"', info.RawQuotes);
            var end = s.IndexOf(closing, i + info.RawQuotes, StringComparison.Ordinal);
            end = end < 0 ? s.Length : end + info.RawQuotes;
            return Blank(s, o, i, end);
        }

        o.Append(' ');
        i++;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == '"')
            {
                if (info.Verbatim && i + 1 < s.Length && s[i + 1] == '"') { o.Append("  "); i += 2; continue; }
                o.Append(' ');
                return i + 1;
            }
            if (c == '\\' && !info.Verbatim && i + 1 < s.Length) { o.Append(' ').Append(Keep(s[i + 1])); i += 2; continue; }
            if (info.Interpolated && c == '{')
            {
                if (i + 1 < s.Length && s[i + 1] == '{') { o.Append("  "); i += 2; continue; }
                i = CopyHole(s, o, i);
                continue;
            }
            if (info.Interpolated && c == '}' && i + 1 < s.Length && s[i + 1] == '}') { o.Append("  "); i += 2; continue; }
            o.Append(Keep(c));
            i++;
        }
        return i;
    }

    /// <summary>Copia o buraco de interpolação como código, até a chave correspondente.</summary>
    private static int CopyHole(string s, StringBuilder o, int open)
    {
        var depth = 0;
        var i = open;
        while (i < s.Length)
        {
            if (s[i] == '{') depth++;
            else if (s[i] == '}' && --depth == 0) { o.Append(' '); return i + 1; }
            o.Append(depth == 1 && i == open ? ' ' : s[i]);
            i++;
        }
        return i;
    }

    private static int SkipChar(string s, StringBuilder o, int start)
    {
        var i = start + 1;
        while (i < s.Length && s[i] != '\'' && s[i] != '\n')
            i += s[i] == '\\' ? 2 : 1;
        i = Math.Min(i + 1, s.Length);
        return Blank(s, o, start, i);
    }

    private static int UntilEndOfLine(string s, int i)
    {
        var end = s.IndexOf('\n', i);
        return end < 0 ? s.Length : end;
    }

    private static int UntilBlockCommentEnd(string s, int i)
    {
        var end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
        return end < 0 ? s.Length : end + 2;
    }

    private static int Blank(string s, StringBuilder o, int from, int to)
    {
        for (var k = from; k < to; k++) o.Append(Keep(s[k]));
        return to;
    }

    private static char Keep(char c) => c is '\n' or '\r' ? c : ' ';
}
