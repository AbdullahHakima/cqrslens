namespace CqrsLens;

/// <summary>
/// Matches resolved endpoints by name, HTTP method, route and target request, with multiset
/// semantics so duplicate endpoints pair one-for-one. The endpoint surface is not part of the
/// CLI's serialized diff (see <see cref="EndpointDiffSection"/>); this comparer exists so the MCP
/// diff answer's endpoint counts come from the same calculated diff as the CQRS shape kinds.
/// </summary>
internal static class EndpointDiffComparer
{
    public static EndpointDiffSection Compare(
        IReadOnlyList<JsonEndpoint> baseEndpoints,
        IReadOnlyList<JsonEndpoint> headEndpoints)
    {
        static string Key(JsonEndpoint endpoint)
            => DiffKey.Combine(endpoint.Name, endpoint.HttpMethod, endpoint.Route, endpoint.TargetRequestFullName);

        var baseRemaining = CountKeys(baseEndpoints);
        var added = new List<JsonEndpoint>();
        foreach (var endpoint in headEndpoints)
        {
            var key = Key(endpoint);
            if (baseRemaining.TryGetValue(key, out var count) && count > 0)
                baseRemaining[key] = count - 1;
            else
                added.Add(endpoint);
        }

        var headRemaining = CountKeys(headEndpoints);
        var removed = new List<JsonEndpoint>();
        var unchanged = 0;
        foreach (var endpoint in baseEndpoints)
        {
            var key = Key(endpoint);
            if (headRemaining.TryGetValue(key, out var count) && count > 0)
            {
                headRemaining[key] = count - 1;
                unchanged++;
            }
            else
            {
                removed.Add(endpoint);
            }
        }

        return new EndpointDiffSection
        {
            Added = added
                .OrderBy(e => e.Name, StringComparer.Ordinal)
                .ThenBy(e => e.HttpMethod, StringComparer.Ordinal)
                .ThenBy(e => e.Route, StringComparer.Ordinal)
                .ToList(),
            Removed = removed
                .OrderBy(e => e.Name, StringComparer.Ordinal)
                .ThenBy(e => e.HttpMethod, StringComparer.Ordinal)
                .ThenBy(e => e.Route, StringComparer.Ordinal)
                .ToList(),
            Unchanged = unchanged,
        };
    }

    private static Dictionary<string, int> CountKeys(IEnumerable<JsonEndpoint> endpoints)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var endpoint in endpoints)
        {
            var key = DiffKey.Combine(endpoint.Name, endpoint.HttpMethod, endpoint.Route, endpoint.TargetRequestFullName);
            counts[key] = counts.TryGetValue(key, out var count) ? count + 1 : 1;
        }

        return counts;
    }
}
