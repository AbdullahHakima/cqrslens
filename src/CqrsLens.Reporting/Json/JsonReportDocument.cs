using System.Text.Json.Serialization;

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

    /// <summary>
    /// Additive section: edges resolved through DI (today handler → DbContext via an
    /// injected interface). Emitted only when non-empty, so reports without resolved
    /// edges stay byte-identical to the base.
    /// </summary>
    public List<JsonResolvedEdge>? ResolvedEdges { get; set; }

    /// <summary>
    /// Additive section: method-body service-call edges (a handler or service invoking a method
    /// on one of its injected members). Emitted only when non-empty, so reports without call
    /// edges stay byte-identical to the base.
    /// </summary>
    public List<JsonServiceCallEdge>? ServiceCallEdges { get; set; }

    public List<JsonDispatchCallSite>? DispatchCallSites { get; set; }
    public List<JsonUnresolvedDispatch>? UnresolvedDispatches { get; set; }

    /// <summary>
    /// Additive section (stage F1): deduplicated spine relations with full structured evidence
    /// occurrences. Emitted only when non-empty; existing sections remain unchanged.
    /// </summary>
    [JsonConverter(typeof(DirectSpineRelationsConverter))]
    public List<JsonSpineRelation>? Relations { get; set; }

    /// <summary>
    /// Additive section (stage F2): deduplicated spine components and their discovered role evidence.
    /// Emitted only when non-empty.
    /// </summary>
    public List<JsonSpineComponent>? Components { get; set; }

    /// <summary>
    /// Additive section (stage F2): spine endpoints including conventional controller action endpoints.
    /// Emitted only when non-empty.
    /// </summary>
    public List<JsonSpineEndpoint>? SpineEndpoints { get; set; }

    /// <summary>
    /// Additive section (stage F2): structured unproven populations recorded instead of speculative edges.
    /// Emitted only when <see cref="JsonUnprovenPopulations.TotalCount"/> is non-zero.
    /// </summary>
    public JsonUnprovenPopulations? Unproven { get; set; }
}
