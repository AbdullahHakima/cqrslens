using System.Collections.Concurrent;
using System.Diagnostics;

namespace CqrsLens;

/// <summary>Runs the full static analysis over a directory tree.</summary>
public static class Analyzer
{
    public static AnalysisResult Analyze(string rootPath, IReadOnlySet<string>? markers = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var root = Path.GetFullPath(rootPath);
        var markerSet = markers ?? new HashSet<string>(StringComparer.Ordinal);
        var result = new AnalysisResult { RootPath = root };

        result.Projects.AddRange(CsprojParser.LoadProjects(root));
        foreach (var project in result.Projects)
            project.Layer = LayerClassifier.Classify(project.Name, project.DirectoryPath, root);

        var files = CsprojParser.FindSourceFiles(root);
        result.FileCount = files.Count;

        // Scan files in parallel into per-file fragments; merging happens afterwards
        // in deterministic (sorted path) order, so output ordering is stable.
        var fragments = new ConcurrentBag<(string File, string Code, CSharpScanner.FileScan Scan)>();
        Parallel.ForEach(files, file =>
        {
            try
            {
                var code = File.ReadAllText(file);
                fragments.Add((file, code, CSharpScanner.ScanText(code)));
            }
            catch
            {
                // An unreadable file should not abort the whole scan.
            }
        });

        var ordered = fragments
            .OrderBy(f => f.File, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rawInterfaceMap = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (_, _, scan) in ordered)
        {
            foreach (var (name, bases) in scan.Interfaces)
            {
                var fullName = string.IsNullOrEmpty(scan.Namespace) ? name : $"{scan.Namespace}.{name}";
                if (!rawInterfaceMap.TryGetValue(fullName, out var list))
                    rawInterfaceMap[fullName] = list = new List<string>();
                foreach (var b in bases)
                    if (!list.Contains(b)) list.Add(b);
            }
        }

        var interfacesBySimpleName = rawInterfaceMap.Keys
            .GroupBy(CSharpScanner.SimpleName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var interfaceMap = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (name, bases) in rawInterfaceMap)
        {
            var ns = CSharpScanner.NamespaceOf(name);
            interfaceMap[name] = bases
                .Select(b => ResolveInterfaceName(b, ns, rawInterfaceMap, interfacesBySimpleName))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        var projectByFile = ordered.ToDictionary(
            f => f.File,
            f => FindOwningProject(result.Projects, f.File)?.Name,
            StringComparer.OrdinalIgnoreCase);

        foreach (var (file, code, scan) in ordered)
        {
            CSharpScanner.ApplyScan(scan, code, projectByFile[file], result);
            DiscoverMarkerRequests(scan, projectByFile[file], interfaceMap, interfacesBySimpleName, markerSet, result);
        }

        Link(result);
        result.Checks.AddRange(ArchitectureChecker.RunChecks(result));

        stopwatch.Stop();
        result.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
        return result;
    }

    /// <summary>
    /// Finds request classes hidden behind marker/custom interfaces (issue #2):
    /// classes whose interface closure reaches IRequest (through custom
    /// abstractions) or any configured --markers root.
    /// </summary>
    private static void DiscoverMarkerRequests(
        CSharpScanner.FileScan scan,
        string? projectName,
        Dictionary<string, List<string>> interfaceMap,
        Dictionary<string, List<string>> interfacesBySimpleName,
        IReadOnlySet<string> markers,
        AnalysisResult result)
    {
        foreach (var decl in scan.Classes)
        {
            if (decl.BaseRawNames.Contains("IRequest")) continue; // direct hit already recorded
            if (decl.BaseRawNames.Contains("IRequestHandler")) continue;
            if (decl.BaseRawNames.Count == 0) continue;

            // Skip types that are other known shapes.
            if (decl.BaseRawNames.Any(b => b is "DbContext" or "IdentityDbContext" or
                                                "AbstractValidator" or "IValidator" or
                                                "IPipelineBehavior"))
                continue;

            var directInterfaces = decl.BaseTypeNames
                .Select(name => ResolveInterfaceName(name, scan.Namespace, interfaceMap, interfacesBySimpleName));
            var reached = MarkerResolver.ReachableMarkers(directInterfaces, interfaceMap, markers);
            if (reached.Count == 0) continue;

            // A class is only a request when the closure reaches IRequest or a
            // configured marker — ReachableMarkers only returns such roots.
            if (!reached.Contains("IRequest") && !reached.Any(markers.Contains))
                continue;

            var via = reached.FirstOrDefault(m => m != "IRequest")
                      ?? decl.BaseRawNames.FirstOrDefault(b => b != "IRequest");

            // Avoid double-reporting the same type name + namespace (e.g. partials).
            if (result.Requests.Any(r => r.Name == decl.Name && r.Namespace == scan.Namespace))
                continue;

            var request = new CqrsRequest
            {
                Name = decl.Name,
                Namespace = scan.Namespace,
                ProjectName = projectName,
                ViaMarker = via,
            };
            request.ResponseType =
                MarkerResolver.TryExtractMarkerResponse(decl, reached)
                ?? decl.BaseRawNames
                    .Select(b => CSharpScanner.ExtractGenericArguments(decl.BaseList, b)?.FirstOrDefault())
                    .FirstOrDefault(a => a is not null);
            if (!request.Name.EndsWith("Command", StringComparison.Ordinal) &&
                !request.Name.EndsWith("Query", StringComparison.Ordinal))
                request.Kind = MarkerResolver.ClassifyMarkerRequest(
                    request.Name, decl.BaseRawNames.Concat(reached));
            result.Requests.Add(request);
        }
    }

    private static string ResolveInterfaceName(
        string name,
        string currentNamespace,
        IReadOnlyDictionary<string, List<string>> interfaceMap,
        IReadOnlyDictionary<string, List<string>> interfacesBySimpleName)
    {
        if (name.Contains('.')) return name;

        var localName = string.IsNullOrEmpty(currentNamespace) ? name : $"{currentNamespace}.{name}";
        if (interfaceMap.ContainsKey(localName))
            return localName;

        return interfacesBySimpleName.TryGetValue(name, out var candidates) && candidates.Count == 1
            ? candidates[0]
            : name;
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
            foreach (var ns in new[] { writtenNs, fallbackNs })
            {
                if (!string.IsNullOrEmpty(ns) && byFullName.TryGetValue($"{ns}.{simpleName}", out var hit))
                    return hit;
            }
            return bySimpleName.TryGetValue(simpleName, out var candidates) && candidates.Count == 1
                ? candidates[0]
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
