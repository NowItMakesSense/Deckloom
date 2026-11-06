using System.Text.RegularExpressions;

namespace Deckloom.ArchitectureTests;

// T-162 (RED, R-47): os motivos do evento 1010 emitidos pela Application sao constantes nomeadas (nao literais em
// HandleGoogleCallback) e o catalogo da Infrastructure nao guarda constante sem uso.
public class GoogleFailureReasonHygieneTests
{
    private static string SourceOf(string project, string fileName) =>
        SolutionLayout.Named(project).SourceFiles.Single(f => Path.GetFileName(f) == fileName);

    [Theory]
    [InlineData("session_invalid")]
    [InlineData("subject_mismatch")]
    [InlineData("provider_error")]
    [InlineData("unverified_email")]
    [InlineData("cancelled")]
    [InlineData("state_mismatch")]
    public void R47_HandleGoogleCallback_has_no_literal_for_the_1010_reason(string reason)
    {
        var code = SolutionLayout.ReadCode(SourceOf("Deckloom.Accounts.Application", "HandleGoogleCallback.cs"));

        Assert.DoesNotContain($"\"{reason}\"", code);
    }

    [Fact]
    public void R47_GoogleAuthFailureLogNames_declares_no_member_unused_by_production_code()
    {
        var catalogFile = SourceOf("Deckloom.Accounts.Infrastructure", "GoogleAuthFailureLogNames.cs");
        var declared = Regex.Matches(SolutionLayout.ReadCode(catalogFile), @"\b(?:const|static)\s+[\w<>?\[\]]+\s+(\w+)\s*[=(]")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();
        Assert.Contains("For", declared);

        var others = SolutionLayout.ProductionProjects()
            .SelectMany(p => p.SourceFiles)
            .Where(f => f != catalogFile)
            .Select(SolutionLayout.ReadCode)
            .ToList();
        var unused = declared
            .Where(name => name != "For")
            .Where(name => !others.Any(code => Regex.IsMatch(code, $@"GoogleAuthFailureLogNames\.{name}\b")))
            .ToList();

        Assert.Empty(unused);
    }
}
