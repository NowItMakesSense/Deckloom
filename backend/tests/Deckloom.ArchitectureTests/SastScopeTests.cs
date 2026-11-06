namespace Deckloom.ArchitectureTests;

/// <summary>
/// Escopo do SAST (CodeQL, SEC-29/SEC-30): o build do CodeQL compila Deckloom.Api e, pela closure de referências,
/// todo projeto de produção do núcleo. Um módulo novo fora dessa closure ficaria sem análise. Verificação estática de .csproj.
/// Serviços de src/Services são deployáveis separados (ADR-0018) e ficam de fora da closure por desenho; o build
/// de SAST deles é responsabilidade do workflow de CI (T-P05).
/// </summary>
public class SastScopeTests
{
    private const string Api = "Deckloom.Api";

    [Fact]
    public void Every_production_project_under_backend_src_is_in_the_closure_of_deckloom_api()
    {
        var projects = SolutionLayout.ProductionProjects().Where(p => !SolutionLayout.IsService(p)).ToList();
        Assert.NotEmpty(projects);

        var closure = SolutionLayout.TransitiveReferences(SolutionLayout.Named(Api));
        var outside = projects
            .Where(p => p.Name != Api && !closure.Contains(p.Name))
            .Select(p => p.Name)
            .ToList();
        Assert.True(outside.Count == 0,
            $"Projetos de backend/src fora da closure de {Api} (ficariam sem SAST no CodeQL): {string.Join(", ", outside)}.");
    }
}
