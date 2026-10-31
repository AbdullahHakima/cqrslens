namespace CqrsLens;

/// <summary>Builds fail-closed full/simple name lookup tables for agent reports.</summary>
internal static class AgentReportLookups
{
    internal static (Dictionary<string, T> ByFull, Dictionary<string, T> BySimple) BuildLookups<T>(
        IEnumerable<T> items,
        Func<T, string> fullName,
        Func<T, string> simpleName) where T : class
    {
        var byFullCandidates = new Dictionary<string, List<T>>(StringComparer.Ordinal);
        var bySimpleCandidates = new Dictionary<string, List<T>>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            var full = fullName(item);
            if (!byFullCandidates.TryGetValue(full, out var fullList))
                byFullCandidates[full] = fullList = new List<T>();
            fullList.Add(item);

            var simple = simpleName(item);
            if (!bySimpleCandidates.TryGetValue(simple, out var list))
                bySimpleCandidates[simple] = list = new List<T>();
            list.Add(item);
        }

        // A name is safe to resolve only when exactly one declaration owns it. Otherwise
        // `Resolve` would silently return whichever type happened to be scanned first.
        var byFull = byFullCandidates
            .Where(pair => pair.Value.Count == 1)
            .ToDictionary(pair => pair.Key, pair => pair.Value[0], StringComparer.Ordinal);
        var bySimple = bySimpleCandidates
            .Where(pair => pair.Value.Count == 1)
            .ToDictionary(pair => pair.Key, pair => pair.Value[0], StringComparer.Ordinal);

        return (byFull, bySimple);
    }

    internal static T? Resolve<T>(
        string name,
        IReadOnlyDictionary<string, T> byFull,
        IReadOnlyDictionary<string, T> bySimple) where T : class
    {
        if (byFull.TryGetValue(name, out var hit))
            return hit;
        return bySimple.TryGetValue(CSharpScanner.SimpleName(name), out hit) ? hit : null;
    }
}