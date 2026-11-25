namespace CqrsLens;

/// <summary>A conditional MSBuild project reference omitted from unconditional dependencies.</summary>
public sealed record ConditionalProjectReference(
    string Include,
    string Condition,
    string? ResolvedProjectName = null);
