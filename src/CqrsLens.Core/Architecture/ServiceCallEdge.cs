namespace CqrsLens;

/// <summary>
/// One method-body call edge between scanned classes: a source class (a request/notification
/// handler, or a service that is a chosen implementation injected somewhere) invoking a method
/// on one of its injected members. The target expression must be exactly the member
/// (<c>_repo.GetAsync(...)</c>, <c>eventLogService.SaveEventAsync(...)</c>) — a call on a
/// property's return value, a local, a parameter, a <c>new</c>-constructed object, a static,
/// <c>base</c>/<c>this</c>, or an unresolved interface is not an edge.
///
/// Additive model data kept out of <see cref="AnalysisResult.ResolvedEdges"/> and
/// <see cref="AnalysisResult.FlowEdges"/>: Stage 2 call edges are a different fact from the
/// Stage 1 dependency edges. The additive JSON section exposes them.
/// </summary>
public sealed class ServiceCallEdge
{
    /// <summary>Full name of the source class making the call.</summary>
    public required string From { get; init; }

    /// <summary>
    /// Full name of the injected member's declared type: the interface when it is one,
    /// otherwise the concrete class.
    /// </summary>
    public required string Via { get; init; }

    /// <summary>The invoked method's name (no arguments, no signature).</summary>
    public required string Method { get; init; }

    /// <summary>
    /// The resolved target: the service-resolution index's chosen implementation when
    /// <see cref="Via"/> is an interface, otherwise the concrete class itself.
    /// </summary>
    public required string To { get; init; }

    /// <summary>Project that declares the source class, when known.</summary>
    public string? ProjectName { get; init; }

    /// <summary>Location of the first call site in deterministic scan order.</summary>
    public SourceLocation? Location { get; init; }
}
