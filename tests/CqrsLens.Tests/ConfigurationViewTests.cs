using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// Phase 6 Stage 1 aspect K: the declared-vs-actual configuration and conventions
/// view. Declared rows come from the resolved-config carrier; the actual side states
/// the layer-model status and what the scan found.
/// </summary>
public sealed class ConfigurationViewTests : IDisposable
{
    private readonly string _root;

    public ConfigurationViewTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cqrslens-k-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private static AnalysisResult WithCarrier()
    {
        var result = new AnalysisResult { RootPath = "/tmp/p" };
        result.Configuration = new ConfigurationProvenance
        {
            ConfigFilePath = "/x/.cqrslens.json",
            DeclaredMarkers = new[] { "IQuery", "ICommand" },
            DeclaredHandlerMarkers = new[] { "ICommandHandler" },
            LayerOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["App"] = "Domain" },
            IncludeGlobs = new[] { "src/**" },
            ExcludeGlobs = new[] { "**/obj/**" },
            RuleOverrides = new Dictionary<string, CheckSeverity?>(StringComparer.OrdinalIgnoreCase)
            {
                ["SomeRule"] = CheckSeverity.Warning,
                ["OtherRule"] = null,
            },
        };
        return result;
    }

    [Fact]
    public void DeclaredRows_ComeFromTheCarrier_SortedByName()
    {
        var view = ConfigurationView.Build(WithCarrier());

        Assert.Equal("Config file", view.Declared[0].Label);
        Assert.Equal("/x/.cqrslens.json", view.Declared[0].Value);
        Assert.Equal("ICommand, IQuery", view.Declared.Single(r => r.Label == "Request markers").Value);
        Assert.Equal("ICommandHandler", view.Declared.Single(r => r.Label == "Handler markers").Value);
        Assert.Equal("App → Domain", view.Declared.Single(r => r.Label == "Layer overrides").Value);
        Assert.Equal("src/**", view.Declared.Single(r => r.Label == "Include globs").Value);
        Assert.Equal("**/obj/**", view.Declared.Single(r => r.Label == "Exclude globs").Value);
        Assert.Equal("2 declared (1 off, 1 warning)",
            view.Declared.Single(r => r.Label == "Rule severity overrides").Value);
    }

    [Fact]
    public void NoConfig_ShowsExplicitEmptyStates()
    {
        var view = ConfigurationView.Build(new AnalysisResult { RootPath = "/tmp/p" });

        Assert.Equal("No config file found", view.Declared.Single(r => r.Label == "Config file").Value);
        Assert.Equal("none declared", view.Declared.Single(r => r.Label == "Request markers").Value);
        Assert.Equal("none declared", view.Declared.Single(r => r.Label == "Handler markers").Value);
        Assert.Equal("none declared", view.Declared.Single(r => r.Label == "Layer overrides").Value);
        Assert.Equal("none declared", view.Declared.Single(r => r.Label == "Rule severity overrides").Value);
        Assert.Equal("none detected", view.Conventions);
    }

    [Theory]
    [InlineData(new[] { "Domain", "Application", "Infrastructure" }, false, "asserted")]
    [InlineData(new[] { "framework", "modules", "templates", "src" }, false, "name-guessed")]
    [InlineData(new[] { "framework", "modules" }, true, "configured")]
    public void LayerModelStatus_UsesTheExistingConfidenceRule(string[] layers, bool configured, string expected)
    {
        var result = new AnalysisResult { RootPath = "/tmp/p" };
        var i = 0;
        foreach (var layer in layers)
            result.Projects.Add(new ProjectInfo
            {
                Name = $"P{i++}",
                FilePath = $"/tmp/P{i}.csproj",
                DirectoryPath = $"/tmp/P{i}",
                Layer = layer,
            });
        result.HasConfiguredLayers = configured;

        var view = ConfigurationView.Build(result);

        Assert.Equal(expected, view.LayerStatus);
        Assert.Contains(expected == "name-guessed" ? "reported at warning severity" : "asserted as errors", view.LayerChecks);
    }

    [Fact]
    public void NameGuessedModel_MakesTheDowngradeLegibleWithFindingCounts()
    {
        var result = new AnalysisResult { RootPath = "/tmp/p" };
        result.Projects.Add(new ProjectInfo { Name = "framework", FilePath = "/tmp/f.csproj", DirectoryPath = "/tmp/f", Layer = "framework" });
        result.Projects.Add(new ProjectInfo { Name = "modules", FilePath = "/tmp/m.csproj", DirectoryPath = "/tmp/m", Layer = "modules" });
        result.Checks.Add(new CheckFinding(CheckSeverity.Warning, ArchitectureChecker.ApplicationLayerViolationCode, "m", Target: "A -> B"));
        for (var i = 0; i < 3; i++)
            result.Checks.Add(new CheckFinding(CheckSeverity.Warning, ArchitectureChecker.AmbiguousProjectNameCode, "m", Target: $"N{i}"));

        var view = ConfigurationView.Build(result);

        Assert.Equal("name-guessed", view.LayerStatus);
        Assert.Contains("reported at warning severity", view.LayerChecks);
        Assert.Contains("3 × AmbiguousProjectName (warning)", view.LayerChecks);
        Assert.Contains("1 × ApplicationLayerViolation (warning)", view.LayerChecks);
    }

    [Fact]
    public void CliScan_ResolvesAndRendersTheDeclaredConfiguration()
    {
        WriteProject("App");
        WriteFile("App/Features.cs", """
            namespace App.Features;
            public sealed record PingCommand : IRequest<int>;
            """);
        File.WriteAllText(Path.Combine(_root, ".cqrslens.json"), """
            {
              "markers": ["ICommand"],
              "handlerMarkers": ["ICommandHandler"],
              "layers": { "App": "Domain" },
              "include": ["App/**"],
              "exclude": ["**/obj/**"],
              "rules": { "MissingHandler": "error" }
            }
            """);

        var output = Path.Combine(_root, "ARCHITECTURE.md");
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var code = CliRunner.Run(new[] { _root, "-o", output, "--generated-at", "2026-06-01T00:00:00Z" }, stdout, stderr);

        Assert.Equal(ExitCodes.Success, code);
        var report = File.ReadAllText(output);
        Assert.Contains("## Configuration & Conventions", report);
        Assert.Contains(".cqrslens.json", report);
        Assert.Contains("ICommand", report);
        Assert.Contains("ICommandHandler", report);
        Assert.Contains("App → Domain", report);
        Assert.Contains("App/**", report);
        Assert.Contains("**/obj/**", report);
        Assert.Contains("1 declared (1 error)", report);
    }

    [Fact]
    public void MarkdownAndHtml_PresentTheSameDeclaredAndActualFacts()
    {
        var result = WithCarrier();
        result.Projects.Add(new ProjectInfo { Name = "framework", FilePath = "/tmp/f.csproj", DirectoryPath = "/tmp/f", Layer = "framework" });
        result.Projects.Add(new ProjectInfo { Name = "modules", FilePath = "/tmp/m.csproj", DirectoryPath = "/tmp/m", Layer = "modules" });

        var md = ReportGenerator.Generate(result);
        var html = HtmlReportGenerator.Generate(result, DiagramRenderMode.Svg);

        foreach (var output in new[] { md, html })
        {
            Assert.Contains("Configuration", output);
            Assert.Contains("/x/.cqrslens.json", output);
            Assert.Contains("ICommand, IQuery", output);
            Assert.Contains("App → Domain", output);
            Assert.Contains("name-guessed", output);
            Assert.Contains("reported at warning severity", output);
            Assert.DoesNotContain("health", output, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void WriteProject(string name)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, name + ".csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\">\n</Project>\n");
    }

    private void WriteFile(string relative, string code)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, code);
    }
}
