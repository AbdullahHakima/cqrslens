namespace CqrsLens;

/// <summary>
/// One resolved edge of the solution-wide flow graph. Both endpoints are full type
/// names; an edge exists only when the target type was statically resolved, never
/// from a runtime-typed or otherwise unresolved dispatch.
/// </summary>
public sealed class FlowEdge
{
    public required FlowEdgeKind Kind { get; init; }

    /// <summary>Full name of the source shape (endpoint label, request, or handler).</summary>
    public required string From { get; init; }

    /// <summary>Full name of the target shape (request, handler, notification, DbContext).</summary>
    public required string To { get; init; }

    /// <summary>Project that declares the source of the edge, when known.</summary>
    public string? ProjectName { get; init; }

    public SourceLocation? Location { get; init; }
}
