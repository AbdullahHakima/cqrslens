namespace CqrsLens;

/// <summary>A resolved or candidate target node for change impact analysis.</summary>
public sealed record ChangeImpactTarget(
    string Name,
    string Kind,
    string? FullName = null,
    string? ProjectName = null,
    SourceLocation? Location = null);
