namespace CqrsLens;

/// <summary>An impacted artifact discovered along a forward or reverse dependency chain.</summary>
public sealed record ChangeImpactItem(
    string Kind,
    string Name,
    string FullName,
    string? ProjectName,
    string Direction,
    int Depth,
    string Reason,
    IReadOnlyList<string> Path,
    SourceLocation? Location = null);
