namespace CqrsLens;

/// <summary>
/// Optional analysis inputs after CLI/config resolution. The public Analyzer
/// overloads use an empty instance, so in-process callers keep the old default
/// behavior.
/// </summary>
public sealed class AnalysisOptions
{
    public IReadOnlySet<string> Markers { get; init; } =
        new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Interface names that count as handler roots in addition to
    /// IRequestHandler. Useful for wrappers supplied by an external package
    /// whose inheritance chain is not part of the scanned source.
    /// </summary>
    public IReadOnlySet<string> HandlerMarkers { get; init; } =
        new HashSet<string>(StringComparer.Ordinal);

    public DateTimeOffset? GeneratedAt { get; init; }

    /// <summary>Root-relative include globs; empty means include everything.</summary>
    public IReadOnlyList<string> Include { get; init; } = Array.Empty<string>();

    /// <summary>Root-relative exclude globs applied after includes.</summary>
    public IReadOnlyList<string> Exclude { get; init; } = Array.Empty<string>();

    /// <summary>Project name to layer overrides; names are matched case-insensitively.</summary>
    public IReadOnlyDictionary<string, string> LayerOverrides { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Rule code to severity. A null value turns the rule off. Rules omitted
    /// from the dictionary keep their built-in severity.
    /// </summary>
    public IReadOnlyDictionary<string, CheckSeverity?> RuleSeverities { get; init; } =
        new Dictionary<string, CheckSeverity?>(StringComparer.OrdinalIgnoreCase);
}
