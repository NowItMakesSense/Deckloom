namespace Deckloom.ArchitectureTests;

/// <summary>
/// Isolamento entre o núcleo modular e os serviços de carga própria (ADR-0018): nenhum lado referencia o outro,
/// direta ou transitivamente. Verificação estática de .csproj.
/// </summary>
public class ServiceIsolationTests
{
    [Fact]
    public void Layout_has_core_projects_and_the_three_services()
    {
        var services = SolutionLayout.ServiceProjects().Select(p => p.Name).ToList();
        Assert.Contains("Deckloom.SuggestionService", services);
        Assert.Contains("Deckloom.MetaIngestionService", services);
        Assert.Contains("Deckloom.NotificationService", services);

        var core = SolutionLayout.CoreProjects().Select(p => p.Name).ToList();
        Assert.Contains("Deckloom.Api", core);
        Assert.Contains("Deckloom.SharedKernel", core);
        Assert.Contains(core, n => n.EndsWith(".Domain", StringComparison.Ordinal));
    }

    [Fact]
    public void Core_never_references_a_service_project()
    {
        var serviceNames = SolutionLayout.ServiceProjects().Select(p => p.Name).ToHashSet();
        var violations = SolutionLayout.CoreProjects()
            .SelectMany(p => SolutionLayout.TransitiveReferences(p)
                .Where(serviceNames.Contains)
                .Select(r => $"{p.Name} -> {r}"))
            .ToList();
        AssertNone(violations, "ADR-0018: o núcleo (Api, SharedKernel, Modules) não referencia projetos de src/Services.");
    }

    [Fact]
    public void Services_never_reference_a_core_project()
    {
        var coreNames = SolutionLayout.CoreProjects().Select(p => p.Name).ToHashSet();
        var violations = SolutionLayout.ServiceProjects()
            .SelectMany(p => SolutionLayout.TransitiveReferences(p)
                .Where(coreNames.Contains)
                .Select(r => $"{p.Name} -> {r}"))
            .ToList();
        AssertNone(violations, "ADR-0018: serviços de src/Services não referenciam projetos do núcleo.");
    }

    private static void AssertNone(List<string> violations, string rule) =>
        Assert.True(violations.Count == 0,
            $"{rule}{Environment.NewLine}  {string.Join($"{Environment.NewLine}  ", violations)}");
}
