using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// Guards the dependency direction of the production projects. The architecture
/// is Core &lt;- Analysis &lt;- Reporting &lt;- CLI: an inner project must never
/// reference an outer one, and the compiled reference graphs are checked too so
/// an accidental transitive dependency cannot slip in unnoticed.
/// </summary>
public sealed class ArchitectureBoundaryTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public void ProductionProjects_ReferenceOnlyInwardLayers()
    {
        var expected = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["CqrsLens.Core"] = [],
            ["CqrsLens.Analysis"] = ["CqrsLens.Core"],
            ["CqrsLens.Reporting"] = ["CqrsLens.Core", "CqrsLens.Analysis"],
            ["CqrsLens.Cli"] = ["CqrsLens.Core", "CqrsLens.Analysis", "CqrsLens.Reporting"],
        };

        foreach (var (project, allowed) in expected)
        {
            var references = ProjectReferences(project);
            Assert.Equal(
                allowed.OrderBy(name => name, StringComparer.Ordinal),
                references.OrderBy(name => name, StringComparer.Ordinal));
        }
    }

    [Fact]
    public void CompiledAssemblies_DoNotReferenceOuterLayers()
    {
        AssertNoCqrsLensReferences(typeof(ToolInfo).Assembly);

        var analysisReferences = AssemblyNames(typeof(Analyzer).Assembly);
        Assert.Contains("CqrsLens.Core", analysisReferences);
        Assert.DoesNotContain("CqrsLens.Reporting", analysisReferences);
        Assert.DoesNotContain("cqrslens", analysisReferences);

        var reportingReferences = AssemblyNames(typeof(ReportGenerator).Assembly);
        Assert.Contains("CqrsLens.Core", reportingReferences);
        Assert.Contains("CqrsLens.Analysis", reportingReferences);
        Assert.DoesNotContain("cqrslens", reportingReferences);

        var cliReferences = AssemblyNames(typeof(CliRunner).Assembly);
        Assert.Contains("CqrsLens.Core", cliReferences);
        Assert.Contains("CqrsLens.Analysis", cliReferences);
        Assert.Contains("CqrsLens.Reporting", cliReferences);
    }

    [Fact]
    public void CliProject_KeepsToolPackagingIdentity()
    {
        var project = LoadProject("CqrsLens.Cli");
        var properties = project.Descendants("PropertyGroup").Elements().ToDictionary(
            element => element.Name.LocalName,
            element => element.Value,
            StringComparer.Ordinal);

        Assert.Equal("cqrslens", properties["AssemblyName"]);
        Assert.Equal("true", properties["PackAsTool"]);
        Assert.Equal("cqrslens", properties["ToolCommandName"]);
        Assert.Equal("CqrsLens", properties["PackageId"]);
        Assert.Equal("README.md", properties["PackageReadmeFile"]);
        Assert.Equal("LatestMajor", properties["RollForward"]);

        var readme = project.Descendants("None")
            .Single(element => element.Attribute("Pack")?.Value == "true");
        Assert.Equal(@"..\..\README.md", readme.Attribute("Include")?.Value);
        Assert.Equal(@"\", readme.Attribute("PackagePath")?.Value);
    }

    [Fact]
    public void CoreSourceFiles_DeclareOneTypeEach()
    {
        var coreRoot = Path.Combine(Root, "src", "CqrsLens.Core");
        var offenders = Directory
            .EnumerateFiles(coreRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .Select(path => new
            {
                Path = path,
                Types = TopLevelTypeDeclarations(File.ReadAllText(path)),
            })
            .Where(file => file.Types > 1)
            .Select(file => $"{Path.GetRelativePath(Root, file.Path)} ({file.Types} types)")
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void TestAndBenchmarkProjects_AreNotReferencedByProductionProjects()
    {
        foreach (var project in new[] { "CqrsLens.Core", "CqrsLens.Analysis", "CqrsLens.Reporting", "CqrsLens.Cli" })
        {
            var references = ProjectReferences(project);
            Assert.DoesNotContain("CqrsLens.Tests", references);
            Assert.DoesNotContain("CqrsLens.Benchmarks", references);
        }
    }

    private static int TopLevelTypeDeclarations(string source)
    {
        var matches = Regex.Matches(
            source,
            @"^(?:public|internal)\s+(?:(?:sealed|static|abstract|readonly|partial)\s+)*(?:class|record|struct|enum|interface)\s+",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);
        return matches.Count;
    }

    private static IReadOnlyList<string> ProjectReferences(string project)
    {
        var document = LoadProject(project);
        return document.Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Path.GetFileNameWithoutExtension(value!.Replace('\\', '/')))
            .ToList();
    }

    private static XDocument LoadProject(string project)
    {
        var path = Path.Combine(Root, "src", project, project + ".csproj");
        Assert.True(File.Exists(path), $"missing project file: {path}");
        return XDocument.Load(path);
    }

    private static IReadOnlyList<string> AssemblyNames(Assembly assembly)
        => assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToList();

    private static void AssertNoCqrsLensReferences(Assembly assembly)
    {
        var references = AssemblyNames(assembly);
        Assert.DoesNotContain(references, name =>
            name.StartsWith("CqrsLens.", StringComparison.Ordinal) ||
            name == "cqrslens");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CqrsLens.sln")))
                return directory.FullName;
        }

        throw new InvalidOperationException("Could not find CqrsLens.sln above the test output directory.");
    }
}
