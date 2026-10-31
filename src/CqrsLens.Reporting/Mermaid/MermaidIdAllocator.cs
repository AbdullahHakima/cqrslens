namespace CqrsLens;

/// <summary>Allocates collision-free ids for one diagram, preserving readable bases.</summary>
internal sealed class MermaidIdAllocator
{
    private readonly Dictionary<string, string> _ids = new(StringComparer.Ordinal);
    private readonly HashSet<string> _used = new(StringComparer.Ordinal);

    internal string Allocate(string key, string prefix, string preferredName)
    {
        if (_ids.TryGetValue(key, out var existing))
            return existing;

        var baseId = prefix + "_" + MermaidDiagramText.SanitizeMermaidId(preferredName);
        var id = baseId;
        var suffix = 2;
        while (!_used.Add(id))
            id = $"{baseId}_{suffix++}";

        _ids[key] = id;
        return id;
    }
}
