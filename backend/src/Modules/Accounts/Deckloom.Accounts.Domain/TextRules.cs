using System.Globalization;
using System.Text;

namespace Deckloom.Accounts.Domain;

internal static class TextRules
{
    public static bool ContainsControlCharacter(string input) =>
        input.Any(char.IsControl);

    public static bool ContainsControlCharacterExceptLineFeed(string input) =>
        input.Any(c => c != '\n' && char.IsControl(c));

    public static string NormalizeLineBreaks(string input) =>
        input.Replace("\r\n", "\n", StringComparison.Ordinal);

    public static string NormalizeAndTrim(string input) =>
        input.Normalize(NormalizationForm.FormC).Trim();

    public static int LengthInTextElements(string input) =>
        new StringInfo(input).LengthInTextElements;
}
