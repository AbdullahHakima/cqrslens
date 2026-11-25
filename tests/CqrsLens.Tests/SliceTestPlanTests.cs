using Xunit;

namespace CqrsLens.Tests;

public sealed class SliceTestPlanTests
{
    private static readonly SliceTestScenario[] Scenarios =
    [
        new("Reject an invalid quantity", "An order has a line with quantity 0",
            "The customer submits the order", "The line is rejected and no order is saved")
    ];

    [Fact]
    public void ProposedRequestPreservesBehaviorWithoutInventingImplementation()
    {
        var result = new AnalysisResult { RootPath = Path.GetTempPath() };

        var plan = AgentSliceTestPlanGenerator.Generate(result, " ReserveOrderCommand ", Scenarios);

        Assert.Equal("proposed", plan.Status);
        Assert.Equal("ReserveOrderCommand", plan.Request);
        Assert.Equal(Scenarios, plan.Scenarios);
        Assert.Empty(plan.Candidates);
        Assert.Empty(plan.ExistingTestDispatches);
        var suggestion = Assert.Single(plan.SuggestedTests);
        Assert.Equal("request-behavior", suggestion.Boundary);
        Assert.Empty(suggestion.Evidence);
        Assert.NotEmpty(plan.OpenQuestions);
        Assert.Equal(new[] { "red", "green", "refactor" }, plan.Steps.Select(s => s.Stage));
    }

    [Fact]
    public void ExistingRequestSuggestsTestsAtSourceBackedBoundaries()
    {
        var result = Analyzer.Analyze(GoldenRoot());

        var plan = AgentSliceTestPlanGenerator.Generate(result, "CreateOrderCommand", Scenarios);

        Assert.Equal("existing", plan.Status);
        Assert.Equal("Golden.Application.Orders.CreateOrderCommand", Assert.Single(plan.Candidates).FullName);
        Assert.Equal(Scenarios[0].Then, Assert.Single(plan.Scenarios).Then);
        Assert.Contains(plan.SuggestedTests, s => s.Boundary == "request-behavior" &&
            s.Evidence.Any(e => e.Target.EndsWith("CreateOrderCommandHandler", StringComparison.Ordinal) &&
                e.Location?.File.EndsWith("CreateOrder.cs", StringComparison.Ordinal) == true));
        Assert.Contains(plan.SuggestedTests, s => s.Boundary == "validation" &&
            s.Evidence.Any(e => e.Target.EndsWith("CreateOrderCommandValidator", StringComparison.Ordinal)));
        Assert.Contains(plan.SuggestedTests, s => s.Boundary == "http" && s.Evidence.Any(e => e.Location is not null));
        Assert.Contains(plan.SuggestedTests, s => s.Boundary == "persistence");
        Assert.Contains(plan.SuggestedTests, s => s.Boundary == "notification" &&
            s.Evidence.Any(e => e.Target.EndsWith("OrderPlacedNotification", StringComparison.Ordinal)));
    }

    [Fact]
    public void AmbiguousSimpleNameDoesNotBorrowEitherSlicesEvidence()
    {
        var result = new AnalysisResult { RootPath = Path.GetTempPath() };
        result.Requests.Add(new CqrsRequest { Namespace = "Billing", Name = "CancelOrderCommand" });
        result.Requests.Add(new CqrsRequest { Namespace = "Fulfillment", Name = "CancelOrderCommand" });

        var plan = AgentSliceTestPlanGenerator.Generate(result, "CancelOrderCommand", Scenarios);

        Assert.Equal("ambiguous", plan.Status);
        Assert.Equal(new[] { "Billing.CancelOrderCommand", "Fulfillment.CancelOrderCommand" },
            plan.Candidates.Select(c => c.FullName));
        Assert.Empty(plan.SuggestedTests);
        Assert.Empty(plan.Steps);
        Assert.Equal("existing",
            AgentSliceTestPlanGenerator.Generate(result, "Billing.CancelOrderCommand", Scenarios).Status);
    }

    [Fact]
    public void UnmatchedRequestWithIncompleteScanNeedsInvestigation()
    {
        var result = new AnalysisResult { RootPath = Path.GetTempPath() };
        result.Diagnostics.Add(new AnalysisDiagnostic(AnalysisResult.SourceFileSkippedCode,
            AnalysisDiagnosticSeverity.Warning, "A file could not be read."));

        var plan = AgentSliceTestPlanGenerator.Generate(result, "ReserveOrderCommand", Scenarios);

        Assert.Equal("unresolved", plan.Status);
        Assert.Empty(plan.SuggestedTests);
        Assert.Contains(plan.OpenQuestions, q => q.Contains("SourceFileSkipped", StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicatedHandlerDeclarationsDoNotSuggestTheirPersistenceEdges()
    {
        var result = new AnalysisResult { RootPath = Path.GetTempPath() };
        var request = new CqrsRequest { Namespace = "Billing", Name = "CancelOrderCommand" };
        request.Handlers.Add("Billing.CancelOrderHandler");
        result.Requests.Add(request);
        foreach (var project in new[] { "Billing.One", "Billing.Two" })
        {
            var handler = new HandlerInfo
            {
                Namespace = "Billing", Name = "CancelOrderHandler", RequestName = request.Name, ProjectName = project,
            };
            handler.TouchedEntities.Add("Orders (Order)");
            result.Handlers.Add(handler);
        }

        var plan = AgentSliceTestPlanGenerator.Generate(result, request.FullName, Scenarios);

        Assert.Equal("existing", plan.Status);
        Assert.Contains(plan.OpenQuestions, q => q.Contains("Which handler", StringComparison.Ordinal));
        var suggestion = Assert.Single(plan.SuggestedTests);
        Assert.Equal(request.FullName, suggestion.Target);
        Assert.Equal("request", Assert.Single(suggestion.Evidence).Kind);
    }

    [Fact]
    public void TestDispatchEvidenceIsScopedToTheExactRequestAndDoesNotClaimCoverage()
    {
        var result = new AnalysisResult { RootPath = Path.GetTempPath() };
        result.Requests.Add(new CqrsRequest { Namespace = "Billing", Name = "CancelOrderCommand" });
        result.DispatchCallSites.Add(Dispatch("Billing.CancelOrderCommand", "Billing.Tests", true));
        result.DispatchCallSites.Add(Dispatch("Fulfillment.CancelOrderCommand", "Fulfillment.Tests", true));
        result.DispatchCallSites.Add(Dispatch("Billing.CancelOrderCommand", "Billing.Api", false));

        var plan = AgentSliceTestPlanGenerator.Generate(result, "Billing.CancelOrderCommand", Scenarios);

        Assert.Equal("Billing.Tests", Assert.Single(plan.ExistingTestDispatches).Project);
        Assert.Contains("assert", plan.Steps[0].Instruction, StringComparison.OrdinalIgnoreCase);
    }

    private static DispatchCallSite Dispatch(string target, string project, bool isTestProject) => new()
    {
        TargetFullName = target,
        TargetKind = DispatchTargetKind.Request,
        ProjectName = project,
        IsTestProject = isTestProject,
        Location = new SourceLocation(project + "/CancelOrder.cs", 10, 1),
    };

    private static string GoldenRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "CqrsLens.sln")))
            current = current.Parent;
        return Path.Combine(current!.FullName, "tests", "fixtures", "GoldenCorpus");
    }
}
