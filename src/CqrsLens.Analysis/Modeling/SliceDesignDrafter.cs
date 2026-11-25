namespace CqrsLens;

/// <summary>Drafts a schema-valid v1 <see cref="SliceDesign"/> from scanned CQRS or universal system facts.</summary>
public static class SliceDesignDrafter
{
    private sealed record ServiceTarget(
        string FullName,
        string SimpleName,
        string? ProjectName,
        SourceLocation? Location);

    public static SliceDesignDraft Draft(AnalysisResult result, string requestName)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestName);

        var query = requestName.Trim();
        var matches = FindMatchingRequests(result, query);
        var scanGaps = CollectScanGaps(result);

        if (matches.Length > 1)
        {
            var hasDuplicateFullNames = matches
                .GroupBy(r => r.FullName, StringComparer.Ordinal)
                .Any(g => g.Count() > 1);
            var candidates = matches
                .Select(r => hasDuplicateFullNames
                    ? $"{r.FullName} ({r.ProjectName ?? r.Location?.File ?? "unknown"})"
                    : r.FullName)
                .ToArray();

            return new SliceDesignDraft(
                "ambiguous",
                query,
                null,
                scanGaps,
                [$"Multiple requests match '{query}'. Retry with an exact full type name from Candidates."],
                candidates);
        }

        if (matches.Length == 1)
            return DraftResolved(result, matches[0], scanGaps);

        var entrypointMatches = FindMatchingEntrypoints(result, query);
        if (entrypointMatches.Length > 1)
        {
            var candidates = entrypointMatches
                .Select(e => $"{e.DisplayLabel} ({e.Name}, {e.ProjectName ?? e.Location?.File ?? "unknown"})")
                .ToArray();

            return new SliceDesignDraft(
                "ambiguous",
                query,
                null,
                scanGaps,
                [$"Multiple system entrypoints match '{query}'. Retry with an exact entrypoint name or route/trigger from Candidates."],
                candidates);
        }

        if (entrypointMatches.Length == 1)
            return DraftResolvedEntrypoint(result, entrypointMatches[0], scanGaps);

        var serviceMatches = FindMatchingServices(result, query);
        if (serviceMatches.Length > 1)
        {
            var hasDuplicateNames = serviceMatches
                .GroupBy(s => s.FullName, StringComparer.Ordinal)
                .Any(g => g.Count() > 1);
            var candidates = serviceMatches
                .Select(s => hasDuplicateNames
                    ? $"{s.FullName} ({s.ProjectName ?? s.Location?.File ?? "unknown"})"
                    : s.FullName)
                .ToArray();

            return new SliceDesignDraft(
                "ambiguous",
                query,
                null,
                scanGaps,
                [$"Multiple services match '{query}'. Retry with an exact full service type name from Candidates."],
                candidates);
        }

        if (serviceMatches.Length == 1)
            return DraftResolvedService(result, serviceMatches[0], scanGaps);

        if (!result.CanRunStrictChecks)
        {
            return new SliceDesignDraft(
                "unresolved",
                query,
                null,
                scanGaps,
                ["Scan diagnostics prevented complete discovery. Resolve scan errors before assuming this target is missing."],
                []);
        }

        return new SliceDesignDraft(
            "not_found",
            query,
            null,
            scanGaps,
            [$"Target '{query}' was not found as a request, system entrypoint, or service in the scanned model. Verify the name or author a greenfield SliceDesign manually."],
            []);
    }

    private static CqrsRequest[] FindMatchingRequests(AnalysisResult result, string query)
    {
        var exactFull = result.Requests
            .Where(r => string.Equals(r.FullName, query, StringComparison.Ordinal))
            .ToArray();
        if (exactFull.Length > 0)
            return OrderRequests(exactFull);

        var ciFull = result.Requests
            .Where(r => string.Equals(r.FullName, query, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (ciFull.Length > 0)
            return OrderRequests(ciFull);

        if (!query.Contains('.', StringComparison.Ordinal))
        {
            var exactSimple = result.Requests
                .Where(r => string.Equals(r.Name, query, StringComparison.Ordinal))
                .ToArray();
            if (exactSimple.Length > 0)
                return OrderRequests(exactSimple);

            var ciSimple = result.Requests
                .Where(r => string.Equals(r.Name, query, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (ciSimple.Length > 0)
                return OrderRequests(ciSimple);
        }

        return [];
    }

    private static SystemEntrypointInfo[] FindMatchingEntrypoints(AnalysisResult result, string query)
    {
        if (result.SystemEntrypoints.Count == 0)
            return [];

        var distinct = DeduplicateEntrypoints(result.SystemEntrypoints);

        var exact = distinct
            .Where(e => EntrypointMatches(e, query, StringComparison.Ordinal))
            .ToArray();
        if (exact.Length > 0)
            return OrderEntrypoints(exact);

        var ci = distinct
            .Where(e => EntrypointMatches(e, query, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (ci.Length > 0)
            return OrderEntrypoints(ci);

        if (!query.Contains(' ', StringComparison.Ordinal) &&
            !query.StartsWith('/'))
        {
            var suffixMatches = distinct
                .Where(e =>
                    e.Name.EndsWith("." + query, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(CSharpScanner.SimpleName(e.RouteOrTrigger), query, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (suffixMatches.Length > 0)
                return OrderEntrypoints(suffixMatches);
        }

        return [];
    }

    private static bool EntrypointMatches(SystemEntrypointInfo e, string query, StringComparison comparison)
        => string.Equals(e.Name, query, comparison) ||
           string.Equals(e.RouteOrTrigger, query, comparison) ||
           string.Equals(e.DisplayLabel, query, comparison) ||
           string.Equals($"{e.Name} ({e.RouteOrTrigger})", query, comparison);

    private static List<SystemEntrypointInfo> DeduplicateEntrypoints(IReadOnlyList<SystemEntrypointInfo> entrypoints)
    {
        var seen = new HashSet<(string Project, SystemEntrypointKind Kind, string Method, string Route, string Name)>();
        var list = new List<SystemEntrypointInfo>(entrypoints.Count);
        foreach (var ep in OrderEntrypoints(entrypoints))
        {
            var key = (ep.ProjectName ?? string.Empty, ep.Kind, ep.Method, ep.RouteOrTrigger, ep.Name);
            if (seen.Add(key))
                list.Add(ep);
        }
        return list;
    }

    private static SystemEntrypointInfo[] OrderEntrypoints(IEnumerable<SystemEntrypointInfo> entrypoints) => entrypoints
        .OrderBy(e => e.ProjectName, StringComparer.Ordinal)
        .ThenBy(e => e.Kind)
        .ThenBy(e => e.Method, StringComparer.Ordinal)
        .ThenBy(e => e.RouteOrTrigger, StringComparer.Ordinal)
        .ThenBy(e => e.Name, StringComparer.Ordinal)
        .ThenBy(e => e.Location?.File, StringComparer.Ordinal)
        .ThenBy(e => e.Location?.Line)
        .ToArray();

    private static ServiceTarget[] FindMatchingServices(AnalysisResult result, string query)
    {
        var testProjects = CollectTestProjects(result);
        var dbContexts = result.DbContexts.Select(c => c.FullName).ToHashSet(StringComparer.Ordinal);
        var byFull = new Dictionary<string, ServiceTarget>(StringComparer.Ordinal);

        void TryAddService(string? fullName, string? projectName, SourceLocation? location)
        {
            if (string.IsNullOrWhiteSpace(fullName) || dbContexts.Contains(fullName))
                return;

            var inferredProj = projectName ?? InferProjectName(result, location);
            if (!string.IsNullOrWhiteSpace(inferredProj) && testProjects.Contains(inferredProj))
                return;

            if (!byFull.TryGetValue(fullName, out var existing))
            {
                byFull[fullName] = new ServiceTarget(
                    fullName,
                    CSharpScanner.SimpleName(fullName),
                    inferredProj,
                    location);
            }
            else if ((existing.ProjectName is null && inferredProj is not null) ||
                     (existing.Location is null && location is not null))
            {
                byFull[fullName] = existing with
                {
                    ProjectName = existing.ProjectName ?? inferredProj,
                    Location = existing.Location ?? location,
                };
            }
        }

        foreach (var sr in result.ServiceResolutions)
        {
            if (sr.IsAmbiguous || string.IsNullOrWhiteSpace(sr.Implementation))
                continue;

            var declaredCandidate = sr.Candidates
                .FirstOrDefault(c => c.DeclaredInScan && string.Equals(c.Type, sr.Implementation, StringComparison.Ordinal));
            if (sr.Candidates.Count > 0 && declaredCandidate is null)
                continue;

            var candidateLoc = declaredCandidate?.Location;
            TryAddService(sr.Implementation, InferProjectName(result, candidateLoc), candidateLoc);
        }

        foreach (var edge in result.ServiceCallEdges)
        {
            TryAddService(edge.From, edge.ProjectName, edge.Location);
            TryAddService(edge.To, null, null);
        }

        foreach (var edge in result.ResolvedEdges)
            TryAddService(edge.From, edge.ProjectName, edge.Location);

        var services = byFull.Values.ToArray();
        if (services.Length == 0)
            return [];

        var exactFull = services.Where(s => string.Equals(s.FullName, query, StringComparison.Ordinal)).ToArray();
        if (exactFull.Length > 0)
            return OrderServices(exactFull);

        var ciFull = services.Where(s => string.Equals(s.FullName, query, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (ciFull.Length > 0)
            return OrderServices(ciFull);

        if (!query.Contains('.', StringComparison.Ordinal))
        {
            var exactSimple = services.Where(s => string.Equals(s.SimpleName, query, StringComparison.Ordinal)).ToArray();
            if (exactSimple.Length > 0)
                return OrderServices(exactSimple);

            var ciSimple = services.Where(s => string.Equals(s.SimpleName, query, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (ciSimple.Length > 0)
                return OrderServices(ciSimple);
        }

        return [];
    }

    private static ServiceTarget[] OrderServices(ServiceTarget[] services) => services
        .OrderBy(s => s.FullName, StringComparer.Ordinal)
        .ThenBy(s => s.ProjectName, StringComparer.Ordinal)
        .ThenBy(s => s.Location?.File, StringComparer.Ordinal)
        .ToArray();

    private static CqrsRequest[] OrderRequests(CqrsRequest[] requests) => requests
        .OrderBy(r => r.FullName, StringComparer.Ordinal)
        .ThenBy(r => r.ProjectName, StringComparer.Ordinal)
        .ThenBy(r => r.Location?.File, StringComparer.Ordinal)
        .ThenBy(r => r.Location?.Line)
        .ToArray();

    private static List<string> CollectScanGaps(AnalysisResult result)
    {
        var gaps = new List<string>();
        if (!result.CanRunStrictChecks)
        {
            var codes = string.Join(", ", result.Diagnostics
                .Select(d => d.Code)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(c => c, StringComparer.Ordinal));
            gaps.Add($"Incomplete scan diagnostics detected ({codes}); drafted slice facts may be incomplete.");
        }

        if (result.HasSourceFilters)
            gaps.Add("Scan ran with include/exclude source filters; omitted files may hide slice links.");

        var prodUnresolved = result.UnresolvedDispatches.Count(u => !u.IsTestProject);
        if (prodUnresolved > 0)
            gaps.Add($"Scan contains {prodUnresolved} unresolved dispatch call site(s); published notifications or dispatched requests may be incomplete.");

        return gaps;
    }

    private static SliceDesignDraft DraftResolved(
        AnalysisResult result,
        CqrsRequest request,
        List<string> gaps)
    {
        var openQuestions = new List<string>();
        var project = ResolveProjectIdentifier(result, request.ProjectName, request.Location, gaps, openQuestions);

        var kind = request.Kind is "Command" or "Query" or "Request" ? request.Kind : "Request";
        var responseType = string.IsNullOrWhiteSpace(request.ResponseType) ||
                           string.Equals(request.ResponseType, "void", StringComparison.Ordinal)
            ? null
            : request.ResponseType.Trim();

        var (handlerName, resolvedHandler) = ResolveHandler(result, request, gaps, openQuestions);
        var validatorName = ResolveValidator(result, request, kind, gaps, openQuestions);
        var endpoint = ResolveEndpoint(result, request, gaps, openQuestions);

        var properties = request.Properties.Count > 0
            ? request.Properties
                .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                .GroupBy(p => p.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var first = g.First();
                    var type = string.IsNullOrWhiteSpace(first.Type) ? null : first.Type.Trim();
                    return new SliceDesignProperty(first.Name.Trim(), type);
                })
                .ToArray()
            : null;

        var publishesList = DeduplicateCaseInsensitive(
            request.PublishedNotifications
                .Concat(resolvedHandler?.PublishedNotifications ?? Enumerable.Empty<string>()));
        var publishes = publishesList.Count > 0 ? publishesList : null;

        var dispatchesList = DeduplicateCaseInsensitive(
            request.DispatchedRequests
                .Concat(resolvedHandler?.DispatchedRequests ?? Enumerable.Empty<string>()));
        var dispatches = dispatchesList.Count > 0 ? dispatchesList : null;

        var persistence = BuildPersistence(result, request, resolvedHandler);

        var dependenciesList = resolvedHandler is not null
            ? DeduplicateCaseInsensitive(resolvedHandler.Dependencies)
            : [];
        var dependencies = dependenciesList.Count > 0 ? dependenciesList : null;

        var starterScenario = new SliceDesignScenario(
            $"DRAFT: {request.Name} expected behavior",
            $"A valid {request.Name} request and required dependencies",
            $"{request.Name} is processed by the slice",
            responseType is null
                ? "The expected state change completes (DRAFT placeholder: refine with concrete assertions)"
                : $"Returns {responseType} matching expected behavior (DRAFT placeholder: refine with concrete assertions)");

        openQuestions.Add("Replace the DRAFT starter scenario with concrete Given/When/Then behavioral acceptance criteria.");
        openQuestions.Add("Specify any ForbiddenDependencies rules (e.g. outer layers or infrastructure namespaces) if architectural boundaries apply.");

        var design = new SliceDesign(
            SliceDesign.CurrentSchemaVersion,
            project,
            request.FullName,
            kind,
            responseType,
            handlerName,
            validatorName,
            endpoint,
            [starterScenario],
            properties,
            publishes,
            dispatches,
            persistence,
            dependencies,
            ForbiddenDependencies: null);

        SliceDesignEvaluator.Validate(design);

        return new SliceDesignDraft(
            "drafted",
            request.FullName,
            design,
            gaps,
            openQuestions,
            [request.FullName]);
    }

    private static SliceDesignDraft DraftResolvedEntrypoint(
        AnalysisResult result,
        SystemEntrypointInfo entrypoint,
        List<string> gaps)
    {
        var openQuestions = new List<string>();
        var project = ResolveProjectIdentifier(result, entrypoint.ProjectName, entrypoint.Location, gaps, openQuestions);
        var kind = MapEntrypointKind(entrypoint.Kind);
        var canonicalTarget = ResolveCanonicalEntrypointTarget(result, entrypoint);
        var endpoint = BuildUniversalEndpoint(entrypoint, gaps);

        var dbContexts = DeduplicateCaseInsensitive(entrypoint.TouchedDbContexts);
        var entities = DeduplicateCaseInsensitive(entrypoint.TouchedEntities.Select(ExtractEntityName));
        var persistence = dbContexts.Count == 0 && entities.Count == 0
            ? null
            : new SliceDesignPersistence(
                dbContexts.Count > 0 ? dbContexts : null,
                entities.Count > 0 ? entities : null);

        var depsList = DeduplicateCaseInsensitive(entrypoint.CalledServices);
        var dependencies = depsList.Count > 0 ? depsList : null;

        var pubsList = DeduplicateCaseInsensitive(entrypoint.PublishedMessages);
        var publishes = pubsList.Count > 0 ? pubsList : null;

        var dispatchesList = DeduplicateCaseInsensitive(entrypoint.TargetRequests);
        var dispatches = dispatchesList.Count > 0 ? dispatchesList : null;

        var starterScenario = new SliceDesignScenario(
            $"DRAFT: {canonicalTarget} expected behavior",
            $"Required inputs and dependencies for {canonicalTarget}",
            $"{canonicalTarget} executes",
            "The expected operation outcome and side effects complete (DRAFT placeholder: refine with concrete assertions)");

        openQuestions.Add("Replace the DRAFT starter scenario with concrete Given/When/Then behavioral acceptance criteria.");
        openQuestions.Add("Specify any ForbiddenDependencies rules (e.g. outer layers or infrastructure namespaces) if architectural boundaries apply.");

        var design = new SliceDesign(
            SliceDesign.CurrentSchemaVersion,
            project,
            canonicalTarget,
            kind,
            ResponseType: null,
            Handler: null,
            Validator: null,
            endpoint,
            [starterScenario],
            Properties: null,
            publishes,
            dispatches,
            persistence,
            dependencies,
            ForbiddenDependencies: null);

        SliceDesignEvaluator.Validate(design);

        return new SliceDesignDraft(
            "drafted",
            canonicalTarget,
            design,
            gaps,
            openQuestions,
            [canonicalTarget]);
    }

    private static SliceDesignDraft DraftResolvedService(
        AnalysisResult result,
        ServiceTarget service,
        List<string> gaps)
    {
        var openQuestions = new List<string>();
        var project = ResolveProjectIdentifier(result, service.ProjectName, service.Location, gaps, openQuestions);

        var dbContexts = DeduplicateCaseInsensitive(
            result.ResolvedEdges
                .Where(e => e.Kind == FlowEdgeKind.HandlerToDbContextResolved &&
                            string.Equals(e.From, service.FullName, StringComparison.Ordinal))
                .Select(e => e.To));
        var persistence = dbContexts.Count > 0
            ? new SliceDesignPersistence(dbContexts, Entities: null)
            : null;

        var depNames = result.ServiceCallEdges
            .Where(e => string.Equals(e.From, service.FullName, StringComparison.Ordinal))
            .Select(e => string.Equals(e.Via, e.To, StringComparison.Ordinal) ? e.To : $"{e.Via} -> {e.To}")
            .Concat(result.ResolvedEdges
                .Where(e => string.Equals(e.From, service.FullName, StringComparison.Ordinal))
                .Select(e => e.Via));
        var depsList = DeduplicateCaseInsensitive(depNames);
        var dependencies = depsList.Count > 0 ? depsList : null;

        var pubsList = DeduplicateCaseInsensitive(
            result.DispatchCallSites
                .Where(s => !s.IsTestProject &&
                            s.TargetKind == DispatchTargetKind.Notification &&
                            string.Equals(s.SenderFullName, service.FullName, StringComparison.Ordinal))
                .Select(s => s.TargetFullName));
        var publishes = pubsList.Count > 0 ? pubsList : null;

        var dispatchesList = DeduplicateCaseInsensitive(
            result.DispatchCallSites
                .Where(s => !s.IsTestProject &&
                            s.TargetKind == DispatchTargetKind.Request &&
                            string.Equals(s.SenderFullName, service.FullName, StringComparison.Ordinal))
                .Select(s => s.TargetFullName));
        var dispatches = dispatchesList.Count > 0 ? dispatchesList : null;

        var starterScenario = new SliceDesignScenario(
            $"DRAFT: {service.SimpleName} expected behavior",
            $"Required dependencies for {service.SimpleName}",
            $"{service.SimpleName} performs its operation",
            "The expected service behavior completes (DRAFT placeholder: refine with concrete assertions)");

        openQuestions.Add("Replace the DRAFT starter scenario with concrete Given/When/Then behavioral acceptance criteria.");
        openQuestions.Add("Specify any ForbiddenDependencies rules (e.g. outer layers or infrastructure namespaces) if architectural boundaries apply.");

        var design = new SliceDesign(
            SliceDesign.CurrentSchemaVersion,
            project,
            service.FullName,
            "Service",
            ResponseType: null,
            Handler: null,
            Validator: null,
            Endpoint: null,
            [starterScenario],
            Properties: null,
            publishes,
            dispatches,
            persistence,
            dependencies,
            ForbiddenDependencies: null);

        SliceDesignEvaluator.Validate(design);

        return new SliceDesignDraft(
            "drafted",
            service.FullName,
            design,
            gaps,
            openQuestions,
            [service.FullName]);
    }

    private static string MapEntrypointKind(SystemEntrypointKind kind) => kind switch
    {
        SystemEntrypointKind.Grpc => "Grpc",
        SystemEntrypointKind.Worker => "Worker",
        SystemEntrypointKind.MessageConsumer => "Consumer",
        _ => "Endpoint",
    };

    private static string ResolveCanonicalEntrypointTarget(AnalysisResult result, SystemEntrypointInfo entrypoint)
    {
        var isGenericMapName = entrypoint.Name is "MapGet" or "MapPost" or "MapPut" or "MapDelete" or "MapPatch";
        var sameProject = DeduplicateEntrypoints(result.SystemEntrypoints)
            .Where(e => string.Equals(e.ProjectName, entrypoint.ProjectName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (!isGenericMapName && !string.IsNullOrWhiteSpace(entrypoint.Name))
        {
            var nameCount = sameProject.Count(e => string.Equals(e.Name, entrypoint.Name, StringComparison.Ordinal));
            if (nameCount <= 1)
                return entrypoint.Name;
        }

        if (!string.IsNullOrWhiteSpace(entrypoint.DisplayLabel))
        {
            var labelCount = sameProject.Count(e => string.Equals(e.DisplayLabel, entrypoint.DisplayLabel, StringComparison.Ordinal));
            if (labelCount <= 1)
                return entrypoint.DisplayLabel;
        }

        return string.IsNullOrWhiteSpace(entrypoint.RouteOrTrigger)
            ? entrypoint.Name
            : $"{entrypoint.Name} ({entrypoint.RouteOrTrigger})";
    }

    private static SliceDesignEndpoint? BuildUniversalEndpoint(SystemEntrypointInfo entrypoint, List<string> gaps)
    {
        if (entrypoint.Kind is SystemEntrypointKind.Http or SystemEntrypointKind.Blazor or SystemEntrypointKind.RazorPage)
        {
            if (entrypoint.IsInferred)
            {
                gaps.Add($"Inferred-only endpoint omitted from draft ({entrypoint.DisplayLabel}); confirm the concrete method and route in source.");
                return null;
            }

            if (!string.IsNullOrWhiteSpace(entrypoint.Method) &&
                !entrypoint.Method.Any(char.IsWhiteSpace) &&
                !string.IsNullOrWhiteSpace(entrypoint.RouteOrTrigger) &&
                entrypoint.RouteOrTrigger.StartsWith('/'))
            {
                return new SliceDesignEndpoint(entrypoint.Method.ToUpperInvariant(), entrypoint.RouteOrTrigger);
            }

            return null;
        }

        if (entrypoint.Kind == SystemEntrypointKind.Grpc &&
            !string.IsNullOrWhiteSpace(entrypoint.RouteOrTrigger))
        {
            return new SliceDesignEndpoint("GRPC", entrypoint.RouteOrTrigger);
        }

        return null;
    }

    private static string ResolveProjectIdentifier(
        AnalysisResult result,
        string? projectName,
        SourceLocation? locationInfo,
        List<string> gaps,
        List<string> openQuestions)
    {
        ProjectInfo? owner = null;
        if (locationInfo is { } location && !string.IsNullOrEmpty(result.RootPath))
        {
            var fullPath = Path.Combine(result.RootPath, location.File.Replace('/', Path.DirectorySeparatorChar));
            owner = ProjectLocator.FindOwningProject(result.Projects, fullPath);
        }

        if (owner is null && !string.IsNullOrWhiteSpace(projectName))
        {
            var named = result.Projects
                .Where(p => string.Equals(p.Name, projectName, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (named.Length == 1)
                owner = named[0];
        }

        if (owner is not null)
        {
            var duplicateName = result.Projects.Count(p =>
                string.Equals(p.Name, owner.Name, StringComparison.OrdinalIgnoreCase)) > 1;
            if (duplicateName &&
                SourceLocation.FromLine(owner.FilePath, result.RootPath, 1, 1)?.File is { } relativePath)
            {
                return relativePath;
            }

            return owner.Name;
        }

        if (!string.IsNullOrWhiteSpace(projectName))
            return projectName.Trim();

        gaps.Add("Owning project could not be resolved for this target.");
        openQuestions.Add("Which .NET project owns this slice?");
        return "UnknownProject";
    }

    private static (string? HandlerName, HandlerInfo? ResolvedHandler) ResolveHandler(
        AnalysisResult result,
        CqrsRequest request,
        List<string> gaps,
        List<string> openQuestions)
    {
        if (request.Handlers.Count == 0)
        {
            gaps.Add("Missing handler: no handler is linked to this request.");
            return (null, null);
        }

        if (request.Handlers.Count > 1)
        {
            gaps.Add($"Multiple handlers ({request.Handlers.Count}) are linked to this request; handler left null in draft.");
            openQuestions.Add($"Confirm which handler owns {request.Name}: {string.Join(", ", request.Handlers)}.");
            return (null, null);
        }

        var target = request.Handlers[0];
        var isSimple = !target.Contains('.', StringComparison.Ordinal);
        var matches = result.Handlers
            .Where(h => string.Equals(h.FullName, target, StringComparison.Ordinal) ||
                        (isSimple && string.Equals(h.Name, target, StringComparison.Ordinal)))
            .ToArray();

        if (matches.Length == 1)
            return (matches[0].FullName, matches[0]);

        gaps.Add($"Linked handler '{target}' matched {matches.Length} declaration(s) in the scan; handler left null in draft.");
        return (null, null);
    }

    private static string? ResolveValidator(
        AnalysisResult result,
        CqrsRequest request,
        string kind,
        List<string> gaps,
        List<string> openQuestions)
    {
        if (request.Validators.Count == 0)
        {
            if (string.Equals(kind, "Command", StringComparison.Ordinal))
                gaps.Add("Command has no linked validator.");
            return null;
        }

        if (request.Validators.Count > 1)
        {
            gaps.Add($"Multiple validators ({request.Validators.Count}) are linked to this request; validator left null in draft.");
            openQuestions.Add($"Confirm which validator should be listed for {request.Name}: {string.Join(", ", request.Validators)}.");
            return null;
        }

        var target = request.Validators[0];
        var isSimple = !target.Contains('.', StringComparison.Ordinal);
        var matches = result.Validators
            .Where(v => string.Equals(v.FullName, target, StringComparison.Ordinal) ||
                        (isSimple && string.Equals(v.Name, target, StringComparison.Ordinal)))
            .ToArray();

        if (matches.Length == 1)
            return matches[0].FullName;

        gaps.Add($"Linked validator '{target}' matched {matches.Length} declaration(s) in the scan; validator left null in draft.");
        return null;
    }

    private static SliceDesignEndpoint? ResolveEndpoint(
        AnalysisResult result,
        CqrsRequest request,
        List<string> gaps,
        List<string> openQuestions)
    {
        var allowSimpleFallback = result.Requests.Count(r =>
            string.Equals(r.Name, request.Name, StringComparison.Ordinal)) == 1;

        var matching = result.Endpoints
            .Where(e =>
            {
                if (!string.IsNullOrEmpty(e.TargetRequestFullName))
                    return string.Equals(e.TargetRequestFullName, request.FullName, StringComparison.Ordinal);

                if (!string.IsNullOrEmpty(e.TargetRequestNamespace))
                {
                    return string.Equals(e.TargetRequestNamespace, request.Namespace, StringComparison.Ordinal) &&
                           string.Equals(e.TargetRequestName, request.Name, StringComparison.Ordinal);
                }

                return allowSimpleFallback && string.Equals(e.TargetRequestName, request.Name, StringComparison.Ordinal);
            })
            .ToArray();

        var explicitEndpoints = matching
            .Where(e => !e.IsInferred &&
                        !string.IsNullOrWhiteSpace(e.HttpMethod) &&
                        !e.HttpMethod.Any(char.IsWhiteSpace) &&
                        !string.IsNullOrWhiteSpace(e.Route) &&
                        e.Route.StartsWith('/'))
            .OrderBy(e => e.Route, StringComparer.Ordinal)
            .ThenBy(e => e.HttpMethod, StringComparer.Ordinal)
            .ToArray();

        if (explicitEndpoints.Length == 1)
            return new SliceDesignEndpoint(explicitEndpoints[0].HttpMethod.ToUpperInvariant(), explicitEndpoints[0].Route);

        if (explicitEndpoints.Length > 1)
        {
            openQuestions.Add(
                $"Multiple explicit endpoints dispatch this request ({string.Join(", ", explicitEndpoints.Select(e => e.DisplayLabel))}); selected {explicitEndpoints[0].DisplayLabel}.");
            return new SliceDesignEndpoint(explicitEndpoints[0].HttpMethod.ToUpperInvariant(), explicitEndpoints[0].Route);
        }

        var inferred = matching.Where(e => e.IsInferred).ToArray();
        if (inferred.Length > 0)
        {
            gaps.Add(
                $"Inferred-only endpoint(s) omitted from draft ({string.Join(", ", inferred.Select(e => e.DisplayLabel))}); confirm the concrete HTTP method and route in source.");
        }
        else
        {
            openQuestions.Add("No HTTP endpoint dispatches this request in the scan; confirm whether it is internal-only.");
        }

        return null;
    }

    private static SliceDesignPersistence? BuildPersistence(
        AnalysisResult result,
        CqrsRequest request,
        HandlerInfo? resolvedHandler)
    {
        var dbContexts = new List<string>();
        if (resolvedHandler is not null)
        {
            var direct = result.FlowEdges
                .Where(e => e.Kind == FlowEdgeKind.HandlerToDbContext &&
                            string.Equals(e.From, resolvedHandler.FullName, StringComparison.Ordinal))
                .Select(e => e.To);
            var resolved = result.ResolvedEdges
                .Where(e => e.Kind == FlowEdgeKind.HandlerToDbContextResolved &&
                            string.Equals(e.From, resolvedHandler.FullName, StringComparison.Ordinal))
                .Select(e => e.To);
            dbContexts = DeduplicateCaseInsensitive(direct.Concat(resolved));
        }

        var entityLabels = request.TouchedEntities
            .Concat(resolvedHandler?.TouchedEntities ?? Enumerable.Empty<string>())
            .Select(ExtractEntityName);
        var entities = DeduplicateCaseInsensitive(entityLabels);

        if (dbContexts.Count == 0 && entities.Count == 0)
            return null;

        return new SliceDesignPersistence(
            dbContexts.Count > 0 ? dbContexts : null,
            entities.Count > 0 ? entities : null);
    }

    private static HashSet<string> CollectTestProjects(AnalysisResult result)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in result.Projects)
        {
            if (string.Equals(p.Layer, "Tests", StringComparison.OrdinalIgnoreCase))
                names.Add(p.Name);
        }
        foreach (var tp in TestProjectDiscovery.DiscoverTestProjects(result))
            names.Add(tp.Name);
        return names;
    }

    private static string? InferProjectName(AnalysisResult result, SourceLocation? location)
    {
        if (location is null || string.IsNullOrEmpty(result.RootPath))
            return null;

        var fullPath = Path.Combine(result.RootPath, location.File.Replace('/', Path.DirectorySeparatorChar));
        return ProjectLocator.FindOwningProject(result.Projects, fullPath)?.Name;
    }

    private static string ExtractEntityName(string label)
    {
        var open = label.IndexOf('(');
        var close = label.LastIndexOf(')');
        return open >= 0 && close > open + 1
            ? label[(open + 1)..close].Trim()
            : label.Trim();
    }

    private static List<string> DeduplicateCaseInsensitive(IEnumerable<string> values)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (var raw in values)
        {
            var trimmed = raw.Trim();
            if (trimmed.Length == 0 || !seen.Add(trimmed))
                continue;
            list.Add(trimmed);
        }

        list.Sort(StringComparer.Ordinal);
        return list;
    }
}
