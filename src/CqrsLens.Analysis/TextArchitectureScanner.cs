namespace CqrsLens;

internal sealed class TextArchitectureScanner : IArchitectureScanner
{
    public void Scan(
        SourceWorkspace workspace,
        IReadOnlySet<string> markers,
        IReadOnlySet<string> handlerMarkers,
        AnalysisResult result)
    {
        var ordered = workspace.SuccessfullyParsed
            .OrderBy(f => f.File.Path, StringComparer.OrdinalIgnoreCase)
            .Select(f => (File: f.File, Scan: f.Scan!))
            .ToList();

        var phaseWatch = System.Diagnostics.Stopwatch.StartNew();
        var phaseAllocated = GC.GetTotalAllocatedBytes(precise: false);
        void Phase(string label)
        {
            phaseWatch.Stop();
            var allocated = GC.GetTotalAllocatedBytes(precise: false) - phaseAllocated;
            result.PhaseTimings.Add(new AnalysisPhaseTiming(
                $"Scan: {label}", phaseWatch.Elapsed.TotalMilliseconds, allocated));
            phaseWatch.Restart();
            phaseAllocated = GC.GetTotalAllocatedBytes(precise: false);
        }

        ApplyProjectGlobalUsings(ordered, result.Projects);
        Phase("global usings");

        var hierarchy = new TypeHierarchy(
            ordered.SelectMany(o => o.Scan.Classes),
            ordered.SelectMany(o => o.Scan.InterfaceDecls));
        Phase("hierarchy");

        var interfaceMap = BuildInterfaceMap(ordered, hierarchy, result);
        Phase("interface map");

        var handlerRoots = BuildHandlerRoots(handlerMarkers);
        var identities = new ScanIdentityIndex(result);
        var masks = workspace.SuccessfullyParsed
            .ToDictionary(f => f.File.Path, f => f.Mask!, StringComparer.OrdinalIgnoreCase);
        Phase("indexes");

        foreach (var (file, scan) in ordered)
        {
            CSharpScanner.ApplyScan(
                scan,
                file.Code,
                file.ProjectName,
                result,
                file.Path,
                masks[file.Path],
                identities);
        }
        Phase("ApplyScan");

        foreach (var (file, scan) in ordered)
            DiscoverMarkerRequests(scan, file, hierarchy, interfaceMap, markers, result);
        Phase("marker requests");

        foreach (var (file, scan) in ordered)
            DiscoverMarkerNotifications(scan, file, hierarchy, interfaceMap, result);
        Phase("marker notifications");

        foreach (var (file, scan) in ordered)
            DiscoverIndirectShapes(scan, file, hierarchy, handlerRoots, result);
        Phase("indirect shapes");

        EfCoreTextModelBuilder.Build(ordered, hierarchy, result, masks);
        Phase("EF model");
    }

    internal static Dictionary<string, List<UsingDirective>> ApplyProjectGlobalUsings(
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan)> ordered,
        IReadOnlyList<ProjectInfo> projects)
    {
        var globalsByProject = new Dictionary<string, List<UsingDirective>>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in projects)
        {
            if (project.ProjectUsings.Count == 0) continue;
            globalsByProject[project.Name] = project.ProjectUsings.Distinct().ToList();
        }

        foreach (var (file, scan) in ordered)
        {
            var key = file.ProjectName ?? "";
            foreach (var u in scan.Usings.Where(u => u.IsGlobal))
            {
                if (!globalsByProject.TryGetValue(key, out var list))
                    globalsByProject[key] = list = new List<UsingDirective>();
                if (!list.Contains(u))
                    list.Add(u);
            }
        }

        var injectedByPath = new Dictionary<string, List<UsingDirective>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (file, scan) in ordered)
        {
            if (!globalsByProject.TryGetValue(file.ProjectName ?? "", out var globals))
                continue;

            List<UsingDirective>? injected = null;
            foreach (var global in globals)
            {
                if (!scan.Usings.Contains(global))
                {
                    scan.Usings.Add(global);
                    injected ??= new List<UsingDirective>();
                    injected.Add(global);
                }
            }

            if (injected is { Count: > 0 })
                injectedByPath[file.Path] = injected;
        }

        return injectedByPath;
    }

    /// <summary>
    /// Builds the interface closure using each declaration's own using directives.
    /// This makes module-local ICommand/IQuery contracts resolve to the contract
    /// imported by the file rather than to a same-named interface from another module.
    /// </summary>
    internal static Dictionary<string, List<string>> BuildInterfaceMap(
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan)> ordered,
        TypeHierarchy hierarchy,
        AnalysisResult result)
    {
        var interfaceMap = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (file, scan) in ordered)
        {
            foreach (var decl in scan.InterfaceDecls)
            {
                var fullName = CSharpScanner.TypeIdentity(
                    decl.FullName, decl.TypeParameters.Count);
                if (!interfaceMap.TryGetValue(fullName, out var list))
                    interfaceMap[fullName] = list = new List<string>();

                foreach (var part in CSharpScanner.SplitTopLevel(decl.BaseList))
                {
                    var (baseName, baseArgs) = CSharpScanner.ParseTypeReference(part);
                    var resolved = hierarchy.ResolveInterfaceReference(
                        baseName,
                        decl.Namespace,
                        decl.Usings,
                        decl.ContainingTypes,
                        baseArgs.Count,
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

        return interfaceMap;
    }

    internal static HashSet<string> BuildHandlerRoots(IReadOnlySet<string> handlerMarkers)
    {
        var handlerRoots = new HashSet<string>(HandlerRoots, StringComparer.Ordinal);
        foreach (var marker in handlerMarkers)
            handlerRoots.Add(CSharpScanner.SimpleName(marker));
        return handlerRoots;
    }

    internal static readonly HashSet<string> HandlerRoots = new(StringComparer.Ordinal)
    {
        "IRequestHandler",
    };

    internal static readonly HashSet<string> NotificationMarkerRoots = new(StringComparer.Ordinal)
    {
        "INotification",
        "IDomainEvent",
    };

    internal static readonly HashSet<string> NotificationHandlerRoots = new(StringComparer.Ordinal)
    {
        "INotificationHandler",
        "IDomainEventHandler",
    };

    internal static readonly HashSet<string> ValidatorRoots = new(StringComparer.Ordinal)
    {
        "AbstractValidator",
        "IValidator",
        // FastEndpoints.Validator<T> is a framework contract that commonly
        // arrives through a package, so it is not always in the scanned source.
        "Validator",
    };

    /// <summary>
    /// Roots whose direct declarations are already handled by <see cref="CSharpScanner.ApplyScan"/>.
    /// Indirect-shape discovery must still run for wrapper bases such as ICommandHandler&lt;T&gt;.
    /// </summary>
    private static bool IsDirectShapeRoot(string simpleName)
        => simpleName is "IRequest" or "INotification" or "IDomainEvent" or
            "IRequestHandler" or "INotificationHandler" or "IDomainEventHandler" or
            "AbstractValidator" or "IValidator" or "IPipelineBehavior" or
            "DbContext" or "IdentityDbContext";

    /// <summary>
    /// Finds handlers/validators declared through application-specific interfaces or
    /// base classes. Direct roots are already handled by <see cref="CSharpScanner.ApplyScan"/>.
    /// </summary>
    private static void DiscoverIndirectShapes(
        CSharpScanner.FileScan scan,
        SourceFile file,
        TypeHierarchy hierarchy,
        IReadOnlySet<string> handlerRoots,
        AnalysisResult result)
    {
        foreach (var decl in scan.Classes)
        {
            if (decl.IsAbstract) continue;

            var baseParts = CSharpScanner.SplitTopLevel(decl.BaseList);
            if (baseParts.Count == 0) continue;

            // Direct roots are already recorded by ApplyScan. Only resolve base parts
            // that could be a user-defined wrapper (e.g. ICommandHandler<T> or a
            // CommandBase<T>); this avoids re-walking the hierarchy for every record
            // and entity in large modular solutions.
            var hasIndirectShape = false;
            foreach (var part in baseParts)
            {
                var (name, _) = CSharpScanner.ParseTypeReference(part);
                if (!IsDirectShapeRoot(CSharpScanner.SimpleName(name)))
                {
                    hasIndirectShape = true;
                    break;
                }
            }
            if (!hasIndirectShape) continue;

            var typeParameters = new HashSet<string>(decl.TypeParameters, StringComparer.Ordinal);
            var location = SourceLocation.Create(
                file.Path, result.RootPath, file.Code, decl.NameIndex);

            foreach (var part in baseParts)
            {
                var (name, _) = CSharpScanner.ParseTypeReference(part);
                var simple = CSharpScanner.SimpleName(name);
                if (simple is "DbContext" or "IdentityDbContext") continue;

                if (simple != "IRequestHandler")
                {
                    var resolvedHandler = hierarchy.TryResolveShapeTarget(
                        decl.Namespace,
                        part,
                        handlerRoots,
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
                                h.ProjectName == file.ProjectName &&
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
                    else if (handlerRoots.Contains(simple) || LooksLikeHandlerContract(simple))
                    {
                        RecordShapeResolutionGap(
                            result, file, decl.NameIndex, decl.Name, simple,
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
                                v.ProjectName == file.ProjectName &&
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
                            result, file, decl.NameIndex, decl.Name, simple,
                            hierarchy, validatorAmbiguity, "validator");
                    }
                }

                if (simple is not ("INotificationHandler" or "IDomainEventHandler"))
                {
                    var resolvedNotificationHandler = hierarchy.TryResolveShapeTarget(
                        decl.Namespace,
                        part,
                        NotificationHandlerRoots,
                        decl.Usings,
                        decl.ContainingTypes,
                        out var notificationTarget,
                        out _);
                    if (resolvedNotificationHandler)
                    {
                        var targetName = CSharpScanner.UnqualifiedTypeName(notificationTarget);
                        if (targetName.Length > 0 && !typeParameters.Contains(targetName) &&
                            !result.NotificationHandlers.Any(h =>
                                h.Namespace == decl.Namespace &&
                                h.Name == decl.Name &&
                                h.ProjectName == file.ProjectName &&
                                h.ContainingTypes.SequenceEqual(decl.ContainingTypes) &&
                                h.NotificationName == targetName))
                        {
                            var notificationHandler = new NotificationHandlerInfo
                            {
                                Name = decl.Name,
                                Namespace = decl.Namespace,
                                NotificationName = targetName,
                                NotificationNamespace = CSharpScanner.NamespaceOfType(notificationTarget) is { Length: > 0 } ns
                                    ? ns
                                    : null,
                                ProjectName = file.ProjectName,
                                Location = location,
                            };
                            notificationHandler.ContainingTypes.AddRange(decl.ContainingTypes);
                            CSharpScanner.AddImportedNamespaces(decl.Usings, notificationHandler.ImportedNamespaces);
                            result.NotificationHandlers.Add(notificationHandler);
                        }
                    }
                }
            }
        }
    }

    private static void DiscoverMarkerNotifications(
        CSharpScanner.FileScan scan,
        SourceFile file,
        TypeHierarchy hierarchy,
        IReadOnlyDictionary<string, List<string>> interfaceMap,
        AnalysisResult result)
    {
        foreach (var decl in scan.Classes)
        {
            if (decl.IsAbstract) continue;
            if (decl.BaseRawNames.Contains("INotification") ||
                decl.BaseRawNames.Contains("IDomainEvent") ||
                decl.BaseRawNames.Contains("INotificationHandler") ||
                decl.BaseRawNames.Contains("IDomainEventHandler"))
                continue;
            if (decl.BaseRawNames.Count == 0) continue;

            if (decl.BaseRawNames.Any(b => b is "DbContext" or "IdentityDbContext" or
                                                "AbstractValidator" or "IValidator" or
                                                "Validator" or "IPipelineBehavior" or
                                                "IRequest" or "IRequestHandler"))
                continue;

            var directInterfaces = new List<string>();
            foreach (var part in CSharpScanner.SplitTopLevel(decl.BaseList))
            {
                var (name, args) = CSharpScanner.ParseTypeReference(part);
                var resolved = hierarchy.ResolveInterfaceReference(
                    name,
                    decl.Namespace,
                    decl.Usings,
                    decl.ContainingTypes,
                    args.Count,
                    out _);
                if (resolved is not null)
                    directInterfaces.Add(resolved);
            }

            var reached = MarkerResolver.ReachableMarkers(directInterfaces, interfaceMap, NotificationMarkerRoots)
                .Where(NotificationMarkerRoots.Contains)
                .ToList();
            if (reached.Count == 0)
            {
                var shape = hierarchy.ResolveRequestShape(decl, NotificationMarkerRoots);
                reached = shape.Roots.Where(NotificationMarkerRoots.Contains).ToList();
            }

            if (reached.Count == 0)
                continue;

            if (result.Notifications.Any(n =>
                    n.Name == decl.Name &&
                    n.ProjectName == file.ProjectName &&
                    n.Namespace == decl.Namespace &&
                    n.ContainingTypes.SequenceEqual(decl.ContainingTypes)))
                continue;

            var via = decl.BaseRawNames.FirstOrDefault(b => b is not "INotification")
                      ?? reached.FirstOrDefault(m => m != "INotification");
            var notification = new CqrsNotification
            {
                Name = decl.Name,
                Namespace = decl.Namespace,
                ProjectName = file.ProjectName,
                ViaMarker = via,
                Location = SourceLocation.Create(file.Path, result.RootPath, file.Code, decl.NameIndex),
            };
            notification.ContainingTypes.AddRange(decl.ContainingTypes);
            result.Notifications.Add(notification);
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
                                                "Validator" or "IPipelineBehavior" or
                                                "INotification" or "IDomainEvent" or
                                                "INotificationHandler" or "IDomainEventHandler"))
                continue;

            var location = SourceLocation.Create(
                file.Path, result.RootPath, file.Code, decl.NameIndex);
            var directInterfaces = new List<string>();
            var directAmbiguity = false;
            foreach (var part in CSharpScanner.SplitTopLevel(decl.BaseList))
            {
                var (name, args) = CSharpScanner.ParseTypeReference(part);
                var resolved = hierarchy.ResolveInterfaceReference(
                    name,
                    decl.Namespace,
                    decl.Usings,
                    decl.ContainingTypes,
                    args.Count,
                    out var ambiguous);
                if (resolved is null)
                {
                    var simple = CSharpScanner.SimpleName(name);
                    if (markers.Contains(simple) || simple is "IRequest")
                    {
                        directAmbiguity = true;
                        RecordAmbiguousContract(
                            result, file, decl.NameIndex, decl.Name, name, ambiguous, "request marker");
                    }
                    continue;
                }

                directInterfaces.Add(resolved);
            }

            var reached = MarkerResolver.ReachableMarkers(directInterfaces, interfaceMap, markers);
            string? shapeResponseType = null;
            if (reached.Count == 0)
            {
                // Direct interfaces did not reach a root. Follow source-declared
                // base classes (QueryBase<T>, custom command bases) as well; this
                // is deliberately attempted only as a fallback so direct-interface
                // behavior and its diagnostics stay unchanged.
                var shape = hierarchy.ResolveRequestShape(decl, markers);
                if (shape.Roots.Count == 0)
                {
                    if (!directAmbiguity)
                    {
                        RecordRequestResolutionGap(
                            result, file, decl.NameIndex, decl.Name, shape);
                    }
                    continue;
                }

                reached = shape.Roots;
                shapeResponseType = shape.ResponseType;
            }

            if (!reached.Contains("IRequest") && !reached.Any(markers.Contains))
                continue;

            var via = reached.FirstOrDefault(m => m != "IRequest")
                      ?? decl.BaseRawNames.FirstOrDefault(b => b != "IRequest");

            if (result.Requests.Any(r =>
                    r.Name == decl.Name &&
                    r.ProjectName == file.ProjectName &&
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
            request.ResponseType = shapeResponseType
                ?? MarkerResolver.TryExtractMarkerResponse(decl, reached)
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

    internal static void RecordRequestResolutionGap(
        AnalysisResult result,
        SourceFile file,
        int sourceIndex,
        string owner,
        RequestShapeResolution shape)
    {
        if (shape.AmbiguousCandidates.Count > 0)
        {
            RecordAmbiguousContract(
                result, file, sourceIndex, owner, "base",
                shape.AmbiguousCandidates, "request base");
            return;
        }

        if (!owner.EndsWith("Command", StringComparison.Ordinal) &&
            !owner.EndsWith("Query", StringComparison.Ordinal))
            return;

        var unresolved = shape.UnresolvedBases
            .Where(name => name.EndsWith("Base", StringComparison.Ordinal) ||
                           name.Contains("Command", StringComparison.Ordinal) ||
                           name.Contains("Query", StringComparison.Ordinal) ||
                           name.Contains("Request", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        if (unresolved.Count == 0) return;

        result.RecordDiagnostic(new AnalysisDiagnostic(
            AnalysisResult.UnresolvedContractCode,
            AnalysisDiagnosticSeverity.Info,
            $"`{owner}` looks like a request, but its base chain references " +
            $"{string.Join(", ", unresolved.Select(n => $"`{n}`"))}, which has no source " +
            "declaration. Request discovery stopped at that base type.",
            SourceLocation.Create(file.Path, result.RootPath, file.Code, sourceIndex),
            file.ProjectName));
    }

    internal static bool LooksLikeHandlerContract(string simple)
        => simple.Contains("CommandHandler", StringComparison.Ordinal) ||
           simple.Contains("QueryHandler", StringComparison.Ordinal) ||
           simple.Contains("RequestHandler", StringComparison.Ordinal);

    internal static bool LooksLikeValidatorContract(string simple)
        => simple is "Validator" or "AbstractValidator" or "IValidator" ||
           simple.EndsWith("CommandValidator", StringComparison.Ordinal) ||
           simple.EndsWith("QueryValidator", StringComparison.Ordinal);

    internal static void RecordShapeResolutionGap(
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

        var hint = role == "handler"
            ? "Name an external handler wrapper with --handler-markers / handlerMarkers to link it."
            : "FastEndpoints `Validator<T>` is recognized; other external validator wrappers are not linked yet.";
        result.RecordDiagnostic(new AnalysisDiagnostic(
            AnalysisResult.UnresolvedContractCode,
            AnalysisDiagnosticSeverity.Info,
            $"`{owner}` looks like a {role} through `{reference}`, but that contract has no source declaration. " +
            hint,
            SourceLocation.Create(file.Path, result.RootPath, file.Code, sourceIndex),
            file.ProjectName));
    }

    internal static void RecordAmbiguousContract(
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
