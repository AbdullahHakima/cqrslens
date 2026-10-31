namespace CqrsLens;

/// <summary>A directed edge between two flowchart nodes.</summary>
internal sealed record FlowEdge(
    string SourceId,
    string TargetId,
    string? Label,
    bool IsDotted);
