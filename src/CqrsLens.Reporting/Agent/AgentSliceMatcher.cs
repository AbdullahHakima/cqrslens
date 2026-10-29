using static CqrsLens.AgentReportFormatter;
using static CqrsLens.AgentReportLookups;

namespace CqrsLens;

/// <summary>Ranks CQRS slices against a natural-language filter.</summary>
internal static class AgentSliceMatcher
{
    /// <summary>
    /// Ranks a slice against a query. Exact request/handler names rank above prefixes, which
    /// rank above substrings; routes and entities rank below names. A score of zero means the
    /// slice does not match at all. This prevents a broad query from being ordered arbitrarily.
    /// </summary>
    internal static int ScoreSlice(
        CqrsRequest r,
        string filter,
        IReadOnlyDictionary<string, HandlerInfo> handlersByFull,
        IReadOnlyDictionary<string, HandlerInfo> handlersBySimple,
        IReadOnlyDictionary<string, ValidatorInfo> validatorsByFull,
        IReadOnlyDictionary<string, ValidatorInfo> validatorsBySimple,
        IReadOnlyDictionary<string, List<EndpointInfo>> endpointsByRequest)
    {
        var score = NameScore(r.Name, filter, exact: 1000, prefix: 900, contains: 750);

        if (r.FullName.Contains(filter, StringComparison.OrdinalIgnoreCase))
            score = Math.Max(score, 600);

        if (endpointsByRequest.TryGetValue(r.Name, out var eps))
        {
            foreach (var endpoint in eps.Where(e => EndpointTargetsRequest(e, r, allowSimpleNameFallback: false)))
            {
                score = Math.Max(score, NameScore(endpoint.Route, filter, exact: 950, prefix: 850, contains: 700));
                score = Math.Max(score, NameScore(endpoint.Name, filter, exact: 900, prefix: 800, contains: 650));
            }
        }

        score = Math.Max(score, NameScore(r.Entrypoints.FirstOrDefault() ?? "", filter, 800, 700, 600));

        foreach (var entity in r.TouchedEntities)
        {
            var entityName = EntityNameFromLabel(entity);
            var entityScore = Math.Max(
                NameScore(entity, filter, 850, 750, 600),
                NameScore(entityName, filter, 850, 750, 600));
            score = Math.Max(score, entityScore);
        }

        foreach (var property in r.Properties)
        {
            score = Math.Max(score, NameScore(property.Name, filter, 650, 550, 400));
            score = Math.Max(score, NameScore(property.Type, filter, 500, 400, 300));
        }

        foreach (var notification in r.PublishedNotifications)
        {
            score = Math.Max(score, NameScore(CSharpScanner.SimpleName(notification), filter, 750, 650, 500));
        }

        foreach (var dispatched in r.DispatchedRequests)
        {
            score = Math.Max(score, NameScore(CSharpScanner.SimpleName(dispatched), filter, 700, 600, 450));
        }

        foreach (var hName in r.Handlers)
        {
            score = Math.Max(score, NameScore(hName, filter, 900, 800, 650));
            var h = Resolve(hName, handlersByFull, handlersBySimple);
            if (h is null) continue;

            score = Math.Max(score, NameScore(h.Name, filter, 900, 800, 650));
            foreach (var dep in h.Dependencies)
                score = Math.Max(score, NameScore(dep, filter, 650, 550, 450));
        }

        foreach (var vName in r.Validators)
        {
            score = Math.Max(score, NameScore(vName, filter, 850, 750, 600));
            var v = Resolve(vName, validatorsByFull, validatorsBySimple);
            if (v is not null)
                score = Math.Max(score, NameScore(v.Name, filter, 850, 750, 600));
        }

        return score;
    }

    internal static int NameScore(string value, string filter, int exact, int prefix, int contains)
    {
        if (value.Length == 0)
            return 0;
        if (value.Equals(filter, StringComparison.OrdinalIgnoreCase))
            return exact;
        if (value.StartsWith(filter, StringComparison.OrdinalIgnoreCase))
            return prefix;
        return value.Contains(filter, StringComparison.OrdinalIgnoreCase) ? contains : 0;
    }
}