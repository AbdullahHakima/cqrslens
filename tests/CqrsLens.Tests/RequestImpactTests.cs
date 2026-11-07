using System.Text.Json.Nodes;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// The <c>trace_request_impact</c> contract: the request's forward chain (handler, validators,
/// pipeline behaviors in registered order, dispatching endpoints, published notifications and
/// their handlers, and the model's handler→DbContext edges) and its reverse reach, from the scan
/// model alone. Absence is stated; an unknown name is a clean not-found answer.
/// </summary>
public sealed class RequestImpactTests
{
    private static readonly string GoldenRoot = Path.Combine(
        FindRepositoryRoot(), "tests", "fixtures", "GoldenCorpus");

    // ------------------------------------------------------------------- forward chain

    [Fact]
    public void FullChain_CarriesHandlerValidatorNotificationAndItsHandlers()
    {
        var text = AgentReportGenerator.GenerateRequestImpact(Scan(), "CreateOrderCommand");

        Assert.Contains("## [Command] `CreateOrderCommand` → `Guid`", text);
        Assert.Contains("`CreateOrderCommandHandler`", text);
        Assert.Contains("`CreateOrderCommandValidator`", text);
        Assert.Contains("### Notifications published (1)", text);
        Assert.Contains("`OrderPlacedNotification`", text);
        Assert.Contains("`OrderPlacedNotificationHandler`", text);
        Assert.Contains("`OrderPlacedEmailHandler`", text);
    }

    [Fact]
    public void Endpoints_AreTheModelsResolvedEndpoints()
    {
        var text = AgentReportGenerator.GenerateRequestImpact(Scan(), "CreateOrderCommand");

        Assert.Contains("### Endpoints (2)", text);
        Assert.Contains("`MapPost`", text);
    }

    [Fact]
    public void Behaviors_CarryTheRegisteredOrderLabel()
    {
        var text = AgentReportGenerator.GenerateRequestImpact(Scan(), "CreateOrderCommand");

        Assert.Contains("### Pipeline behaviors (1)", text);
        Assert.Contains("registered order", text);
        Assert.Contains("Execution order is not guaranteed", text);
        // The Golden behavior has no resolvable registration site.
        Assert.Contains("order not determinable", text);
        Assert.Contains("`LoggingBehavior`", text);
    }

    [Fact]
    public void NoEndpoint_StatesAbsenceRatherThanInventingOne()
    {
        var text = AgentReportGenerator.GenerateRequestImpact(BuildMinimalResult(), "OnlyCommand");

        Assert.Contains("### Endpoints (0)", text);
        Assert.Contains("No endpoint dispatches this request in the scanned model.", text);
    }

    [Fact]
    public void NoNotifications_StatesAbsence()
    {
        var text = AgentReportGenerator.GenerateRequestImpact(BuildMinimalResult(), "OnlyCommand");

        Assert.Contains("### Notifications published (0)", text);
        Assert.Contains("records no notification published", text);
    }

    // --------------------------------------------------------------------- reverse reach

    [Fact]
    public void ReverseReach_IncludesEndpointsResolvedSitesHandlerEdgesAndDispatchedLists()
    {
        var result = BuildReverseReachResult();
        var text = AgentReportGenerator.GenerateRequestImpact(result, "TargetCommand");

        Assert.Contains("### Reverse reach (4)", text);
        Assert.Contains("endpoint `EndpointSender` — dispatches this request (`endpointToRequest` edge)", text);
        Assert.Contains("handler `App.HandlerSender` — sends this request (`handlerToRequest` edge)", text);
        Assert.Contains("dispatch site `App.OtherSender` — resolved dispatch call site", text);
        Assert.Contains("notification handler `App.NotificationSender` — records dispatching this request", text);
    }

    [Fact]
    public void ReverseReach_StateAbsenceWithTheUnresolvedDispatchCaveat()
    {
        var result = BuildMinimalResult();
        result.UnresolvedDispatches.Add(new UnresolvedDispatch { Method = "Send", ReceiverName = "_mediator" });
        var text = AgentReportGenerator.GenerateRequestImpact(result, "OnlyCommand");

        Assert.Contains("### Reverse reach (0)", text);
        Assert.Contains("No shape in the scanned model references this request.", text);
        Assert.Contains("1 unresolved dispatch call site(s)", text);
    }

    [Fact]
    public void DbContextEdges_AreTheModelsHandlerToDbContextEdges()
    {
        var result = BuildReverseReachResult();
        result.FlowEdges.Add(new FlowEdge
        {
            Kind = FlowEdgeKind.HandlerToDbContext,
            From = "App.TargetCommandHandler",
            To = "App.AppDbContext",
            ProjectName = "App",
        });
        var text = AgentReportGenerator.GenerateRequestImpact(result, "TargetCommand");

        Assert.Contains("### DbContext edges (1)", text);
        Assert.Contains("`handlerToDbContext` `App.TargetCommandHandler` → `App.AppDbContext`", text);
    }

    // ------------------------------------------------------------------------ not found

    [Fact]
    public void UnknownName_IsACleanNotFoundAnswer_NotAnError()
    {
        var result = Call("NoSuchCommand");

        Assert.False(result["isError"]!.GetValue<bool>());
        var text = result["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("No request named `NoSuchCommand` is present in the scanned model", text);
        Assert.Contains("not completed or guessed", text);
        Assert.Contains("Known requests:", text);
    }

    // --------------------------------------------------------------------- tool surface

    [Fact]
    public void Tool_TraceRequest_ReturnsTheFullChain()
    {
        var text = CallText("CreateOrderCommand");

        Assert.Contains("# CqrsLens Request Impact Trace — `CreateOrderCommand` (`GoldenCorpus`)", text);
        Assert.Contains("`CreateOrderCommandHandler`", text);
        Assert.Contains("`OrderPlacedNotification`", text);
    }

    [Fact]
    public void Tool_ContainsMatch_ReturnsEveryMatchingRequest()
    {
        var text = CallText("Order", match: "contains");

        Assert.Contains("Matched 4 request(s)", text);
        Assert.Contains("`GetOrderQuery`", text);
        Assert.Contains("`CancelOrderCommand`", text);
    }

    [Fact]
    public void Tool_MissingRequest_IsAnError()
    {
        var result = Call(null);

        Assert.True(result["isError"]!.GetValue<bool>());
        Assert.Contains("'request' argument is required", result["content"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public void Tool_InvalidMatch_IsAnError()
    {
        var result = Call("CreateOrderCommand", match: "fuzzy");

        Assert.True(result["isError"]!.GetValue<bool>());
        Assert.Contains("'match' must be 'exact' or 'contains'", result["content"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public void Tool_LimitAboveMaximum_IsAnError()
    {
        var result = Call("CreateOrderCommand", limit: 51);

        Assert.True(result["isError"]!.GetValue<bool>());
        Assert.Contains("between 1 and 50", result["content"]![0]!["text"]!.GetValue<string>());
    }

    // ------------------------------------------------------------------------------ helpers

    private static AnalysisResult BuildMinimalResult()
    {
        var result = new AnalysisResult { RootPath = "/tmp/cqrslens-request-impact" };
        var request = new CqrsRequest { Name = "OnlyCommand", Namespace = "App", ProjectName = "App" };
        request.Handlers.Add("App.OnlyCommandHandler");
        result.Requests.Add(request);
        result.Handlers.Add(new HandlerInfo
        {
            Name = "OnlyCommandHandler",
            Namespace = "App",
            RequestName = "OnlyCommand",
            ProjectName = "App",
        });
        return result;
    }

    private static AnalysisResult BuildReverseReachResult()
    {
        var result = new AnalysisResult { RootPath = "/tmp/cqrslens-request-impact" };

        // Namespace "App" + Name "TargetCommand" composes to the full name App.TargetCommand.
        var request = new CqrsRequest
        {
            Name = "TargetCommand",
            Namespace = "App",
            ProjectName = "App",
        };
        request.Handlers.Add("App.TargetCommandHandler");
        result.Requests.Add(request);

        result.Handlers.Add(new HandlerInfo
        {
            Name = "TargetCommandHandler",
            Namespace = "App",
            RequestName = "TargetCommand",
            ProjectName = "App",
        });
        result.Handlers.Add(new HandlerInfo
        {
            Name = "HandlerSender",
            Namespace = "App",
            RequestName = "DoorCommand",
            ProjectName = "App",
        });
        result.Handlers[1].DispatchedRequests.Add("App.TargetCommand");

        result.NotificationHandlers.Add(new NotificationHandlerInfo
        {
            Name = "NotificationSender",
            Namespace = "App",
            NotificationName = "SomethingHappened",
            ProjectName = "App",
        });
        result.NotificationHandlers[0].DispatchedRequests.Add("App.TargetCommand");

        result.FlowEdges.Add(new FlowEdge
        {
            Kind = FlowEdgeKind.EndpointToRequest,
            From = "EndpointSender",
            To = "App.TargetCommand",
            ProjectName = "App",
        });
        result.FlowEdges.Add(new FlowEdge
        {
            Kind = FlowEdgeKind.HandlerToRequest,
            From = "App.HandlerSender",
            To = "App.TargetCommand",
            ProjectName = "App",
        });
        result.DispatchCallSites.Add(new DispatchCallSite
        {
            TargetFullName = "App.TargetCommand",
            TargetKind = DispatchTargetKind.Request,
            SenderFullName = "App.OtherSender",
            ProjectName = "App",
        });
        return result;
    }

    private static AnalysisResult Scan()
        => McpScanProvider.GetOrRun(
            GoldenRoot, "text", new CliOptions { Path = GoldenRoot, Mcp = true }, new McpScanCache());

    private static string CallText(string? request, string? match = null, int limit = 0)
        => Call(request, match, limit)["content"]![0]!["text"]!.GetValue<string>();

    private static JsonObject Call(string? request, string? match = null, int limit = 0)
    {
        var arguments = new JsonObject { ["path"] = GoldenRoot };
        if (request is not null) arguments["request"] = request;
        if (match is not null) arguments["match"] = match;
        if (limit > 0) arguments["limit"] = limit;

        var parameters = new JsonObject
        {
            ["name"] = "trace_request_impact",
            ["arguments"] = arguments,
        };

        var json = McpToolInvoker.Call(
            JsonValue.Create(1),
            parameters,
            new CliOptions { Path = GoldenRoot, Mcp = true },
            new McpScanCache());

        return JsonNode.Parse(json)!.AsObject()["result"]!.AsObject();
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
