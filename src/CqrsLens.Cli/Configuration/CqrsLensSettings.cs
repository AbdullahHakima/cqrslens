namespace CqrsLens;

/// <summary>Validated .cqrslens.json values.</summary>
public sealed record CqrsLensSettings(
    string? ConfigPath,
    AnalysisMode? AnalysisMode,
    IReadOnlySet<string> Markers,
    IReadOnlySet<string> HandlerMarkers,
    IReadOnlyList<string> Include,
    IReadOnlyList<string> Exclude,
    IReadOnlyDictionary<string, string> LayerOverrides,
    IReadOnlyDictionary<string, CheckSeverity?> RuleSeverities,
    DiagramRenderMode? RenderMode = null,
    string? Watermark = null)
{
    public static readonly CqrsLensSettings Empty = new(
        null,
        null,
        new HashSet<string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal),
        Array.Empty<string>(),
        Array.Empty<string>(),
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, CheckSeverity?>(StringComparer.OrdinalIgnoreCase),
        null);
}
