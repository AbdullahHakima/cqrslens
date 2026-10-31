namespace CqrsLens;

internal sealed class JsonReportDocument
{
    public int? SchemaVersion { get; set; }
    public string ToolVersion { get; set; } = "";
    public string Scanner { get; set; } = "";
    public string GeneratedAt { get; set; } = "";
    public long ElapsedMilliseconds { get; set; }
    public string RootName { get; set; } = "";
    public int DiscoveredFileCount { get; set; }
    public int FileCount { get; set; }
    public string? Watermark { get; set; }
    public List<string>? Conventions { get; set; }
    public bool BaselineApplied { get; set; }
    public bool CanRunStrictChecks { get; set; }
    public JsonSummary Summary { get; set; } = new();
    public List<JsonProject> Projects { get; set; } = new();
    public List<JsonRequest> Requests { get; set; } = new();
    public List<JsonHandler> Handlers { get; set; } = new();
    public List<JsonValidator> Validators { get; set; } = new();
    public List<JsonBehavior> Behaviors { get; set; } = new();
    public List<JsonDbContext> DbContexts { get; set; } = new();
    public List<JsonCheckFinding> Checks { get; set; } = new();
    public List<JsonDiagnostic> Diagnostics { get; set; } = new();
    public List<string> SkippedFiles { get; set; } = new();
    public List<JsonNotification> Notifications { get; set; } = new();
    public List<JsonNotificationHandler> NotificationHandlers { get; set; } = new();
    public List<JsonEndpoint>? Endpoints { get; set; }
    public List<JsonFlowEdge>? FlowEdges { get; set; }
    public List<JsonDispatchCallSite>? DispatchCallSites { get; set; }
    public List<JsonUnresolvedDispatch>? UnresolvedDispatches { get; set; }
}
