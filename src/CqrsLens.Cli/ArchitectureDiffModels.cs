using System.Text.Json.Serialization;

using static CqrsLens.JsonReportGenerator;

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
}

internal sealed record DiffMetric(int Base, int Head)
{
    public int Delta => Head - Base;
}

internal sealed class DiffSummary
{
    public DiffMetric Projects { get; set; } = new(0, 0);
    public DiffMetric Requests { get; set; } = new(0, 0);
    public DiffMetric Handlers { get; set; } = new(0, 0);
    public DiffMetric Validators { get; set; } = new(0, 0);
    public DiffMetric Behaviors { get; set; } = new(0, 0);
    public DiffMetric DbContexts { get; set; } = new(0, 0);
    public DiffMetric Errors { get; set; } = new(0, 0);
    public DiffMetric Warnings { get; set; } = new(0, 0);
}

internal sealed class ProjectDiffSection
{
    public List<JsonProject> Added { get; set; } = new();
    public List<JsonProject> Removed { get; set; } = new();
    public List<ModifiedProjectDiff> Modified { get; set; } = new();

    [JsonIgnore]
    public bool HasChanges => Added.Count > 0 || Removed.Count > 0 || Modified.Count > 0;
}

internal sealed class ModifiedProjectDiff
{
    public string Name { get; set; } = "";
    public string? OldLayer { get; set; }
    public string? NewLayer { get; set; }
    public List<string> AddedReferences { get; set; } = new();
    public List<string> RemovedReferences { get; set; } = new();
    public List<string> AddedPackages { get; set; } = new();
    public List<string> RemovedPackages { get; set; } = new();
}

internal sealed class RequestDiffSection
{
    public List<JsonRequest> Added { get; set; } = new();
    public List<JsonRequest> Removed { get; set; } = new();
    public List<ModifiedRequestDiff> Modified { get; set; } = new();

    [JsonIgnore]
    public bool HasChanges => Added.Count > 0 || Removed.Count > 0 || Modified.Count > 0;
}

internal sealed class ModifiedRequestDiff
{
    public string FullName { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Project { get; set; }
    public string? OldKind { get; set; }
    public string? NewKind { get; set; }
    public string? OldResponseType { get; set; }
    public string? NewResponseType { get; set; }
    public string? OldProject { get; set; }
    public string? NewProject { get; set; }
    public List<string> AddedHandlers { get; set; } = new();
    public List<string> RemovedHandlers { get; set; } = new();
    public List<string> AddedValidators { get; set; } = new();
    public List<string> RemovedValidators { get; set; } = new();

    [JsonIgnore]
    public JsonRequest HeadSnapshot { get; set; } = new();
}

internal sealed class HandlerDiffSection
{
    public List<JsonHandler> Added { get; set; } = new();
    public List<JsonHandler> Removed { get; set; } = new();

    [JsonIgnore]
    public bool HasChanges => Added.Count > 0 || Removed.Count > 0;
}

internal sealed class ValidatorDiffSection
{
    public List<JsonValidator> Added { get; set; } = new();
    public List<JsonValidator> Removed { get; set; } = new();

    [JsonIgnore]
    public bool HasChanges => Added.Count > 0 || Removed.Count > 0;
}

internal sealed class BehaviorDiffSection
{
    public List<JsonBehavior> Added { get; set; } = new();
    public List<JsonBehavior> Removed { get; set; } = new();

    [JsonIgnore]
    public bool HasChanges => Added.Count > 0 || Removed.Count > 0;
}

internal sealed class DbContextDiffSection
{
    public List<JsonDbContext> Added { get; set; } = new();
    public List<JsonDbContext> Removed { get; set; } = new();
    public List<ModifiedDbContextDiff> Modified { get; set; } = new();

    [JsonIgnore]
    public bool HasChanges => Added.Count > 0 || Removed.Count > 0 || Modified.Count > 0;
}

internal sealed class ModifiedDbContextDiff
{
    public string Name { get; set; } = "";
    public string? Project { get; set; }
    public List<string> AddedDbSets { get; set; } = new();
    public List<string> RemovedDbSets { get; set; } = new();
    public List<string> AddedEntities { get; set; } = new();
    public List<string> RemovedEntities { get; set; } = new();
    public List<string> AddedRelationships { get; set; } = new();
    public List<string> RemovedRelationships { get; set; } = new();
}

internal sealed class CheckDiffSection
{
    public List<JsonCheckFinding> NewFindings { get; set; } = new();
    public List<JsonCheckFinding> ResolvedFindings { get; set; } = new();

    [JsonIgnore]
    public bool HasChanges => NewFindings.Count > 0 || ResolvedFindings.Count > 0;
}