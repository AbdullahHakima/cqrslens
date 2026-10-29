namespace CqrsLens;

/// <summary>Runs an analysis for an MCP tool call, reusing the cached scan when inputs are unchanged.</summary>
internal static class McpScanProvider
{
    public static AnalysisResult GetOrRun(
        string path,
        string? analysisOverride,
        CliOptions defaultOptions,
        McpScanCache cache)
    {
        var fullRoot = Path.GetFullPath(path);
        var settings = ConfigurationLoader.Load(fullRoot, defaultOptions.ConfigPath);
        var mode = ResolveAnalysisMode(analysisOverride, defaultOptions, settings);
        var cacheKey = $"{fullRoot}|{mode}";
        var fingerprint = McpScanFingerprintCalculator.Calculate(
            fullRoot,
            defaultOptions.ConfigPath,
            defaultOptions.BaselinePath);

        if (cache.TryGet(cacheKey, fingerprint, out var cached))
            return cached;

        var markers = defaultOptions.MarkersSpecified ? defaultOptions.Markers : settings.Markers;
        var handlerMarkers = defaultOptions.HandlerMarkersSpecified
            ? defaultOptions.HandlerMarkers
            : settings.HandlerMarkers;

        var analysisOptions = new AnalysisOptions
        {
            Markers = markers,
            HandlerMarkers = handlerMarkers,
            Include = settings.Include,
            Exclude = settings.Exclude,
            LayerOverrides = settings.LayerOverrides,
            RuleSeverities = settings.RuleSeverities,
        };

        var result = Analyzer.Analyze(fullRoot, mode, analysisOptions);
        if (defaultOptions.BaselinePath is not null && File.Exists(defaultOptions.BaselinePath))
        {
            var baseline = BaselineStore.Read(defaultOptions.BaselinePath);
            BaselineMatcher.Apply(result, baseline, defaultOptions.BaselinePath);
        }

        cache.Set(cacheKey, result, fingerprint);
        return result;
    }

    private static AnalysisMode ResolveAnalysisMode(
        string? analysisOverride,
        CliOptions defaultOptions,
        CqrsLensSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(analysisOverride))
        {
            return string.Equals(analysisOverride, "roslyn", StringComparison.OrdinalIgnoreCase)
                ? AnalysisMode.Roslyn
                : AnalysisMode.Text;
        }

        return defaultOptions.AnalysisModeSpecified
            ? defaultOptions.AnalysisMode
            : settings.AnalysisMode ?? defaultOptions.AnalysisMode;
    }
}
