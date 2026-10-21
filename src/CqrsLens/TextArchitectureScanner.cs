using System.Collections.Concurrent;

namespace CqrsLens;

internal sealed class TextArchitectureScanner : IArchitectureScanner
{
    public void Scan(
        IReadOnlyList<SourceFile> files,
        IReadOnlySet<string> markers,
        AnalysisResult result)
    {
        var fragments = new ConcurrentBag<(SourceFile File, CSharpScanner.FileScan Scan)>();
        var failed = new ConcurrentBag<string>();
        Parallel.ForEach(files, file =>
        {
            try
            {
                fragments.Add((file, CSharpScanner.ScanText(file.Code)));
            }
            catch
            {
                // Keep text mode best-effort, but don't silently claim the file was scanned.
                failed.Add(file.Path);
            }
        });

        foreach (var path in failed.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            result.RecordSkippedFile(path);

        var ordered = fragments
            .OrderBy(f => f.File.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rawInterfaceMap = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (_, scan) in ordered)
        {
            foreach (var decl in scan.InterfaceDecls)
            {
                var fullName = decl.FullName;
                if (!rawInterfaceMap.TryGetValue(fullName, out var list))
                    rawInterfaceMap[fullName] = list = new List<string>();
                foreach (var b in CSharpScanner.ExtractBaseTypeNames(decl.BaseList))
                    if (!list.Contains(b)) list.Add(b);
            }
        }

        var interfacesBySimpleName = rawInterfaceMap.Keys
            .GroupBy(CSharpScanner.SimpleName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var interfaceMap = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (_, scan) in ordered)
        {
            foreach (var decl in scan.InterfaceDecls)
            {
                var fullName = decl.FullName;
                if (!interfaceMap.TryGetValue(fullName, out var list))
                    interfaceMap[fullName] = list = new List<string>();
                foreach (var b in CSharpScanner.ExtractBaseTypeNames(decl.BaseList))
                {
                    var resolved = ResolveInterfaceName(b, decl.Namespace, rawInterfaceMap, interfacesBySimpleName);
                    if (!list.Contains(resolved)) list.Add(resolved);
                }
            }
        }

        var hierarchy = new TypeHierarchy(
            ordered.SelectMany(o => o.Scan.Classes),
            ordered.SelectMany(o => o.Scan.InterfaceDecls));

        foreach (var (file, scan) in ordered)
        {
            CSharpScanner.ApplyScan(scan, file.Code, file.ProjectName, result, file.Path);
            DiscoverMarkerRequests(
                scan,
                file,
                interfaceMap,
                interfacesBySimpleName,
                markers,
                result);
            DiscoverIndirectShapes(scan, file, hierarchy, result);
        }
    }

    private static readonly HashSet<string> HandlerRoots = new(StringComparer.Ordinal)
    {
        "IRequestHandler",
    };

    private static readonly HashSet<string> ValidatorRoots = new(StringComparer.Ordinal)
    {
        "AbstractValidator",
        "IValidator",
    };

    /// <summary>
    /// Finds handlers/validators declared through application-specific interfaces or
    /// base classes. Direct roots are already handled by <see cref="CSharpScanner.ApplyScan"/>.
    /// </summary>
    private static void DiscoverIndirectShapes(
        CSharpScanner.FileScan scan,
        SourceFile file,
        TypeHierarchy hierarchy,
        AnalysisResult result)
    {
        foreach (var decl in scan.Classes)
        {
            if (decl.IsAbstract) continue;
            var typeParameters = new HashSet<string>(decl.TypeParameters, StringComparer.Ordinal);
            var location = SourceLocation.Create(
                file.Path, result.RootPath, file.Code, decl.BodySpan.Start);

            foreach (var part in CSharpScanner.SplitTopLevel(decl.BaseList))
            {
                var (name, _) = CSharpScanner.ParseTypeReference(part);
                var simple = CSharpScanner.SimpleName(name);
                if (simple is "DbContext" or "IdentityDbContext") continue;

                if (simple != "IRequestHandler" &&
                    hierarchy.TryResolveShapeTarget(decl.Namespace, part, HandlerRoots, out var requestType))
                {
                    var requestName = CSharpScanner.UnqualifiedTypeName(requestType);
                    if (requestName.Length > 0 && !typeParameters.Contains(requestName) &&
                        !result.Handlers.Any(h =>
                            h.Namespace == decl.Namespace &&
                            h.Name == decl.Name &&
                            h.ContainingTypes.SequenceEqual(decl.ContainingTypes) &&
                            h.RequestName == requestName))
                    {
                        var handler = new HandlerInfo
                        {
                            Name = decl.Name,
                            Namespace = decl.Namespace,
                            RequestName = requestName,
                            RequestNamespace = CSharpScanner.NamespaceOfType(requestType) is { Length: > 0 } ns
                                ? ns
                                : null,
                            ProjectName = file.ProjectName,
                            Location = location,
                        };
                        handler.ContainingTypes.AddRange(decl.ContainingTypes);
                        result.Handlers.Add(handler);
                    }
                }

                if (simple is not ("AbstractValidator" or "IValidator") &&
                    hierarchy.TryResolveShapeTarget(decl.Namespace, part, ValidatorRoots, out var validatorTarget))
                {
                    var targetName = CSharpScanner.UnqualifiedTypeName(validatorTarget);
                    if (targetName.Length > 0 && !typeParameters.Contains(targetName) &&
                        !result.Validators.Any(v =>
                            v.Namespace == decl.Namespace &&
                            v.Name == decl.Name &&
                            v.ContainingTypes.SequenceEqual(decl.ContainingTypes) &&
                            v.TargetType == targetName))
                    {
                        var validator = new ValidatorInfo
                        {
                            Name = decl.Name,
                            Namespace = decl.Namespace,
                            TargetType = targetName,
                            TargetNamespace = CSharpScanner.NamespaceOfType(validatorTarget) is { Length: > 0 } ns
                                ? ns
                                : null,
                            ProjectName = file.ProjectName,
                            Location = location,
                        };
                        validator.ContainingTypes.AddRange(decl.ContainingTypes);
                        result.Validators.Add(validator);
                    }
                }
            }
        }
    }

    private static void DiscoverMarkerRequests(
        CSharpScanner.FileScan scan,
        SourceFile file,
        Dictionary<string, List<string>> interfaceMap,
        Dictionary<string, List<string>> interfacesBySimpleName,
        IReadOnlySet<string> markers,
        AnalysisResult result)
    {
        foreach (var decl in scan.Classes)
        {
            if (decl.IsAbstract) continue;
            if (decl.BaseRawNames.Contains("IRequest")) continue;
            if (decl.BaseRawNames.Contains("IRequestHandler")) continue;
            if (decl.BaseRawNames.Count == 0) continue;

            if (decl.BaseRawNames.Any(b => b is "DbContext" or "IdentityDbContext" or
                                                "AbstractValidator" or "IValidator" or
                                                "IPipelineBehavior"))
                continue;

            var location = SourceLocation.Create(
                file.Path, result.RootPath, file.Code, decl.BodySpan.Start);
            var directInterfaces = decl.BaseTypeNames
                .Select(name => ResolveInterfaceName(name, decl.Namespace, interfaceMap, interfacesBySimpleName));
            var reached = MarkerResolver.ReachableMarkers(directInterfaces, interfaceMap, markers);
            if (reached.Count == 0) continue;

            if (!reached.Contains("IRequest") && !reached.Any(markers.Contains))
                continue;

            var via = reached.FirstOrDefault(m => m != "IRequest")
                      ?? decl.BaseRawNames.FirstOrDefault(b => b != "IRequest");

            if (result.Requests.Any(r =>
                    r.Name == decl.Name &&
                    r.Namespace == decl.Namespace &&
                    r.ContainingTypes.SequenceEqual(decl.ContainingTypes)))
                continue;

            var request = new CqrsRequest
            {
                Name = decl.Name,
                Namespace = decl.Namespace,
                ProjectName = file.ProjectName,
                ViaMarker = via,
                Location = location,
            };
            request.ContainingTypes.AddRange(decl.ContainingTypes);
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
        if (interfaceMap.ContainsKey(name)) return name;

        var localName = string.IsNullOrEmpty(currentNamespace) ? name : $"{currentNamespace}.{name}";
        if (interfaceMap.ContainsKey(localName))
            return localName;

        return interfacesBySimpleName.TryGetValue(CSharpScanner.SimpleName(name), out var candidates) &&
               candidates.Count == 1
            ? candidates[0]
            : name;
    }
}
