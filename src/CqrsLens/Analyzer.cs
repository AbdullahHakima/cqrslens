using System.Collections.Concurrent;
using System.Diagnostics;

namespace CqrsLens;

/// <summary>Runs the full static analysis over a directory tree.</summary>
public static class Analyzer
{
    private static readonly IArchitectureScanner DefaultScanner = new TextArchitectureScanner();
    private static readonly IArchitectureScanner RoslynScanner = new RoslynArchitectureScanner();

    public static AnalysisResult Analyze(string rootPath, IReadOnlySet<string>? markers = null)
        => Analyze(rootPath, DefaultScanner, markers);

    public static AnalysisResult Analyze(
        string rootPath,
        AnalysisMode mode,
        IReadOnlySet<string>? markers = null)
        => Analyze(
            rootPath,
            mode == AnalysisMode.Roslyn ? RoslynScanner : DefaultScanner,
            markers);

    internal static AnalysisResult Analyze(
        string rootPath,
        IArchitectureScanner scanner,
        IReadOnlySet<string>? markers = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var root = Path.GetFullPath(rootPath);
        var markerSet = markers ?? new HashSet<string>(StringComparer.Ordinal);
        var result = new AnalysisResult { RootPath = root };

        result.Projects.AddRange(CsprojParser.LoadProjects(root));
        foreach (var project in result.Projects)
            project.Layer = LayerClassifier.Classify(project.Name, project.DirectoryPath, root);

        var files = CsprojParser.FindSourceFiles(root);
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
                FindOwningProject(result.Projects, f.File)?.Name))
            .ToList();

        scanner.Scan(sourceFiles, markerSet, result);
        result.SkippedFiles.Sort(StringComparer.OrdinalIgnoreCase);
        result.FileCount = Math.Max(0, result.DiscoveredFileCount - result.SkippedFiles.Count);

        Link(result);
        result.Checks.AddRange(ArchitectureChecker.RunChecks(result));

        stopwatch.Stop();
        result.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
        return result;
    }

    private static ProjectInfo? FindOwningProject(List<ProjectInfo> projects, string file)
    {
        ProjectInfo? best = null;
        foreach (var p in projects)
        {
            var dir = p.DirectoryPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (file.StartsWith(dir, StringComparison.OrdinalIgnoreCase) &&
                (best is null || dir.Length > best.DirectoryPath.Length))
                best = p;
        }
        return best;
    }

    /// <summary>
    /// Connects handlers and validators to the requests they serve. Edges resolve by
    /// fully-qualified name first (issue #3); a simple-name match is used only when
    /// exactly one request carries that name, so same-named requests in different
    /// namespaces never share links.
    /// </summary>
    private static void Link(AnalysisResult result)
    {
        var byFullName = new Dictionary<string, CqrsRequest>(StringComparer.Ordinal);
        var bySimpleName = new Dictionary<string, List<CqrsRequest>>(StringComparer.Ordinal);
        foreach (var request in result.Requests)
        {
            byFullName.TryAdd(request.FullName, request);
            if (!bySimpleName.TryGetValue(request.Name, out var list))
                bySimpleName[request.Name] = list = new List<CqrsRequest>();
            list.Add(request);
        }

        CqrsRequest? Resolve(string simpleName, string? writtenNs, string? fallbackNs)
        {
            // A handler may write a type as `Outer.Inner`, `Ns.Outer.Inner`, or just
            // `Inner`; try the nested-type path and the handler's own namespace.
            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(writtenNs))
            {
                candidates.Add($"{writtenNs}.{simpleName}");
                if (!string.IsNullOrEmpty(fallbackNs))
                    candidates.Add($"{fallbackNs}.{writtenNs}.{simpleName}");
            }
            if (!string.IsNullOrEmpty(fallbackNs))
                candidates.Add($"{fallbackNs}.{simpleName}");
            candidates.Add(simpleName);

            foreach (var candidate in candidates)
            {
                if (byFullName.TryGetValue(candidate, out var hit))
                    return hit;
            }

            return bySimpleName.TryGetValue(simpleName, out var matches) && matches.Count == 1
                ? matches[0]
                : null;
        }

        foreach (var handler in result.Handlers.OrderBy(h => h.Name, StringComparer.OrdinalIgnoreCase))
        {
            var request = Resolve(handler.RequestName, handler.RequestNamespace, handler.Namespace);
            if (request is not null && !request.Handlers.Contains(handler.FullName))
                request.Handlers.Add(handler.FullName);
        }

        foreach (var validator in result.Validators.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase))
        {
            var request = Resolve(validator.TargetType, validator.TargetNamespace, validator.Namespace);
            if (request is not null && !request.Validators.Contains(validator.FullName))
                request.Validators.Add(validator.FullName);
        }

        foreach (var request in result.Requests)
        {
            request.Handlers.Sort(StringComparer.OrdinalIgnoreCase);
            request.Validators.Sort(StringComparer.OrdinalIgnoreCase);
        }

        result.Requests.Sort((a, b) =>
        {
            var byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : string.Compare(a.FullName, b.FullName, StringComparison.Ordinal);
        });
    }
}
