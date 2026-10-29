namespace CqrsLens;

/// <summary>Validates scan inputs and merges command-line flags with .cqrslens.json.</summary>
internal static class ScanPreparer
{
    public static PreparedScan Prepare(CliOptions options)
    {
        var path = options.Path!;
        if (!Directory.Exists(path))
            throw new CliFailureException($"path not found: {path}", ExitCodes.PathNotFound);

        var root = Path.GetFullPath(path);
        var settings = ConfigurationLoader.Load(root, options.ConfigPath);
        var ciRoot = ResolveCiRoot(options.CiRoot);

        if (CsprojParser.FindProjectFiles(root).Count == 0)
        {
            throw new CliFailureException(
                $"no .csproj files found under: {path}{Environment.NewLine}" +
                "CqrsLens scans .NET solutions - point it at a folder containing at least one project.",
                ExitCodes.NoProjects);
        }

        return new PreparedScan(
            root,
            path,
            ciRoot,
            settings,
            options.AnalysisModeSpecified
                ? options.AnalysisMode
                : settings.AnalysisMode ?? options.AnalysisMode,
            options.RenderModeSpecified
                ? options.RenderMode
                : settings.RenderMode ?? options.RenderMode,
            options.MarkersSpecified ? options.Markers : settings.Markers,
            options.HandlerMarkersSpecified ? options.HandlerMarkers : settings.HandlerMarkers,
            options.WatermarkSpecified ? options.Watermark : settings.Watermark,
            options.GeneratedAt);
    }

    private static string? ResolveCiRoot(string? raw)
    {
        if (raw is null)
            return null;

        string full;
        try
        {
            full = Path.GetFullPath(raw);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new CliFailureException($"invalid --ci-root '{raw}': {ex.Message}", ExitCodes.Usage);
        }

        if (!Directory.Exists(full))
            throw new CliFailureException($"--ci-root directory not found: {raw}", ExitCodes.Usage);

        return full;
    }
}
