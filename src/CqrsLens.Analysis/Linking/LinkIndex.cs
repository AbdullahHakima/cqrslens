namespace CqrsLens;

/// <summary>
/// Resolves a written type reference against known shapes. Fully-qualified names are
/// preferred; a simple-name fallback is used only when it is unambiguous. Duplicate
/// declarations in the same project fail closed instead of linking to an arbitrary hit.
/// </summary>
internal sealed class LinkIndex<T>
    where T : class
{
    private readonly Dictionary<string, List<T>> _byFullName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<T>> _bySimpleName = new(StringComparer.Ordinal);
    private readonly Func<T, string> _fullName;
    private readonly Func<T, string> _simpleName;
    private readonly Func<T, string?> _projectName;

    public LinkIndex(
        IEnumerable<T> items,
        Func<T, string> fullName,
        Func<T, string> simpleName,
        Func<T, string?> projectName)
    {
        _fullName = fullName;
        _simpleName = simpleName;
        _projectName = projectName;

        foreach (var item in items)
        {
            Add(_byFullName, fullName(item), item);
            Add(_bySimpleName, simpleName(item), item);
        }
    }

    public IReadOnlyList<T> SimpleMatches(string simpleName)
        => _bySimpleName.TryGetValue(simpleName, out var matches) ? matches : Array.Empty<T>();

    public T? Resolve(
        string simpleName,
        string? writtenNamespace,
        string? fallbackNamespace,
        IReadOnlyList<string> importedNamespaces,
        string? ownerProject,
        IReadOnlyList<string>? containingTypes = null,
        string? targetFullNameHint = null)
    {
        if (!string.IsNullOrEmpty(targetFullNameHint) &&
            _byFullName.TryGetValue(targetFullNameHint, out var hintHits))
        {
            return PickHit(hintHits, ownerProject);
        }

        // A handler may write a type as `Outer.Inner`, `Ns.Outer.Inner`, or an unqualified
        // nested sibling `Inner` inside an outer feature class (`containingTypes`).
        var candidates = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void AddCandidate(string candidate)
        {
            if (seen.Add(candidate))
                candidates.Add(candidate);
        }

        if (containingTypes is { Count: > 0 })
        {
            for (var depth = containingTypes.Count; depth >= 1; depth--)
            {
                var scope = string.Join(".", containingTypes.Take(depth));
                var prefix = string.IsNullOrEmpty(fallbackNamespace)
                    ? scope
                    : $"{fallbackNamespace}.{scope}";
                if (!string.IsNullOrEmpty(writtenNamespace))
                    AddCandidate($"{prefix}.{writtenNamespace}.{simpleName}");
                if (string.IsNullOrEmpty(writtenNamespace) ||
                    string.Equals(writtenNamespace, fallbackNamespace, StringComparison.Ordinal))
                    AddCandidate($"{prefix}.{simpleName}");
            }
        }

        if (!string.IsNullOrEmpty(writtenNamespace))
        {
            AddCandidate($"{writtenNamespace}.{simpleName}");
            if (!string.IsNullOrEmpty(fallbackNamespace))
                AddCandidate($"{fallbackNamespace}.{writtenNamespace}.{simpleName}");
        }
        if (!string.IsNullOrEmpty(fallbackNamespace))
            AddCandidate($"{fallbackNamespace}.{simpleName}");
        else
            AddCandidate(simpleName);

        foreach (var candidate in candidates)
        {
            if (_byFullName.TryGetValue(candidate, out var hits))
                return PickHit(hits, ownerProject);
        }

        // An unqualified (or outer-qualified, e.g. Create.Command) target name is often
        // imported by a using in the handler or validator file. Two imported matches mean
        // the source is ambiguous; do not pick one and create a false link.
        var importedMatches = new List<T>();
        foreach (var ns in importedNamespaces.Distinct(StringComparer.Ordinal))
        {
            var importedKey = !string.IsNullOrEmpty(writtenNamespace)
                ? $"{ns}.{writtenNamespace}.{simpleName}"
                : $"{ns}.{simpleName}";
            if (_byFullName.TryGetValue(importedKey, out var hits))
            {
                var picked = PickHit(hits, ownerProject);
                if (picked is not null && !importedMatches.Contains(picked))
                    importedMatches.Add(picked);
            }
        }

        if (importedMatches.Count == 1)
            return importedMatches[0];
        if (importedMatches.Count > 1)
            return null;

        if (!string.IsNullOrEmpty(fallbackNamespace) &&
            _byFullName.TryGetValue(simpleName, out var globalHits))
        {
            return PickHit(globalHits, ownerProject);
        }

        return _bySimpleName.TryGetValue(simpleName, out var matches) && matches.Count == 1
            ? matches[0]
            : null;
    }

    private T? PickHit(List<T> hits, string? ownerProject)
    {
        if (hits.Count == 1)
            return hits[0];

        if (!string.IsNullOrEmpty(ownerProject))
        {
            var sameProject = hits
                .Where(item => string.Equals(
                    _projectName(item), ownerProject, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (sameProject.Count == 1)
                return sameProject[0];
        }

        // Duplicate declarations of the same fully-qualified shape cannot be
        // distinguished by source position; fail closed instead of linking to hits[0].
        return null;
    }

    private static void Add(Dictionary<string, List<T>> map, string key, T value)
    {
        if (!map.TryGetValue(key, out var list))
            map[key] = list = new List<T>();
        list.Add(value);
    }
}
