using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// Integration fixture with comments/strings containing fake declarations, two
/// namespaces, indirect custom handlers, several DbContexts and project names
/// that collide after Mermaid id sanitization.
/// </summary>
public class AdversarialFixtureTests
{
    [Fact]
    public void TextAndRoslyn_DetectTheSameShapesWithoutDiagnostics()
    {
        var root = FixtureRoot();
        var text = Analyzer.Analyze(root);
        var roslyn = Analyzer.Analyze(root, AnalysisMode.Roslyn);

        foreach (var result in new[] { text, roslyn })
        {
            Assert.Equal(2, result.Projects.Count);
            Assert.Equal(2, result.Requests.Count);
            Assert.Equal(2, result.Handlers.Count);
            Assert.Equal(2, result.Validators.Count);
            Assert.Single(result.Behaviors);
            Assert.Equal(2, result.DbContexts.Count);
            Assert.True(result.CanRunStrictChecks);
            Assert.Empty(result.Diagnostics);
        }

        var textCreate = Assert.Single(
            text.Requests,
            r => r.Name == "CreateWidgetCommand");
        var textArchive = Assert.Single(
            text.Requests,
            r => r.Name == "ArchiveWidgetCommand");
        Assert.Single(textCreate.Handlers);
        Assert.Single(textArchive.Handlers);

        // Noise inside comments, ordinary strings and raw string literals is ignored.
        Assert.DoesNotContain(text.Requests, r => r.Name.Contains("Fake", StringComparison.Ordinal));
    }

    [Fact]
    public void CqrsMap_MatchesTheGoldenFixture()
    {
        var root = FixtureRoot();
        var result = Analyzer.Analyze(root);
        var expectedPath = Path.Combine(
            root, "expected-cqrs.mmd");

        var sections = DiagramBuilder.CqrsMapSections(result);
        var section = Assert.Single(sections);
        var actual = Normalize(section.Source);
        var expected = Normalize(File.ReadAllText(expectedPath));

        Assert.Equal(expected, actual);
        Assert.Equal(2, section.Requests.Count);
        Assert.All(sections, s => Assert.True(DiagramBuilder.IsWithinMermaidLimits(s.Source)));
    }

    [Fact]
    public void TextAndRoslyn_ProduceTheSameCqrsMap()
    {
        var root = FixtureRoot();
        var text = DiagramBuilder.CqrsMapSections(Analyzer.Analyze(root));
        var roslyn = DiagramBuilder.CqrsMapSections(Analyzer.Analyze(root, AnalysisMode.Roslyn));

        Assert.Equal(
            text.Select(s => Normalize(s.Source)),
            roslyn.Select(s => Normalize(s.Source)));
    }

    [Fact]
    public void ProjectGraph_DisambiguatesSanitizedProjectNames()
    {
        var graph = DiagramBuilder.ProjectGraph(Analyzer.Analyze(FixtureRoot()));

        Assert.NotNull(graph);
        Assert.Contains("prj_Foo_Bar[\"Foo-Bar\"]", graph);
        Assert.Contains("prj_Foo_Bar_2[\"Foo.Bar\"]", graph);
        Assert.Contains("prj_Foo_Bar_2 --> prj_Foo_Bar", graph);
    }

    private static string FixtureRoot()
        => Path.Combine(FindRepositoryRoot(), "tests", "fixtures", "Adversarial");

    private static string Normalize(string value)
        => value.ReplaceLineEndings("\n").TrimEnd();

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
