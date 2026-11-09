using System.Text.Json.Nodes;
using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// Stage 2 CP3 coverage for the three surfaces that render method-body service-call edges:
/// the per-request sequence (a dashed, method+via labelled arrow to the target participant),
/// the MCP <c>trace_request_impact</c> service-calls section, and the structured
/// <c>trace_slice</c> output. Only the request handler's OWN edges are drawn; a service
/// target's outgoing calls never expand into a chain.
/// </summary>
public sealed class ServiceCallSurfaceTests
{
    private const string HandlerFull = "App.CreateOrderCommandHandler";
    private const string RepoInterface = "App.IRepo";
    private const string RepoFull = "App.Repo";
    private const string ServiceOwnInterface = "App.IOther";
    private const string ServiceOwnTarget = "App.Other";
    private const string NotificationHandlerFull = "App.OrderPlacedHandler";
    private const string NotifierInterface = "App.INotifier";
    private const string NotifierFull = "App.Notifier";

    // ------------------------------------------------------------------ request sequence

    [Fact]
    public void Sequence_DrawsTheHandlersOwnCallAsADashedMethodViaArrow()
    {
        var sequence = Sequence(Model());
        var source = sequence.Source;

        Assert.Contains("participant s1 as Repo", source);
        Assert.Contains($"h1-->>s1: Save via IRepo", source);
        // The arrow is dashed (not a solid direct hop).
        Assert.DoesNotContain("h1->>s1:", source);
    }

    [Fact]
    public void Sequence_DoesNotExpandServiceChainsOrNotificationHandlerCalls()
    {
        var source = Sequence(Model()).Source;

        // The service's own outgoing edge and the notification handler's edge are not the
        // request handler's, so they are never drawn in the request sequence.
        Assert.DoesNotContain("Run via IOther", source);
        Assert.DoesNotContain("Notify via INotifier", source);
        Assert.DoesNotContain("participant s2", source);
    }

    [Fact]
    public void Sequence_WithNoCallEdges_HasNoServiceParticipantOrArrow()
    {
        var result = Model();
        result.ServiceCallEdges.Clear();

        var source = Sequence(result).Source;

        Assert.DoesNotContain("participant s", source);
        Assert.DoesNotContain(" via IRepo", source);
    }

    // --------------------------------------------------------- trace_request_impact (MCP)

    [Fact]
    public void RequestImpact_ListsTheHandlersServiceCalls()
    {
        var text = AgentReportGenerator.GenerateRequestImpact(Model(), "CreateOrderCommand");

        Assert.Contains("### Service calls (1)", text);
        Assert.Contains($"`Save` via `{RepoInterface}` → `{RepoFull}`", text);
        // Only the request handler's edges: not the notification handler's, not the service's.
        Assert.DoesNotContain(NotifierInterface, text);
    }

    [Fact]
    public void RequestImpact_StatesAbsenceWhenThereAreNoServiceCalls()
    {
        var result = Model();
        result.ServiceCallEdges.Clear();

        var text = AgentReportGenerator.GenerateRequestImpact(result, "CreateOrderCommand");

        Assert.Contains("### Service calls (0)", text);
        Assert.Contains("No service-call edge is recorded for this request's handler(s)", text);
    }

    // ------------------------------------------------------ trace_slice (structured MCP)

    [Fact]
    public void Slice_IncludesEdgesWhoseFromIsASliceMember()
    {
        var slice = OnlySlice(Model());

        var edges = slice["serviceCallEdges"]!.AsArray();
        var seen = edges.Select(e => (
            Kind: e!["kind"]!.GetValue<string>(),
            From: e["from"]!.GetValue<string>(),
            Method: e["method"]!.GetValue<string>(),
            Via: e["via"]!.GetValue<string>(),
            To: e["to"]!.GetValue<string>())).ToList();

        Assert.All(seen, e => Assert.Equal("serviceCall", e.Kind));
        Assert.Contains((Kind: "serviceCall", From: HandlerFull, Method: "Save", Via: RepoInterface, To: RepoFull), seen);
        // The notification handler named under the published notification is a slice member too.
        Assert.Contains((Kind: "serviceCall", From: NotificationHandlerFull, Method: "Notify", Via: NotifierInterface, To: NotifierFull), seen);
        // The service's own outgoing edge is not a member of this slice.
        Assert.DoesNotContain(seen, e => e.From == RepoFull);
    }

    [Fact]
    public void Slice_HasAnEmptyServiceCallArrayWhenThereAreNone()
    {
        var result = Model();
        result.ServiceCallEdges.Clear();

        var slice = OnlySlice(result);

        Assert.Empty(slice["serviceCallEdges"]!.AsArray());
    }

    // ------------------------------------------------------------------------------ helpers

    private static RequestSequence Sequence(AnalysisResult result)
        => Assert.Single(RequestSequenceBuilder.Build(result), s => s.RequestName == "CreateOrderCommand");

    private static JsonObject OnlySlice(AnalysisResult result)
    {
        var json = AgentReportGenerator.GenerateSliceData(result, "CreateOrderCommand");
        var slices = JsonNode.Parse(json)!.AsObject()["slices"]!.AsArray();
        Assert.Single(slices);
        return slices[0]!.AsObject();
    }

    private static AnalysisResult Model()
    {
        var result = new AnalysisResult { RootPath = "/tmp/cqrslens-service-calls" };

        var request = new CqrsRequest
        {
            Name = "CreateOrderCommand",
            Namespace = "App",
            ProjectName = "App",
        };
        request.Handlers.Add(HandlerFull);
        result.Requests.Add(request);

        var handler = new HandlerInfo
        {
            Name = "CreateOrderCommandHandler",
            Namespace = "App",
            RequestName = "CreateOrderCommand",
            ProjectName = "App",
        };
        handler.PublishedNotifications.Add("App.OrderPlaced");
        result.Handlers.Add(handler);

        var notification = new CqrsNotification
        {
            Name = "OrderPlaced",
            Namespace = "App",
            ProjectName = "App",
        };
        notification.Handlers.Add(NotificationHandlerFull);
        result.Notifications.Add(notification);

        result.NotificationHandlers.Add(new NotificationHandlerInfo
        {
            Name = "OrderPlacedHandler",
            Namespace = "App",
            NotificationName = "OrderPlaced",
            ProjectName = "App",
        });

        // The request handler's own call.
        result.ServiceCallEdges.Add(new ServiceCallEdge
        {
            From = HandlerFull, Via = RepoInterface, Method = "Save", To = RepoFull, ProjectName = "App",
        });
        // The notification handler's call (a slice member through the published notification).
        result.ServiceCallEdges.Add(new ServiceCallEdge
        {
            From = NotificationHandlerFull, Via = NotifierInterface, Method = "Notify", To = NotifierFull, ProjectName = "App",
        });
        // A service's own outgoing call: not a slice member, never expanded.
        result.ServiceCallEdges.Add(new ServiceCallEdge
        {
            From = RepoFull, Via = ServiceOwnInterface, Method = "Run", To = ServiceOwnTarget, ProjectName = "App",
        });

        return result;
    }
}
