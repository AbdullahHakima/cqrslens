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
        var (mode, analysisOptions) = ResolveScanInputs(fullRoot, analysisOverride, defaultOptions);
        var cacheKey = $"{fullRoot}|{mode}";
        var fingerprint = McpScanFingerprintCalculator.Calculate(
            fullRoot,
            defaultOptions.ConfigPath,
            defaultOptions.BaselinePath);

        if (cache.TryGet(cacheKey, fingerprint, out var cached))
            return cached;

        var result = Analyzer.Analyze(fullRoot, mode, analysisOptions);
        if (defaultOptions.BaselinePath is not null && File.Exists(defaultOptions.BaselinePath))
        {
            var baseline = BaselineStore.Read(defaultOptions.BaselinePath);
            BaselineMatcher.Apply(result, baseline, defaultOptions.BaselinePath);
        }

        cache.Set(cacheKey, result, fingerprint);
        return result;
    }

    /// <summary>
    /// Resolves the analysis mode and options the provider would use for a scan, without scanning.
    /// The MCP diff tool passes these to the CLI's snapshot loader when its base operand is a folder,
    /// exactly as the CLI's <c>--diff</c> passes the head scan's options.
    /// </summary>
    internal static (AnalysisMode Mode, AnalysisOptions Options) ResolveScanInputs(
        string fullRoot,
        string? analysisOverride,
        CliOptions defaultOptions)
    {
        var settings = ConfigurationLoader.Load(fullRoot, defaultOptions.ConfigPath);
        var mode = ResolveAnalysisMode(analysisOverride, defaultOptions, settings);
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

        return (mode, analysisOptions);
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
