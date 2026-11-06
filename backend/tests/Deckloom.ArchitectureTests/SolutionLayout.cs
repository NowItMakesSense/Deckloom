using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Deckloom.ArchitectureTests;

/// <summary>Leitura estática de projetos e fontes do backend (sem executar nada).</summary>
internal sealed record SourceProject(string Name, string CsprojPath)
{
    public string Directory => Path.GetDirectoryName(CsprojPath)!;

    public IReadOnlyList<string> DirectProjectReferences =>
        Load().Descendants("ProjectReference")
            .Select(e => (string)e.Attribute("Include")!)
            .Select(i => Path.GetFileNameWithoutExtension(i.Replace('\\', '/')))
            .ToList();

    public IReadOnlyList<string> PackageReferences =>
        Load().Descendants("PackageReference")
            .Select(e => (string)e.Attribute("Include")!)
            .ToList();

    public IEnumerable<string> SourceFiles =>
        System.IO.Directory.EnumerateFiles(Directory, "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsBuildOutput(f));

    private XElement Load() => XDocument.Load(CsprojPath).Root!;

    private static bool IsBuildOutput(string path)
    {
        var parts = path.Replace('\\', '/').Split('/');
        return parts.Contains("bin") || parts.Contains("obj");
    }
}

internal static class SolutionLayout
{
    private static readonly Lazy<string> BackendRoot = new(FindBackendRoot);

    /// <summary>Todos os projetos de produção (backend/src).</summary>
    public static IReadOnlyList<SourceProject> ProductionProjects()
    {
        var src = Path.Combine(BackendRoot.Value, "src");
        return System.IO.Directory.EnumerateFiles(src, "*.csproj", SearchOption.AllDirectories)
            .Select(p => new SourceProject(Path.GetFileNameWithoutExtension(p), p))
            .ToList();
    }

    public static IReadOnlyList<SourceProject> WithSuffix(string suffix) =>
        ProductionProjects().Where(p => p.Name.EndsWith(suffix, StringComparison.Ordinal)).ToList();

    /// <summary>Serviço deployável separado (ADR-0018): projeto sob backend/src/Services.</summary>
    public static bool IsService(SourceProject project) =>
        project.CsprojPath.Replace('\\', '/').Contains("/src/Services/", StringComparison.Ordinal);

    /// <summary>Núcleo (ADR-0018): Deckloom.Api, Deckloom.SharedKernel e os projetos de src/Modules.</summary>
    public static IReadOnlyList<SourceProject> CoreProjects() =>
        ProductionProjects().Where(p => !IsService(p)).ToList();

    public static IReadOnlyList<SourceProject> ServiceProjects() =>
        ProductionProjects().Where(IsService).ToList();

    public static SourceProject Named(string name) =>
        ProductionProjects().Single(p => p.Name == name);

    /// <summary>Fecho transitivo das referências de projeto (por nome).</summary>
    public static IReadOnlySet<string> TransitiveReferences(SourceProject project)
    {
        var all = ProductionProjects().ToDictionary(p => p.Name);
        var seen = new HashSet<string>();
        var pending = new Queue<string>(project.DirectProjectReferences);
        while (pending.Count > 0)
        {
            var name = pending.Dequeue();
            if (!seen.Add(name)) continue;
            if (all.TryGetValue(name, out var next))
                foreach (var r in next.DirectProjectReferences) pending.Enqueue(r);
        }
        return seen;
    }

    public static string BackendDirectory => BackendRoot.Value;

    /// <summary>Directory.Build.props/.targets e Directory.Packages.props sob backend/ (valem para os projetos).</summary>
    public static IReadOnlyList<string> BuildWidePropsFiles() =>
        System.IO.Directory.EnumerateFiles(BackendRoot.Value, "Directory.*", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(f) is "Directory.Build.props" or "Directory.Build.targets" or "Directory.Packages.props")
            .Where(f => !f.Replace('\\', '/').Split('/').Any(part => part is "bin" or "obj"))
            .ToList();

    /// <summary>Código sem comentários, para não acusar menções em documentação.</summary>
    public static string ReadCode(string file) =>
        Regex.Replace(File.ReadAllText(file), @"//[^\r\n]*|/\*.*?\*/", string.Empty, RegexOptions.Singleline);

    private static string FindBackendRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Deckloom.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("backend/Deckloom.sln não encontrada.");
    }
}
