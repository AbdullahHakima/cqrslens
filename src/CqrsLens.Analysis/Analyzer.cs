using System.Diagnostics;

namespace CqrsLens;

/// <summary>Runs the full static analysis over a directory tree.</summary>
public static class Analyzer
{
    private static readonly IArchitectureScanner DefaultScanner = new TextArchitectureScanner();
    private static readonly IArchitectureScanner RoslynScanner = new RoslynArchitectureScanner();

    public static AnalysisResult Analyze(
        string rootPath,
        IReadOnlySet<string>? markers = null,
        DateTimeOffset? generatedAt = null)
        => Analyze(
            rootPath,
            DefaultScanner,
            new AnalysisOptions
            {
                Markers = markers ?? new HashSet<string>(StringComparer.Ordinal),
                GeneratedAt = generatedAt,
            });

    public static AnalysisResult Analyze(
        string rootPath,
        AnalysisMode mode,
        IReadOnlySet<string>? markers = null,
        DateTimeOffset? generatedAt = null)
        => Analyze(
            rootPath,
            mode,
            new AnalysisOptions
            {
                Markers = markers ?? new HashSet<string>(StringComparer.Ordinal),
                GeneratedAt = generatedAt,
            });

    public static AnalysisResult Analyze(
        string rootPath,
        AnalysisMode mode,
        AnalysisOptions options)
        => Analyze(
            rootPath,
            mode == AnalysisMode.Roslyn ? RoslynScanner : DefaultScanner,
            options);

    internal static AnalysisResult Analyze(
        string rootPath,
        IArchitectureScanner scanner,
        AnalysisOptions options)
    {
        var stopwatch = Stopwatch.StartNew();
        var totalAllocatedStart = GC.GetTotalAllocatedBytes(precise: false);
        var root = Path.GetFullPath(rootPath);
        var markerSet = options.Markers;
        var result = new AnalysisResult
        {
            RootPath = root,
            ScannerName = scanner.GetType().Name switch
            {
                nameof(TextArchitectureScanner) => "text",
                nameof(RoslynArchitectureScanner) => "roslyn",
                var name => name,
            },
            GeneratedAt = options.GeneratedAt ?? DateTimeOffset.UtcNow,
            Watermark = string.IsNullOrWhiteSpace(options.Watermark) ? null : options.Watermark.Trim(),
        };
        var phases = new AnalysisPhaseRecorder(result);

        DiscoverProjects(root, options, result);
        phases.Record("Discover projects");

        var pathFilter = new SourcePathFilter(options.Include, options.Exclude);
        var workspace = SourceWorkspaceBuilder.Build(
            root, pathFilter, result, options.Progress, phases);

        ScanShapes(workspace, scanner, markerSet, options, result);
        phases.Record("Scan shapes");

        RecordSkippedSourceDiagnostics(root, result);

        options.Progress?.Invoke(new ScanProgress(
            "Linking CQRS & checks", 3, 4, $"{result.Requests.Count} request(s)"));
        ShapeLinker.Link(result);
        phases.Record("Link shapes");

        DeepSliceAnalyzer.Analyze(workspace, result);
        phases.Record("Deep slice");

        result.Checks.AddRange(ArchitectureChecker.ApplyRuleSeverities(
            ArchitectureChecker.RunChecks(result),
            options.RuleSeverities));
        phases.Record("Architecture checks");

        options.Progress?.Invoke(new ScanProgress(
            "Scan complete", 4, 4,
            $"{result.Requests.Count} request(s), {result.Handlers.Count} handler(s)"));
        stopwatch.Stop();
        result.ElapsedMilliseconds = options.GeneratedAt is null
            ? stopwatch.ElapsedMilliseconds
            : 0;
        result.AllocatedBytes = GC.GetTotalAllocatedBytes(precise: false) - totalAllocatedStart;
        return result;
    }

    private static void DiscoverProjects(
        string root,
        AnalysisOptions options,
        AnalysisResult result)
    {
        options.Progress?.Invoke(new ScanProgress("Discovering projects", 0, 4));
        result.Projects.AddRange(CsprojParser.LoadProjects(root));
        result.HasConfiguredLayers = options.LayerOverrides.Count > 0;
        foreach (var project in result.Projects)
        {
            project.Layer = options.LayerOverrides.TryGetValue(project.Name, out var layerOverride)
                ? layerOverride
                : LayerClassifier.Classify(project.Name, project.DirectoryPath, root);
        }
    }

    private static void ScanShapes(
        SourceWorkspace workspace,
        IArchitectureScanner scanner,
        IReadOnlySet<string> markerSet,
        AnalysisOptions options,
        AnalysisResult result)
    {
        options.Progress?.Invoke(new ScanProgress(
            "Analyzing C# shapes", 2, 4,
            $"{workspace.Files.Count} file(s) · {result.ScannerName}"));
        result.Conventions.AddRange(MediatorConventions.Detect(result.Projects, workspace.Files));
        markerSet = MediatorConventions.EffectiveMarkers(markerSet, result.Conventions);
        var handlerMarkerSet = MediatorConventions.EffectiveHandlerMarkers(
            options.HandlerMarkers, result.Conventions);

        scanner.Scan(workspace, markerSet, handlerMarkerSet, result);
        if (result.Conventions.Contains(MediatorConventions.WolverineName))
            MediatorConventions.ApplyWolverine(workspace.Files, result);
        result.SkippedFiles.Sort(StringComparer.OrdinalIgnoreCase);
        result.FileCount = Math.Max(0, result.DiscoveredFileCount - result.SkippedFiles.Count);
    }

    private static void RecordSkippedSourceDiagnostics(string root, AnalysisResult result)
    {
        foreach (var skipped in result.SkippedFiles)
        {
            var fullPath = Path.Combine(root, skipped.Replace('/', Path.DirectorySeparatorChar));
            var project = ProjectLocator.FindOwningProject(result.Projects, fullPath);
            result.RecordDiagnostic(new AnalysisDiagnostic(
                AnalysisResult.SourceFileSkippedCode,
                AnalysisDiagnosticSeverity.Warning,
                $"Source file `{skipped}` could not be read or scanned; shapes in this file are missing.",
                SourceLocation.FromLine(fullPath, root, 1, 1),
                project?.Name));
        }
    }
}
