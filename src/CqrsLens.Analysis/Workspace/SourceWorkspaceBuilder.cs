using System.Collections.Concurrent;

namespace CqrsLens;

/// <summary>
/// Reads and parses every included source file once. The resulting workspace is
/// reused by the text scanner, Roslyn scanner, EF builders and deep slicing.
/// </summary>
internal static class SourceWorkspaceBuilder
{
    public static SourceWorkspace Build(
        string root,
        SourcePathFilter pathFilter,
        AnalysisResult result,
        Action<ScanProgress>? progress,
        AnalysisPhaseRecorder phases)
    {
        progress?.Invoke(new ScanProgress(
            "Reading source files", 1, 4, $"{result.Projects.Count} project(s)"));

        var files = CsprojParser.FindSourceFiles(root)
            .Where(file => pathFilter.IsIncluded(
                Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/')))
            .ToList();
        result.DiscoveredFileCount = files.Count;

        var sources = new ConcurrentBag<(string File, string Code)>();
        var unreadable = new ConcurrentBag<string>();
        Parallel.ForEach(files, file =>
        {
            try
            {
                sources.Add((file, File.ReadAllText(file)));
            }
            catch
            {
                // Keep scanning best-effort, but make the omission visible in the report.
                unreadable.Add(file);
            }
        });

        foreach (var file in unreadable.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            result.RecordSkippedFile(file);

        var sourceFiles = sources
            .OrderBy(f => f.File, StringComparer.OrdinalIgnoreCase)
            .Select(f => new SourceFile(
                f.File,
                f.Code,
                ProjectLocator.FindOwningProject(result.Projects, f.File)?.Name))
            .ToList();
        phases.Record("Read sources");

        // Parse every file exactly once. The text scan and the code mask are reused by
        // the shape scanner, the deep-slice analyzer and Roslyn's text fallback.
        progress?.Invoke(new ScanProgress(
            "Parsing C# files", 1, 4, $"{sourceFiles.Count} file(s)"));
        var parsed = new ConcurrentBag<ParsedSourceFile>();
        var parseFailures = new ConcurrentBag<string>();
        Parallel.ForEach(sourceFiles, file =>
        {
            try
            {
                var mask = SourceMask.Build(file.Code);
                parsed.Add(new ParsedSourceFile(file, CSharpScanner.ScanText(file.Code, mask), mask));
            }
            catch
            {
                parseFailures.Add(file.Path);
            }
        });

        foreach (var path in parseFailures.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            result.RecordSkippedFile(path);

        var workspace = new SourceWorkspace(
            sourceFiles,
            parsed.OrderBy(p => p.File.Path, StringComparer.OrdinalIgnoreCase).ToList());
        phases.Record("Parse sources");
        return workspace;
    }
}
