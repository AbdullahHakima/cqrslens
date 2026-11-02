using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// Item B: the CQRS map split mode option. Default stays byte-identical (Count);
/// the new modes group by feature or emit one unsplit diagram per project.
/// </summary>
public sealed class ItemBCqrsMapSplitTests : IDisposable
{
    private readonly string _root;

    public ItemBCqrsMapSplitTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cqrslens-itemb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private static AnalysisResult MultiProjectResult(int projectARequests, int projectBRequests = 0)
    {
        var result = new AnalysisResult { RootPath = "/tmp/shop" };

        void AddRequests(string project, string ns, int count)
        {
            for (var i = 0; i < count; i++)
            {
                var name = $"Command{i:000}";
                var request = new CqrsRequest { Name = name, Namespace = ns, ProjectName = project };
                request.Handlers.Add($"{ns}.{name}Handler");
                request.Validators.Add($"{ns}.{name}Validator");
                result.Requests.Add(request);
                result.Handlers.Add(new HandlerInfo
                {
                    Name = $"{name}Handler",
                    Namespace = ns,
                    RequestName = name,
                    ProjectName = project,
                });
            }
        }

        result.Projects.Add(new ProjectInfo
        {
            Name = "App.Large",
            FilePath = "/tmp/App.Large/App.Large.csproj",
            DirectoryPath = "/tmp/App.Large",
            Layer = "Application",
        });
        AddRequests("App.Large", "App.Large.Orders", projectARequests);

        if (projectBRequests > 0)
        {
            result.Projects.Add(new ProjectInfo
            {
                Name = "App.Small",
                FilePath = "/tmp/App.Small/App.Small.csproj",
                DirectoryPath = "/tmp/App.Small",
                Layer = "Application",
            });
            AddRequests("App.Small", "App.Small.Billing", projectBRequests);
        }

        return result;
    }

    [Fact]
    public void DefaultMode_IsCount_AndMatchesTheExistingOutput()
    {
        var result = MultiProjectResult(projectARequests: 20, projectBRequests: 3);

        var defaultSections = DiagramBuilder.CqrsMapSections(result);
        var countSections = DiagramBuilder.CqrsMapSections(result, CqrsMapSplitMode.Count);

        Assert.Equal(
            countSections.Select(s => s.Source),
            defaultSections.Select(s => s.Source));

        // The default report entry points are the Count mode.
        Assert.Equal(ReportGenerator.Generate(result, CqrsMapSplitMode.Count), ReportGenerator.Generate(result));
        Assert.Equal(
            HtmlReportGenerator.Generate(result, DiagramRenderMode.Svg, CqrsMapSplitMode.Count),
            HtmlReportGenerator.Generate(result, DiagramRenderMode.Svg));
    }

    [Fact]
    public void UnsplitMode_EmitsOneSectionPerProject_AndKeepsEveryRequest()
    {
        var result = MultiProjectResult(projectARequests: 20, projectBRequests: 3);

        var unsplit = DiagramBuilder.CqrsMapSections(result, CqrsMapSplitMode.Unsplit);
        var count = DiagramBuilder.CqrsMapSections(result, CqrsMapSplitMode.Count);

        // One section per project (2), versus the size-split default (3).
        Assert.Equal(2, unsplit.Count);
        Assert.True(count.Count > unsplit.Count);

        // Every request appears exactly once, and all nodes are inside the limits.
        Assert.Equal(result.Requests.Count, unsplit.Sum(s => s.Requests.Count));
        Assert.Equal(
            result.Requests.Select(r => r.FullName).OrderBy(n => n, StringComparer.Ordinal),
            unsplit.SelectMany(s => s.Requests).Select(r => r.FullName).OrderBy(n => n, StringComparer.Ordinal));
        Assert.All(unsplit, s => Assert.True(DiagramBuilder.IsWithinMermaidLimits(s.Source)));

        // Handlers are preserved: each request's handler node appears in its section.
        foreach (var section in unsplit)
            foreach (var request in section.Requests)
                Assert.Contains(MermaidDiagramText.ShortTypeName(request.Handlers.Single()), section.Source);
    }

    [Fact]
    public void UnsplitMode_StillBoundsAProjectWhoseMapWouldBeHuge()
    {
        // 250 requests x (handler + validator) = 500 edges > the 400-edge limit.
        var result = MultiProjectResult(projectARequests: 250);

        var unsplit = DiagramBuilder.CqrsMapSections(result, CqrsMapSplitMode.Unsplit);

        Assert.True(unsplit.Count >= 2, "Huge map must still be bounded into parts.");
        Assert.All(unsplit, s => Assert.True(DiagramBuilder.IsWithinMermaidLimits(s.Source)));
        Assert.Equal(result.Requests.Count, unsplit.Sum(s => s.Requests.Count));
    }

    [Fact]
    public void FeatureSplitMode_GroupsByFeatureAndKeepsEveryRequest()
    {
        var result = new AnalysisResult { RootPath = "/tmp/shop" };
        foreach (var (name, feature) in new[]
                 {
                     ("AlphaCreate", "App.Features.Alpha"),
                     ("AlphaGet", "App.Features.Alpha"),
                     ("BetaCreate", "App.Features.Beta"),
                 })
        {
            var request = new CqrsRequest { Name = name, Namespace = feature, ProjectName = "App" };
            request.Handlers.Add($"{feature}.{name}Handler");
            result.Requests.Add(request);
        }
        result.Projects.Add(new ProjectInfo
        {
            Name = "App",
            FilePath = "/tmp/App/App.csproj",
            DirectoryPath = "/tmp/App",
            Layer = "Application",
        });

        var sections = DiagramBuilder.CqrsMapSections(result, CqrsMapSplitMode.Feature);

        Assert.Equal(2, sections.Count);
        Assert.Equal(result.Requests.Count, sections.Sum(s => s.Requests.Count));
        Assert.Contains(sections, s => s.Heading.Contains("Alpha", StringComparison.Ordinal));
        Assert.Contains(sections, s => s.Heading.Contains("Beta", StringComparison.Ordinal));
    }

    [Fact]
    public void FlagWinsOverConfig_WhenBothSpecifyTheSplitMode()
    {
        WriteProject("App");
        WriteProject("Lib");
        File.WriteAllText(Path.Combine(_root, ".cqrslens.json"), """
            { "$schema": "x", "cqrsMapSplit": "unsplit" }
            """);

        // Config only.
        var fromConfig = ScanPreparer.Prepare(new CliOptions { Path = _root });
        Assert.Equal(CqrsMapSplitMode.Unsplit, fromConfig.CqrsMapSplit);

        // CLI flag overrides the config key.
        var fromFlag = ScanPreparer.Prepare(new CliOptions
        {
            Path = _root,
            CqrsMapSplit = CqrsMapSplitMode.Feature,
            CqrsMapSplitSpecified = true,
        });
        Assert.Equal(CqrsMapSplitMode.Feature, fromFlag.CqrsMapSplit);
    }

    [Fact]
    public void NoConfigOrFlag_DefaultsToCount()
    {
        WriteProject("App");
        var scan = ScanPreparer.Prepare(new CliOptions { Path = _root });
        Assert.Equal(CqrsMapSplitMode.Count, scan.CqrsMapSplit);
    }

    [Fact]
    public void Parser_ReadsTheSplitMode_AndRejectsUnknownValues()
    {
        var parsed = CliParser.Parse(new[] { "src", "--cqrs-map-split", "feature" });
        var options = Assert.IsType<CliOptions>(parsed.Options);
        Assert.Equal(CqrsMapSplitMode.Feature, options.CqrsMapSplit);
        Assert.True(options.CqrsMapSplitSpecified);

        var bad = CliParser.Parse(new[] { "src", "--cqrs-map-split", "sideways" });
        Assert.Equal(CliCommandKind.Error, bad.Kind);
        Assert.Contains("cqrs-map-split", bad.Error);
    }

    [Fact]
    public void Config_RejectsUnknownSplitMode()
    {
        WriteProject("App");
        File.WriteAllText(Path.Combine(_root, ".cqrslens.json"), """
            { "$schema": "x", "cqrsMapSplit": "sideways" }
            """);

        Assert.Throws<ConfigurationException>(() => ScanPreparer.Prepare(new CliOptions { Path = _root }));
    }

    private void WriteProject(string name)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, name + ".csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n</Project>\n");
    }
}
