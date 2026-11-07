using System.Text.Json.Serialization;

namespace CqrsLens;

/// <summary>Serializable model for an architecture diff between two scans.</summary>
internal sealed class ArchitectureDiff
{
    public int SchemaVersion { get; set; }
    public string ToolVersion { get; set; } = "";
    public string Scanner { get; set; } = "";
    public string GeneratedAt { get; set; } = "";
    public string BaseRootName { get; set; } = "";
    public string HeadRootName { get; set; } = "";
    public string? Watermark { get; set; }
    public bool HasChanges { get; set; }
    public DiffSummary Summary { get; set; } = new();
    public ProjectDiffSection Projects { get; set; } = new();
    public RequestDiffSection Requests { get; set; } = new();
    public HandlerDiffSection Handlers { get; set; } = new();
    public ValidatorDiffSection Validators { get; set; } = new();
    public BehaviorDiffSection Behaviors { get; set; } = new();
    public DbContextDiffSection DbContexts { get; set; } = new();
    public CheckDiffSection Checks { get; set; } = new();

    /// <summary>
    /// Endpoint delta for the MCP diff answer. Deliberately <see cref="JsonIgnoreAttribute">ignored</see>
    /// by the CLI's diff serializer and excluded from <see cref="HasChanges"/>, so the CLI
    /// <c>--diff</c> JSON and Markdown stay byte-for-byte unchanged.
    /// </summary>
    [JsonIgnore]
    public EndpointDiffSection Endpoints { get; set; } = new();
}
