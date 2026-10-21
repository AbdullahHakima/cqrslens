namespace CqrsLens;

/// <summary>
/// Resolves requests that are declared through custom marker interfaces
/// (issue #2): home-grown abstractions such as <c>ICommand&lt;T&gt; : IRequest&lt;T&gt;</c>,
/// or markers configured with --markers, instead of implementing IRequest directly.
/// </summary>
public static class MarkerResolver
{
    /// <summary>
    /// Walks the transitive closure of the interfaces a class implements.
    /// Returns the configured marker names encountered on the way (the roots that
    /// make the class a request), or an empty list when the closure never reaches
    /// IRequest or a configured marker.
    /// </summary>
    public static List<string> ReachableMarkers(
        IEnumerable<string> directInterfaces,
        IReadOnlyDictionary<string, List<string>> interfaceMap,
        IReadOnlySet<string> markerRoots)
    {
        // BFS encounter order is preserved so classification is deterministic.
        var found = new List<string>();
        var foundSet = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>(directInterfaces);
        var reachesRequest = false;

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!visited.Add(current)) continue;

            if (current is "IRequest")
                reachesRequest = true;
            if (markerRoots.Contains(current) && foundSet.Add(current))
                found.Add(current);

            if (interfaceMap.TryGetValue(current, out var bases))
                foreach (var b in bases)
                    queue.Enqueue(b);
        }

        if (reachesRequest && foundSet.Add("IRequest"))
            found.Add("IRequest");
        return found;
    }

    /// <summary>
    /// Classifies a marker-discovered request from an ordered list of "via"
    /// candidates (the class's own interfaces first, then the marker roots its
    /// closure reached): a name suffix wins; otherwise the first candidate whose
    /// name mentions "Command" or "Query" decides; otherwise it is a Command.
    /// </summary>
    public static string ClassifyMarkerRequest(string typeName, IEnumerable<string> viaCandidates)
    {
        if (typeName.EndsWith("Command", StringComparison.Ordinal)) return "Command";
        if (typeName.EndsWith("Query", StringComparison.Ordinal)) return "Query";
        foreach (var marker in viaCandidates)
        {
            if (marker.Contains("Command", StringComparison.OrdinalIgnoreCase)) return "Command";
            if (marker.Contains("Query", StringComparison.OrdinalIgnoreCase)) return "Query";
        }
        return "Command";
    }

    /// <summary>
    /// Extracts a response type for a marker-discovered request from the class's own
    /// generic base (IMyMarker&lt;TResponse&gt;) — the shape most marker designs use.
    /// </summary>
    public static string? TryExtractMarkerResponse(CSharpScanner.ClassDecl decl, IEnumerable<string> reachedMarkers)
    {
        foreach (var marker in reachedMarkers)
        {
            if (marker is "IRequest") continue;
            var args = CSharpScanner.ExtractGenericArguments(decl.BaseList, marker);
            if (args is { Count: >= 1 })
                return args[0];
        }
        return null;
    }
}
