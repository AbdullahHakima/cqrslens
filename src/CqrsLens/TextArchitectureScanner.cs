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

        ApplyProjectGlobalUsings(ordered);

        var hierarchy = new TypeHierarchy(
            ordered.SelectMany(o => o.Scan.Classes),
            ordered.SelectMany(o => o.Scan.InterfaceDecls));

        // Build the interface closure using each declaration's own using
        // directives. This is what makes module-local ICommand/IQuery contracts
        // resolve to the contract imported by the file rather than to a
        // same-named interface from another module.
        var interfaceMap = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (file, scan) in ordered)
        {
            foreach (var decl in scan.InterfaceDecls)
            {
                var fullName = decl.FullName;
                if (!interfaceMap.TryGetValue(fullName, out var list))
                    interfaceMap[fullName] = list = new List<string>();

                foreach (var baseName in CSharpScanner.ExtractBaseTypeNames(decl.BaseList))
                {
                    var resolved = hierarchy.ResolveInterfaceReference(
                        baseName,
                        decl.Namespace,
                        decl.Usings,
                        decl.ContainingTypes,
                        out var ambiguous);
                    if (resolved is null)
                    {
                        RecordAmbiguousContract(
                            result, file, decl.BodySpan.Start, decl.Name, baseName, ambiguous, "interface");
                        continue;
                    }

                    if (!list.Contains(resolved))
                        list.Add(resolved);
                }
            }
        }

        foreach (var (file, scan) in ordered)
        {
            CSharpScanner.ApplyScan(scan, file.Code, file.ProjectName, result, file.Path);
            DiscoverMarkerRequests(scan, file, hierarchy, interfaceMap, markers, result);
            DiscoverIndirectShapes(scan, file, hierarchy, result);
        }
    }

    private static void ApplyProjectGlobalUsings(
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan)> ordered)
    {
        var globalsByProject = ordered
            .SelectMany(o => o.Scan.Usings
                .Where(u => u.IsGlobal)
                .Select(u => (Project: o.File.ProjectName ?? "", Using: u)))
            .GroupBy(x => x.Project, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.Using).Distinct().ToList(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var (file, scan) in ordered)
        {
            if (!globalsByProject.TryGetValue(file.ProjectName ?? "", out var globals))
                continue;

            foreach (var global in globals)
            {
                if (!scan.Usings.Contains(global))
                    scan.Usings.Add(global);
            }
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

                if (simple != "IRequestHandler")
                {
                    var resolvedHandler = hierarchy.TryResolveShapeTarget(
                        decl.Namespace,
                        part,
                        HandlerRoots,
                        decl.Usings,
                        decl.ContainingTypes,
                        out var requestType,
                        out var handlerAmbiguity);
                    if (resolvedHandler)
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
                            CSharpScanner.AddImportedNamespaces(decl.Usings, handler.ImportedNamespaces);
                            result.Handlers.Add(handler);
                        }
                    }
                    else if (LooksLikeHandlerContract(simple))
                    {
                        RecordShapeResolutionGap(
                            result, file, decl.BodySpan.Start, decl.Name, simple,
                            hierarchy, handlerAmbiguity, "handler");
                    }
                }

                if (simple is not ("AbstractValidator" or "IValidator"))
                {
                    var resolvedValidator = hierarchy.TryResolveShapeTarget(
                        decl.Namespace,
                        part,
                        ValidatorRoots,
                        decl.Usings,
                        decl.ContainingTypes,
                        out var validatorTarget,
                        out var validatorAmbiguity);
                    if (resolvedValidator)
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
                            CSharpScanner.AddImportedNamespaces(decl.Usings, validator.ImportedNamespaces);
                            result.Validators.Add(validator);
                        }
                    }
                    else if (LooksLikeValidatorContract(simple))
                    {
                        RecordShapeResolutionGap(
                            result, file, decl.BodySpan.Start, decl.Name, simple,
                            hierarchy, validatorAmbiguity, "validator");
                    }
                }
            }
        }
    }

    private static void DiscoverMarkerRequests(
        CSharpScanner.FileScan scan,
        SourceFile file,
        TypeHierarchy hierarchy,
        IReadOnlyDictionary<string, List<string>> interfaceMap,
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
            var directInterfaces = new List<string>();
            foreach (var name in decl.BaseTypeNames)
            {
                var resolved = hierarchy.ResolveInterfaceReference(
                    name,
                    decl.Namespace,
                    decl.Usings,
                    decl.ContainingTypes,
                    out var ambiguous);
                if (resolved is null)
                {
                    var simple = CSharpScanner.SimpleName(name);
                    if (markers.Contains(simple) || simple is "IRequest")
                    {
                        RecordAmbiguousContract(
                            result, file, decl.BodySpan.Start, decl.Name, name, ambiguous, "request marker");
                    }
                    continue;
                }

                directInterfaces.Add(resolved);
            }

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

    private static bool LooksLikeHandlerContract(string simple)
        => simple.Contains("CommandHandler", StringComparison.Ordinal) ||
           simple.Contains("QueryHandler", StringComparison.Ordinal) ||
           simple.Contains("RequestHandler", StringComparison.Ordinal);

    private static bool LooksLikeValidatorContract(string simple)
        => simple is "Validator" or "AbstractValidator" or "IValidator" ||
           simple.EndsWith("CommandValidator", StringComparison.Ordinal) ||
           simple.EndsWith("QueryValidator", StringComparison.Ordinal);

    private static void RecordShapeResolutionGap(
        AnalysisResult result,
        SourceFile file,
        int sourceIndex,
        string owner,
        string reference,
        TypeHierarchy hierarchy,
        IReadOnlyList<string> ambiguousCandidates,
        string role)
    {
        if (ambiguousCandidates.Count > 0)
        {
            RecordAmbiguousContract(
                result, file, sourceIndex, owner, reference, ambiguousCandidates, role);
            return;
        }

        // A source-declared interface that simply does not reach a known root is
        // not an external wrapper; stay quiet. Only an unknown type is a gap.
        if (hierarchy.HasInterface(reference)) return;

        result.RecordDiagnostic(new AnalysisDiagnostic(
            AnalysisResult.UnresolvedContractCode,
            AnalysisDiagnosticSeverity.Info,
            $"`{owner}` looks like a {role} through `{reference}`, but that contract has no source declaration. " +
            "External wrappers need explicit support; this shape was not linked.",
            SourceLocation.Create(file.Path, result.RootPath, file.Code, sourceIndex),
            file.ProjectName));
    }

    private static void RecordAmbiguousContract(
        AnalysisResult result,
        SourceFile file,
        int sourceIndex,
        string owner,
        string reference,
        IReadOnlyList<string> candidates,
        string role)
    {
        if (candidates.Count < 2) return;

        result.RecordDiagnostic(new AnalysisDiagnostic(
            AnalysisResult.AmbiguousContractCode,
            AnalysisDiagnosticSeverity.Info,
            $"`{owner}` references `{CSharpScanner.SimpleName(reference)}` as a {role}, " +
            $"but it matches {candidates.Count} source types ({string.Join(", ", candidates)}); " +
            "the reference was not resolved.",
            SourceLocation.Create(file.Path, result.RootPath, file.Code, sourceIndex),
            file.ProjectName));
    }
}
