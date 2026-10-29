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
        var totalAllocatedStart = GC.GetTotalAllocatedBytes(precise: false);
        var phaseWatch = Stopwatch.StartNew();
        var phaseAllocatedStart = totalAllocatedStart;
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

        void EndPhase(string name)
        {
            phaseWatch.Stop();
            var allocated = GC.GetTotalAllocatedBytes(precise: false) - phaseAllocatedStart;
            result.PhaseTimings.Add(new AnalysisPhaseTiming(name, phaseWatch.Elapsed.TotalMilliseconds, allocated));
            phaseWatch.Restart();
            phaseAllocatedStart = GC.GetTotalAllocatedBytes(precise: false);
        }

        options.Progress?.Invoke(new ScanProgress("Discovering projects", 0, 4));
        result.Projects.AddRange(CsprojParser.LoadProjects(root));
        foreach (var project in result.Projects)
        {
            project.Layer = options.LayerOverrides.TryGetValue(project.Name, out var layerOverride)
                ? layerOverride
                : LayerClassifier.Classify(project.Name, project.DirectoryPath, root);
        }
        EndPhase("Discover projects");

        options.Progress?.Invoke(new ScanProgress("Reading source files", 1, 4, $"{result.Projects.Count} project(s)"));
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
        EndPhase("Read sources");

        // Parse every file exactly once. The text scan and the code mask are reused by
        // the shape scanner, the deep-slice analyzer and Roslyn's text fallback.
        options.Progress?.Invoke(new ScanProgress("Parsing C# files", 1, 4, $"{sourceFiles.Count} file(s)"));
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
        EndPhase("Parse sources");

        options.Progress?.Invoke(new ScanProgress("Analyzing C# shapes", 2, 4, $"{sourceFiles.Count} file(s) · {result.ScannerName}"));
        result.Conventions.AddRange(MediatorConventions.Detect(result.Projects, sourceFiles));
        markerSet = MediatorConventions.EffectiveMarkers(markerSet, result.Conventions);
        var handlerMarkerSet = MediatorConventions.EffectiveHandlerMarkers(options.HandlerMarkers, result.Conventions);

        scanner.Scan(workspace, markerSet, handlerMarkerSet, result);
        if (result.Conventions.Contains(MediatorConventions.WolverineName))
            MediatorConventions.ApplyWolverine(sourceFiles, result);
        result.SkippedFiles.Sort(StringComparer.OrdinalIgnoreCase);
        result.FileCount = Math.Max(0, result.DiscoveredFileCount - result.SkippedFiles.Count);
        EndPhase("Scan shapes");

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

        options.Progress?.Invoke(new ScanProgress("Linking CQRS & checks", 3, 4, $"{result.Requests.Count} request(s)"));
        Link(result);
        EndPhase("Link shapes");

        DeepSliceAnalyzer.Analyze(workspace, result);
        EndPhase("Deep slice");

        result.Checks.AddRange(ArchitectureChecker.ApplyRuleSeverities(
            ArchitectureChecker.RunChecks(result),
            options.RuleSeverities));
        EndPhase("Architecture checks");

        options.Progress?.Invoke(new ScanProgress("Scan complete", 4, 4, $"{result.Requests.Count} request(s), {result.Handlers.Count} handler(s)"));
        stopwatch.Stop();
        result.ElapsedMilliseconds = options.GeneratedAt is null
            ? stopwatch.ElapsedMilliseconds
            : 0;
        result.AllocatedBytes = GC.GetTotalAllocatedBytes(precise: false) - totalAllocatedStart;
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
        var byFullName = new Dictionary<string, List<CqrsRequest>>(StringComparer.Ordinal);
        var bySimpleName = new Dictionary<string, List<CqrsRequest>>(StringComparer.Ordinal);
        foreach (var request in result.Requests)
        {
            if (!byFullName.TryGetValue(request.FullName, out var fullList))
                byFullName[request.FullName] = fullList = new List<CqrsRequest>();
            fullList.Add(request);

            if (!bySimpleName.TryGetValue(request.Name, out var list))
                bySimpleName[request.Name] = list = new List<CqrsRequest>();
            list.Add(request);
        }

        static CqrsRequest? PickHit(List<CqrsRequest> hits, string? ownerProject)
        {
            if (hits.Count == 1)
                return hits[0];
            if (!string.IsNullOrEmpty(ownerProject))
            {
                var sameProject = hits
                    .Where(r => string.Equals(r.ProjectName, ownerProject, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (sameProject.Count == 1)
                    return sameProject[0];
            }
            // Duplicate declarations of the same fully-qualified request cannot be
            // distinguished by source position; fail closed instead of linking to hits[0].
            return null;
        }

        CqrsRequest? Resolve(
            string simpleName,
            string? writtenNs,
            string? fallbackNs,
            IReadOnlyList<string> importedNamespaces,
            string? ownerProject)
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
            else
                candidates.Add(simpleName);

            foreach (var candidate in candidates)
            {
                if (byFullName.TryGetValue(candidate, out var hits))
                    return PickHit(hits, ownerProject);
            }

            // An unqualified request name is often imported by a using in the
            // handler/validator file. Two imported matches mean the source is
            // ambiguous; do not pick one and create a false link.
            var importedMatches = new List<CqrsRequest>();
            foreach (var ns in importedNamespaces.Distinct(StringComparer.Ordinal))
            {
                if (byFullName.TryGetValue($"{ns}.{simpleName}", out var hits))
                {
                    var picked = PickHit(hits, ownerProject);
                    if (picked is not null && !importedMatches.Contains(picked))
                        importedMatches.Add(picked);
                }
            }

            if (importedMatches.Count == 1)
                return importedMatches[0];
            if (importedMatches.Count > 1)
                return null;

            if (!string.IsNullOrEmpty(fallbackNs) &&
                byFullName.TryGetValue(simpleName, out var globalHits))
            {
                return PickHit(globalHits, ownerProject);
            }

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
                handler.ImportedNamespaces,
                handler.ProjectName);
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
                validator.ImportedNamespaces,
                validator.ProjectName);
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

        LinkNotifications(result);
    }

    private static void LinkNotifications(AnalysisResult result)
    {
        var byFullName = new Dictionary<string, List<CqrsNotification>>(StringComparer.Ordinal);
        var bySimpleName = new Dictionary<string, List<CqrsNotification>>(StringComparer.Ordinal);
        foreach (var notification in result.Notifications)
        {
            if (!byFullName.TryGetValue(notification.FullName, out var fullList))
                byFullName[notification.FullName] = fullList = new List<CqrsNotification>();
            fullList.Add(notification);

            if (!bySimpleName.TryGetValue(notification.Name, out var list))
                bySimpleName[notification.Name] = list = new List<CqrsNotification>();
            list.Add(notification);
        }

        static CqrsNotification? PickHit(List<CqrsNotification> hits, string? ownerProject)
        {
            if (hits.Count == 1)
                return hits[0];
            if (!string.IsNullOrEmpty(ownerProject))
            {
                var sameProject = hits
                    .Where(n => string.Equals(n.ProjectName, ownerProject, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (sameProject.Count == 1)
                    return sameProject[0];
            }
            return null;
        }

        CqrsNotification? Resolve(
            string simpleName,
            string? writtenNs,
            string? fallbackNs,
            IReadOnlyList<string> importedNamespaces,
            string? ownerProject)
        {
            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(writtenNs))
            {
                candidates.Add($"{writtenNs}.{simpleName}");
                if (!string.IsNullOrEmpty(fallbackNs))
                    candidates.Add($"{fallbackNs}.{writtenNs}.{simpleName}");
            }
            if (!string.IsNullOrEmpty(fallbackNs))
                candidates.Add($"{fallbackNs}.{simpleName}");
            else
                candidates.Add(simpleName);

            foreach (var candidate in candidates)
            {
                if (byFullName.TryGetValue(candidate, out var hits))
                    return PickHit(hits, ownerProject);
            }

            var importedMatches = new List<CqrsNotification>();
            foreach (var ns in importedNamespaces.Distinct(StringComparer.Ordinal))
            {
                if (byFullName.TryGetValue($"{ns}.{simpleName}", out var hits))
                {
                    var picked = PickHit(hits, ownerProject);
                    if (picked is not null && !importedMatches.Contains(picked))
                        importedMatches.Add(picked);
                }
            }

            if (importedMatches.Count == 1)
                return importedMatches[0];
            if (importedMatches.Count > 1)
                return null;

            if (!string.IsNullOrEmpty(fallbackNs) &&
                byFullName.TryGetValue(simpleName, out var globalHits))
            {
                return PickHit(globalHits, ownerProject);
            }

            return bySimpleName.TryGetValue(simpleName, out var matches) && matches.Count == 1
                ? matches[0]
                : null;
        }

        foreach (var handler in result.NotificationHandlers.OrderBy(h => h.Name, StringComparer.OrdinalIgnoreCase))
        {
            var notification = Resolve(
                handler.NotificationName,
                handler.NotificationNamespace,
                handler.Namespace,
                handler.ImportedNamespaces,
                handler.ProjectName);
            if (notification is null)
            {
                if (bySimpleName.TryGetValue(handler.NotificationName, out var matches) && matches.Count >= 2)
                {
                    var names = string.Join(
                        ", ",
                        matches.Select(m => m.FullName).OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
                    result.RecordDiagnostic(new AnalysisDiagnostic(
                        AnalysisResult.AmbiguousLinkCode,
                        AnalysisDiagnosticSeverity.Warning,
                        $"NotificationHandler `{handler.FullName}` targets `{handler.NotificationName}`, which matches {matches.Count} notifications ({names}); the link was omitted.",
                        handler.Location,
                        handler.ProjectName));
                }
                continue;
            }

            handler.NotificationNamespace ??= notification.Namespace;
            if (!notification.Handlers.Contains(handler.FullName))
                notification.Handlers.Add(handler.FullName);
        }

        foreach (var notification in result.Notifications)
            notification.Handlers.Sort(StringComparer.OrdinalIgnoreCase);

        result.Notifications.Sort((a, b) =>
        {
            var byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : string.Compare(a.FullName, b.FullName, StringComparison.Ordinal);
        });
    }
}
