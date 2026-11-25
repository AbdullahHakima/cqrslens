namespace CqrsLens;

/// <summary>A statically resolved integration-event or topic publish call site.</summary>
internal sealed record PublishedEventHit(
    string ProjectName,
    string FilePath,
    int Offset,
    string SenderFullName,
    string EventName,
    string EventSimpleName,
    string? ResolvedFullName,
    bool IsTopic,
    SourceLocation? Location);
