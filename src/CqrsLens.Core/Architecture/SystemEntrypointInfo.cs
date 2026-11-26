namespace CqrsLens;

/// <summary>
/// A system-wide entrypoint across HTTP, gRPC, Blazor, Razor Pages, background workers,
/// and message consumers, together with its traced downstream dependencies.
/// </summary>
public sealed class SystemEntrypointInfo
{
    public required string Name { get; init; }
    public required SystemEntrypointKind Kind { get; init; }
    public required SystemExecutionStyle ExecutionStyle { get; set; }
    public required string Method { get; init; }
    public required string RouteOrTrigger { get; init; }
    public string? ProjectName { get; set; }
    public SourceLocation? Location { get; set; }
    public bool IsInferred { get; set; }

    public List<string> TargetRequests { get; } = new();
    public List<string> CalledServices { get; } = new();
    public List<string> TouchedDbContexts { get; } = new();
    public List<string> TouchedDataStores { get; } = new();
    public List<string> TouchedEntities { get; } = new();
    public List<string> PublishedMessages { get; } = new();

    public string DisplayLabel => string.IsNullOrEmpty(RouteOrTrigger)
        ? $"{Method} ({Name})"
        : $"{Method} {RouteOrTrigger}";
}
