using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>Tests for the 0.2.0 features: markers (#2), namespace-aware
/// linking (#3), multiple DbContexts per file (#4), HTML output (#5),
/// and architecture checks + --strict semantics (#6).</summary>
public class V02FeatureTests : IDisposable
{
    private readonly string _root;

    public V02FeatureTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cqrslens-v02-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "App.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private void WriteFile(string name, string code)
        => File.WriteAllText(Path.Combine(_root, name), code);

    // ------------------------------------------------ marker interfaces (#2)

    [Fact]
    public void Marker_DirectChainToIRequest_DiscoversRequest()
    {
        WriteFile("Markers.cs", "public interface ICommand<T> : IRequest<T> { }");
        WriteFile("Feature.cs", "public sealed record PlaceOrder(string Item) : ICommand<Guid>;");

        var result = Analyzer.Analyze(_root);

        var request = Assert.Single(result.Requests);
        Assert.Equal("PlaceOrder", request.Name);
        Assert.Equal("Command", request.Kind);
        Assert.Equal("Guid", request.ResponseType);
        Assert.Equal("ICommand", request.ViaMarker);
    }

    [Fact]
    public void Marker_TransitiveChain_DiscoversRequest()
    {
        WriteFile("Markers.cs", """
            public interface ICommand<T> : IRequest<T> { }
            public interface IAppCommand<T> : ICommand<T> { }
            """);
        WriteFile("Feature.cs", "public sealed record ReserveStock(int Qty) : IAppCommand<int>;");

        var result = Analyzer.Analyze(_root);

        var request = Assert.Single(result.Requests);
        Assert.Equal("ReserveStock", request.Name);
        Assert.Equal("Command", request.Kind);
        Assert.Equal("int", request.ResponseType);
    }

    [Fact]
    public void Marker_ConfiguredRootWithoutIRequestChain_NeedsTheMarkerFlag()
    {
        WriteFile("Markers.cs", """
            public interface IMessage { }
            public interface IQueryMessage<T> : IMessage { }
            """);
        WriteFile("Feature.cs", "public sealed record FetchStats(int Year) : IQueryMessage<Stats>;");

        var withoutMarkers = Analyzer.Analyze(_root);
        Assert.Empty(withoutMarkers.Requests);

        var withMarkers = Analyzer.Analyze(_root, new HashSet<string> { "IMessage" });
        var request = Assert.Single(withMarkers.Requests);
        Assert.Equal("FetchStats", request.Name);
        Assert.Equal("Query", request.Kind);
        Assert.Equal("Stats", request.ResponseType);
        Assert.Equal("IMessage", request.ViaMarker);
    }

    [Fact]
    public void Marker_SameNameInDifferentNamespaces_DoesNotShareInheritance()
    {
        WriteFile("AlphaMarkers.cs", """
            namespace Alpha;
            public interface ICommand<T> : IRequest<T> { }
            """);
        WriteFile("BetaMarkers.cs", """
            namespace Beta;
            public interface ICommand<T> { }
            """);
        WriteFile("AlphaFeature.cs", """
            namespace Alpha;
            public sealed record PlaceOrder() : ICommand<Guid>;
            """);
        WriteFile("BetaFeature.cs", """
            namespace Beta;
            public sealed record RebuildIndex() : ICommand<Guid>;
            """);

        var result = Analyzer.Analyze(_root);

        var request = Assert.Single(result.Requests);
        Assert.Equal("PlaceOrder", request.Name);
        Assert.Equal("Alpha", request.Namespace);
    }

    [Fact]
    public void Marker_Classification_SuffixWinsThenMarkerNameThenCommand()
    {
        // Suffix beats the marker it was reached through.
        Assert.Equal("Query", MarkerResolver.ClassifyMarkerRequest(
            "ListItemsQuery", new[] { "ICommand" }));
        // Marker name decides when the type name has no suffix.
        Assert.Equal("Query", MarkerResolver.ClassifyMarkerRequest(
            "FetchReport", new[] { "IQuery" }));
        Assert.Equal("Command", MarkerResolver.ClassifyMarkerRequest(
            "DoWork", new[] { "ICommand" }));
        // Neither suffix nor marker hint -> Command.
        Assert.Equal("Command", MarkerResolver.ClassifyMarkerRequest(
            "DoWork", new[] { "IMessage" }));
    }

    // --------------------------------------- namespace-aware linking (#3)

    [Fact]
    public void Linking_SameNamedRequestsInTwoNamespaces_GetTheirOwnHandlers()
    {
        WriteFile("Alpha.cs", """
            namespace Alpha;
            public sealed record GetDataQuery() : IRequest<int>;
            public sealed class AlphaGetDataHandler : IRequestHandler<GetDataQuery, int> { }
            public sealed class GetDataQueryValidator : AbstractValidator<GetDataQuery> { }
            """);
        WriteFile("Beta.cs", """
            namespace Beta;
            public sealed record GetDataQuery() : IRequest<string>;
            public sealed class BetaGetDataHandler : IRequestHandler<GetDataQuery, string> { }
            """);

        var result = Analyzer.Analyze(_root);

        Assert.Equal(2, result.Requests.Count);
        var alpha = result.Requests.Single(r => r.Namespace == "Alpha");
        var beta = result.Requests.Single(r => r.Namespace == "Beta");
        Assert.Equal(new[] { "Alpha.AlphaGetDataHandler" }, alpha.Handlers);
        Assert.Equal(new[] { "Alpha.GetDataQueryValidator" }, alpha.Validators);
        Assert.Equal(new[] { "Beta.BetaGetDataHandler" }, beta.Handlers);
        Assert.Empty(beta.Validators);

        // The diagram keeps the two requests distinct.
        var map = DiagramBuilder.CqrsMap(result);
        Assert.NotNull(map);
        Assert.Contains("Alpha_GetDataQuery", map);
        Assert.Contains("Beta_GetDataQuery", map);
    }

    [Fact]
    public void Linking_SameNamedHandlersInTwoNamespaces_StayDistinctInTheDiagram()
    {
        WriteFile("Alpha.cs", """
            namespace Alpha;
            public sealed record GetDataQuery() : IRequest<int>;
            public sealed class GetDataQueryHandler : IRequestHandler<GetDataQuery, int> { }
            """);
        WriteFile("Beta.cs", """
            namespace Beta;
            public sealed record GetDataQuery() : IRequest<string>;
            public sealed class GetDataQueryHandler : IRequestHandler<GetDataQuery, string> { }
            """);

        var result = Analyzer.Analyze(_root);

        var alpha = result.Requests.Single(r => r.Namespace == "Alpha");
        var beta = result.Requests.Single(r => r.Namespace == "Beta");
        Assert.Equal(new[] { "Alpha.GetDataQueryHandler" }, alpha.Handlers);
        Assert.Equal(new[] { "Beta.GetDataQueryHandler" }, beta.Handlers);

        var map = DiagramBuilder.CqrsMap(result);
        Assert.NotNull(map);
        Assert.Contains("Alpha_GetDataQueryHandler", map);
        Assert.Contains("Beta_GetDataQueryHandler", map);
    }

    [Fact]
    public void Linking_AmbiguousSimpleNameWithoutNamespace_DoesNotLink()
    {
        WriteFile("Alpha.cs", """
            namespace Alpha;
            public sealed record GetDataQuery() : IRequest<int>;
            """);
        WriteFile("Beta.cs", """
            namespace Beta;
            public sealed record GetDataQuery() : IRequest<string>;
            """);
        WriteFile("Handler.cs",
            "public sealed class GetDataHandler : IRequestHandler<GetDataQuery, int> { }");

        var result = Analyzer.Analyze(_root);

        Assert.Equal(2, result.Requests.Count);
        Assert.All(result.Requests, r => Assert.Empty(r.Handlers));
    }

    [Fact]
    public void Linking_HandlerMayWriteTheRequestNamespaceExplicitly()
    {
        WriteFile("Alpha.cs", """
            namespace Alpha;
            public sealed record GetDataQuery() : IRequest<int>;
            """);
        WriteFile("Beta.cs", """
            namespace Beta;
            public sealed record GetDataQuery() : IRequest<string>;
            """);
        WriteFile("Handler.cs", """
            namespace Gamma;
            public sealed class GetDataHandler : IRequestHandler<Beta.GetDataQuery, string> { }
            """);

        var result = Analyzer.Analyze(_root);

        var beta = result.Requests.Single(r => r.Namespace == "Beta");
        Assert.Equal(new[] { "Gamma.GetDataHandler" }, beta.Handlers);
        var alpha = result.Requests.Single(r => r.Namespace == "Alpha");
        Assert.Empty(alpha.Handlers);
    }

    // --------------------------------------- multiple DbContexts / file (#4)

    [Fact]
    public void Scanner_TwoDbContextsInOneFile_SetsAttributedPerClass()
    {
        var result = new AnalysisResult { RootPath = "/tmp" };
        CSharpScanner.ScanText("""
            public sealed class ShopDbContext : DbContext
            {
                public DbSet<Order> Orders { get; set; }
            }
            public sealed class AuditDbContext : DbContext
            {
                public DbSet<AuditLog> Logs { get; set; }
                public DbSet<AuditEntry> Entries { get; set; }
            }
            """, "App", result);

        Assert.Equal(2, result.DbContexts.Count);
        var shop = result.DbContexts.Single(c => c.Name == "ShopDbContext");
        Assert.Equal(new[] { new DbSetInfo("Order", "Orders") }, shop.DbSets);
        var audit = result.DbContexts.Single(c => c.Name == "AuditDbContext");
        Assert.Equal(2, audit.DbSets.Count);
        Assert.Contains(audit.DbSets, s => s.Entity == "AuditLog" && s.Property == "Logs");
        Assert.Contains(audit.DbSets, s => s.Entity == "AuditEntry" && s.Property == "Entries");
    }

    // ------------------------------------------------------ HTML output (#5)

    private static AnalysisResult SampleResult()
    {
        var result = new AnalysisResult { RootPath = "/tmp/Demo", FileCount = 7, ElapsedMilliseconds = 42 };
        result.Projects.Add(new ProjectInfo
        {
            Name = "Demo.App",
            FilePath = "/tmp/Demo/Demo.App.csproj",
            DirectoryPath = "/tmp/Demo",
        });
        var request = new CqrsRequest
        {
            Name = "GetThingQuery",
            ResponseType = "List<Thing>",
            ProjectName = "Demo.App",
        };
        request.Handlers.Add("GetThingQueryHandler");
        result.Requests.Add(request);
        result.Handlers.Add(new HandlerInfo
        {
            Name = "GetThingQueryHandler",
            RequestName = "GetThingQuery",
            ProjectName = "Demo.App",
        });
        result.Checks.Add(new CheckFinding(
            CheckSeverity.Warning, "CommandWithoutValidator", "Command `X` has no validator."));
        return result;
    }

    [Fact]
    public void Html_ContainsCardsEscapedContentAndMermaidRuntime()
    {
        var html = HtmlReportGenerator.Generate(SampleResult());

        Assert.Contains("<pre class=\"mermaid\">", html);
        Assert.Contains("mermaid@10", html);
        Assert.Contains("mermaid.initialize", html);
        // Stat card for the single project / request.
        Assert.Contains("<div class=\"card-value\">1</div>", html);
        // Response types are HTML-escaped in the table.
        Assert.Contains("List&lt;Thing&gt;", html);
        // Diagram code is escaped too ('>' in arrows).
        Assert.Contains("--&gt;", html);
        // Checks section rendered.
        Assert.Contains("Architecture Checks", html);
        Assert.Contains("CommandWithoutValidator", html);
    }

    [Fact]
    public void Html_RenderDiagram_IsTheSingleRenderingSeam()
    {
        var rendered = HtmlReportGenerator.RenderDiagram("A --> B");
        Assert.Equal("<pre class=\"mermaid\">A --&gt; B</pre>", rendered);
    }

    // ------------------------------------------------ architecture checks (#6)

    [Fact]
    public void Checks_MissingHandler_IsAnError()
    {
        var result = new AnalysisResult { RootPath = "/tmp" };
        result.Requests.Add(new CqrsRequest { Name = "DoThingCommand" });

        var findings = ArchitectureChecker.RunChecks(result);

        Assert.Contains(findings, f => f.Code == "MissingHandler" && f.Severity == CheckSeverity.Error);
        Assert.True(ArchitectureChecker.HasErrors(findings)); // -> --strict exits 4
    }

    [Fact]
    public void Checks_LayerViolation_DomainReferencingInfrastructure_IsAnError()
    {
        var result = new AnalysisResult { RootPath = "/tmp" };
        var domain = new ProjectInfo
        {
            Name = "Shop.Domain", FilePath = "/tmp/Shop.Domain.csproj", DirectoryPath = "/tmp",
            Layer = "Domain",
        };
        domain.ReferenceNames.Add("Shop.Infrastructure");
        var infra = new ProjectInfo
        {
            Name = "Shop.Infrastructure", FilePath = "/tmp/Shop.Infrastructure.csproj",
            DirectoryPath = "/tmp", Layer = "Infrastructure",
        };
        result.Projects.Add(domain);
        result.Projects.Add(infra);

        var findings = ArchitectureChecker.RunChecks(result);

        Assert.Contains(findings, f => f.Code == "LayerViolation" && f.Severity == CheckSeverity.Error);
        Assert.True(ArchitectureChecker.HasErrors(findings));
    }

    [Fact]
    public void Checks_CommandWithoutValidator_IsOnlyAWarning()
    {
        var result = new AnalysisResult { RootPath = "/tmp" };
        var request = new CqrsRequest { Name = "DoThingCommand" };
        request.Handlers.Add("DoThingCommandHandler");
        result.Requests.Add(request);

        var findings = ArchitectureChecker.RunChecks(result);

        Assert.Contains(findings, f =>
            f.Code == "CommandWithoutValidator" && f.Severity == CheckSeverity.Warning);
        Assert.False(ArchitectureChecker.HasErrors(findings)); // -> --strict still exits 0
    }

    [Fact]
    public void Checks_CleanResult_PassesEverything()
    {
        var result = new AnalysisResult { RootPath = "/tmp" };
        var request = new CqrsRequest { Name = "DoThingCommand" };
        request.Handlers.Add("DoThingCommandHandler");
        request.Validators.Add("DoThingCommandValidator");
        result.Requests.Add(request);

        var findings = ArchitectureChecker.RunChecks(result);

        Assert.Empty(findings);
        Assert.False(ArchitectureChecker.HasErrors(findings));
    }

    // --------------------------------------------------------- report footer (#1)

    [Fact]
    public void Report_SkippedFiles_AreVisible()
    {
        var result = SampleResult();
        result.DiscoveredFileCount = 9;
        result.FileCount = 7;
        result.SkippedFiles.Add("Features/Broken.cs");
        result.SkippedFiles.Add("Legacy/Unreadable.cs");

        var md = ReportGenerator.Generate(result);
        Assert.Contains("## Scan Diagnostics", md);
        Assert.Contains("7 of 9 source files scanned. 2 skipped:", md);
        Assert.Contains("Features/Broken.cs", md);
        Assert.Contains("7 of 9 files scanned; 2 skipped.", md);

        var html = HtmlReportGenerator.Generate(result);
        Assert.Contains("Scan Diagnostics", html);
        Assert.Contains("Legacy/Unreadable.cs", html);
        Assert.Contains("7 of 9 source files scanned", html);
        Assert.Contains("2 skipped", html);
    }

    [Fact]
    public void Report_MarkdownAndHtml_CarryTheTimingFooter()
    {
        var result = SampleResult();

        var md = ReportGenerator.Generate(result);
        Assert.Contains($"Generated by CqrsLens {ToolInfo.Version} in 42 ms — 7 files scanned.", md);
        Assert.Contains("## Architecture Checks", md);

        var html = HtmlReportGenerator.Generate(result);
        Assert.Contains($"Generated by CqrsLens {ToolInfo.Version} in 42 ms — 7 files scanned.", html);
    }
}
