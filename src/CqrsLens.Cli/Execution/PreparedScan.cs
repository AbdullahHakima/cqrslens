namespace CqrsLens;

/// <summary>
/// A scan after CLI flags and .cqrslens.json have been merged. Everything the
/// pipeline needs comes from this object, so no later step re-reads the config.
/// </summary>
internal sealed record PreparedScan(
    string RootPath,
    string DisplayPath,
    string? CiRoot,
    CqrsLensSettings Settings,
    AnalysisMode AnalysisMode,
    DiagramRenderMode RenderMode,
    IReadOnlySet<string> Markers,
    IReadOnlySet<string> HandlerMarkers,
    string? Watermark,
    DateTimeOffset? GeneratedAt)
{
    public AnalysisOptions CreateAnalysisOptions(Action<ScanProgress>? progress = null) => new()
    {
        Markers = Markers,
        HandlerMarkers = HandlerMarkers,
        GeneratedAt = GeneratedAt,
        Watermark = Watermark,
        Progress = progress,
        Include = Settings.Include,
        Exclude = Settings.Exclude,
        LayerOverrides = Settings.LayerOverrides,
        RuleSeverities = Settings.RuleSeverities,
    };
}
