namespace CqrsLens;

/// <summary>
/// One edge resolved through the service-resolution index rather than written
/// directly in source. Today the only kind is
/// <see cref="FlowEdgeKind.HandlerToDbContextResolved"/>: a handler that injects an
/// interface (<see cref="Via"/>) whose resolved implementation is a DbContext
/// (<see cref="To"/>). Additive model data; never presented as a direct edge.
/// </summary>
public sealed class ResolvedEdge
{
    public required FlowEdgeKind Kind { get; init; }

    /// <summary>Full name of the source shape (the handler).</summary>
    public required string From { get; init; }

    /// <summary>Full name of the interface the hop goes through.</summary>
    public required string Via { get; init; }

    /// <summary>Full name of the resolved target (the DbContext).</summary>
    public required string To { get; init; }

    /// <summary>Project that declares the resolved target, when known.</summary>
    public string? ProjectName { get; init; }

    public SourceLocation? Location { get; init; }
}
