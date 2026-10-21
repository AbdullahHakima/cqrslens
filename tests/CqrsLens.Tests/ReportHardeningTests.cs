using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>Regression coverage for report trust: unique ids, escaping, pinning and determinism (#31).</summary>
public class ReportHardeningTests
{
    private static AnalysisResult FixedResult(string requestName = "Pipe|Name")
    {
        var result = new AnalysisResult
        {
            RootPath = Path.Combine(Path.GetTempPath(), "cqrslens-report"),
            GeneratedAt = new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero),
            ScannerName = "text",
        };

        var project = new ProjectInfo
        {
            Name = "Foo.Bar",
            FilePath = "Foo.Bar.csproj",
            DirectoryPath = "Foo.Bar",
        };
        result.Projects.Add(project);

        var request = new CqrsRequest
        {
            Name = requestName,
            Namespace = "Demo",
            ResponseType = "A|B",
            ProjectName = "Foo.Bar",
        };
        request.Handlers.Add("Demo.PipeNameHandler");
        request.Validators.Add("Demo.PipeNameValidator");
        result.Requests.Add(request);
        result.Handlers.Add(new HandlerInfo
        {
            Name = "PipeNameHandler",
            Namespace = "Demo",
            RequestName = "Pipe|Name",
            ProjectName = "Foo.Bar",
        });
        result.Validators.Add(new ValidatorInfo
        {
            Name = "PipeNameValidator",
            Namespace = "Demo",
            TargetType = "Pipe|Name",
            ProjectName = "Foo.Bar",
        });
        return result;
    }

    [Fact]
    public void ProjectGraph_AllocatesStableIdsForSanitizedCollisions()
    {
        var result = new AnalysisResult { RootPath = "/tmp" };
        result.Projects.Add(new ProjectInfo
        {
            Name = "Foo.Bar", FilePath = "/tmp/Foo.Bar.csproj", DirectoryPath = "/tmp/Foo.Bar",
        });
        result.Projects.Add(new ProjectInfo
        {
            Name = "Foo-Bar", FilePath = "/tmp/Foo-Bar.csproj", DirectoryPath = "/tmp/Foo-Bar",
        });

        var graph = DiagramBuilder.ProjectGraph(result);

        Assert.NotNull(graph);
        Assert.Contains("Foo_Bar[\"Foo.Bar\"]", graph);
        Assert.Contains("Foo_Bar_2[\"Foo-Bar\"]", graph);
        Assert.DoesNotContain(" Foo_Bar[\"Foo-Bar\"]", graph);
    }

    [Fact]
    public void CqrsMap_AllocatesDistinctIdsForSanitizedRequestNames()
    {
        var result = new AnalysisResult { RootPath = "/tmp" };
        result.Requests.Add(new CqrsRequest
        {
            Name = "Foo.Bar",
            Namespace = "One",
            ProjectName = "App",
        });
        result.Requests.Add(new CqrsRequest
        {
            Name = "Foo-Bar",
            Namespace = "Two",
            ProjectName = "App",
        });

        var map = DiagramBuilder.CqrsMap(result);

        Assert.NotNull(map);
        Assert.Contains("Foo_Bar[\"Foo.Bar\"]", map);
        Assert.Contains("Foo_Bar_2[\"Foo-Bar\"]", map);
    }

    [Fact]
    public void MermaidLabels_EscapeQuotesBracketsPipesAndNewlines()
    {
        var label = DiagramBuilder.MermaidLabel("A\"B<C>|D\nE");

        Assert.Equal("A&quot;B&lt;C&gt;&#124;D E", label);
    }

    [Fact]
    public void MarkdownTables_EscapePipesAndCollapseNewlines()
    {
        var result = FixedResult("Pipe|Name\nSecond");

        var md = ReportGenerator.Generate(result);

        Assert.Contains("Pipe\\|Name Second", md);
        Assert.Contains("A\\|B", md);
        Assert.Contains("Foo.Bar", md);
        Assert.DoesNotContain("Pipe|Name\nSecond", md);
    }

    [Fact]
    public void RepeatedGeneration_WithTheSameTimestampIsByteIdentical()
    {
        var first = ReportGenerator.Generate(FixedResult());
        var second = ReportGenerator.Generate(FixedResult());

        Assert.Equal(first, second);
        Assert.Contains("2026-02-03 04:05:06 UTC", first);

        var firstHtml = HtmlReportGenerator.Generate(FixedResult());
        var secondHtml = HtmlReportGenerator.Generate(FixedResult());
        Assert.Equal(firstHtml, secondHtml);
    }

    [Fact]
    public void Html_PinsMermaidWithSriAndDocumentsOfflineBehavior()
    {
        var html = HtmlReportGenerator.Generate(FixedResult());

        Assert.Contains("mermaid@10.9.4", html);
        Assert.Contains("integrity=\"sha384-", html);
        Assert.Contains("crossorigin=\"anonymous\"", html);
        Assert.Contains("Analysis Diagnostics", html);
    }

    [Fact]
    public void Html_EscapesUserControlledLabelsAndMessages()
    {
        var result = FixedResult("A<B>&\"C\"");
        result.Diagnostics.Add(new AnalysisDiagnostic(
            "Test",
            AnalysisDiagnosticSeverity.Warning,
            "Bad <label> & value",
            new SourceLocation("A<B>.cs", 1, 2)));

        var html = HtmlReportGenerator.Generate(result);

        Assert.Contains("A&lt;B&gt;&amp;&quot;C&quot;", html);
        Assert.Contains("Bad &lt;label&gt; &amp; value", html);
        Assert.Contains("A&lt;B&gt;.cs", html);
    }
}
