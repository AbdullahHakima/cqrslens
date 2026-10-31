namespace CqrsLens;

/// <summary>Include/exclude filtering for source paths relative to the scan root.</summary>
public sealed class SourcePathFilter
{
    private readonly IReadOnlyList<GlobPattern> _includes;
    private readonly IReadOnlyList<GlobPattern> _excludes;

    public SourcePathFilter(
        IEnumerable<string>? includes = null,
        IEnumerable<string>? excludes = null)
    {
        _includes = (includes ?? Array.Empty<string>())
            .Select(p => new GlobPattern(p))
            .ToArray();
        _excludes = (excludes ?? Array.Empty<string>())
            .Select(p => new GlobPattern(p))
            .ToArray();
    }

    public bool IsIncluded(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        if (_includes.Count > 0 && !_includes.Any(p => p.IsMatch(normalized)))
            return false;
        return !_excludes.Any(p => p.IsMatch(normalized));
    }
}
