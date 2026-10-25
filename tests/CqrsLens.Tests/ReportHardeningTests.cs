using System.Text.RegularExpressions;
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
        Assert.Contains("prj_Foo_Bar[\"Foo-Bar\"]", graph);
        Assert.Contains("prj_Foo_Bar_2[\"Foo.Bar\"]", graph);
        Assert.DoesNotContain(" prj_Foo_Bar[\"Foo.Bar\"]", graph);
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
        Assert.Contains("req_Foo_Bar[\"Foo.Bar\"]", map);
        Assert.Contains("req_Foo_Bar_2[\"Foo-Bar\"]", map);
    }

    [Fact]
    public void CqrsMap_DisambiguatesDuplicateSimpleNamesInLabels()
    {
        var result = new AnalysisResult { RootPath = "/tmp" };
        var one = new CqrsRequest
        {
            Name = "PingCommand", Namespace = "Demo.One", ProjectName = "App",
        };
        one.Handlers.Add("Demo.One.PingHandler");
        one.Validators.Add("Demo.One.PingValidator");
        var two = new CqrsRequest
        {
            Name = "PingCommand", Namespace = "Demo.Two", ProjectName = "App",
        };
        two.Handlers.Add("Demo.Two.PingHandler");
        two.Validators.Add("Demo.Two.PingValidator");
        result.Requests.Add(one);
        result.Requests.Add(two);

        var map = Assert.Single(DiagramBuilder.CqrsMapSections(result)).Source;

        Assert.Contains("req_Demo_One_PingCommand[\"Demo.One.PingCommand\"]", map);
        Assert.Contains("req_Demo_Two_PingCommand[\"Demo.Two.PingCommand\"]", map);
        Assert.Contains("hdl_Demo_One_PingHandler([\"Demo.One.PingHandler\"])", map);
        Assert.Contains("hdl_Demo_Two_PingHandler([\"Demo.Two.PingHandler\"])", map);
        Assert.Contains("val_Demo_One_PingValidator[\"Demo.One.PingValidator\"]", map);
        Assert.Contains("val_Demo_Two_PingValidator[\"Demo.Two.PingValidator\"]", map);
    }

    [Fact]
    public void MermaidLabels_UseNativeEntitiesSafeFromGitHubHtmlDecoding()
    {
        var label = DiagramBuilder.MermaidLabel("A\"B<C>|D\nE");
        Assert.Equal("A#quot;B#lt;C#gt;#124;D E", label);

        var ampersands = DiagramBuilder.MermaidLabel("A&B#C");
        Assert.Equal("A#38;B#35;C", ampersands);
        Assert.DoesNotContain("&quot;", label);
        Assert.DoesNotContain("&lt;", label);
    }

    [Theory]
    [InlineData("end", "n_end")]
    [InlineData("graph", "n_graph")]
    [InlineData("subgraph", "n_subgraph")]
    [InlineData("style", "n_style")]
    [InlineData("class", "n_class")]
    [InlineData("classDef", "n_classDef")]
    [InlineData("linkStyle", "n_linkStyle")]
    [InlineData("flowchart", "n_flowchart")]
    public void MermaidId_PrefixesReservedWords(string name, string expected)
        => Assert.Equal(expected, DiagramBuilder.MermaidId(name));

    [Fact]
    public void MermaidId_StaysAsciiAndReadable()
    {
        Assert.Equal("SampleShop_Application", DiagramBuilder.MermaidId("SampleShop.Application"));
        Assert.Equal("n_9lives", DiagramBuilder.MermaidId("9lives"));
        Assert.Equal("n", DiagramBuilder.MermaidId(""));
        Assert.Matches("^[A-Za-z_][A-Za-z0-9_]*$", DiagramBuilder.MermaidId("café/订单"));
    }

    [Fact]
    public void MarkdownTables_EscapePipesAnglesAndCollapseNewlines()
    {
        var result = FixedResult("Pipe|Name\nSecond");

        var md = ReportGenerator.Generate(result);

        Assert.Contains("Pipe\\|Name Second", md);
        Assert.Contains("A\\|B", md);
        Assert.Contains("Foo.Bar", md);
        Assert.DoesNotContain("Pipe|Name\nSecond", md);

        var generic = ReportGenerator.Generate(FixedResult("List<ProductDto> & A<B>C"));
        Assert.Contains("List&lt;ProductDto&gt; &amp; A&lt;B&gt;C", generic);
        Assert.Contains("List#lt;ProductDto#gt; #38; A#lt;B#gt;C", generic);
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
    public void Html_PinsMermaidWithSriAndGitHubCompatibleConfiguration()
    {
        var html = HtmlReportGenerator.Generate(FixedResult());

        Assert.Contains("mermaid@12.2.0", html);
        Assert.Contains(
            "integrity=\"sha384-8xUhuLNMjW/cI2Dacg3DR4WliYznvIg0LVG1stctThKD/YVrJFI9TNCFxft5NYgp\"",
            html);
        Assert.Contains("crossorigin=\"anonymous\"", html);
        Assert.Contains("startOnLoad: false", html);
        Assert.Contains("theme: 'default'", html);
        Assert.Contains("look: 'classic'", html);
        Assert.Contains("layout: 'elk'", html);
        Assert.Contains("maxTextSize: 50000", html);
        Assert.Contains("maxEdges: 500", html);
        Assert.Contains("securityLevel: 'strict'", html);
        Assert.Contains("suppressErrorRendering: true", html);
        Assert.Contains("mermaid.render", html);
        Assert.Contains("showSource", html);
        Assert.Contains("This diagram could not be rendered", html);
        Assert.Contains("node.replaceWith", html);
        Assert.Contains("diagram-error", html);
        Assert.Contains("Analysis Diagnostics", html);
    }

    [Fact]
    public void Html_SplitsLargeCqrsMapsAndLabelsEveryPart()
    {
        var result = new AnalysisResult { RootPath = "/tmp" };
        for (var i = 0; i < 20; i++)
            result.Requests.Add(Request($"Command{i:00}", "App"));

        var html = HtmlReportGenerator.Generate(result);

        Assert.Equal(2, Regex.Count(html, "<pre class=\"mermaid\">"));
        Assert.Contains("<h3>App — 10 of 20 requests (part 1 of 2)</h3>", html);
        Assert.Contains("<h3>App — 10 of 20 requests (part 2 of 2)</h3>", html);
        Assert.Contains("20 requests across 1 project.", html);
        Assert.Contains("every request appears exactly once", html);
        Assert.Contains("Command00", html);
        Assert.Contains("Command19", html);
    }

    [Fact]
    public void Markdown_SplitsLargeCqrsMapsAndLabelsEveryPart()
    {
        var result = new AnalysisResult { RootPath = "/tmp" };
        for (var i = 0; i < 20; i++)
            result.Requests.Add(Request($"Command{i:00}", "App"));

        var markdown = ReportGenerator.Generate(result);

        Assert.Contains("### App — 10 of 20 requests (part 1 of 2)", markdown);
        Assert.Contains("### App — 10 of 20 requests (part 2 of 2)", markdown);
        Assert.Contains("20 requests across 1 project.", markdown);
        Assert.Contains("every request appears exactly once", markdown);
        Assert.Contains("| Command00 |", markdown);
        Assert.Contains("| Command19 |", markdown);
    }

    [Fact]
    public void CqrsMapSections_LabelUnassignedSplits()
    {
        var result = new AnalysisResult { RootPath = "/tmp" };
        for (var i = 0; i < 20; i++)
        {
            result.Requests.Add(new CqrsRequest
            {
                Name = $"Command{i:00}",
                Namespace = "Demo",
                ResponseType = "Unit",
            });
        }

        var sections = DiagramBuilder.CqrsMapSections(result);

        Assert.Equal(2, sections.Count);
        Assert.Equal("CQRS Map — 10 of 20 requests (part 1 of 2)", sections[0].Heading);
        Assert.Equal("CQRS Map — 10 of 20 requests (part 2 of 2)", sections[1].Heading);
    }

    [Fact]
    public void CqrsMapSections_SplitLargeMapsIntoBoundedCoverage()
    {
        var result = new AnalysisResult { RootPath = "/tmp" };
        for (var i = 0; i < 44; i++)
            result.Requests.Add(Request($"Command{i:00}", "App"));

        var sections = DiagramBuilder.CqrsMapSections(result);

        Assert.Equal(3, sections.Count);
        Assert.Equal(new[] { 15, 15, 14 }, sections.Select(s => s.Requests.Count));
        Assert.Equal(new[] { 1, 2, 3 }, sections.Select(s => s.Part));
        Assert.All(sections, s => Assert.Equal(3, s.PartCount));
        Assert.All(sections, s => Assert.True(DiagramBuilder.IsWithinMermaidLimits(s.Source)));
        Assert.Equal(
            result.Requests.OrderBy(r => r.FullName, StringComparer.Ordinal).Select(r => r.FullName),
            sections.SelectMany(s => s.Requests).Select(r => r.FullName).OrderBy(n => n, StringComparer.Ordinal));
        Assert.All(sections, s => Assert.Contains("subgraph prj_App", s.Source));
    }

    [Fact]
    public void CqrsMapSections_GroupByProjectAndCoverEveryRequestOnce()
    {
        var result = new AnalysisResult { RootPath = "/tmp" };
        for (var i = 0; i < 30; i++)
            result.Requests.Add(Request($"ACommand{i:00}", "A"));
        for (var i = 0; i < 14; i++)
            result.Requests.Add(Request($"BCommand{i:00}", "B"));

        var sections = DiagramBuilder.CqrsMapSections(result);

        Assert.Equal(3, sections.Count);
        Assert.Equal(new[] { "A", "A", "B" }, sections.Select(s => s.ProjectName));
        Assert.Equal(new[] { 15, 15, 14 }, sections.Select(s => s.Requests.Count));
        Assert.Equal(44, sections.Sum(s => s.Requests.Count));
        Assert.Equal(
            result.Requests.Select(r => r.FullName).OrderBy(n => n, StringComparer.Ordinal),
            sections.SelectMany(s => s.Requests).Select(r => r.FullName).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void CqrsMapSections_ApplyFindingStylesOnlyWhenTheChecksExist()
    {
        var result = new AnalysisResult { RootPath = "/tmp" };
        result.Requests.Add(Request("PingCommand", "App"));

        var map = Assert.Single(DiagramBuilder.CqrsMapSections(result)).Source;
        Assert.Contains(":::clsCommand", map);
        Assert.DoesNotContain("MissingHandler", map);
        Assert.DoesNotContain("CommandWithoutValidator", map);

        result.Checks.Add(new CheckFinding(
            CheckSeverity.Error,
            ArchitectureChecker.MissingHandlerCode,
            "No handler.",
            Target: "Demo.PingCommand"));
        map = Assert.Single(DiagramBuilder.CqrsMapSections(result)).Source;
        Assert.Contains(":::clsMissingHandler", map);
        Assert.Contains("classDef clsMissingHandler", map);

        var noValidator = new AnalysisResult { RootPath = "/tmp" };
        noValidator.Requests.Add(Request("PingCommand", "App"));
        noValidator.Checks.Add(new CheckFinding(
            CheckSeverity.Warning,
            ArchitectureChecker.CommandWithoutValidatorCode,
            "No validator.",
            Target: "Demo.PingCommand"));
        map = Assert.Single(DiagramBuilder.CqrsMapSections(noValidator)).Source;
        Assert.Contains(":::clsCommandNoValidator", map);
        Assert.Contains("classDef clsCommandNoValidator", map);
        Assert.DoesNotContain("MissingHandler", map);
    }

    [Fact]
    public void ProjectGraph_AppliesViolationStylesOnlyWhenTheFindingExists()
    {
        var result = new AnalysisResult { RootPath = "/tmp" };
        var api = new ProjectInfo
        {
            Name = "Shop.Api", FilePath = "/tmp/Shop.Api.csproj", DirectoryPath = "/tmp/Shop.Api",
            Layer = "Api",
        };
        var domain = new ProjectInfo
        {
            Name = "Shop.Domain", FilePath = "/tmp/Shop.Domain.csproj", DirectoryPath = "/tmp/Shop.Domain",
            Layer = "Domain",
        };
        api.ReferenceNames.Add("Shop.Domain");
        result.Projects.Add(api);
        result.Projects.Add(domain);

        var plain = DiagramBuilder.ProjectGraph(result);
        Assert.NotNull(plain);
        Assert.Contains("prj_Shop_Api --> prj_Shop_Domain", plain);
        Assert.DoesNotContain("violates", plain);
        Assert.DoesNotContain("linkStyle", plain);

        result.Checks.Add(new CheckFinding(
            CheckSeverity.Error,
            ArchitectureChecker.LayerViolationCode,
            "Api references Domain.",
            Target: "Shop.Api -> Shop.Domain"));
        var styled = DiagramBuilder.ProjectGraph(result);
        Assert.NotNull(styled);
        Assert.Contains("prj_Shop_Api -->|violates| prj_Shop_Domain", styled);
        Assert.Contains("linkStyle 0 stroke:#dc2626", styled);
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

    [Fact]
    public void RenderDiagram_SupportsBothMermaidAndSvgModes()
    {
        Assert.Equal(
            "<pre class=\"mermaid\">A --&gt; B</pre>",
            HtmlReportGenerator.RenderDiagram("A --> B"));

        var svg = HtmlReportGenerator.RenderDiagram("A --> B", DiagramRenderMode.Svg);
        Assert.StartsWith("<div class=\"svg-diagram\"><svg ", svg);
        Assert.Contains("data-id=\"A\"", svg);
        Assert.Contains("data-id=\"B\"", svg);
        Assert.Contains("data-source=\"A\" data-target=\"B\"", svg);
        Assert.EndsWith("</svg></div>", svg);
    }

    [Fact]
    public void Html_SvgRenderMode_EmitsZeroJavaScriptAndInlineSvgsForAllDiagrams()
    {
        var result = new AnalysisResult
        {
            RootPath = Path.Combine(Path.GetTempPath(), "cqrslens-svg"),
            GeneratedAt = new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero),
            ScannerName = "text",
        };

        var domain = new ProjectInfo
        {
            Name = "Shop.Domain",
            FilePath = "/tmp/Shop.Domain.csproj",
            DirectoryPath = "/tmp/Shop.Domain",
            Layer = "Domain",
        };
        var app = new ProjectInfo
        {
            Name = "Shop.Application",
            FilePath = "/tmp/Shop.Application.csproj",
            DirectoryPath = "/tmp/Shop.Application",
            Layer = "Application",
        };
        app.ReferenceNames.Add("Shop.Domain");
        domain.ReferenceNames.Add("Shop.Application");
        result.Projects.Add(domain);
        result.Projects.Add(app);
        result.Checks.Add(new CheckFinding(
            CheckSeverity.Error,
            ArchitectureChecker.LayerViolationCode,
            "Domain references Application.",
            Target: "Shop.Domain -> Shop.Application"));

        var cmd = new CqrsRequest
        {
            Name = "PlaceOrderCommand",
            Namespace = "Shop.Application",
            ProjectName = "Shop.Application",
            ResponseType = "Guid",
        };
        cmd.Handlers.Add("Shop.Application.PlaceOrderHandler");
        cmd.Validators.Add("Shop.Application.PlaceOrderValidator");
        result.Requests.Add(cmd);

        var db = new DbContextInfo
        {
            Name = "ShopDbContext",
            Namespace = "Shop.Infrastructure",
            ProjectName = "Shop.Application",
        };
        db.DbSets.Add(new DbSetInfo("Order", "Orders"));
        var customer = new EntityInfo
        {
            Name = "Customer",
            Namespace = "Shop.Domain",
        };
        customer.Properties.Add(new EntityPropertyInfo { Name = "Id", Type = "int", IsKey = true });
        var order = new EntityInfo
        {
            Name = "Order",
            Namespace = "Shop.Domain",
        };
        order.Properties.Add(new EntityPropertyInfo { Name = "Id", Type = "int", IsKey = true });
        order.Properties.Add(new EntityPropertyInfo { Name = "CustomerId", Type = "int", IsForeignKey = true });
        db.Entities.Add(customer);
        db.Entities.Add(order);
        db.Relationships.Add(new EntityRelationshipInfo(
            "Shop.Domain.Customer",
            "Shop.Domain.Order",
            PrincipalNavigation: "Orders",
            DependentNavigation: "Customer",
            ForeignKey: "CustomerId",
            IsRequired: true,
            Kind: EntityRelationshipKind.OneToMany));
        result.DbContexts.Add(db);

        var html = HtmlReportGenerator.Generate(result, DiagramRenderMode.Svg);
        var secondHtml = HtmlReportGenerator.Generate(result, DiagramRenderMode.Svg);

        Assert.Equal(html, secondHtml);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mermaid.min.js", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<pre class=\"mermaid\">", html, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(4, Regex.Count(html, "<div class=\"svg-diagram\"><svg "));
        Assert.Contains("<title>Project dependency graph</title>", html);
        Assert.Contains("<title>Layer diagram</title>", html);
        Assert.Contains("<title>CQRS map</title>", html);
        Assert.Contains("<title>ShopDbContext ER diagram</title>", html);
        Assert.Contains("PlaceOrderCommand", html);
        Assert.Contains("PlaceOrderHandler", html);
        Assert.Contains("PlaceOrderValidator", html);
        Assert.Contains(">violates<", html);
        Assert.Contains(">validates<", html);
        Assert.Contains("Orders / Customer (1..*)", html);
        Assert.Contains(">int Id<", html);
        Assert.Contains(">PK<", html);
        Assert.Contains(">FK<", html);
    }

    [Fact]
    public void Html_SvgRenderMode_DecodesMermaidEntitiesAndXmlEscapesUserLabelsOnce()
    {
        var result = FixedResult("A<B>&\"C\"#quot;<script>");

        var html = HtmlReportGenerator.Generate(result, DiagramRenderMode.Svg);

        Assert.DoesNotContain("<script>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("A&lt;B&gt;&amp;&quot;C&quot;#quot;&lt;script&gt;", html);
    }

    private static CqrsRequest Request(string name, string project)
        => new()
        {
            Name = name,
            Namespace = "Demo",
            ProjectName = project,
            ResponseType = "Unit",
        };
}
