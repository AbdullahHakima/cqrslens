using System.Text;

namespace CqrsLens;

/// <summary>Compares a saved architecture design with facts from the static scan.</summary>
public static class SliceDesignEvaluator
{
    internal const string Matched = "matched";
    internal const string Mismatch = "mismatch";
    internal const string Missing = "missing";
    internal const string NotDeterminable = "not_determinable";

    public static void Validate(SliceDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        if (design.SchemaVersion != SliceDesign.CurrentSchemaVersion)
            throw new ArgumentException($"Unsupported slice design version {design.SchemaVersion}.");

        ArgumentException.ThrowIfNullOrWhiteSpace(design.Project, "project");
        ArgumentException.ThrowIfNullOrWhiteSpace(design.Request, "request");
        if (design.Kind is not ("Command" or "Query" or "Request" or "Endpoint" or "Grpc" or "Worker" or "Consumer" or "Service"))
            throw new ArgumentException("The slice kind must be Command, Query, Request, Endpoint, Grpc, Worker, Consumer, or Service.");

        ValidateOptionalString(design.ResponseType, "responseType");
        if (design.ResponseType == "void")
            throw new ArgumentException("Use null as the response type for a request with no response.");
        ValidateOptionalString(design.Handler, "handler");
        ValidateOptionalString(design.Validator, "validator");
        if (design.Endpoint is { } endpoint)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(endpoint.Method, "endpoint.method");
            if (endpoint.Method.Any(char.IsWhiteSpace))
                throw new ArgumentException("The endpoint method must be a single method or trigger token.");
            if (string.IsNullOrWhiteSpace(endpoint.Route))
                throw new ArgumentException("The endpoint route must not be blank.");

            var upperMethod = endpoint.Method.Trim().ToUpperInvariant();
            if (upperMethod is not ("GRPC" or "WORKER" or "CONSUME") && !endpoint.Route.StartsWith('/'))
                throw new ArgumentException("The endpoint route must start with '/'.");
        }

        if (design.Scenarios is not { Count: >= 1 and <= SliceDesign.MaxScenarios })
            throw new ArgumentException($"Provide between 1 and {SliceDesign.MaxScenarios} scenarios.");

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var scenario in design.Scenarios)
        {
            if (scenario is null || string.IsNullOrWhiteSpace(scenario.Name) ||
                string.IsNullOrWhiteSpace(scenario.Given) || string.IsNullOrWhiteSpace(scenario.When) ||
                string.IsNullOrWhiteSpace(scenario.Then))
            {
                throw new ArgumentException("Each scenario requires non-empty name, given, when, and then values.");
            }

            if (!names.Add(scenario.Name.Trim()))
                throw new ArgumentException($"The scenario name '{scenario.Name}' is repeated.");
        }

        if (design.Properties is { } properties)
        {
            var propertyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in properties)
            {
                if (prop is null || string.IsNullOrWhiteSpace(prop.Name))
                    throw new ArgumentException("Each declared property requires a non-empty name.");

                ValidateOptionalString(prop.Type, "properties[].type");
                if (!propertyNames.Add(prop.Name.Trim()))
                    throw new ArgumentException($"The property name '{prop.Name}' is repeated.");
            }
        }

        ValidateStringList(design.Publishes, "publishes");
        ValidateStringList(design.Dispatches, "dispatches");
        ValidateStringList(design.Persistence?.DbContexts, "persistence.dbContexts");
        ValidateStringList(design.Persistence?.Entities, "persistence.entities");
        ValidateStringList(design.Dependencies, "dependencies");
        ValidateStringList(design.ForbiddenDependencies, "forbiddenDependencies");
    }

    public static SliceDesignEvaluation Evaluate(AnalysisResult result, SliceDesign design)
    {
        ArgumentNullException.ThrowIfNull(result);
        Validate(design);

        return design.IsCqrsKind
            ? EvaluateCqrs(result, design)
            : EvaluateUniversal(result, design);
    }

    private static SliceDesignEvaluation EvaluateCqrs(AnalysisResult result, SliceDesign design)
    {
        var absenceStatus = CanConfirmAbsence(result) ? Missing : NotDeterminable;
        var checks = new List<SliceDesignCheck>();
        var project = CheckProject(result, design.Project, checks);
        var requestCandidates = result.Requests.Where(request => request.FullName == design.Request).ToArray();
        var request = CheckRequest(result, design, project, requestCandidates, absenceStatus, checks);

        checks.Add(CheckRequestValue("kind", design.Kind, request?.Kind, request));
        checks.Add(CheckRequestValue("responseType", design.ResponseType ?? "void", request?.ResponseType ?? "void", request));

        if (design.Handler is not null)
            checks.Add(CheckLink("handler", design.Handler, request, request?.Handlers, absenceStatus,
                result.Handlers.Select(handler => (handler.FullName, handler.Location))));

        if (design.Validator is not null)
            checks.Add(CheckLink("validator", design.Validator, request, request?.Validators, absenceStatus,
                result.Validators.Select(validator => (validator.FullName, validator.Location))));

        if (design.Endpoint is not null)
            checks.Add(SliceDesignEndpointCheck.Evaluate(result, design.Request, design.Endpoint, request is not null,
                requestCandidates.Length > 1, absenceStatus));

        if (design.Properties is not null || design.Publishes is not null || design.Dispatches is not null ||
            design.Persistence?.DbContexts is not null || design.Persistence?.Entities is not null ||
            design.Dependencies is not null || design.ForbiddenDependencies is not null)
        {
            var handlerContext = ResolveLinkedHandlers(result, request);

            if (design.Properties is { } properties)
                checks.Add(CheckProperties(request, properties, absenceStatus));

            if (design.Publishes is { } publishes)
                checks.Add(CheckPublishes(result, request, handlerContext, publishes));

            if (design.Dispatches is { } dispatches)
                checks.Add(CheckDispatches(result, request, handlerContext, dispatches));

            if (design.Persistence?.DbContexts is { } dbContexts)
                checks.Add(CheckDbContexts(result, request, handlerContext, dbContexts));

            if (design.Persistence?.Entities is { } entities)
                checks.Add(CheckEntities(result, request, handlerContext, entities));

            if (design.Dependencies is { } dependencies)
                checks.Add(CheckDependencies(result, request, handlerContext, dependencies));

            if (design.ForbiddenDependencies is { } forbidden)
                checks.Add(CheckForbiddenDependencies(result, request, handlerContext, forbidden));
        }

        var status = new[] { Mismatch, Missing, NotDeterminable, Matched }
            .First(candidate => checks.Any(check => check.Status == candidate));
        return new SliceDesignEvaluation(status, design, checks);
    }

    private static ProjectInfo? CheckProject(
        AnalysisResult result, string expected, List<SliceDesignCheck> checks)
    {
        var path = expected.Replace('\\', '/');
        if (path.StartsWith("./", StringComparison.Ordinal))
            path = path[2..];

        var projects = result.Projects.Where(project =>
            project.Name.Equals(expected, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ProjectLocation(result, project)?.File, path, StringComparison.Ordinal)).ToArray();
        var locations = projects.Select(project => ProjectLocation(result, project)).ToArray();
        var observed = locations.Select((location, index) => location?.File ?? projects[index].Name);
        if (projects.Length == 1)
        {
            checks.Add(Check("project", Matched, expected, observed, "The project was found in the scan.", locations));
            return projects[0];
        }

        checks.Add(projects.Length == 0
            ? Check("project", Missing, expected, [], "The project was not found in the scan.")
            : Check("project", NotDeterminable, expected, observed,
                "Multiple projects match. Use a root-relative project file path.", locations));
        return null;
    }

    private static CqrsRequest? CheckRequest(
        AnalysisResult result, SliceDesign design, ProjectInfo? project, CqrsRequest[] candidates,
        string absenceStatus, List<SliceDesignCheck> checks)
    {
        if (candidates.Length == 0)
        {
            checks.Add(Check("request", absenceStatus, design.Request, [],
                "The request was not found in the scanned model." + AbsenceNote(absenceStatus)));
            return null;
        }

        var owners = candidates.Select(request => ResolveOwner(result, request)).ToArray();
        var owned = candidates.Where((request, index) => project is not null && owners[index] == project).ToArray();
        var otherProjects = owners.OfType<ProjectInfo>().Select(owner => ProjectLocation(result, owner)?.File ?? owner.Name);
        var candidateEvidence = candidates.Select(request => request.Location).ToArray();
        if (owned.Length == 1 && owners.All(owner => owner is not null))
        {
            checks.Add(Check("request", Matched, design.Request, [owned[0].FullName],
                "The request was found in the selected project.", owned[0].Location));
            return owned[0];
        }

        if (project is not null && owned.Length == 0 && owners.All(owner => owner is not null))
        {
            checks.Add(Check("request", Mismatch, design.Request, otherProjects,
                "The request was found in another project.", candidateEvidence));
            return null;
        }

        checks.Add(Check("request", NotDeterminable, design.Request, otherProjects,
            "The scan could not identify one declaration in the selected project.", candidateEvidence));
        return null;
    }

    private static ProjectInfo? ResolveOwner(AnalysisResult result, CqrsRequest request)
    {
        if (request.Location is { } location)
        {
            var sourcePath = Path.Combine(result.RootPath, location.File.Replace('/', Path.DirectorySeparatorChar));
            var owner = ProjectLocator.FindOwningProject(result.Projects, sourcePath);
            if (owner is not null)
            {
                var owners = result.Projects.Where(project => string.Equals(
                    Path.TrimEndingDirectorySeparator(project.DirectoryPath),
                    Path.TrimEndingDirectorySeparator(owner.DirectoryPath), StringComparison.OrdinalIgnoreCase));
                return owners.Skip(1).Any() ? null : owner;
            }
        }

        var named = result.Projects.Where(project => project.Name.Equals(
            request.ProjectName, StringComparison.OrdinalIgnoreCase)).ToArray();
        return named.Length == 1 ? named[0] : null;
    }

    private static SourceLocation? ProjectLocation(AnalysisResult result, ProjectInfo project)
        => SourceLocation.FromLine(project.FilePath, result.RootPath, 1, 1);

    private static SliceDesignCheck CheckRequestValue(
        string field, string expected, string? observed, CqrsRequest? request)
    {
        if (request is null)
            return Check(field, NotDeterminable, expected, [], "Resolve the request before comparing this field.");

        if (observed is null)
            return Check(field, NotDeterminable, expected, [], "The scanner did not report a value.", request.Location);

        return Check(field, expected == observed ? Matched : Mismatch, expected, [observed],
            "The value is compared with the scanner's request label.", request.Location);
    }

    private static SliceDesignCheck CheckLink(
        string field, string expected, CqrsRequest? request, List<string>? links,
        string absenceStatus, IEnumerable<(string FullName, SourceLocation? Location)> shapes)
    {
        if (request is null || links is null)
            return Check(field, NotDeterminable, expected, [], "Resolve the request before comparing this link.");

        var relevant = shapes.Where(shape => shape.FullName == expected ||
            links.Contains(shape.FullName, StringComparer.Ordinal)).ToArray();
        var evidence = relevant.Select(shape => shape.Location).Prepend(request.Location).ToArray();
        if (links.Contains(expected, StringComparer.Ordinal))
        {
            if (relevant.Count(shape => shape.FullName == expected) != 1)
                return Check(field, NotDeterminable, expected, links,
                    "The link does not resolve to one scanned declaration.", evidence);

            if (field == "handler" && links.Distinct(StringComparer.Ordinal).Skip(1).Any())
                return Check(field, Mismatch, expected, links,
                    "The request has additional linked handlers.", evidence);

            return Check(field, Matched, expected, links, "The scan links this declaration to the request.", evidence);
        }

        if (absenceStatus == NotDeterminable)
            return Check(field, absenceStatus, expected, links, "The expected link was not observed." + AbsenceNote(absenceStatus), evidence);

        return links.Count > 0
            ? Check(field, Mismatch, expected, links, "The scan links different declarations to the request.", evidence)
            : Check(field, Missing, expected, links, "The scan found no link to this request.", evidence);
    }

    private static bool CanConfirmAbsence(AnalysisResult result) =>
        !result.HasSourceFilters && result.CanRunStrictChecks && !result.Diagnostics.Any(diagnostic =>
            diagnostic.Code is AnalysisResult.AmbiguousContractCode or AnalysisResult.UnresolvedContractCode);

    internal static string AbsenceNote(string status) => status == NotDeterminable
        ? " Source filters or scan diagnostics may hide the expected evidence." : "";

    internal static SliceDesignCheck Check(
        string field, string status, string expected, IEnumerable<string> observed, string message,
        params SourceLocation?[] locations) => new(
            field, status, expected, observed.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), message,
            locations.OfType<SourceLocation>().Distinct().OrderBy(location => location.File, StringComparer.Ordinal)
                .ThenBy(location => location.Line).ThenBy(location => location.Column).ToArray());

    private static void ValidateOptionalString(string? value, string field)
    {
        if (value is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(value, field);
    }

    private static void ValidateStringList(IReadOnlyList<string>? values, string fieldName)
    {
        if (values is null)
            return;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException($"Entries in '{fieldName}' must not be blank.");
            if (!seen.Add(value.Trim()))
                throw new ArgumentException($"Duplicate entry '{value}' in '{fieldName}'.");
        }
    }

    private sealed record LinkedHandlerContext(
        IReadOnlyList<HandlerInfo> Handlers,
        HandlerInfo? UniqueHandler,
        bool AllResolved);

    private static LinkedHandlerContext ResolveLinkedHandlers(AnalysisResult result, CqrsRequest? request)
    {
        if (request is null)
            return new LinkedHandlerContext([], null, false);

        var resolved = new List<HandlerInfo>();
        var allResolved = true;
        foreach (var hName in request.Handlers.Distinct(StringComparer.Ordinal))
        {
            var isSimple = !hName.Contains('.', StringComparison.Ordinal);
            var matches = result.Handlers
                .Where(h => string.Equals(h.FullName, hName, StringComparison.Ordinal) ||
                            (isSimple && string.Equals(h.Name, hName, StringComparison.Ordinal)))
                .ToArray();
            if (matches.Length == 1)
                resolved.Add(matches[0]);
            else
                allResolved = false;
        }

        var unique = request.Handlers.Count == 1 && resolved.Count == 1 && allResolved
            ? resolved[0]
            : null;
        return new LinkedHandlerContext(resolved, unique, allResolved);
    }

    private static SliceDesignCheck CheckProperties(
        CqrsRequest? request,
        IReadOnlyList<SliceDesignProperty> expectedProperties,
        string absenceStatus)
    {
        var expectedLabel = expectedProperties.Count == 0
            ? "(none)"
            : string.Join(", ", expectedProperties.Select(p =>
                p.Type is null ? p.Name.Trim() : $"{p.Name.Trim()}: {p.Type.Trim()}"));

        if (request is null)
            return Check("properties", NotDeterminable, expectedLabel, [], "Resolve the request before comparing properties.");

        var observedLabels = request.Properties
            .Select(p => $"{p.Name}: {p.Type}")
            .ToArray();

        if (expectedProperties.Count == 0)
        {
            if (request.Properties.Count > 0)
                return Check("properties", Mismatch, expectedLabel, observedLabels,
                    "The request declares properties not expected by the design.", request.Location);

            return Check("properties", absenceStatus == NotDeterminable ? NotDeterminable : Matched,
                expectedLabel, observedLabels,
                absenceStatus == NotDeterminable
                    ? "The request declares no properties, but scan completeness cannot confirm absence." + AbsenceNote(absenceStatus)
                    : "The request declares no properties, matching the design.",
                request.Location);
        }

        var hasMismatch = false;
        var hasMissing = false;
        foreach (var expected in expectedProperties)
        {
            var propName = expected.Name.Trim();
            var match = request.Properties.FirstOrDefault(p =>
                string.Equals(p.Name, propName, StringComparison.Ordinal));
            if (match is null)
            {
                hasMissing = true;
                continue;
            }

            if (expected.Type is not null && !TypesMatch(expected.Type, match.Type))
                hasMismatch = true;
        }

        if (hasMismatch)
            return Check("properties", Mismatch, expectedLabel, observedLabels,
                "One or more declared property types do not match the request declaration.", request.Location);

        if (hasMissing)
            return Check("properties", absenceStatus, expectedLabel, observedLabels,
                "One or more declared properties were not observed on the request." + AbsenceNote(absenceStatus),
                request.Location);

        return Check("properties", Matched, expectedLabel, observedLabels,
            "All declared properties match the scanned request.", request.Location);
    }

    private static SliceDesignCheck CheckPublishes(
        AnalysisResult result,
        CqrsRequest? request,
        LinkedHandlerContext handlers,
        IReadOnlyList<string> expectedPublishes)
    {
        var expectedLabel = expectedPublishes.Count == 0
            ? "(none)"
            : string.Join(", ", expectedPublishes.Select(p => p.Trim()));

        if (request is null)
            return Check("publishes", NotDeterminable, expectedLabel, [], "Resolve the request before comparing published notifications.");

        var observed = new List<string>();
        foreach (var n in request.PublishedNotifications)
        {
            if (!observed.Contains(n, StringComparer.Ordinal))
                observed.Add(n);
        }

        if (handlers.UniqueHandler is { } uniqueHandler)
        {
            foreach (var n in uniqueHandler.PublishedNotifications)
            {
                if (!observed.Contains(n, StringComparer.Ordinal))
                    observed.Add(n);
            }
        }

        var evidence = new List<SourceLocation?> { request.Location, handlers.UniqueHandler?.Location };
        foreach (var n in result.Notifications)
        {
            if (observed.Any(obs => NamesMatch(obs, n.FullName)))
                evidence.Add(n.Location);
        }

        var inconclusive = !CanConfirmAbsence(result) ||
                           result.UnresolvedDispatches.Count > 0 ||
                           (request.Handlers.Count > 0 && handlers.UniqueHandler is null);

        if (expectedPublishes.Count == 0)
        {
            if (observed.Count > 0)
                return Check("publishes", Mismatch, expectedLabel, observed,
                    "The slice publishes notifications not expected by the design.", evidence.ToArray());

            return inconclusive
                ? Check("publishes", NotDeterminable, expectedLabel, observed,
                    "No published notifications were observed, but unresolved dispatches or scan diagnostics prevent confirming absence.",
                    evidence.ToArray())
                : Check("publishes", Matched, expectedLabel, observed,
                    "The slice publishes no notifications, matching the design.", evidence.ToArray());
        }

        var allMatched = expectedPublishes.All(exp =>
            observed.Any(obs => NamesMatch(obs, exp.Trim())));
        if (allMatched)
            return Check("publishes", Matched, expectedLabel, observed,
                "All declared published notifications were observed.", evidence.ToArray());

        if (inconclusive)
            return Check("publishes", NotDeterminable, expectedLabel, observed,
                "One or more expected notifications were not observed, and unresolved dispatches or scan diagnostics prevent confirming absence.",
                evidence.ToArray());

        return observed.Count > 0
            ? Check("publishes", Mismatch, expectedLabel, observed,
                "The slice publishes different notifications than declared.", evidence.ToArray())
            : Check("publishes", Missing, expectedLabel, observed,
                "No matching published notification was observed in the scan.", evidence.ToArray());
    }

    private static SliceDesignCheck CheckDispatches(
        AnalysisResult result,
        CqrsRequest? request,
        LinkedHandlerContext handlers,
        IReadOnlyList<string> expectedDispatches)
    {
        var expectedLabel = expectedDispatches.Count == 0
            ? "(none)"
            : string.Join(", ", expectedDispatches.Select(d => d.Trim()));

        if (request is null)
            return Check("dispatches", NotDeterminable, expectedLabel, [], "Resolve the request before comparing dispatched requests.");

        var observed = new List<string>();
        foreach (var d in request.DispatchedRequests.Concat(handlers.Handlers.SelectMany(h => h.DispatchedRequests)))
        {
            if (!observed.Contains(d, StringComparer.Ordinal))
                observed.Add(d);
        }

        var evidence = new List<SourceLocation?> { request.Location };
        evidence.AddRange(handlers.Handlers.Select(h => h.Location));
        foreach (var req in result.Requests)
        {
            if (observed.Any(obs => NamesMatch(obs, req.FullName)))
                evidence.Add(req.Location);
        }

        var inconclusive = !CanConfirmAbsence(result) ||
                           result.UnresolvedDispatches.Count > 0 ||
                           (request.Handlers.Count > 0 && !handlers.AllResolved);

        if (expectedDispatches.Count == 0)
        {
            if (observed.Count > 0)
                return Check("dispatches", Mismatch, expectedLabel, observed,
                    "The slice dispatches requests not expected by the design.", evidence.ToArray());

            return inconclusive
                ? Check("dispatches", NotDeterminable, expectedLabel, observed,
                    "No dispatched requests were observed, but unresolved dispatches or scan diagnostics prevent confirming absence.",
                    evidence.ToArray())
                : Check("dispatches", Matched, expectedLabel, observed,
                    "The slice dispatches no nested requests, matching the design.", evidence.ToArray());
        }

        var allMatched = expectedDispatches.All(exp =>
            observed.Any(obs => NamesMatch(obs, exp.Trim())));
        if (allMatched)
            return Check("dispatches", Matched, expectedLabel, observed,
                "All declared dispatched requests were observed.", evidence.ToArray());

        if (inconclusive)
            return Check("dispatches", NotDeterminable, expectedLabel, observed,
                "One or more expected dispatched requests were not observed, and unresolved dispatches or scan diagnostics prevent confirming absence.",
                evidence.ToArray());

        return observed.Count > 0
            ? Check("dispatches", Mismatch, expectedLabel, observed,
                "The slice dispatches different requests than declared.", evidence.ToArray())
            : Check("dispatches", Missing, expectedLabel, observed,
                "No matching dispatched request was observed in the scan.", evidence.ToArray());
    }

    private static SliceDesignCheck CheckDbContexts(
        AnalysisResult result,
        CqrsRequest? request,
        LinkedHandlerContext handlers,
        IReadOnlyList<string> expectedDbContexts)
    {
        var expectedLabel = expectedDbContexts.Count == 0
            ? "(none)"
            : string.Join(", ", expectedDbContexts.Select(c => c.Trim()));

        if (request is null)
            return Check("persistence.dbContexts", NotDeterminable, expectedLabel, [],
                "Resolve the request before comparing DbContext usage.");

        var handlerNames = handlers.Handlers.Select(h => h.FullName).ToHashSet(StringComparer.Ordinal);
        var directEdges = result.FlowEdges
            .Where(e => e.Kind == FlowEdgeKind.HandlerToDbContext && handlerNames.Contains(e.From))
            .ToArray();
        var resolvedEdges = result.ResolvedEdges
            .Where(e => e.Kind == FlowEdgeKind.HandlerToDbContextResolved && handlerNames.Contains(e.From))
            .ToArray();

        var observed = directEdges.Select(e => e.To)
            .Concat(resolvedEdges.Select(e => e.To))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var evidence = new List<SourceLocation?> { request.Location };
        evidence.AddRange(handlers.Handlers.Select(h => h.Location));
        evidence.AddRange(directEdges.Select(e => e.Location));
        evidence.AddRange(resolvedEdges.Select(e => e.Location));

        var hasAmbiguousService = handlers.Handlers.Any(h =>
            h.Dependencies.Any(dep => MatchingServiceResolutions(result, dep).Any(sr => sr.IsAmbiguous)));
        var inconclusive = !CanConfirmAbsence(result) ||
                           (request.Handlers.Count > 0 && !handlers.AllResolved) ||
                           hasAmbiguousService;

        if (expectedDbContexts.Count == 0)
        {
            if (observed.Count > 0)
                return Check("persistence.dbContexts", Mismatch, expectedLabel, observed,
                    "The slice accesses DbContext(s) not expected by the design.", evidence.ToArray());

            return inconclusive || request.Handlers.Count == 0
                ? Check("persistence.dbContexts", NotDeterminable, expectedLabel, observed,
                    "No DbContext usage was observed, but missing handlers or scan diagnostics prevent confirming absence.",
                    evidence.ToArray())
                : Check("persistence.dbContexts", Matched, expectedLabel, observed,
                    "No DbContext usage was observed, matching the design.", evidence.ToArray());
        }

        var allMatched = expectedDbContexts.All(exp =>
            observed.Any(obs => NamesMatch(obs, exp.Trim())));
        if (allMatched)
            return Check("persistence.dbContexts", Matched, expectedLabel, observed,
                "All declared DbContexts are linked to the slice handler(s).", evidence.ToArray());

        if (inconclusive)
            return Check("persistence.dbContexts", NotDeterminable, expectedLabel, observed,
                "One or more expected DbContexts were not observed." + AbsenceNote(NotDeterminable),
                evidence.ToArray());

        return observed.Count > 0
            ? Check("persistence.dbContexts", Mismatch, expectedLabel, observed,
                "The slice uses different DbContext(s) than declared.", evidence.ToArray())
            : Check("persistence.dbContexts", Missing, expectedLabel, observed,
                "No matching DbContext edge was observed for the slice.", evidence.ToArray());
    }

    private static SliceDesignCheck CheckEntities(
        AnalysisResult result,
        CqrsRequest? request,
        LinkedHandlerContext handlers,
        IReadOnlyList<string> expectedEntities)
    {
        var expectedLabel = expectedEntities.Count == 0
            ? "(none)"
            : string.Join(", ", expectedEntities.Select(e => e.Trim()));

        if (request is null)
            return Check("persistence.entities", NotDeterminable, expectedLabel, [],
                "Resolve the request before comparing touched entities.");

        var observed = request.TouchedEntities
            .Concat(handlers.Handlers.SelectMany(h => h.TouchedEntities))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var evidence = new List<SourceLocation?> { request.Location };
        evidence.AddRange(handlers.Handlers.Select(h => h.Location));

        var inconclusive = !CanConfirmAbsence(result) ||
                           (request.Handlers.Count > 0 && !handlers.AllResolved);

        if (expectedEntities.Count == 0)
        {
            if (observed.Count > 0)
                return Check("persistence.entities", Mismatch, expectedLabel, observed,
                    "The slice touches entities not expected by the design.", evidence.ToArray());

            return inconclusive
                ? Check("persistence.entities", NotDeterminable, expectedLabel, observed,
                    "No touched entities were observed, but scan diagnostics prevent confirming absence.",
                    evidence.ToArray())
                : Check("persistence.entities", Matched, expectedLabel, observed,
                    "No touched entities were observed, matching the design.", evidence.ToArray());
        }

        var allMatched = expectedEntities.All(exp =>
            observed.Any(obs => MatchesEntity(obs, exp.Trim())));
        if (allMatched)
            return Check("persistence.entities", Matched, expectedLabel, observed,
                "All declared touched entities were observed in the slice.", evidence.ToArray());

        if (inconclusive)
            return Check("persistence.entities", NotDeterminable, expectedLabel, observed,
                "One or more expected entities were not observed." + AbsenceNote(NotDeterminable),
                evidence.ToArray());

        return observed.Count > 0
            ? Check("persistence.entities", Mismatch, expectedLabel, observed,
                "The slice touches different entities than declared.", evidence.ToArray())
            : Check("persistence.entities", Missing, expectedLabel, observed,
                "No matching touched entity was observed in the slice.", evidence.ToArray());
    }

    private static SliceDesignCheck CheckDependencies(
        AnalysisResult result,
        CqrsRequest? request,
        LinkedHandlerContext handlers,
        IReadOnlyList<string> expectedDependencies)
    {
        var expectedLabel = expectedDependencies.Count == 0
            ? "(none)"
            : string.Join(", ", expectedDependencies.Select(d => d.Trim()));

        if (request is null)
            return Check("dependencies", NotDeterminable, expectedLabel, [],
                "Resolve the request before comparing handler dependencies.");

        var observed = new List<string>();
        var evidence = new List<SourceLocation?> { request.Location };
        evidence.AddRange(handlers.Handlers.Select(h => h.Location));

        foreach (var handler in handlers.Handlers)
        {
            foreach (var dep in handler.Dependencies)
            {
                if (!observed.Contains(dep, StringComparer.Ordinal))
                    observed.Add(dep);

                foreach (var sr in MatchingServiceResolutions(result, dep))
                {
                    if (sr.Implementation is not null && !observed.Contains(sr.Implementation, StringComparer.Ordinal))
                        observed.Add(sr.Implementation);
                }
            }
        }

        var handlerNames = handlers.Handlers.Select(h => h.FullName).ToHashSet(StringComparer.Ordinal);
        foreach (var edge in result.ServiceCallEdges.Where(e => handlerNames.Contains(e.From)))
        {
            if (!observed.Contains(edge.Via, StringComparer.Ordinal))
                observed.Add(edge.Via);
            if (!observed.Contains(edge.To, StringComparer.Ordinal))
                observed.Add(edge.To);
            evidence.Add(edge.Location);
        }

        foreach (var edge in result.ResolvedEdges.Where(e => handlerNames.Contains(e.From)))
        {
            if (!observed.Contains(edge.Via, StringComparer.Ordinal))
                observed.Add(edge.Via);
            if (!observed.Contains(edge.To, StringComparer.Ordinal))
                observed.Add(edge.To);
            evidence.Add(edge.Location);
        }

        var inconclusive = !CanConfirmAbsence(result) ||
                           (request.Handlers.Count > 0 && !handlers.AllResolved);

        if (expectedDependencies.Count == 0)
        {
            if (observed.Count > 0)
                return Check("dependencies", Mismatch, expectedLabel, observed,
                    "The linked handler(s) declare dependencies not expected by the design.", evidence.ToArray());

            return inconclusive || request.Handlers.Count == 0
                ? Check("dependencies", NotDeterminable, expectedLabel, observed,
                    "No dependencies were observed, but missing handlers or scan diagnostics prevent confirming absence.",
                    evidence.ToArray())
                : Check("dependencies", Matched, expectedLabel, observed,
                    "The linked handler declares no dependencies, matching the design.", evidence.ToArray());
        }

        var allMatched = expectedDependencies.All(exp =>
        {
            var trimmed = exp.Trim();
            return observed.Any(obs => NamesMatch(obs, trimmed) || TypesMatch(trimmed, obs));
        });
        if (allMatched)
            return Check("dependencies", Matched, expectedLabel, observed,
                "All declared dependencies were observed on the linked handler(s).", evidence.ToArray());

        if (inconclusive)
            return Check("dependencies", NotDeterminable, expectedLabel, observed,
                "One or more expected dependencies were not observed." + AbsenceNote(NotDeterminable),
                evidence.ToArray());

        return observed.Count > 0
            ? Check("dependencies", Mismatch, expectedLabel, observed,
                "The linked handler(s) declare different dependencies than expected.", evidence.ToArray())
            : Check("dependencies", Missing, expectedLabel, observed,
                "No matching dependencies were observed on the slice.", evidence.ToArray());
    }

    private static SliceDesignCheck CheckForbiddenDependencies(
        AnalysisResult result,
        CqrsRequest? request,
        LinkedHandlerContext handlers,
        IReadOnlyList<string> forbiddenDependencies)
    {
        var expectedLabel = forbiddenDependencies.Count == 0
            ? "(none)"
            : string.Join(", ", forbiddenDependencies.Select(f => f.Trim()));

        if (request is null)
            return Check("forbiddenDependencies", NotDeterminable, expectedLabel, [],
                "Resolve the request before checking forbidden dependencies.");

        var candidates = new List<(string Name, string Display, SourceLocation? Location)>();
        var handlerLocations = new List<SourceLocation?> { request.Location };

        foreach (var handler in handlers.Handlers)
        {
            handlerLocations.Add(handler.Location);
            foreach (var dep in handler.Dependencies)
            {
                candidates.Add((dep, dep, handler.Location));
                foreach (var sr in MatchingServiceResolutions(result, dep))
                {
                    if (sr.Implementation is not null)
                        candidates.Add((sr.Implementation, $"{dep} -> {sr.Implementation}", handler.Location));
                }
            }

            foreach (var edge in result.FlowEdges.Where(e =>
                e.Kind == FlowEdgeKind.HandlerToDbContext &&
                string.Equals(e.From, handler.FullName, StringComparison.Ordinal)))
            {
                candidates.Add((edge.To, edge.To, edge.Location ?? handler.Location));
            }

            foreach (var edge in result.ServiceCallEdges.Where(e =>
                string.Equals(e.From, handler.FullName, StringComparison.Ordinal)))
            {
                candidates.Add((edge.Via, $"{edge.Via}.{edge.Method}", edge.Location ?? handler.Location));
                candidates.Add((edge.To, $"{edge.To}.{edge.Method}", edge.Location ?? handler.Location));
            }

            foreach (var edge in result.ResolvedEdges.Where(e =>
                string.Equals(e.From, handler.FullName, StringComparison.Ordinal)))
            {
                candidates.Add((edge.Via, edge.Via, edge.Location ?? handler.Location));
                candidates.Add((edge.To, edge.To, edge.Location ?? handler.Location));
            }
        }

        var violations = new List<(string Display, SourceLocation? Location)>();
        foreach (var candidate in candidates)
        {
            if (forbiddenDependencies.Any(forbidden => MatchesForbidden(candidate.Name, forbidden.Trim())))
                violations.Add((candidate.Display, candidate.Location));
        }

        if (violations.Count > 0)
        {
            return Check("forbiddenDependencies", Mismatch, expectedLabel,
                violations.Select(v => v.Display),
                "The linked handler references one or more forbidden dependencies.",
                violations.Select(v => v.Location).ToArray());
        }

        var observedAll = candidates.Select(c => c.Display).ToArray();
        if (request.Handlers.Count == 0 || !handlers.AllResolved || !CanConfirmAbsence(result))
        {
            return Check("forbiddenDependencies", NotDeterminable, expectedLabel, observedAll,
                "The scan cannot conclusively confirm the absence of forbidden dependencies." + AbsenceNote(NotDeterminable),
                handlerLocations.ToArray());
        }

        return Check("forbiddenDependencies", Matched, expectedLabel, observedAll,
            "No forbidden dependencies were observed on the linked handler.", handlerLocations.ToArray());
    }

    private static IEnumerable<ServiceResolution> MatchingServiceResolutions(AnalysisResult result, string dependency)
    {
        var (depName, depArgs) = CSharpSyntax.ParseTypeReference(dependency);
        if (depName.Length == 0)
            yield break;

        foreach (var sr in result.ServiceResolutions)
        {
            if (!NamesMatch(sr.Interface, dependency) && !NamesMatch(sr.InterfaceName, depName))
                continue;

            if (depArgs.Count > 0 && sr.Arity > 0 && depArgs.Count != sr.Arity)
                continue;

            if (depArgs.Count > 0 && sr.Arguments.Count == depArgs.Count)
            {
                var argsMatch = true;
                for (var i = 0; i < depArgs.Count; i++)
                {
                    if (!TypesMatch(depArgs[i], sr.Arguments[i]))
                    {
                        argsMatch = false;
                        break;
                    }
                }
                if (!argsMatch)
                    continue;
            }

            yield return sr;
        }
    }

    private static bool MatchesEntity(string observedLabel, string expected)
    {
        if (string.Equals(observedLabel, expected, StringComparison.Ordinal))
            return true;

        var open = observedLabel.IndexOf('(');
        var close = observedLabel.LastIndexOf(')');
        var entityName = open >= 0 && close > open + 1
            ? observedLabel[(open + 1)..close].Trim()
            : observedLabel.Trim();
        return NamesMatch(entityName, expected);
    }

    private static bool MatchesForbidden(string candidate, string forbidden)
    {
        if (string.Equals(candidate, forbidden, StringComparison.Ordinal))
            return true;

        var (baseName, _) = CSharpSyntax.ParseTypeReference(candidate);
        if (baseName.Length == 0)
            return false;

        if (forbidden.EndsWith('.'))
            return baseName.StartsWith(forbidden, StringComparison.Ordinal);

        return NamesMatch(baseName, forbidden) ||
               baseName.StartsWith(forbidden + ".", StringComparison.Ordinal);
    }

    private static bool NamesMatch(string stored, string expected)
    {
        if (string.Equals(stored, expected, StringComparison.Ordinal))
            return true;

        return stored.EndsWith("." + expected, StringComparison.Ordinal) ||
               expected.EndsWith("." + stored, StringComparison.Ordinal);
    }

    private static bool TypesMatch(string expected, string observed)
    {
        var a = NormalizeType(expected);
        var b = NormalizeType(observed);
        return TypesMatchNormalized(a, b);
    }

    private static bool TypesMatchNormalized(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.Ordinal))
            return true;

        if (a.StartsWith("global::", StringComparison.Ordinal))
            a = a[8..];
        if (b.StartsWith("global::", StringComparison.Ordinal))
            b = b[8..];

        if (a.EndsWith('?') != b.EndsWith('?'))
            return false;
        if (a.EndsWith('?'))
        {
            a = a[..^1];
            b = b[..^1];
        }

        if (a.EndsWith("[]", StringComparison.Ordinal) != b.EndsWith("[]", StringComparison.Ordinal))
            return false;
        while (a.EndsWith("[]", StringComparison.Ordinal) && b.EndsWith("[]", StringComparison.Ordinal))
        {
            a = a[..^2];
            b = b[..^2];
        }

        var (aName, aArgs) = CSharpSyntax.ParseTypeReference(a);
        var (bName, bArgs) = CSharpSyntax.ParseTypeReference(b);
        if (aArgs.Count != bArgs.Count)
            return false;

        var outerMatches = string.Equals(aName, bName, StringComparison.Ordinal) ||
                           ((!aName.Contains('.', StringComparison.Ordinal) || !bName.Contains('.', StringComparison.Ordinal)) &&
                            string.Equals(CSharpSyntax.SimpleName(aName), CSharpSyntax.SimpleName(bName), StringComparison.Ordinal));
        if (!outerMatches)
            return false;

        for (var i = 0; i < aArgs.Count; i++)
        {
            if (!TypesMatchNormalized(aArgs[i], bArgs[i]))
                return false;
        }

        return true;
    }

    private static string NormalizeType(string value)
    {
        var sb = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var ch in value.Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }

            if (ch is '<' or '>' or ',' or '?' or '[' or ']' or '.')
            {
                pendingSpace = false;
                sb.Append(ch);
                continue;
            }

            if (pendingSpace && sb.Length > 0 && sb[^1] is not ('<' or ',' or '[' or '.'))
                sb.Append(' ');
            pendingSpace = false;
            sb.Append(ch);
        }
        return sb.ToString();
    }

    private sealed record UniversalTarget(
        bool IsService,
        SystemEntrypointInfo? Entrypoint,
        string FullName,
        string ObservedKind,
        string? ProjectName,
        SourceLocation? Location);

    private static SliceDesignEvaluation EvaluateUniversal(AnalysisResult result, SliceDesign design)
    {
        var absenceStatus = CanConfirmAbsence(result) ? Missing : NotDeterminable;
        var checks = new List<SliceDesignCheck>();
        var project = CheckProject(result, design.Project, checks);

        var target = design.Kind == "Service"
            ? CheckUniversalServiceTarget(result, design, project, absenceStatus, checks)
            : CheckUniversalEntrypointTarget(result, design, project, absenceStatus, checks);

        checks.Add(CheckUniversalKind(design.Kind, target));
        checks.Add(CheckUniversalResponseType(result, design, target));

        if (design.Handler is not null)
            checks.Add(CheckUniversalLink("handler", design.Handler, target, isValidator: false, absenceStatus, result));

        if (design.Validator is not null)
            checks.Add(CheckUniversalLink("validator", design.Validator, target, isValidator: true, absenceStatus, result));

        if (design.Endpoint is not null)
            checks.Add(CheckUniversalEndpoint(result, design.Endpoint, target, absenceStatus));

        if (design.Properties is { } properties)
            checks.Add(CheckUniversalProperties(target, properties, absenceStatus));

        if (design.Publishes is { } publishes)
            checks.Add(CheckUniversalPublishes(result, target, publishes));

        if (design.Dispatches is { } dispatches)
            checks.Add(CheckUniversalDispatches(result, target, dispatches));

        if (design.Persistence?.DbContexts is { } dbContexts)
            checks.Add(CheckUniversalDbContexts(result, target, dbContexts));

        if (design.Persistence?.Entities is { } entities)
            checks.Add(CheckUniversalEntities(result, target, entities));

        if (design.Dependencies is { } dependencies)
            checks.Add(CheckUniversalDependencies(result, target, dependencies));

        if (design.ForbiddenDependencies is { } forbidden)
            checks.Add(CheckUniversalForbiddenDependencies(result, target, forbidden));

        var status = new[] { Mismatch, Missing, NotDeterminable, Matched }
            .First(candidate => checks.Any(check => check.Status == candidate));
        return new SliceDesignEvaluation(status, design, checks);
    }

    private static UniversalTarget? CheckUniversalEntrypointTarget(
        AnalysisResult result,
        SliceDesign design,
        ProjectInfo? project,
        string absenceStatus,
        List<SliceDesignCheck> checks)
    {
        var candidates = DeduplicateEntrypoints(result.SystemEntrypoints)
            .Where(e => EntrypointMatches(e, design.Request))
            .ToArray();

        if (candidates.Length == 0)
        {
            var serviceFallback = CollectServiceTargets(result)
                .Where(s => string.Equals(s.FullName, design.Request, StringComparison.Ordinal))
                .ToArray();
            if (serviceFallback.Length == 1 && project is not null && serviceFallback[0].Owner == project)
            {
                var svc = serviceFallback[0];
                checks.Add(Check("request", Matched, design.Request, [svc.FullName],
                    "The target type was found in the selected project.", svc.Location));
                return new UniversalTarget(true, null, svc.FullName, "Service", project.Name, svc.Location);
            }

            checks.Add(Check("request", absenceStatus, design.Request, [],
                "The target operation was not found in the scanned model." + AbsenceNote(absenceStatus)));
            return null;
        }

        var owners = candidates.Select(e => ResolveEntrypointOwner(result, e)).ToArray();
        var owned = candidates.Where((_, idx) => project is not null && owners[idx] == project).ToArray();
        if (owned.Length > 1 && design.Endpoint is { } epFilter)
        {
            var routeFiltered = owned
                .Where(e => e.Method.Equals(epFilter.Method, StringComparison.OrdinalIgnoreCase) &&
                            e.RouteOrTrigger.Equals(epFilter.Route, StringComparison.Ordinal))
                .ToArray();
            if (routeFiltered.Length == 1)
                owned = routeFiltered;
        }

        var otherProjects = owners.OfType<ProjectInfo>()
            .Select(owner => ProjectLocation(result, owner)?.File ?? owner.Name);
        var evidence = candidates.Select(e => e.Location).ToArray();

        if (owned.Length == 1 && owners.All(o => o is not null))
        {
            var picked = owned[0];
            var observedName = string.IsNullOrWhiteSpace(picked.Name) ? picked.DisplayLabel : picked.Name;
            checks.Add(Check("request", Matched, design.Request, [observedName],
                "The target operation was found in the selected project.", picked.Location));
            return new UniversalTarget(
                false,
                picked,
                observedName,
                MapEntrypointKind(picked.Kind),
                project!.Name,
                picked.Location);
        }

        if (project is not null && owned.Length == 0 && owners.All(o => o is not null))
        {
            checks.Add(Check("request", Mismatch, design.Request, otherProjects,
                "The target operation was found in another project.", evidence));
            return null;
        }

        checks.Add(Check("request", NotDeterminable, design.Request, otherProjects,
            "The scan could not identify one target declaration in the selected project.", evidence));
        return null;
    }

    private static UniversalTarget? CheckUniversalServiceTarget(
        AnalysisResult result,
        SliceDesign design,
        ProjectInfo? project,
        string absenceStatus,
        List<SliceDesignCheck> checks)
    {
        var candidates = CollectServiceTargets(result)
            .Where(s => string.Equals(s.FullName, design.Request, StringComparison.Ordinal))
            .ToArray();

        if (candidates.Length == 0)
        {
            var epFallback = DeduplicateEntrypoints(result.SystemEntrypoints)
                .Where(e => EntrypointMatches(e, design.Request))
                .ToArray();
            if (epFallback.Length == 1 && project is not null && ResolveEntrypointOwner(result, epFallback[0]) == project)
            {
                var ep = epFallback[0];
                checks.Add(Check("request", Matched, design.Request, [ep.Name],
                    "The target declaration was found in the selected project.", ep.Location));
                return new UniversalTarget(false, ep, ep.Name, MapEntrypointKind(ep.Kind), project.Name, ep.Location);
            }

            checks.Add(Check("request", absenceStatus, design.Request, [],
                "The service class was not found in the scanned model." + AbsenceNote(absenceStatus)));
            return null;
        }

        var owned = candidates.Where(c => project is not null && c.Owner == project).ToArray();
        var otherProjects = candidates.Select(c => c.Owner)
            .OfType<ProjectInfo>()
            .Select(owner => ProjectLocation(result, owner)?.File ?? owner.Name);
        var evidence = candidates.Select(c => c.Location).ToArray();

        if (owned.Length == 1 && candidates.All(c => c.Owner is not null))
        {
            var picked = owned[0];
            checks.Add(Check("request", Matched, design.Request, [picked.FullName],
                "The service class was found in the selected project.", picked.Location));
            return new UniversalTarget(true, null, picked.FullName, "Service", picked.Owner!.Name, picked.Location);
        }

        if (project is not null && owned.Length == 0 && candidates.All(c => c.Owner is not null))
        {
            checks.Add(Check("request", Mismatch, design.Request, otherProjects,
                "The service class was found in another project.", evidence));
            return null;
        }

        checks.Add(Check("request", NotDeterminable, design.Request, otherProjects,
            "The scan could not identify one service declaration in the selected project.", evidence));
        return null;
    }

    private static SliceDesignCheck CheckUniversalKind(string expectedKind, UniversalTarget? target)
    {
        if (target is null)
            return Check("kind", NotDeterminable, expectedKind, [], "Resolve the target before comparing kind.");

        return Check("kind", expectedKind == target.ObservedKind ? Matched : Mismatch,
            expectedKind, [target.ObservedKind],
            "The value is compared with the scanner's operation kind.", target.Location);
    }

    private static SliceDesignCheck CheckUniversalResponseType(
        AnalysisResult result,
        SliceDesign design,
        UniversalTarget? target)
    {
        var expected = design.ResponseType ?? "void";
        if (target is null)
            return Check("responseType", NotDeterminable, expected, [], "Resolve the target before comparing responseType.");

        if (design.ResponseType is null)
        {
            return Check("responseType", Matched, expected, ["void"],
                "No response type is declared for this operation.", target.Location);
        }

        if (target.Entrypoint is { TargetRequests.Count: 1 } ep)
        {
            var matchedReq = result.Requests.FirstOrDefault(r =>
                string.Equals(r.FullName, ep.TargetRequests[0], StringComparison.Ordinal));
            if (matchedReq is not null)
            {
                var observed = matchedReq.ResponseType ?? "void";
                return Check("responseType", design.ResponseType == observed ? Matched : Mismatch,
                    expected, [observed],
                    "The value is compared with the linked CQRS request response type.", target.Location);
            }
        }

        return Check("responseType", NotDeterminable, expected, [],
            "Non-CQRS operation scans do not capture return types; set responseType to null.", target.Location);
    }

    private static SliceDesignCheck CheckUniversalLink(
        string field,
        string expected,
        UniversalTarget? target,
        bool isValidator,
        string absenceStatus,
        AnalysisResult result)
    {
        if (target is null)
            return Check(field, NotDeterminable, expected, [], $"Resolve the target before comparing {field}.");

        if (target.Entrypoint is { TargetRequests.Count: > 0 } ep)
        {
            var req = result.Requests.FirstOrDefault(r => ep.TargetRequests.Contains(r.FullName, StringComparer.Ordinal));
            if (req is not null)
            {
                var links = isValidator ? req.Validators : req.Handlers;
                var shapes = isValidator
                    ? result.Validators.Select(v => (v.FullName, v.Location))
                    : result.Handlers.Select(h => (h.FullName, h.Location));
                return CheckLink(field, expected, req, links, absenceStatus, shapes);
            }
        }

        return Check(field, absenceStatus == NotDeterminable ? NotDeterminable : Missing, expected, [],
            $"Non-CQRS slices do not use a MediatR {field}; omit {field} or set it to null.", target.Location);
    }

    private static SliceDesignCheck CheckUniversalEndpoint(
        AnalysisResult result,
        SliceDesignEndpoint endpoint,
        UniversalTarget? target,
        string absenceStatus)
    {
        var expected = $"{endpoint.Method.ToUpperInvariant()} {endpoint.Route}";
        var allEntrypoints = DeduplicateEntrypoints(result.SystemEntrypoints);
        var matchingRoute = allEntrypoints
            .Where(e => e.Method.Equals(endpoint.Method, StringComparison.OrdinalIgnoreCase) &&
                        e.RouteOrTrigger.Equals(endpoint.Route, StringComparison.Ordinal))
            .ToArray();

        if (target is null)
        {
            if (matchingRoute.Any(e => e.IsInferred))
            {
                return Check("endpoint", NotDeterminable, expected, matchingRoute.Select(e => e.DisplayLabel),
                    "The route or trigger was inferred.", matchingRoute.Select(e => e.Location).ToArray());
            }

            if (matchingRoute.Length > 0)
            {
                return Check("endpoint", Mismatch, expected, matchingRoute.Select(e => $"{e.DisplayLabel} ({e.Name})"),
                    "The scan links this route/trigger and method to a different operation.",
                    matchingRoute.Select(e => e.Location).ToArray());
            }

            return Check("endpoint", absenceStatus, expected, [],
                "The scan found no association for the planned route or trigger." + AbsenceNote(absenceStatus));
        }

        if (target.Entrypoint is not { } ep)
        {
            return Check("endpoint", absenceStatus == NotDeterminable ? NotDeterminable : Missing, expected, [],
                "A Service slice does not expose a direct route or trigger.", target.Location);
        }

        if (ep.IsInferred)
        {
            return Check("endpoint", NotDeterminable, expected, [ep.DisplayLabel],
                "The route or trigger was inferred rather than explicitly declared.", ep.Location);
        }

        if (ep.Method.Equals(endpoint.Method, StringComparison.OrdinalIgnoreCase) &&
            ep.RouteOrTrigger.Equals(endpoint.Route, StringComparison.Ordinal))
        {
            return Check("endpoint", Matched, expected, [ep.DisplayLabel],
                "The scan links the declared route/trigger and method to the operation.", ep.Location);
        }

        return Check("endpoint", Mismatch, expected, [ep.DisplayLabel],
            "The scan links the operation to a different declared route/trigger or method.", ep.Location);
    }

    private static SliceDesignCheck CheckUniversalProperties(
        UniversalTarget? target,
        IReadOnlyList<SliceDesignProperty> expectedProperties,
        string absenceStatus)
    {
        var expectedLabel = expectedProperties.Count == 0
            ? "(none)"
            : string.Join(", ", expectedProperties.Select(p =>
                p.Type is null ? p.Name.Trim() : $"{p.Name.Trim()}: {p.Type.Trim()}"));

        if (target is null)
            return Check("properties", NotDeterminable, expectedLabel, [], "Resolve the target before comparing properties.");

        if (expectedProperties.Count == 0)
        {
            return Check("properties", absenceStatus == NotDeterminable ? NotDeterminable : Matched,
                expectedLabel, [],
                absenceStatus == NotDeterminable
                    ? "Scan completeness cannot confirm absence of properties." + AbsenceNote(absenceStatus)
                    : "No properties are declared, matching the design.",
                target.Location);
        }

        return Check("properties", NotDeterminable, expectedLabel, [],
            "Non-CQRS operation scans do not extract input DTO properties.", target.Location);
    }

    private static SliceDesignCheck CheckUniversalPublishes(
        AnalysisResult result,
        UniversalTarget? target,
        IReadOnlyList<string> expectedPublishes)
    {
        var expectedLabel = expectedPublishes.Count == 0
            ? "(none)"
            : string.Join(", ", expectedPublishes.Select(p => p.Trim()));

        if (target is null)
            return Check("publishes", NotDeterminable, expectedLabel, [], "Resolve the target before comparing published events.");

        var observed = new List<string>();
        if (target.Entrypoint is { } ep)
        {
            observed.AddRange(ep.PublishedMessages);
        }
        else
        {
            foreach (var site in result.DispatchCallSites)
            {
                if (!site.IsTestProject &&
                    site.TargetKind == DispatchTargetKind.Notification &&
                    string.Equals(site.SenderFullName, target.FullName, StringComparison.Ordinal) &&
                    !observed.Contains(site.TargetFullName, StringComparer.Ordinal))
                {
                    observed.Add(site.TargetFullName);
                }
            }
        }

        var inconclusive = !CanConfirmAbsence(result) || result.UnresolvedDispatches.Any(u => !u.IsTestProject);

        if (expectedPublishes.Count == 0)
        {
            if (observed.Count > 0)
                return Check("publishes", Mismatch, expectedLabel, observed,
                    "The slice publishes events not expected by the design.", target.Location);

            return inconclusive
                ? Check("publishes", NotDeterminable, expectedLabel, observed,
                    "No published events were observed, but unresolved dispatches or scan diagnostics prevent confirming absence.",
                    target.Location)
                : Check("publishes", Matched, expectedLabel, observed,
                    "The slice publishes no events, matching the design.", target.Location);
        }

        var allMatched = expectedPublishes.All(exp =>
            observed.Any(obs => NamesMatch(obs, exp.Trim())));
        if (allMatched)
            return Check("publishes", Matched, expectedLabel, observed,
                "All declared published events were observed.", target.Location);

        if (inconclusive)
            return Check("publishes", NotDeterminable, expectedLabel, observed,
                "One or more expected published events were not observed, and unresolved dispatches or scan diagnostics prevent confirming absence.",
                target.Location);

        return observed.Count > 0
            ? Check("publishes", Mismatch, expectedLabel, observed,
                "The slice publishes different events than declared.", target.Location)
            : Check("publishes", Missing, expectedLabel, observed,
                "No matching published event was observed in the scan.", target.Location);
    }

    private static SliceDesignCheck CheckUniversalDispatches(
        AnalysisResult result,
        UniversalTarget? target,
        IReadOnlyList<string> expectedDispatches)
    {
        var expectedLabel = expectedDispatches.Count == 0
            ? "(none)"
            : string.Join(", ", expectedDispatches.Select(d => d.Trim()));

        if (target is null)
            return Check("dispatches", NotDeterminable, expectedLabel, [], "Resolve the target before comparing dispatched requests.");

        var observed = new List<string>();
        if (target.Entrypoint is { } ep)
        {
            observed.AddRange(ep.TargetRequests);
        }
        else
        {
            foreach (var site in result.DispatchCallSites)
            {
                if (!site.IsTestProject &&
                    site.TargetKind == DispatchTargetKind.Request &&
                    string.Equals(site.SenderFullName, target.FullName, StringComparison.Ordinal) &&
                    !observed.Contains(site.TargetFullName, StringComparer.Ordinal))
                {
                    observed.Add(site.TargetFullName);
                }
            }
        }

        var inconclusive = !CanConfirmAbsence(result) || result.UnresolvedDispatches.Any(u => !u.IsTestProject);

        if (expectedDispatches.Count == 0)
        {
            if (observed.Count > 0)
                return Check("dispatches", Mismatch, expectedLabel, observed,
                    "The slice dispatches requests not expected by the design.", target.Location);

            return inconclusive
                ? Check("dispatches", NotDeterminable, expectedLabel, observed,
                    "No dispatched requests were observed, but unresolved dispatches or scan diagnostics prevent confirming absence.",
                    target.Location)
                : Check("dispatches", Matched, expectedLabel, observed,
                    "The slice dispatches no nested requests, matching the design.", target.Location);
        }

        var allMatched = expectedDispatches.All(exp =>
            observed.Any(obs => NamesMatch(obs, exp.Trim())));
        if (allMatched)
            return Check("dispatches", Matched, expectedLabel, observed,
                "All declared dispatched requests were observed.", target.Location);

        if (inconclusive)
            return Check("dispatches", NotDeterminable, expectedLabel, observed,
                "One or more expected dispatched requests were not observed, and unresolved dispatches or scan diagnostics prevent confirming absence.",
                target.Location);

        return observed.Count > 0
            ? Check("dispatches", Mismatch, expectedLabel, observed,
                "The slice dispatches different requests than declared.", target.Location)
            : Check("dispatches", Missing, expectedLabel, observed,
                "No matching dispatched request was observed in the scan.", target.Location);
    }

    private static SliceDesignCheck CheckUniversalDbContexts(
        AnalysisResult result,
        UniversalTarget? target,
        IReadOnlyList<string> expectedDbContexts)
    {
        var expectedLabel = expectedDbContexts.Count == 0
            ? "(none)"
            : string.Join(", ", expectedDbContexts.Select(c => c.Trim()));

        if (target is null)
            return Check("persistence.dbContexts", NotDeterminable, expectedLabel, [],
                "Resolve the target before comparing DbContext usage.");

        var observed = new List<string>();
        var evidence = new List<SourceLocation?> { target.Location };
        var hasAmbiguousService = false;

        if (target.Entrypoint is { } ep)
        {
            observed.AddRange(ep.TouchedDbContexts.Distinct(StringComparer.Ordinal));
            foreach (var svc in ep.CalledServices)
            {
                foreach (var token in ExpandServicePair(svc))
                {
                    if (MatchingServiceResolutions(result, token).Any(sr => sr.IsAmbiguous))
                        hasAmbiguousService = true;
                }
            }
        }
        else
        {
            foreach (var edge in result.ResolvedEdges.Where(e =>
                e.Kind == FlowEdgeKind.HandlerToDbContextResolved &&
                string.Equals(e.From, target.FullName, StringComparison.Ordinal)))
            {
                if (!observed.Contains(edge.To, StringComparer.Ordinal))
                    observed.Add(edge.To);
                evidence.Add(edge.Location);
            }
        }

        var inconclusive = !CanConfirmAbsence(result) || hasAmbiguousService || target.IsService;

        if (expectedDbContexts.Count == 0)
        {
            if (observed.Count > 0)
                return Check("persistence.dbContexts", Mismatch, expectedLabel, observed,
                    "The slice accesses DbContext(s) not expected by the design.", evidence.ToArray());

            return inconclusive
                ? Check("persistence.dbContexts", NotDeterminable, expectedLabel, observed,
                    "No DbContext usage was observed, but scan scope cannot conclusively confirm absence.",
                    evidence.ToArray())
                : Check("persistence.dbContexts", Matched, expectedLabel, observed,
                    "No DbContext usage was observed, matching the design.", evidence.ToArray());
        }

        var allMatched = expectedDbContexts.All(exp =>
            observed.Any(obs => NamesMatch(obs, exp.Trim())));
        if (allMatched)
            return Check("persistence.dbContexts", Matched, expectedLabel, observed,
                "All declared DbContexts are linked to the slice.", evidence.ToArray());

        if (inconclusive)
            return Check("persistence.dbContexts", NotDeterminable, expectedLabel, observed,
                "One or more expected DbContexts were not observed." + AbsenceNote(NotDeterminable),
                evidence.ToArray());

        return observed.Count > 0
            ? Check("persistence.dbContexts", Mismatch, expectedLabel, observed,
                "The slice uses different DbContext(s) than declared.", evidence.ToArray())
            : Check("persistence.dbContexts", Missing, expectedLabel, observed,
                "No matching DbContext usage was observed for the slice.", evidence.ToArray());
    }

    private static SliceDesignCheck CheckUniversalEntities(
        AnalysisResult result,
        UniversalTarget? target,
        IReadOnlyList<string> expectedEntities)
    {
        var expectedLabel = expectedEntities.Count == 0
            ? "(none)"
            : string.Join(", ", expectedEntities.Select(e => e.Trim()));

        if (target is null)
            return Check("persistence.entities", NotDeterminable, expectedLabel, [],
                "Resolve the target before comparing touched entities.");

        var observed = target.Entrypoint is { } ep
            ? ep.TouchedEntities.Distinct(StringComparer.Ordinal).ToList()
            : [];

        var inconclusive = !CanConfirmAbsence(result) || target.IsService;

        if (expectedEntities.Count == 0)
        {
            if (observed.Count > 0)
                return Check("persistence.entities", Mismatch, expectedLabel, observed,
                    "The slice touches entities not expected by the design.", target.Location);

            return inconclusive
                ? Check("persistence.entities", NotDeterminable, expectedLabel, observed,
                    "No touched entities were observed, but scan scope cannot confirm absence.", target.Location)
                : Check("persistence.entities", Matched, expectedLabel, observed,
                    "No touched entities were observed, matching the design.", target.Location);
        }

        var allMatched = expectedEntities.All(exp =>
            observed.Any(obs => MatchesEntity(obs, exp.Trim())));
        if (allMatched)
            return Check("persistence.entities", Matched, expectedLabel, observed,
                "All declared touched entities were observed in the slice.", target.Location);

        if (inconclusive)
            return Check("persistence.entities", NotDeterminable, expectedLabel, observed,
                "One or more expected entities were not observed." + AbsenceNote(NotDeterminable),
                target.Location);

        return observed.Count > 0
            ? Check("persistence.entities", Mismatch, expectedLabel, observed,
                "The slice touches different entities than declared.", target.Location)
            : Check("persistence.entities", Missing, expectedLabel, observed,
                "No matching touched entity was observed in the slice.", target.Location);
    }

    private static SliceDesignCheck CheckUniversalDependencies(
        AnalysisResult result,
        UniversalTarget? target,
        IReadOnlyList<string> expectedDependencies)
    {
        var expectedLabel = expectedDependencies.Count == 0
            ? "(none)"
            : string.Join(", ", expectedDependencies.Select(d => d.Trim()));

        if (target is null)
            return Check("dependencies", NotDeterminable, expectedLabel, [],
                "Resolve the target before comparing dependencies.");

        var observed = new List<string>();
        var evidence = new List<SourceLocation?> { target.Location };

        void AddObserved(string value)
        {
            if (!string.IsNullOrWhiteSpace(value) && !observed.Contains(value, StringComparer.Ordinal))
                observed.Add(value);
        }

        if (target.Entrypoint is { } ep)
        {
            var serviceCallRoots = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in ep.CalledServices)
            {
                AddObserved(raw);
                foreach (var token in ExpandServicePair(raw))
                {
                    AddObserved(token);
                    serviceCallRoots.Add(token);
                    foreach (var sr in MatchingServiceResolutions(result, token))
                    {
                        if (sr.Implementation is not null)
                        {
                            AddObserved(sr.Implementation);
                            serviceCallRoots.Add(sr.Implementation);
                        }
                    }
                }
            }

            foreach (var edge in result.ServiceCallEdges.Where(e => serviceCallRoots.Contains(e.From)))
            {
                AddObserved(edge.Via);
                AddObserved(edge.To);
                evidence.Add(edge.Location);
            }
        }
        else
        {
            foreach (var edge in result.ServiceCallEdges.Where(e =>
                string.Equals(e.From, target.FullName, StringComparison.Ordinal)))
            {
                AddObserved(string.Equals(edge.Via, edge.To, StringComparison.Ordinal) ? edge.To : $"{edge.Via} -> {edge.To}");
                AddObserved(edge.Via);
                AddObserved(edge.To);
                evidence.Add(edge.Location);
            }

            foreach (var edge in result.ResolvedEdges.Where(e =>
                string.Equals(e.From, target.FullName, StringComparison.Ordinal)))
            {
                AddObserved(edge.Via);
                AddObserved(edge.To);
                evidence.Add(edge.Location);
            }
        }

        var inconclusive = !CanConfirmAbsence(result);

        if (expectedDependencies.Count == 0)
        {
            if (observed.Count > 0)
                return Check("dependencies", Mismatch, expectedLabel, observed,
                    "The slice declares dependencies not expected by the design.", evidence.ToArray());

            return inconclusive
                ? Check("dependencies", NotDeterminable, expectedLabel, observed,
                    "No dependencies were observed, but scan diagnostics prevent confirming absence.",
                    evidence.ToArray())
                : Check("dependencies", Matched, expectedLabel, observed,
                    "The slice declares no dependencies, matching the design.", evidence.ToArray());
        }

        var allMatched = expectedDependencies.All(exp =>
        {
            var trimmed = exp.Trim();
            return observed.Any(obs => NamesMatch(obs, trimmed) || TypesMatch(trimmed, obs));
        });
        if (allMatched)
            return Check("dependencies", Matched, expectedLabel, observed,
                "All declared dependencies were observed on the slice.", evidence.ToArray());

        if (inconclusive)
            return Check("dependencies", NotDeterminable, expectedLabel, observed,
                "One or more expected dependencies were not observed." + AbsenceNote(NotDeterminable),
                evidence.ToArray());

        return observed.Count > 0
            ? Check("dependencies", Mismatch, expectedLabel, observed,
                "The slice declares different dependencies than expected.", evidence.ToArray())
            : Check("dependencies", Missing, expectedLabel, observed,
                "No matching dependencies were observed on the slice.", evidence.ToArray());
    }

    private static SliceDesignCheck CheckUniversalForbiddenDependencies(
        AnalysisResult result,
        UniversalTarget? target,
        IReadOnlyList<string> forbiddenDependencies)
    {
        var expectedLabel = forbiddenDependencies.Count == 0
            ? "(none)"
            : string.Join(", ", forbiddenDependencies.Select(f => f.Trim()));

        if (target is null)
            return Check("forbiddenDependencies", NotDeterminable, expectedLabel, [],
                "Resolve the target before checking forbidden dependencies.");

        var candidates = new List<(string Name, string Display, SourceLocation? Location)>();
        var locations = new List<SourceLocation?> { target.Location };

        if (target.Entrypoint is { } ep)
        {
            var callRoots = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in ep.CalledServices)
            {
                foreach (var token in ExpandServicePair(raw))
                {
                    candidates.Add((token, raw, ep.Location));
                    callRoots.Add(token);
                    foreach (var sr in MatchingServiceResolutions(result, token))
                    {
                        if (sr.Implementation is not null)
                        {
                            candidates.Add((sr.Implementation, $"{token} -> {sr.Implementation}", ep.Location));
                            callRoots.Add(sr.Implementation);
                        }
                    }
                }
            }

            foreach (var db in ep.TouchedDbContexts)
                candidates.Add((db, db, ep.Location));

            foreach (var edge in result.ServiceCallEdges.Where(e => callRoots.Contains(e.From)))
            {
                candidates.Add((edge.Via, $"{edge.Via}.{edge.Method}", edge.Location ?? ep.Location));
                candidates.Add((edge.To, $"{edge.To}.{edge.Method}", edge.Location ?? ep.Location));
            }
        }
        else
        {
            foreach (var edge in result.ServiceCallEdges.Where(e =>
                string.Equals(e.From, target.FullName, StringComparison.Ordinal)))
            {
                candidates.Add((edge.Via, $"{edge.Via}.{edge.Method}", edge.Location ?? target.Location));
                candidates.Add((edge.To, $"{edge.To}.{edge.Method}", edge.Location ?? target.Location));
            }

            foreach (var edge in result.ResolvedEdges.Where(e =>
                string.Equals(e.From, target.FullName, StringComparison.Ordinal)))
            {
                candidates.Add((edge.Via, edge.Via, edge.Location ?? target.Location));
                candidates.Add((edge.To, edge.To, edge.Location ?? target.Location));
            }
        }

        var violations = new List<(string Display, SourceLocation? Location)>();
        foreach (var candidate in candidates)
        {
            if (forbiddenDependencies.Any(forbidden => MatchesForbidden(candidate.Name, forbidden.Trim())))
                violations.Add((candidate.Display, candidate.Location));
        }

        if (violations.Count > 0)
        {
            return Check("forbiddenDependencies", Mismatch, expectedLabel,
                violations.Select(v => v.Display),
                "The slice references one or more forbidden dependencies.",
                violations.Select(v => v.Location).ToArray());
        }

        var observedAll = candidates.Select(c => c.Display).ToArray();
        if (!CanConfirmAbsence(result))
        {
            return Check("forbiddenDependencies", NotDeterminable, expectedLabel, observedAll,
                "The scan cannot conclusively confirm the absence of forbidden dependencies." + AbsenceNote(NotDeterminable),
                locations.ToArray());
        }

        return Check("forbiddenDependencies", Matched, expectedLabel, observedAll,
            "No forbidden dependencies were observed on the slice.", locations.ToArray());
    }

    private static IEnumerable<string> ExpandServicePair(string raw)
    {
        var arrow = raw.IndexOf(" -> ", StringComparison.Ordinal);
        if (arrow < 0)
        {
            yield return raw.Trim();
            yield break;
        }

        var left = raw[..arrow].Trim();
        var right = raw[(arrow + 4)..].Trim();
        if (left.Length > 0) yield return left;
        if (right.Length > 0) yield return right;
    }

    private static List<SystemEntrypointInfo> DeduplicateEntrypoints(IReadOnlyList<SystemEntrypointInfo> entrypoints)
    {
        var seen = new HashSet<(string Project, SystemEntrypointKind Kind, string Method, string Route, string Name)>();
        var list = new List<SystemEntrypointInfo>(entrypoints.Count);
        foreach (var ep in entrypoints)
        {
            var key = (ep.ProjectName ?? string.Empty, ep.Kind, ep.Method, ep.RouteOrTrigger, ep.Name);
            if (seen.Add(key))
                list.Add(ep);
        }
        return list;
    }

    private static bool EntrypointMatches(SystemEntrypointInfo e, string target)
        => string.Equals(e.Name, target, StringComparison.Ordinal) ||
           string.Equals(e.RouteOrTrigger, target, StringComparison.Ordinal) ||
           string.Equals(e.DisplayLabel, target, StringComparison.Ordinal) ||
           string.Equals($"{e.Name} ({e.RouteOrTrigger})", target, StringComparison.Ordinal);

    private static string MapEntrypointKind(SystemEntrypointKind kind) => kind switch
    {
        SystemEntrypointKind.Grpc => "Grpc",
        SystemEntrypointKind.Worker => "Worker",
        SystemEntrypointKind.MessageConsumer => "Consumer",
        _ => "Endpoint",
    };

    private static ProjectInfo? ResolveEntrypointOwner(AnalysisResult result, SystemEntrypointInfo ep)
    {
        if (ep.Location is { } location && !string.IsNullOrEmpty(result.RootPath))
        {
            var sourcePath = Path.Combine(result.RootPath, location.File.Replace('/', Path.DirectorySeparatorChar));
            var owner = ProjectLocator.FindOwningProject(result.Projects, sourcePath);
            if (owner is not null)
            {
                var owners = result.Projects.Where(project => string.Equals(
                    Path.TrimEndingDirectorySeparator(project.DirectoryPath),
                    Path.TrimEndingDirectorySeparator(owner.DirectoryPath),
                    StringComparison.OrdinalIgnoreCase));
                return owners.Skip(1).Any() ? null : owner;
            }
        }

        if (!string.IsNullOrWhiteSpace(ep.ProjectName))
        {
            var named = result.Projects
                .Where(project => project.Name.Equals(ep.ProjectName, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            return named.Length == 1 ? named[0] : null;
        }

        return null;
    }

    private sealed record ServiceCandidateNode(string FullName, ProjectInfo? Owner, SourceLocation? Location);

    private static List<ServiceCandidateNode> CollectServiceTargets(AnalysisResult result)
    {
        var dbContexts = result.DbContexts.Select(c => c.FullName).ToHashSet(StringComparer.Ordinal);
        var testProjects = new HashSet<string>(
            result.Projects.Where(p => string.Equals(p.Layer, "Tests", StringComparison.OrdinalIgnoreCase)).Select(p => p.Name),
            StringComparer.OrdinalIgnoreCase);
        foreach (var tp in TestProjectDiscovery.DiscoverTestProjects(result))
            testProjects.Add(tp.Name);

        var map = new Dictionary<string, ServiceCandidateNode>(StringComparer.Ordinal);

        void TryAdd(string? fullName, string? projectName, SourceLocation? location)
        {
            if (string.IsNullOrWhiteSpace(fullName) || dbContexts.Contains(fullName))
                return;

            ProjectInfo? owner = null;
            if (location is not null && !string.IsNullOrEmpty(result.RootPath))
            {
                var fullPath = Path.Combine(result.RootPath, location.File.Replace('/', Path.DirectorySeparatorChar));
                owner = ProjectLocator.FindOwningProject(result.Projects, fullPath);
            }
            if (owner is null && !string.IsNullOrWhiteSpace(projectName))
            {
                var named = result.Projects
                    .Where(p => p.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (named.Length == 1)
                    owner = named[0];
            }

            if (owner is not null && testProjects.Contains(owner.Name))
                return;
            if (!string.IsNullOrWhiteSpace(projectName) && testProjects.Contains(projectName))
                return;

            if (!map.TryGetValue(fullName, out var existing))
            {
                map[fullName] = new ServiceCandidateNode(fullName, owner, location);
            }
            else if ((existing.Owner is null && owner is not null) || (existing.Location is null && location is not null))
            {
                map[fullName] = existing with
                {
                    Owner = existing.Owner ?? owner,
                    Location = existing.Location ?? location,
                };
            }
        }

        foreach (var sr in result.ServiceResolutions)
        {
            if (sr.IsAmbiguous || string.IsNullOrWhiteSpace(sr.Implementation))
                continue;
            var candidate = sr.Candidates.FirstOrDefault(c =>
                c.DeclaredInScan && string.Equals(c.Type, sr.Implementation, StringComparison.Ordinal));
            if (sr.Candidates.Count > 0 && candidate is null)
                continue;
            TryAdd(sr.Implementation, null, candidate?.Location);
        }

        foreach (var edge in result.ServiceCallEdges)
        {
            TryAdd(edge.From, edge.ProjectName, edge.Location);
            TryAdd(edge.To, null, null);
        }

        foreach (var edge in result.ResolvedEdges)
            TryAdd(edge.From, edge.ProjectName, edge.Location);

        return map.Values.ToList();
    }
}
