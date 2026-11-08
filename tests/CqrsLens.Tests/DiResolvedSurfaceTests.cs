using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// CP3 coverage for the three surfaces that render DI-resolved handler → DbContext
/// hops: the flow diagram (dotted, labelled with the interface), the per-request
/// sequence (dashed, labelled with the interface) and the MCP request-impact trace
/// (marked resolved-through-DI, naming the interface).
/// </summary>
public class DiResolvedSurfaceTests
{
    private const string HandlerFull = "App.CreateCustomerCommandHandler";
    private const string ContextFull = "App.Persistence.AppDbContext";
    private const string InterfaceFull = "App.Persistence.IAppDbContext";

    // ------------------------------------------------------------------ flow diagram

    [Fact]
    public void FlowDiagram_DrawsTheResolvedHopDottedAndLabelledWithTheInterface()
    {
        var result = BuildResult();
        var section = Assert.Single(DiagramBuilder.FlowDiagramSections(result));

        var resolved = Assert.Single(section.ResolvedEdges);
        Assert.Equal(FlowEdgeKind.HandlerToDbContextResolved, resolved.Kind);

        Assert.Contains("-.->|resolved via IAppDbContext|", section.Source);
        Assert.DoesNotContain("-->|uses|", section.Source);
        // The dotted hop is not a direct flow edge.
        Assert.DoesNotContain(section.Edges, e => e.Kind == FlowEdgeKind.HandlerToDbContextResolved);
    }

    [Fact]
    public void FlowDiagram_KeepsDirectEdgesSolidWhenAResolvedHopCoexists()
    {
        var result = BuildResult();
        result.FlowEdges.Add(new FlowEdge
        {
            Kind = FlowEdgeKind.HandlerToDbContext,
            From = HandlerFull,
            To = ContextFull,
            ProjectName = "Persistence",
        });
        var section = Assert.Single(DiagramBuilder.FlowDiagramSections(result));

        Assert.Contains("-->|uses|", section.Source);
        Assert.Contains("-.->|resolved via IAppDbContext|", section.Source);
        Assert.Single(section.Edges);
        Assert.Single(section.ResolvedEdges);
    }

    [Fact]
    public void FlowDiagram_SvgExportRendersTheResolvedHop()
    {
        var result = BuildResult();
        var html = HtmlReportGenerator.Generate(result, DiagramRenderMode.Svg);

        Assert.Contains("resolved via IAppDbContext", html);
        Assert.Contains("data-source=\"hdl_", html);
        Assert.Contains("data-target=\"dbc_", html);
    }

    // -------------------------------------------------------------- request sequences

    [Fact]
    public void RequestSequence_IncludesTheResolvedHopDashedWithTheInterface()
    {
        var sequence = Assert.Single(RequestSequenceBuilder.Build(BuildResult()));

        Assert.Contains("h1-->>c1: uses via IAppDbContext", sequence.Source);
        Assert.Contains("participant c1 as AppDbContext", sequence.Source);
    }

    [Fact]
    public void RequestSequence_ShowsTouchedDbSetsAlongsideTheViaInterface()
    {
        var result = BuildResult();
        result.Handlers[0].TouchedEntities.Add("Orders (Order)");
        result.DbContexts[0].DbSets.Add(new DbSetInfo("Order", "Orders"));

        var sequence = Assert.Single(RequestSequenceBuilder.Build(result));

        Assert.Contains("uses Orders via IAppDbContext", sequence.Source);
    }

    // ------------------------------------------------------------------- MCP trace

    [Fact]
    public void TraceRequestImpact_MarksTheResolvedHopAndNamesTheInterface()
    {
        var text = AgentReportGenerator.GenerateRequestImpact(BuildResult(), "CreateCustomerCommand");

        Assert.Contains("### DbContext edges (1)", text);
        Assert.Contains("DbContext edges: 1", text);
        Assert.Contains("`handlerToDbContextResolved`", text);
        Assert.Contains("(resolved through DI via `App.Persistence.IAppDbContext`)", text);
    }

    [Fact]
    public void TraceRequestImpact_KeepsTheAbsenceWordingWhenThereAreNoEdges()
    {
        var result = BuildResult();
        result.ResolvedEdges.Clear();

        var text = AgentReportGenerator.GenerateRequestImpact(result, "CreateCustomerCommand");

        Assert.Contains("### DbContext edges (0)", text);
        Assert.Contains(
            "No `handlerToDbContext` flow edge is recorded for this request's handler(s) in the scanned model.",
            text);
    }

    // ------------------------------------------------------------------------ helpers

    private static AnalysisResult BuildResult()
    {
        var result = new AnalysisResult { RootPath = "/tmp/cqrslens-di-surface" };

        var request = new CqrsRequest
        {
            Name = "CreateCustomerCommand",
            Namespace = "App",
            ProjectName = "App",
        };
        request.Handlers.Add(HandlerFull);
        result.Requests.Add(request);

        result.Handlers.Add(new HandlerInfo
        {
            Name = "CreateCustomerCommandHandler",
            Namespace = "App",
            RequestName = "CreateCustomerCommand",
            ProjectName = "App",
        });

        result.DbContexts.Add(new DbContextInfo
        {
            Name = "AppDbContext",
            Namespace = "App.Persistence",
            ProjectName = "Persistence",
            Location = new SourceLocation("Persistence/AppDbContext.cs", 5, 18),
        });

        result.ResolvedEdges.Add(new ResolvedEdge
        {
            Kind = FlowEdgeKind.HandlerToDbContextResolved,
            From = HandlerFull,
            Via = InterfaceFull,
            To = ContextFull,
            ProjectName = "Persistence",
            Location = new SourceLocation("Persistence/AppDbContext.cs", 5, 18),
        });

        return result;
    }
}
