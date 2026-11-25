namespace CqrsLens;

/// <summary>
/// Internal per-service dependency and persistence summary collected during system scanning
/// for universal change-impact tracing.
/// </summary>
internal sealed record ServiceClassEffect(
    string? ProjectName,
    SourceLocation? Location,
    IReadOnlyList<string> CalledServices,
    IReadOnlyList<string> TouchedDbContexts,
    IReadOnlyList<string> TouchedEntities,
    IReadOnlyList<string> PublishedMessages);
