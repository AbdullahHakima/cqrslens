namespace CqrsLens;

/// <summary>A directed edge between two flowchart nodes.</summary>
internal sealed record FlowchartEdge(
    string SourceId,
    string TargetId,
    string? Label,
    bool IsDotted);
