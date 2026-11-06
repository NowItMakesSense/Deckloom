using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Deckloom.ArchitectureTests;

/// <summary>
/// Ferramentas de cobertura exigidas pelo comando do design ("Cobertura": --collect:"XPlat Code Coverage" + vitest --coverage,
/// limite 80%) e pelo DoD-P (R-06). Verificação estática de arquivos.
/// </summary>
public partial class CoverageToolingTests
{
    private const string BackendCollector = "coverlet.collector";
    private const string FrontendCoveragePackage = "@vitest/coverage-v8";
    private const int CoverageThreshold = 80;

    [Fact]
    public void R06_central_package_versions_pin_coverlet_collector_to_an_exact_version()
    {
        var props = XDocument.Load(Path.Combine(SolutionLayout.BackendDirectory, "Directory.Packages.props"));
        var version = props.Descendants("PackageVersion")
            .Where(e => string.Equals((string?)e.Attribute("Include"), BackendCollector, StringComparison.OrdinalIgnoreCase))
            .Select(e => (string?)e.Attribute("Version"))
            .SingleOrDefault();
        Assert.True(version is not null, $"Directory.Packages.props sem <PackageVersion Include=\"{BackendCollector}\">.");
        Assert.Matches(ExactVersion(), version);
    }

    [Fact]
    public void R06_every_test_project_references_coverlet_collector()
    {
        var testProjects = Directory.EnumerateFiles(Path.Combine(SolutionLayout.BackendDirectory, "tests"), "*.csproj", SearchOption.AllDirectories)
            .Where(f => XDocument.Load(f).Descendants("PackageReference")
                .Any(e => (string?)e.Attribute("Include") == "Microsoft.NET.Test.Sdk"))
            .ToList();
        Assert.NotEmpty(testProjects);

        var missing = testProjects
            .Where(f => !XDocument.Load(f).Descendants("PackageReference")
                .Any(e => string.Equals((string?)e.Attribute("Include"), BackendCollector, StringComparison.OrdinalIgnoreCase)))
            .Select(Path.GetFileNameWithoutExtension)
            .ToList();
        Assert.True(missing.Count == 0, $"Projetos de teste sem {BackendCollector}: {string.Join(", ", missing)}");
    }

    [Fact]
    public void R06_frontend_pins_vitest_coverage_v8_to_the_exact_version_of_vitest()
    {
        using var package = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoHygieneTests.RepoRoot(), "frontend", "package.json")));
        var dev = package.RootElement.GetProperty("devDependencies");
        Assert.True(dev.TryGetProperty("vitest", out var vitest), "frontend/package.json sem vitest.");
        Assert.True(dev.TryGetProperty("@vitest/coverage-v8", out var coverage),
            $"frontend/package.json sem {FrontendCoveragePackage} em devDependencies.");
        Assert.Matches(ExactVersion(), coverage.GetString());
        Assert.Equal(vitest.GetString(), coverage.GetString());
    }

    [Fact]
    public void R06_vitest_config_enforces_the_coverage_threshold()
    {
        var config = File.ReadAllText(Path.Combine(RepoHygieneTests.RepoRoot(), "frontend", "vite.config.ts"));
        var thresholds = ThresholdsBlock().Match(config);
        Assert.True(thresholds.Success, "frontend/vite.config.ts sem bloco coverage.thresholds.");
        var values = Regex.Matches(thresholds.Groups[1].Value, @"\b(lines|statements|functions|branches)\s*:\s*(\d+)")
            .Select(m => int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        Assert.NotEmpty(values);
        Assert.All(values, v => Assert.True(v >= CoverageThreshold, $"Limite de cobertura {v} < {CoverageThreshold}."));
    }

    [GeneratedRegex(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$")]
    private static partial Regex ExactVersion();

    [GeneratedRegex(@"thresholds\s*:\s*\{([^}]*)\}")]
    private static partial Regex ThresholdsBlock();
}
