using System.Text.RegularExpressions;

namespace Deckloom.ArchitectureTests;

/// <summary>
/// Regras de dependência e higiene de código (ADR-0001, ADR-0008, design.md "Layout do repositório").
/// Cada teste RuleN corresponde à regra de mesmo número em T-002. Verificação estática de .csproj e fontes.
/// Para estender (nova feature/módulo): adicione itens às listas de dados abaixo; os sufixos valem para todo módulo.
/// </summary>
public class ArchitectureRulesTests
{
    private const string Domain = ".Domain";
    private const string Application = ".Application";
    private const string Infrastructure = ".Infrastructure";
    private const string SharedKernel = "Deckloom.SharedKernel";
    private const string Api = "Deckloom.Api";

    private static readonly string[] DomainAllowedReferences = [SharedKernel];
    private static readonly string[] ApplicationAllowedReferences = [Domain, SharedKernel];
    private static readonly string[] InfrastructureAllowedReferences = [Application, Domain, SharedKernel];

    // Camadas internas: sufixos de projeto que nunca podem alcançar as camadas externas.
    private static readonly string[] InnerLayerSuffixes = [Domain, Application];
    private static readonly string[] OuterLayerMarkers = [Infrastructure, Api];

    // Projetos de teste/host que a API de produção não pode alcançar.
    private static readonly string[] TestOnlySuffixes = [".Testing", ".E2E.Host"];

    // Regras de higiene como dados: (descrição, regex proibida).
    private static readonly (string Description, Regex Pattern)[] ForbiddenInDomainAndApplication =
    [
        ("DateTime.Now/UtcNow (use TimeProvider)", new(@"\bDateTime\s*\.\s*(Utc)?Now\b")),
        ("DateTimeOffset.Now/UtcNow (use TimeProvider)", new(@"\bDateTimeOffset\s*\.\s*(Utc)?Now\b")),
        ("Thread.Sleep (use IDelayer)", new(@"\bThread\s*\.\s*Sleep\b")),
        ("Task.Delay (use IDelayer)", new(@"\bTask\s*\.\s*Delay\b")),
    ];

    private static readonly (string Description, Regex Pattern)[] ForbiddenInInfrastructure =
    [
        ("FromSqlRaw (use forma interpolada parametrizada)", new(@"\bFromSqlRaw\b")),
        ("ExecuteSqlRaw (use forma interpolada parametrizada)", new(@"\bExecuteSqlRaw(Async)?\b")),
        ("EnableSensitiveDataLogging (logs sem dados sensíveis)", new(@"\bEnableSensitiveDataLogging\b")),
    ];

    [Fact]
    public void Rule1_Domain_references_no_package_and_only_SharedKernel_project()
    {
        var domains = ExistingWithSuffix(Domain);
        var violations = new List<string>();
        foreach (var p in domains)
        {
            violations.AddRange(p.PackageReferences.Select(x => $"{p.Name} -> pacote {x}"));
            violations.AddRange(DisallowedProjectReferences(p, DomainAllowedReferences));
        }
        AssertNone(violations, "Regra 1 (ADR-0001): Domain não referencia nada além da BCL (e SharedKernel).");
    }

    [Fact]
    public void Rule1_SharedKernel_references_no_package_and_no_project()
    {
        var kernel = SolutionLayout.Named(SharedKernel);
        var violations = kernel.PackageReferences.Select(x => $"{kernel.Name} -> pacote {x}")
            .Concat(kernel.DirectProjectReferences.Select(x => $"{kernel.Name} -> projeto {x}"))
            .ToList();
        AssertNone(violations, "Regra 1 (ADR-0001, R-03): SharedKernel não referencia pacote nem projeto (só BCL).");
    }

    [Fact]
    public void Rule1_no_package_enters_through_build_props_or_global_package_reference()
    {
        var violations = new List<string>();
        foreach (var file in SolutionLayout.BuildWidePropsFiles())
        {
            var xml = System.Xml.Linq.XDocument.Load(file).Root!;
            foreach (var tag in new[] { "PackageReference", "GlobalPackageReference" })
                violations.AddRange(xml.Descendants(tag).Select(e =>
                    $"{Path.GetFileName(file)}: <{tag} Include=\"{(string?)e.Attribute("Include") ?? (string?)e.Attribute("Update")}\">"));
        }
        foreach (var project in SolutionLayout.ProductionProjects())
            violations.AddRange(System.Xml.Linq.XDocument.Load(project.CsprojPath).Descendants("GlobalPackageReference")
                .Select(_ => $"{project.Name}: <GlobalPackageReference>"));
        AssertNone(violations,
            "Regra 1 (ADR-0001, R-03): Directory.Build.props/Directory.Packages.props não injetam pacote em todos os projetos.");
    }

    [Theory]
    [InlineData("Deckloom.Accounts.Domain", new[] { "Deckloom.SharedKernel" })]
    [InlineData("Deckloom.SharedKernel", new string[0])]
    public void Rule1_assembly_references_only_BCL_and_allowed_Deckloom_assemblies(string assemblyName, string[] allowedDeckloom)
    {
        var referenced = System.Reflection.Assembly.Load(assemblyName).GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => n != "mscorlib" && n != "netstandard" && n != "System" && !n.StartsWith("System.", StringComparison.Ordinal))
            .Where(n => !allowedDeckloom.Contains(n))
            .ToList();
        AssertNone(referenced.Select(n => $"{assemblyName} -> assembly {n}").ToList(),
            "Regra 1 (ADR-0001, R-03): o assembly referencia só System.*/BCL e Deckloom.SharedKernel.");
    }

    [Fact]
    public void Rule2_Application_references_only_Domain_and_SharedKernel()
    {
        var violations = ExistingWithSuffix(Application)
            .SelectMany(p => DisallowedProjectReferences(p, ApplicationAllowedReferences)).ToList();
        AssertNone(violations, "Regra 2 (ADR-0001): Application referencia só Domain e SharedKernel.");
    }

    [Fact]
    public void Rule3_Infrastructure_references_only_Application_Domain_and_SharedKernel()
    {
        var violations = ExistingWithSuffix(Infrastructure)
            .SelectMany(p => DisallowedProjectReferences(p, InfrastructureAllowedReferences)).ToList();
        AssertNone(violations,
            "Regra 3 (ADR-0001): Infrastructure referencia só Application, Domain e SharedKernel (nunca Api).");
    }

    [Fact]
    public void Rule4_Domain_and_Application_never_reach_Infrastructure_or_Api_even_transitively()
    {
        // Pré-condição: as camadas externas existem; renomeá-las faria a regra passar em silêncio.
        Assert.NotEmpty(SolutionLayout.WithSuffix(Infrastructure));
        Assert.Contains(SolutionLayout.ProductionProjects(), p => p.Name == Api);

        var inner = InnerLayerSuffixes.SelectMany(ExistingWithSuffix).ToList();
        var violations = inner.SelectMany(p => SolutionLayout.TransitiveReferences(p)
                .Where(IsOuterLayer)
                .Select(r => $"{p.Name} -> {r}"))
            .ToList();
        AssertNone(violations, "Regra 4 (ADR-0001): Domain/Application não referenciam Infrastructure nem Api.");
    }

    [Fact]
    public void Rule5_Infrastructure_types_are_used_in_Api_only_inside_Composition_namespace()
    {
        Assert.NotEmpty(SolutionLayout.WithSuffix(Infrastructure));
        var api = SolutionLayout.Named(Api);
        var infraName = new Regex(@"\bDeckloom\.\w+\.Infrastructure\b");
        var composition = new Regex(@"\bnamespace\s+Deckloom\.Api\.Composition\b");
        var violations = new List<string>();
        foreach (var file in api.SourceFiles)
        {
            var code = SolutionLayout.ReadCode(file);
            if (infraName.IsMatch(code) && !composition.IsMatch(code))
                violations.Add($"{Path.GetRelativePath(api.Directory, file)} usa Infrastructure fora de Deckloom.Api.Composition");
        }
        AssertNone(violations,
            "Regra 5 (ADR-0001, ADR-0008): tipos de Infrastructure só no namespace Deckloom.Api.Composition.");
    }

    [Fact]
    public void Rule5_Api_has_no_global_using_of_Infrastructure_in_source_or_csproj()
    {
        var api = SolutionLayout.Named(Api);
        var globalUsing = new Regex(@"\bglobal\s+using\s+(static\s+)?([\w.]+\s*=\s*)?Deckloom\.\w+\.Infrastructure\b");
        var violations = api.SourceFiles
            .Where(f => globalUsing.IsMatch(SolutionLayout.ReadCode(f)))
            .Select(f => $"{Path.GetRelativePath(api.Directory, f)}: global using de Infrastructure")
            .ToList();

        var infraUsing = new Regex(@"Deckloom\.\w+\.Infrastructure\b");
        foreach (var file in new[] { api.CsprojPath }.Concat(SolutionLayout.BuildWidePropsFiles()))
            violations.AddRange(System.Xml.Linq.XDocument.Load(file).Descendants("Using")
                .Select(e => (string?)e.Attribute("Include") ?? (string?)e.Attribute("Update") ?? string.Empty)
                .Where(i => infraUsing.IsMatch(i))
                .Select(i => $"{Path.GetFileName(file)}: <Using Include=\"{i}\">"));
        AssertNone(violations,
            "Regra 5 (ADR-0001, ADR-0008, R-13): nenhum using global (código ou csproj) libera Infrastructure para toda a API.");
    }

    [Fact]
    public void Rule6_Domain_and_Application_do_not_use_clock_or_blocking_calls()
    {
        var projects = InnerLayerSuffixes.SelectMany(ExistingWithSuffix).ToList();
        var violations = FindForbiddenCalls(projects, ForbiddenInDomainAndApplication);
        AssertNone(violations, "Regra 6 (test-strategy): use TimeProvider/IDelayer em Domain e Application.");
    }

    [Fact]
    public void Rule7_Infrastructure_does_not_use_raw_sql_or_sensitive_data_logging()
    {
        var violations = FindForbiddenCalls(ExistingWithSuffix(Infrastructure), ForbiddenInInfrastructure);
        AssertNone(violations,
            "Regra 7 (threat model: injeção de SQL, logs sem dados sensíveis; security-policy).");
    }

    [Fact]
    public void Rule8_production_Api_does_not_reference_Testing_or_E2E_Host_projects()
    {
        var violations = SolutionLayout.TransitiveReferences(SolutionLayout.Named(Api))
            .Where(r => TestOnlySuffixes.Any(s => r.EndsWith(s, StringComparison.Ordinal)))
            .Select(r => $"{Api} -> {r}")
            .ToList();
        AssertNone(violations,
            "Regra 8 (tasks.md, decisão 5 da fase 4): fakes só chegam à API pelo host de teste e pela WebApplicationFactory.");
    }

    private static List<SourceProject> ExistingWithSuffix(string suffix)
    {
        var projects = SolutionLayout.WithSuffix(suffix);
        Assert.True(projects.Count > 0, $"Nenhum projeto de produção com sufixo '{suffix}': a regra passaria em silêncio.");
        return projects.ToList();
    }

    private static bool IsOuterLayer(string name) =>
        OuterLayerMarkers.Any(m => m.StartsWith('.')
            ? name.EndsWith(m, StringComparison.Ordinal)
            : name == m);

    private static IEnumerable<string> DisallowedProjectReferences(SourceProject project, string[] allowed) =>
        project.DirectProjectReferences
            .Where(r => !allowed.Any(a => a.StartsWith('.')
                ? r.EndsWith(a, StringComparison.Ordinal)
                : r == a))
            .Select(r => $"{project.Name} -> projeto {r}");

    private static List<string> FindForbiddenCalls(
        IEnumerable<SourceProject> projects, (string Description, Regex Pattern)[] rules)
    {
        var violations = new List<string>();
        foreach (var project in projects)
            foreach (var file in project.SourceFiles)
            {
                var code = SolutionLayout.ReadCode(file);
                foreach (var (description, pattern) in rules)
                    if (pattern.IsMatch(code))
                        violations.Add($"{project.Name}/{Path.GetRelativePath(project.Directory, file)}: {description}");
            }
        return violations;
    }

    private static void AssertNone(List<string> violations, string rule) =>
        Assert.True(violations.Count == 0,
            $"{rule}{Environment.NewLine}  {string.Join($"{Environment.NewLine}  ", violations)}");
}
