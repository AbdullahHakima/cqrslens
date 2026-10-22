using System.Collections.Concurrent;
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
        };

        result.Projects.AddRange(CsprojParser.LoadProjects(root));
        foreach (var project in result.Projects)
        {
            project.Layer = options.LayerOverrides.TryGetValue(project.Name, out var layerOverride)
                ? layerOverride
                : LayerClassifier.Classify(project.Name, project.DirectoryPath, root);
        }

        var pathFilter = new SourcePathFilter(options.Include, options.Exclude);
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
                FindOwningProject(result.Projects, f.File)?.Name))
            .ToList();

        scanner.Scan(sourceFiles, markerSet, options.HandlerMarkers, result);
        result.SkippedFiles.Sort(StringComparer.OrdinalIgnoreCase);
        result.FileCount = Math.Max(0, result.DiscoveredFileCount - result.SkippedFiles.Count);

        foreach (var skipped in result.SkippedFiles)
        {
            var fullPath = Path.Combine(root, skipped.Replace('/', Path.DirectorySeparatorChar));
            var project = FindOwningProject(result.Projects, fullPath);
            result.RecordDiagnostic(new AnalysisDiagnostic(
                AnalysisResult.SourceFileSkippedCode,
                AnalysisDiagnosticSeverity.Warning,
                $"Source file `{skipped}` could not be read or scanned; shapes in this file are missing.",
                SourceLocation.FromLine(fullPath, root, 1, 1),
                project?.Name));
        }

        Link(result);
        result.Checks.AddRange(ArchitectureChecker.ApplyRuleSeverities(
            ArchitectureChecker.RunChecks(result),
            options.RuleSeverities));

        stopwatch.Stop();
        result.ElapsedMilliseconds = options.GeneratedAt is null
            ? stopwatch.ElapsedMilliseconds
            : 0;
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

        CqrsRequest? Resolve(
            string simpleName,
            string? writtenNs,
            string? fallbackNs,
            IReadOnlyList<string> importedNamespaces)
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

            // An unqualified request name is often imported by a using in the
            // handler/validator file. Two imported matches mean the source is
            // ambiguous; do not pick one and create a false link.
            var importedMatches = new List<CqrsRequest>();
            foreach (var ns in importedNamespaces.Distinct(StringComparer.Ordinal))
            {
                if (byFullName.TryGetValue($"{ns}.{simpleName}", out var hit) &&
                    !importedMatches.Contains(hit))
                    importedMatches.Add(hit);
            }

            if (importedMatches.Count == 1)
                return importedMatches[0];
            if (importedMatches.Count > 1)
                return null;

            return bySimpleName.TryGetValue(simpleName, out var matches) && matches.Count == 1
                ? matches[0]
                : null;
        }

        void RecordAmbiguousLink(
            string ownerKind,
            string owner,
            string target,
            SourceLocation? location,
            string? project)
        {
            if (!bySimpleName.TryGetValue(target, out var matches) || matches.Count < 2)
                return;

            var names = string.Join(
                ", ",
                matches.Select(m => m.FullName).OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
            result.RecordDiagnostic(new AnalysisDiagnostic(
                AnalysisResult.AmbiguousLinkCode,
                AnalysisDiagnosticSeverity.Warning,
                $"{ownerKind} `{owner}` targets `{target}`, which matches {matches.Count} requests ({names}); the link was omitted.",
                location,
                project));
        }

        foreach (var handler in result.Handlers.OrderBy(h => h.Name, StringComparer.OrdinalIgnoreCase))
        {
            var request = Resolve(
                handler.RequestName,
                handler.RequestNamespace,
                handler.Namespace,
                handler.ImportedNamespaces);
            if (request is null)
            {
                RecordAmbiguousLink(
                    "Handler",
                    handler.FullName,
                    handler.RequestName,
                    handler.Location,
                    handler.ProjectName);
                continue;
            }

            // Text mode only knows a written namespace when the generic argument was
            // qualified. Once linking has identified the real request, retain that
            // identity so text and Roslyn results describe the same shape.
            handler.RequestNamespace ??= request.Namespace;
            if (!request.Handlers.Contains(handler.FullName))
                request.Handlers.Add(handler.FullName);
        }

        foreach (var validator in result.Validators.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase))
        {
            var request = Resolve(
                validator.TargetType,
                validator.TargetNamespace,
                validator.Namespace,
                validator.ImportedNamespaces);
            if (request is null)
            {
                RecordAmbiguousLink(
                    "Validator",
                    validator.FullName,
                    validator.TargetType,
                    validator.Location,
                    validator.ProjectName);
                continue;
            }

            validator.TargetNamespace ??= request.Namespace;
            if (!request.Validators.Contains(validator.FullName))
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
