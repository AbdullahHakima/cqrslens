using static CqrsLens.JsonReportGenerator;

namespace CqrsLens;

/// <summary>Loads a diff baseline from either a live directory scan or a JSON report file.</summary>
internal static class ArchitectureSnapshotLoader
{
    internal static JsonReportDocument LoadSnapshot(
        string path,
        AnalysisMode analysisMode,
        AnalysisOptions options)
    {
        if (Directory.Exists(path))
        {
            var fullDir = Path.GetFullPath(path);
            if (CsprojParser.FindProjectFiles(fullDir).Count == 0)
                throw new DiffException($"no .csproj files found under diff directory: {path}");

            var baseSettings = ConfigurationLoader.Load(fullDir, null);
            var baseResult = Analyzer.Analyze(
                path,
                baseSettings.AnalysisMode ?? analysisMode,
                new AnalysisOptions
                {
                    Markers = options.Markers.Count > 0 ? options.Markers : baseSettings.Markers,
                    HandlerMarkers = options.HandlerMarkers.Count > 0 ? options.HandlerMarkers : baseSettings.HandlerMarkers,
                    GeneratedAt = options.GeneratedAt,
                    Include = baseSettings.Include.Count > 0 ? baseSettings.Include : options.Include,
                    Exclude = baseSettings.Exclude.Count > 0 ? baseSettings.Exclude : options.Exclude,
                    LayerOverrides = baseSettings.LayerOverrides.Count > 0 ? baseSettings.LayerOverrides : options.LayerOverrides,
                    RuleSeverities = baseSettings.RuleSeverities.Count > 0 ? baseSettings.RuleSeverities : options.RuleSeverities,
                });
            return CreateDocument(baseResult);
        }

        if (File.Exists(path))
            return ReadDocument(path);

        throw new DiffException($"diff path not found: {path}");
    }
}
