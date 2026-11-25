namespace CqrsLens;

/// <summary>The status and source evidence for one field in a CQRS slice design.</summary>
public sealed record SliceDesignCheck(
    string Field,
    string Status,
    string Expected,
    IReadOnlyList<string> Observed,
    string Message,
    IReadOnlyList<SourceLocation> Evidence);
