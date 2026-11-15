namespace CqrsLens;

/// <summary>
/// Rolls handler side-effects (touched entities, published notifications, dispatched
/// requests) onto the requests linked to those handlers.
/// </summary>
internal static class HandlerEffectRollup
{
    internal static void Apply(AnalysisResult result)
    {
        // Request.TouchedEntities, Request.PublishedNotifications, and Request.DispatchedRequests
        // are now derived SpineLinkList projections over Request --Serves--> Handler --> Target
        // relations in ArchitectureSpine, so no duplicate request-level relations are materialized.
        _ = result;
    }
}
