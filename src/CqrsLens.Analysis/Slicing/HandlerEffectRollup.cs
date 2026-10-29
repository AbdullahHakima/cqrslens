namespace CqrsLens;

/// <summary>
/// Rolls handler side-effects (touched entities, published notifications, dispatched
/// requests) onto the requests linked to those handlers.
/// </summary>
internal static class HandlerEffectRollup
{
    internal static void Apply(AnalysisResult result)
    {
        var handlerLookup = result.Handlers
            .GroupBy(h => h.FullName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        foreach (var request in result.Requests)
        {
            foreach (var handlerFullName in request.Handlers)
            {
                if (!handlerLookup.TryGetValue(handlerFullName, out var handlers))
                    continue;

                foreach (var handler in handlers)
                {
                    AddUnique(request.TouchedEntities, handler.TouchedEntities);
                    AddUnique(request.PublishedNotifications, handler.PublishedNotifications);
                    AddUnique(request.DispatchedRequests, handler.DispatchedRequests);
                }
            }
        }
    }

    private static void AddUnique(List<string> target, List<string> source)
    {
        foreach (var value in source)
        {
            if (!target.Contains(value, StringComparer.Ordinal))
                target.Add(value);
        }
    }
}
