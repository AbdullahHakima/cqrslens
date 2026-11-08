namespace CqrsLens;

/// <summary>The direction of one solution-wide flow edge.</summary>
public enum FlowEdgeKind
{
    /// <summary>An endpoint action dispatches a request.</summary>
    EndpointToRequest,

    /// <summary>A request is served by a handler (the inventory edge, unchanged).</summary>
    RequestToHandler,

    /// <summary>A handler dispatches another request.</summary>
    HandlerToRequest,

    /// <summary>A handler publishes a notification.</summary>
    HandlerToNotification,

    /// <summary>A handler uses a discovered DbContext type.</summary>
    HandlerToDbContext,

    /// <summary>
    /// A handler uses a DbContext through an interface it injects, resolved via the
    /// service-resolution index. Distinct from <see cref="HandlerToDbContext"/>: the
    /// hop is DI-resolved, not a direct reference, so it is styled and labelled apart.
    /// </summary>
    HandlerToDbContextResolved,
}
