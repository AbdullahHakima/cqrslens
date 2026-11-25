namespace CqrsLens;

/// <summary>Turns user-supplied behavior into a test-first plan grounded in the scanned slice.</summary>
internal static class AgentSliceTestPlanGenerator
{
    internal const int MaxScenarios = 20;

    internal static SliceTestPlan Generate(
        AnalysisResult result, string requestName, IReadOnlyList<SliceTestScenario> scenarios)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestName);
        ArgumentNullException.ThrowIfNull(scenarios);
        if (scenarios.Count is < 1 or > MaxScenarios)
            throw new ArgumentException($"Provide between 1 and {MaxScenarios} behavior scenarios.", nameof(scenarios));

        foreach (var scenario in scenarios)
        {
            if (scenario is null || string.IsNullOrWhiteSpace(scenario.Name) ||
                string.IsNullOrWhiteSpace(scenario.Given) || string.IsNullOrWhiteSpace(scenario.When) ||
                string.IsNullOrWhiteSpace(scenario.Then))
            {
                throw new ArgumentException("Each scenario requires non-empty name, given, when, and then values.",
                    nameof(scenarios));
            }
        }

        var name = requestName.Trim();
        var matches = result.Requests
            .Where(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                        r.FullName.Equals(name, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.FullName, StringComparer.Ordinal)
            .ThenBy(r => r.ProjectName, StringComparer.Ordinal)
            .ThenBy(r => r.Location?.File, StringComparer.Ordinal)
            .ThenBy(r => r.Location?.Line)
            .ToArray();
        var candidates = matches.Select(r => new SliceTestTarget(r.FullName, r.ProjectName, Location(r.Location)))
            .ToArray();
        var plan = new SliceTestPlan(name, "proposed", scenarios.ToArray(), candidates, [], [], [], []);

        if (matches.Length > 1)
        {
            return plan with
            {
                Status = "ambiguous",
                OpenQuestions = ["Which request owns the behavior? Use a full type name or scan the owning project when full names are duplicated."],
            };
        }

        if (matches.Length == 1)
            return PlanExisting(result, matches[0], plan);

        if (TryPlanEntrypoint(result, name, plan, out var entrypointPlan))
            return entrypointPlan;

        if (TryPlanService(result, name, plan, out var servicePlan))
            return servicePlan;

        if (!result.CanRunStrictChecks)
        {
            var codes = string.Join(", ", result.Diagnostics.Select(d => d.Code).Distinct(StringComparer.Ordinal));
            return plan with
            {
                Status = "unresolved",
                OpenQuestions = [$"Can the owning source be scanned successfully? Review these diagnostics before deciding whether the request is new: {codes}."],
            };
        }

        var proposedTestProjects = TestProjectDiscovery.DiscoverTestProjects(result);
        var proposedBehaviors = GetActiveBehaviors(result);
        var proposedMappings = MapScenarios(scenarios, new HashSet<string>(StringComparer.Ordinal) { "request-behavior" });

        return plan with
        {
            SuggestedTests = [new("request-behavior", "unit", name,
                "Choose the smallest public behavior boundary and turn each agreed scenario into a test for its observable outcome.", [])],
            OpenQuestions = ["The request was not found in the scanned model. Which project owns it, and what public behavior contract should its tests exercise?"],
            Steps = TestSteps(),
            ActiveBehaviors = proposedBehaviors,
            TestProjects = proposedTestProjects,
            ScenarioMappings = proposedMappings,
        };
    }

    private static SliceTestPlan PlanExisting(AnalysisResult result, CqrsRequest request, SliceTestPlan plan)
    {
        var suggestions = new List<SliceTestSuggestion>();
        var questions = new List<string>();
        var handlers = ResolveLinked(result.Handlers, request.Handlers, h => h.FullName);
        var requestEvidence = Evidence("request", request.FullName, request.ProjectName, request.Location);
        HandlerInfo? uniqueHandler = null;
        if (request.Handlers.Count == 1 && handlers.Length == 1)
        {
            uniqueHandler = handlers[0];
            suggestions.Add(new("request-behavior", "unit", uniqueHandler.FullName,
                "Exercise the linked handler and assert the scenario's observable result or side effect. Substitute collaborators outside the unit boundary.",
                [requestEvidence, Evidence("handler", uniqueHandler.FullName, uniqueHandler.ProjectName, uniqueHandler.Location)]));
        }
        else
        {
            questions.Add("Which handler owns the behavior? The scan does not identify one unambiguous handler for this request.");
            suggestions.Add(new("request-behavior", "unit", request.FullName,
                "Choose the smallest owning behavior boundary before implementing the scenarios.", [requestEvidence]));
        }

        var validators = ResolveLinked(result.Validators, request.Validators, v => v.FullName);
        var validatorEvidence = validators.Select(v => Evidence("validator", v.FullName, v.ProjectName, v.Location));
        AddSuggestion(suggestions, request, "validation", "unit",
            "Test the linked validator for any stated input rules. Derive invalid examples and expected errors from the agreed behavior and validator source.",
            validatorEvidence);

        var endpoints = result.Endpoints.Where(e =>
            string.Equals(e.TargetRequestFullName, request.FullName, StringComparison.Ordinal)).ToArray();
        AddSuggestion(suggestions, request, "http", "integration",
            "Use a test host for scenarios that depend on HTTP binding, status, authorization, or middleware. Confirm the contract in endpoint source before writing assertions.",
            endpoints.Select(e => Evidence(e.IsInferred ? "inferred-endpoint" : "endpoint",
                e.IsInferred ? e.Name : e.DisplayLabel, e.ProjectName, e.Location)));

        AddPersistenceSuggestion(result, request, handlers, suggestions);

        var published = request.PublishedNotifications.Concat(handlers.SelectMany(h => h.PublishedNotifications));
        var notifications = ResolveLinked(result.Notifications, published, n => n.FullName);
        AddSuggestion(suggestions, request, "notification", "integration",
            "Test publication and consumer behavior when the scenario depends on notification delivery. Confirm the event contract and registration in source.",
            notifications.Select(n => Evidence("notification", n.FullName, n.ProjectName, n.Location)));

        var testDispatches = result.DispatchCallSites
            .Where(s => s.IsTestProject && s.TargetKind == DispatchTargetKind.Request &&
                        string.Equals(s.TargetFullName, request.FullName, StringComparison.Ordinal))
            .Select(s => Evidence("test-dispatch", s.SenderFullName ?? s.TargetFullName, s.ProjectName, s.Location))
            .ToArray();

        if (!result.CanRunStrictChecks)
            questions.Add("Could scan diagnostics affect this slice? Inspect the source before relying on missing links.");

        var testProjects = TestProjectDiscovery.DiscoverTestProjects(result);
        AddTestProjectCoverageQuestion(questions, testProjects, request.ProjectName);

        var payload = request.Properties
            .Select(p => new SliceTestPayloadField(p.Name, p.Type))
            .ToArray();
        var collaborators = uniqueHandler is not null
            ? BuildCollaborators(result, request, uniqueHandler, testProjects)
            : Array.Empty<SliceTestCollaborator>();
        var activeBehaviors = GetActiveBehaviors(result);
        var discoveredTests = TestProjectDiscovery.FindTestReferencesForSlice(result, request);
        var availableBoundaries = suggestions.Select(s => s.Boundary).ToHashSet(StringComparer.Ordinal);
        var scenarioMappings = MapScenarios(plan.Scenarios, availableBoundaries);

        return plan with
        {
            Status = "existing",
            SuggestedTests = suggestions,
            ExistingTestDispatches = testDispatches,
            OpenQuestions = questions,
            Steps = TestSteps(),
            Payload = payload,
            Collaborators = collaborators,
            ActiveBehaviors = activeBehaviors,
            TestProjects = testProjects,
            DiscoveredTests = discoveredTests,
            ScenarioMappings = scenarioMappings,
        };
    }

    private static bool TryPlanEntrypoint(
        AnalysisResult result,
        string name,
        SliceTestPlan plan,
        out SliceTestPlan resolvedPlan)
    {
        if (result.SystemEntrypoints.Count == 0)
        {
            resolvedPlan = plan;
            return false;
        }

        var matched = result.SystemEntrypoints
            .Where(ep => ep.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                         ep.DisplayLabel.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                         ep.RouteOrTrigger.Equals(name, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matched.Length == 0)
        {
            var suffix = "." + name;
            matched = result.SystemEntrypoints
                .Where(ep => ep.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        if (matched.Length == 0)
        {
            var prefix = name + ".";
            var mid = "." + name + ".";
            matched = result.SystemEntrypoints
                .Where(ep => ep.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                             ep.Name.Contains(mid, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        if (matched.Length == 0)
        {
            resolvedPlan = plan;
            return false;
        }

        matched = matched
            .OrderBy(ep => ep.Name, StringComparer.Ordinal)
            .ThenBy(ep => ep.DisplayLabel, StringComparer.Ordinal)
            .ThenBy(ep => ep.ProjectName, StringComparer.Ordinal)
            .ThenBy(ep => ep.Location?.File, StringComparer.Ordinal)
            .ThenBy(ep => ep.Location?.Line)
            .ToArray();

        if (matched.Length > 1)
        {
            var candidates = matched
                .Select(ep => new SliceTestTarget(
                    !string.IsNullOrWhiteSpace(ep.DisplayLabel) &&
                    !string.Equals(ep.DisplayLabel, ep.Name, StringComparison.OrdinalIgnoreCase)
                        ? $"{ep.Name} ({ep.DisplayLabel})"
                        : ep.Name,
                    ep.ProjectName,
                    Location(ep.Location)))
                .ToArray();
            resolvedPlan = plan with
            {
                Status = "ambiguous",
                Candidates = candidates,
                OpenQuestions = ["Which operation owns the behavior? Use a full operation name (such as Class.Method) or exact route/trigger when multiple operations match."],
            };
            return true;
        }

        resolvedPlan = PlanEntrypoint(result, matched[0], plan);
        return true;
    }

    private static SliceTestPlan PlanEntrypoint(AnalysisResult result, SystemEntrypointInfo ep, SliceTestPlan plan)
    {
        var suggestions = new List<SliceTestSuggestion>();
        var questions = new List<string>();
        var epEvidence = Evidence("entrypoint", ep.DisplayLabel, ep.ProjectName, ep.Location);

        var serviceEvidence = ep.CalledServices
            .Distinct(StringComparer.Ordinal)
            .Select(s => Evidence("service", s, ep.ProjectName, ep.Location))
            .ToList();
        var primaryTarget = ep.CalledServices.Count == 1 ? ep.CalledServices[0] : ep.Name;
        var unitEvidence = new List<SliceTestEvidence> { epEvidence };
        unitEvidence.AddRange(serviceEvidence);
        AddTargetSuggestion(suggestions, primaryTarget, "request-behavior", "unit",
            "Exercise the operation or its primary service with test doubles for external collaborators and assert the observable result or side effect.",
            unitEvidence);

        if (ep.Kind is SystemEntrypointKind.Http or SystemEntrypointKind.Blazor or SystemEntrypointKind.RazorPage)
        {
            AddTargetSuggestion(suggestions, ep.Name, "http", "integration",
                "Use a test host for scenarios that depend on HTTP routing, binding, status codes, or authorization. Confirm the contract in endpoint source before writing assertions.",
                [epEvidence]);
        }
        else if (ep.Kind == SystemEntrypointKind.Grpc)
        {
            AddTargetSuggestion(suggestions, ep.Name, "grpc", "integration",
                "Exercise the gRPC service method via a test host or ServerCallContext double to verify request metadata, status codes, and payload mapping.",
                [epEvidence]);
        }
        else
        {
            AddTargetSuggestion(suggestions, ep.Name, "host", "integration",
                "Exercise the background worker or message consumer in a test host with stubbed external dependencies to verify execution, retries, and side effects.",
                [epEvidence]);
        }

        var persistenceEvidence = ep.TouchedDbContexts
            .Distinct(StringComparer.Ordinal)
            .Select(ctx => Evidence("db-context", ctx, ep.ProjectName, ep.Location))
            .Concat(ep.TouchedEntities
                .Distinct(StringComparer.Ordinal)
                .Select(entity => Evidence("db-set-touch", entity, ep.ProjectName, ep.Location)));
        AddTargetSuggestion(suggestions, ep.Name, "persistence", "integration",
            "When the scenario depends on stored or retrieved state, test against the configured provider and assert the agreed state or result at the persistence boundary.",
            persistenceEvidence);

        var publishEvidence = ep.PublishedMessages
            .Distinct(StringComparer.Ordinal)
            .Select(msg => Evidence("published-message", msg, ep.ProjectName, ep.Location));
        AddTargetSuggestion(suggestions, ep.Name, "notification", "integration",
            "Test message publication and downstream consumer contracts when the scenario depends on event delivery.",
            publishEvidence);

        if (!result.CanRunStrictChecks)
            questions.Add("Could scan diagnostics affect this operation? Inspect the source before relying on missing links.");

        var testProjects = TestProjectDiscovery.DiscoverTestProjects(result);
        AddTestProjectCoverageQuestion(questions, testProjects, ep.ProjectName);

        var deps = ep.CalledServices
            .Concat(ep.TouchedDbContexts)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var ownerNames = new List<string> { ep.Name };
        var dot = ep.Name.LastIndexOf('.');
        if (dot > 0)
            ownerNames.Add(ep.Name[..dot]);

        var collaborators = BuildCollaboratorsCore(
            result,
            deps,
            ownerNames,
            dot > 0 ? ep.Name[..dot] : null,
            [],
            ep.ProjectName,
            testProjects);

        var testTargets = new List<string> { ep.Name };
        if (dot > 0)
            testTargets.Add(ep.Name[..dot]);
        testTargets.AddRange(ep.CalledServices);

        var discoveredTests = FindTargetTests(result, testTargets, ep.ProjectName);
        var availableBoundaries = suggestions.Select(s => s.Boundary).ToHashSet(StringComparer.Ordinal);

        return plan with
        {
            Status = "existing",
            SuggestedTests = suggestions,
            OpenQuestions = questions,
            Steps = TestSteps(),
            Collaborators = collaborators,
            ActiveBehaviors = GetActiveBehaviors(result),
            TestProjects = testProjects,
            DiscoveredTests = discoveredTests,
            ScenarioMappings = MapScenarios(plan.Scenarios, availableBoundaries),
        };
    }

    private static bool TryPlanService(
        AnalysisResult result,
        string name,
        SliceTestPlan plan,
        out SliceTestPlan resolvedPlan)
    {
        if (result.ServiceResolutions.Count == 0)
        {
            resolvedPlan = plan;
            return false;
        }

        var implMatches = result.ServiceResolutions
            .Where(r => !r.IsAmbiguous && !string.IsNullOrWhiteSpace(r.Implementation))
            .Where(r => r.Implementation!.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                        CSharpScanner.SimpleName(r.Implementation!).Equals(name, StringComparison.OrdinalIgnoreCase))
            .GroupBy(r => r.Implementation!, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToArray();

        if (implMatches.Length == 0)
        {
            implMatches = result.ServiceResolutions
                .Where(r => !r.IsAmbiguous && !string.IsNullOrWhiteSpace(r.Implementation))
                .Where(r => r.Interface.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                            r.InterfaceName.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                            CSharpScanner.SimpleName(r.InterfaceName).Equals(name, StringComparison.OrdinalIgnoreCase))
                .GroupBy(r => r.Implementation!, StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .ToArray();
        }

        if (implMatches.Length == 0)
        {
            resolvedPlan = plan;
            return false;
        }

        if (implMatches.Length > 1)
        {
            var candidates = implMatches
                .Select(g =>
                {
                    var loc = g.SelectMany(r => r.Candidates)
                        .FirstOrDefault(c => string.Equals(c.Type, g.Key, StringComparison.Ordinal))?.Location;
                    var proj = ResolveServiceProjectName(result, g.Key);
                    return new SliceTestTarget(g.Key, proj, Location(loc));
                })
                .ToArray();
            resolvedPlan = plan with
            {
                Status = "ambiguous",
                Candidates = candidates,
                OpenQuestions = ["Multiple concrete services matched. Use the full service type name to target one implementation."],
            };
            return true;
        }

        resolvedPlan = PlanService(result, implMatches[0].Key, implMatches[0].ToArray(), plan);
        return true;
    }

    private static SliceTestPlan PlanService(
        AnalysisResult result,
        string implFullName,
        IReadOnlyList<ServiceResolution> matchedResolutions,
        SliceTestPlan plan)
    {
        var suggestions = new List<SliceTestSuggestion>();
        var questions = new List<string>();
        var implLocation = matchedResolutions
            .SelectMany(r => r.Candidates)
            .FirstOrDefault(c => string.Equals(c.Type, implFullName, StringComparison.Ordinal))?.Location;
        var projectName = ResolveServiceProjectName(result, implFullName);

        var outgoingCalls = result.ServiceCallEdges
            .Where(e => string.Equals(e.From, implFullName, StringComparison.Ordinal))
            .ToArray();
        var unitEvidence = new List<SliceTestEvidence>
        {
            Evidence("service", implFullName, projectName, implLocation),
        };
        foreach (var edge in outgoingCalls)
            unitEvidence.Add(Evidence("service-call", $"{edge.To}.{edge.Method}", edge.ProjectName ?? projectName, edge.Location));

        AddTargetSuggestion(suggestions, implFullName, "request-behavior", "unit",
            "Exercise the concrete service directly with substituted collaborators and assert its return values and side effects.",
            unitEvidence);

        var interfaceNames = matchedResolutions
            .SelectMany(r => new[] { r.InterfaceName, r.Interface })
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToHashSet(StringComparer.Ordinal);
        interfaceNames.Add(implFullName);

        var callingEntrypoints = result.SystemEntrypoints
            .Where(ep => ep.CalledServices.Any(s =>
                interfaceNames.Contains(s) ||
                string.Equals(CSharpScanner.SimpleName(s), CSharpScanner.SimpleName(implFullName), StringComparison.Ordinal)))
            .ToArray();

        var httpEvidence = callingEntrypoints
            .Where(ep => ep.Kind is SystemEntrypointKind.Http or SystemEntrypointKind.Blazor or SystemEntrypointKind.RazorPage)
            .Select(ep => Evidence("entrypoint", ep.DisplayLabel, ep.ProjectName, ep.Location));
        AddTargetSuggestion(suggestions, implFullName, "http", "integration",
            "Use a test host for scenarios entering through an HTTP endpoint calling this service.",
            httpEvidence);

        var persistenceEvidence = result.FlowEdges
            .Where(e => e.Kind == FlowEdgeKind.HandlerToDbContext && string.Equals(e.From, implFullName, StringComparison.Ordinal))
            .Select(e => Evidence("db-context", e.To, e.ProjectName, e.Location))
            .Concat(result.ResolvedEdges
                .Where(e => e.Kind == FlowEdgeKind.HandlerToDbContextResolved && string.Equals(e.From, implFullName, StringComparison.Ordinal))
                .Select(e => Evidence("di-resolved-db-context", e.To, e.ProjectName, e.Location)));
        AddTargetSuggestion(suggestions, implFullName, "persistence", "integration",
            "When the scenario depends on stored or retrieved state, test against the configured provider and assert the agreed state or result at the persistence boundary.",
            persistenceEvidence);

        if (!result.CanRunStrictChecks)
            questions.Add("Could scan diagnostics affect this service? Inspect the source before relying on missing links.");

        var testProjects = TestProjectDiscovery.DiscoverTestProjects(result);
        AddTestProjectCoverageQuestion(questions, testProjects, projectName);

        var deps = outgoingCalls.Select(e => e.Via)
            .Concat(result.FlowEdges
                .Where(e => e.Kind == FlowEdgeKind.HandlerToDbContext && string.Equals(e.From, implFullName, StringComparison.Ordinal))
                .Select(e => e.To))
            .Concat(result.ResolvedEdges
                .Where(e => e.Kind == FlowEdgeKind.HandlerToDbContextResolved && string.Equals(e.From, implFullName, StringComparison.Ordinal))
                .Select(e => e.Via))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var dot = implFullName.LastIndexOf('.');
        var collaborators = BuildCollaboratorsCore(
            result,
            deps,
            [implFullName],
            dot > 0 ? implFullName[..dot] : null,
            [],
            projectName,
            testProjects);

        var discoveredTests = FindTargetTests(result, interfaceNames, projectName);
        var availableBoundaries = suggestions.Select(s => s.Boundary).ToHashSet(StringComparer.Ordinal);

        return plan with
        {
            Status = "existing",
            SuggestedTests = suggestions,
            OpenQuestions = questions,
            Steps = TestSteps(),
            Collaborators = collaborators,
            ActiveBehaviors = GetActiveBehaviors(result),
            TestProjects = testProjects,
            DiscoveredTests = discoveredTests,
            ScenarioMappings = MapScenarios(plan.Scenarios, availableBoundaries),
        };
    }

    private static string? ResolveServiceProjectName(AnalysisResult result, string implFullName)
        => result.ServiceCallEdges.FirstOrDefault(e => string.Equals(e.From, implFullName, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(e.ProjectName))?.ProjectName
           ?? result.ServiceCallEdges.FirstOrDefault(e => string.Equals(e.To, implFullName, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(e.ProjectName))?.ProjectName
           ?? result.SystemEntrypoints.FirstOrDefault(ep => ep.CalledServices.Contains(implFullName, StringComparer.Ordinal) && !string.IsNullOrWhiteSpace(ep.ProjectName))?.ProjectName;

    private static IReadOnlyList<TestMethodReference> FindTargetTests(
        AnalysisResult result,
        IEnumerable<string> targetNames,
        string? projectName)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var discovered = new List<TestMethodReference>();
        foreach (var candidate in targetNames)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            foreach (var testRef in TestProjectDiscovery.FindTestReferencesForTarget(result, candidate, projectName))
            {
                var key = $"{testRef.ProjectName}|{testRef.File}|{testRef.Line}|{testRef.MethodName}|{testRef.TargetFullName}|{testRef.ReferenceKind}";
                if (seen.Add(key))
                    discovered.Add(testRef);
            }
        }

        discovered.Sort(static (a, b) =>
        {
            var c = StringComparer.Ordinal.Compare(a.ProjectName, b.ProjectName);
            if (c != 0) return c;
            c = StringComparer.Ordinal.Compare(a.File, b.File);
            if (c != 0) return c;
            c = a.Line.CompareTo(b.Line);
            if (c != 0) return c;
            return StringComparer.Ordinal.Compare(a.MethodName, b.MethodName);
        });
        return discovered;
    }

    private static void AddTestProjectCoverageQuestion(
        List<string> questions,
        IReadOnlyList<TestProjectInfo> testProjects,
        string? projectName)
    {
        if (string.IsNullOrWhiteSpace(projectName) ||
            testProjects.Any(tp => tp.ReferencedProductionProjects.Contains(projectName, StringComparer.OrdinalIgnoreCase)))
        {
            return;
        }

        var suggestion = projectName.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)
            ? $"No test project currently references `{projectName}`. Add or reference a test project covering `{projectName}`."
            : $"No test project currently references `{projectName}`. Consider creating or referencing `{projectName}.Tests`.";
        questions.Add(suggestion);
    }

    private static IReadOnlyList<string> GetActiveBehaviors(AnalysisResult result)
        => BehaviorOrdering.Registered(result.Behaviors)
            .SelectMany(g => g.Behaviors)
            .Select(b => b.Name)
            .ToArray();

    private static IReadOnlyList<SliceTestCollaborator> BuildCollaborators(
        AnalysisResult result,
        CqrsRequest request,
        HandlerInfo handler,
        IReadOnlyList<TestProjectInfo> testProjects)
        => BuildCollaboratorsCore(
            result,
            handler.Dependencies,
            [handler.FullName],
            handler.Namespace,
            handler.ImportedNamespaces,
            request.ProjectName,
            testProjects);

    private static IReadOnlyList<SliceTestCollaborator> BuildCollaboratorsCore(
        AnalysisResult result,
        IReadOnlyList<string> dependencies,
        IReadOnlyList<string> ownerFullNames,
        string? ownerNamespace,
        IReadOnlyList<string> importedNamespaces,
        string? projectName,
        IReadOnlyList<TestProjectInfo> testProjects)
    {
        if (dependencies.Count == 0)
            return Array.Empty<SliceTestCollaborator>();

        var owners = ownerFullNames.ToHashSet(StringComparer.Ordinal);
        var mockingLibraries = SelectMockingLibraries(testProjects, projectName);
        var dbContextFullNames = result.DbContexts
            .Select(c => c.FullName)
            .ToHashSet(StringComparer.Ordinal);
        var dbContextSimpleNames = result.DbContexts
            .Select(c => c.Name)
            .ToHashSet(StringComparer.Ordinal);
        var resolvedDbContextInterfaces = result.ResolvedEdges
            .Where(e => e.Kind == FlowEdgeKind.HandlerToDbContextResolved &&
                        owners.Contains(e.From))
            .Select(e => e.Via)
            .ToHashSet(StringComparer.Ordinal);
        var knownImplementations = result.ServiceResolutions
            .Where(r => !string.IsNullOrWhiteSpace(r.Implementation))
            .Select(r => r.Implementation!)
            .ToHashSet(StringComparer.Ordinal);

        var collaborators = new List<SliceTestCollaborator>(dependencies.Count);
        foreach (var declaredType in dependencies)
        {
            var (depName, depArgs) = CSharpScanner.ParseTypeReference(declaredType);
            var depSimple = CSharpScanner.SimpleName(depName);

            var resolution = ResolveServiceCore(
                result.ServiceResolutions, ownerNamespace, importedNamespaces, declaredType, depName, depSimple, depArgs);
            string? resolvedType;
            string resolutionSource;

            if (resolution is not null)
            {
                resolvedType = resolution.Implementation;
                resolutionSource = resolution.IsAmbiguous
                    ? "ambiguous"
                    : resolution.Source switch
                    {
                        ServiceResolutionSource.Declaration => "declaration",
                        ServiceResolutionSource.Registration => "registration",
                        ServiceResolutionSource.Both => "both",
                        _ => "none",
                    };
            }
            else if (TryResolveConcreteDbContext(result.DbContexts, depName, depSimple, out var dbContext))
            {
                resolvedType = dbContext!.FullName;
                resolutionSource = "concrete";
            }
            else if (ownerFullNames.Count > 0 &&
                     TryResolveConcreteServiceEdge(result.ServiceCallEdges, ownerFullNames[0], depName, depSimple, out var concreteTarget))
            {
                resolvedType = concreteTarget;
                resolutionSource = "concrete";
            }
            else if (!IsLikelyInterfaceName(depSimple) && knownImplementations.Contains(depName))
            {
                resolvedType = depName;
                resolutionSource = "concrete";
            }
            else
            {
                resolvedType = null;
                resolutionSource = "unresolved";
            }

            var calledMethods = result.ServiceCallEdges
                .Where(e => owners.Contains(e.From) &&
                            (string.Equals(e.Via, depName, StringComparison.Ordinal) ||
                             string.Equals(CSharpScanner.SimpleName(e.Via), depSimple, StringComparison.Ordinal) ||
                             (resolution is not null &&
                              (string.Equals(e.Via, resolution.InterfaceName, StringComparison.Ordinal) ||
                               string.Equals(e.Via, resolution.Interface, StringComparison.Ordinal))) ||
                             (resolvedType is not null && string.Equals(e.To, resolvedType, StringComparison.Ordinal))))
                .Select(e => e.Method)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(m => m, StringComparer.Ordinal)
                .ToArray();

            var isDbContext =
                dbContextFullNames.Contains(depName) ||
                dbContextSimpleNames.Contains(depSimple) ||
                (resolvedType is not null && dbContextFullNames.Contains(resolvedType)) ||
                (resolution is not null && resolvedDbContextInterfaces.Contains(resolution.InterfaceName)) ||
                resolvedDbContextInterfaces.Contains(depName) ||
                CSharpSyntax.IsDbContextBase(depSimple);

            string recommendedDouble;
            if (isDbContext)
            {
                recommendedDouble = "real-or-test-container";
            }
            else if (resolution is not null || IsLikelyInterfaceName(depSimple))
            {
                recommendedDouble = mockingLibraries.Count > 0
                    ? $"fake-or-stub ({string.Join(", ", mockingLibraries)})"
                    : "fake-or-stub";
            }
            else
            {
                recommendedDouble = "real";
            }

            collaborators.Add(new SliceTestCollaborator(
                declaredType,
                resolvedType,
                resolutionSource,
                calledMethods,
                recommendedDouble));
        }

        return collaborators;
    }

    private static ServiceResolution? ResolveServiceCore(
        IReadOnlyList<ServiceResolution> resolutions,
        string? ownerNamespace,
        IReadOnlyList<string> importedNamespaces,
        string declaredType,
        string depName,
        string depSimple,
        List<string> depArgs)
    {
        if (resolutions.Count == 0 || depName.Length == 0)
            return null;

        var exactMatches = resolutions
            .Where(r => string.Equals(r.Interface, declaredType, StringComparison.Ordinal) ||
                        (string.Equals(r.InterfaceName, depName, StringComparison.Ordinal) &&
                         ArgumentsMatch(r, depArgs)))
            .ToList();
        if (exactMatches.Count == 1)
            return exactMatches[0];

        var simpleMatches = resolutions
            .Where(r => string.Equals(CSharpScanner.SimpleName(r.InterfaceName), depSimple, StringComparison.Ordinal) &&
                        ArgumentsMatch(r, depArgs))
            .ToList();
        if (simpleMatches.Count == 0)
            return null;
        if (simpleMatches.Count == 1)
            return simpleMatches[0];

        // Disambiguate using owner namespace and imported namespaces.
        var scoped = new List<ServiceResolution>();
        if (!string.IsNullOrEmpty(ownerNamespace))
        {
            foreach (var candidate in simpleMatches)
            {
                if (string.Equals(candidate.InterfaceName, $"{ownerNamespace}.{depName}", StringComparison.Ordinal))
                    scoped.Add(candidate);
            }
            if (scoped.Count == 1)
                return scoped[0];
        }

        scoped.Clear();
        foreach (var ns in importedNamespaces.Distinct(StringComparer.Ordinal))
        {
            var full = $"{ns}.{depName}";
            foreach (var candidate in simpleMatches)
            {
                if (string.Equals(candidate.InterfaceName, full, StringComparison.Ordinal) &&
                    !scoped.Contains(candidate))
                {
                    scoped.Add(candidate);
                }
            }
        }

        return scoped.Count == 1 ? scoped[0] : null;
    }

    private static bool ArgumentsMatch(ServiceResolution resolution, List<string> depArgs)
    {
        if (resolution.Arity != depArgs.Count)
            return false;
        if (depArgs.Count == 0 || resolution.IsOpenGeneric)
            return true;
        if (resolution.Arguments.Count != depArgs.Count)
            return false;

        for (var i = 0; i < depArgs.Count; i++)
        {
            var expected = CSharpScanner.SimpleName(CSharpScanner.ParseTypeReference(depArgs[i]).Name);
            var actual = CSharpScanner.SimpleName(CSharpScanner.ParseTypeReference(resolution.Arguments[i]).Name);
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    private static bool TryResolveConcreteDbContext(
        IReadOnlyList<DbContextInfo> contexts,
        string depName,
        string depSimple,
        out DbContextInfo? matched)
    {
        var full = contexts.Where(c => string.Equals(c.FullName, depName, StringComparison.Ordinal)).ToArray();
        if (full.Length == 1)
        {
            matched = full[0];
            return true;
        }

        var simple = contexts.Where(c => string.Equals(c.Name, depSimple, StringComparison.Ordinal)).ToArray();
        if (simple.Length == 1)
        {
            matched = simple[0];
            return true;
        }

        matched = null;
        return false;
    }

    private static bool TryResolveConcreteServiceEdge(
        IReadOnlyList<ServiceCallEdge> edges,
        string handlerFullName,
        string depName,
        string depSimple,
        out string? target)
    {
        var targets = edges
            .Where(e => string.Equals(e.From, handlerFullName, StringComparison.Ordinal) &&
                        string.Equals(e.Via, e.To, StringComparison.Ordinal) &&
                        (string.Equals(e.Via, depName, StringComparison.Ordinal) ||
                         string.Equals(CSharpScanner.SimpleName(e.Via), depSimple, StringComparison.Ordinal)))
            .Select(e => e.To)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (targets.Length == 1)
        {
            target = targets[0];
            return true;
        }

        target = null;
        return false;
    }

    private static List<string> SelectMockingLibraries(
        IReadOnlyList<TestProjectInfo> testProjects,
        string? requestProjectName)
    {
        var scoped = string.IsNullOrWhiteSpace(requestProjectName)
            ? Array.Empty<TestProjectInfo>()
            : testProjects
                .Where(tp => tp.ReferencedProductionProjects.Contains(requestProjectName, StringComparer.OrdinalIgnoreCase))
                .ToArray();

        var source = scoped.Length > 0 ? scoped : testProjects;
        return source
            .SelectMany(tp => tp.MockingLibraries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsLikelyInterfaceName(string simpleName)
        => simpleName.Length > 1 && simpleName[0] == 'I' && char.IsUpper(simpleName[1]);

    private static IReadOnlyList<SliceTestScenarioMapping> MapScenarios(
        IReadOnlyList<SliceTestScenario> scenarios,
        IReadOnlySet<string> availableBoundaries)
    {
        var mappings = new List<SliceTestScenarioMapping>(scenarios.Count);
        foreach (var scenario in scenarios)
        {
            mappings.Add(MapScenario(scenario, availableBoundaries));
        }
        return mappings;
    }

    private static SliceTestScenarioMapping MapScenario(
        SliceTestScenario scenario,
        IReadOnlySet<string> availableBoundaries)
    {
        var text = $"{scenario.Name} {scenario.Given} {scenario.When} {scenario.Then}";
        var hints = new List<(string Boundary, string Level, string Reason)>();

        if (MatchesValidation(text))
        {
            hints.Add(("validation", "unit",
                "Scenario describes validation or input rejection rules and the slice has a linked validator."));
        }
        if (MatchesHttp(text))
        {
            hints.Add(("http", "integration",
                "Scenario exercises HTTP routing, binding, or status codes and the slice exposes an endpoint."));
        }
        if (MatchesNotification(text))
        {
            hints.Add(("notification", "integration",
                "Scenario asserts notification publication or consumer delivery and the slice publishes notifications."));
        }
        if (MatchesPersistence(text))
        {
            hints.Add(("persistence", "integration",
                "Scenario depends on stored or retrieved persistence state and the slice has a persistence boundary."));
        }

        foreach (var hint in hints)
        {
            if (availableBoundaries.Contains(hint.Boundary))
            {
                return new SliceTestScenarioMapping(
                    scenario.Name,
                    hint.Boundary,
                    hint.Level,
                    "heuristic",
                    hint.Reason);
            }
        }

        if (hints.Count > 0)
        {
            return new SliceTestScenarioMapping(
                scenario.Name,
                "request-behavior",
                "unit",
                "heuristic",
                $"Scenario mentions {hints[0].Boundary} terms, but no {hints[0].Boundary} boundary was observed in the static slice; mapping to request-behavior.");
        }

        return new SliceTestScenarioMapping(
            scenario.Name,
            "request-behavior",
            "unit",
            "heuristic",
            "Maps to the core request/handler behavior boundary based on the scenario outcome.");
    }

    private static bool MatchesValidation(string text)
        => ContainsPrefix(text, "validat") ||
           ContainsPrefix(text, "invalid") ||
           ContainsPrefix(text, "reject") ||
           ContainsWholeWord(text, "required") ||
           ContainsWholeWord(text, "empty") ||
           ContainsWholeWord(text, "missing") ||
           ContainsWholeWord(text, "format") ||
           ContainsWholeWord(text, "range") ||
           ContainsWholeWord(text, "validation") ||
           text.Contains("bad input", StringComparison.OrdinalIgnoreCase) ||
           text.Contains("error for", StringComparison.OrdinalIgnoreCase);

    private static bool MatchesHttp(string text)
        => ContainsWholeWord(text, "http") ||
           ContainsWholeWord(text, "https") ||
           ContainsWholeWord(text, "endpoint") ||
           ContainsWholeWord(text, "route") ||
           ContainsWholeWord(text, "controller") ||
           ContainsWholeWord(text, "api") ||
           ContainsWholeWord(text, "unauthorized") ||
           ContainsWholeWord(text, "forbidden") ||
           text.Contains("status code", StringComparison.OrdinalIgnoreCase) ||
           text.Contains("query string", StringComparison.OrdinalIgnoreCase) ||
           ContainsRouteLiteral(text) ||
           ContainsWholeWordOrdinal(text, "GET") ||
           ContainsWholeWordOrdinal(text, "POST") ||
           ContainsWholeWordOrdinal(text, "PUT") ||
           ContainsWholeWordOrdinal(text, "DELETE") ||
           ContainsWholeWordOrdinal(text, "PATCH") ||
           ContainsWholeWord(text, "200") ||
           ContainsWholeWord(text, "201") ||
           ContainsWholeWord(text, "204") ||
           ContainsWholeWord(text, "400") ||
           ContainsWholeWord(text, "401") ||
           ContainsWholeWord(text, "403") ||
           ContainsWholeWord(text, "404") ||
           ContainsWholeWord(text, "409") ||
           ContainsWholeWord(text, "422") ||
           ContainsWholeWord(text, "500");

    private static bool MatchesNotification(string text)
        => ContainsPrefix(text, "notif") ||
           ContainsPrefix(text, "publish") ||
           ContainsPrefix(text, "emit") ||
           ContainsPrefix(text, "subscriber") ||
           ContainsPrefix(text, "consumer") ||
           ContainsWholeWord(text, "broadcast") ||
           ContainsWholeWord(text, "email") ||
           text.Contains("domain event", StringComparison.OrdinalIgnoreCase) ||
           text.Contains("integration event", StringComparison.OrdinalIgnoreCase);

    private static bool MatchesPersistence(string text)
        => ContainsPrefix(text, "persist") ||
           ContainsPrefix(text, "retriev") ||
           ContainsWholeWord(text, "database") ||
           ContainsWholeWord(text, "dbcontext") ||
           ContainsWholeWord(text, "save") ||
           ContainsWholeWord(text, "saved") ||
           ContainsWholeWord(text, "saves") ||
           ContainsWholeWord(text, "saving") ||
           ContainsWholeWord(text, "store") ||
           ContainsWholeWord(text, "stored") ||
           ContainsWholeWord(text, "stores") ||
           ContainsWholeWord(text, "entity") ||
           ContainsWholeWord(text, "entities") ||
           ContainsWholeWord(text, "table") ||
           ContainsWholeWord(text, "repository") ||
           ContainsWholeWord(text, "transaction") ||
           ContainsWholeWord(text, "sql") ||
           ContainsWholeWord(text, "queried") ||
           text.Contains("ef core", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsRouteLiteral(string text)
    {
        for (var i = 0; i + 1 < text.Length; i++)
        {
            if (text[i] == '/' && char.IsLetter(text[i + 1]) &&
                (i == 0 || char.IsWhiteSpace(text[i - 1])))
            {
                return true;
            }
        }
        return false;
    }

    private static bool ContainsPrefix(string text, string prefix)
    {
        var index = 0;
        while (index < text.Length)
        {
            index = text.IndexOf(prefix, index, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return false;

            if (index == 0 || !IsWordChar(text[index - 1]))
                return true;

            index += prefix.Length;
        }
        return false;
    }

    private static bool ContainsWholeWord(string text, string word)
        => ContainsWholeWord(text, word, StringComparison.OrdinalIgnoreCase);

    private static bool ContainsWholeWordOrdinal(string text, string word)
        => ContainsWholeWord(text, word, StringComparison.Ordinal);

    private static bool ContainsWholeWord(string text, string word, StringComparison comparison)
    {
        var index = 0;
        while (index < text.Length)
        {
            index = text.IndexOf(word, index, comparison);
            if (index < 0)
                return false;

            var leftOk = index == 0 || !IsWordChar(text[index - 1]);
            var rightIndex = index + word.Length;
            var rightOk = rightIndex >= text.Length || !IsWordChar(text[rightIndex]);
            if (leftOk && rightOk)
                return true;

            index += word.Length;
        }
        return false;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static void AddPersistenceSuggestion(
        AnalysisResult result, CqrsRequest request, HandlerInfo[] handlers, List<SliceTestSuggestion> suggestions)
    {
        var linked = handlers.Select(h => h.FullName).ToHashSet(StringComparer.Ordinal);
        var directContexts = result.FlowEdges
            .Where(e => e.Kind == FlowEdgeKind.HandlerToDbContext && linked.Contains(e.From))
            .Select(e => Evidence("db-context", e.To, e.ProjectName, e.Location));
        var resolvedContexts = result.ResolvedEdges
            .Where(e => e.Kind == FlowEdgeKind.HandlerToDbContextResolved && linked.Contains(e.From))
            .Select(e => Evidence("di-resolved-db-context", e.To, e.ProjectName, e.Location));
        var touches = handlers.SelectMany(h => h.TouchedEntities
            .Select(entity => Evidence("db-set-touch", entity, h.ProjectName, h.Location)));
        AddSuggestion(suggestions, request, "persistence", "integration",
            "When the scenario depends on stored or retrieved state, test against the configured provider and assert the agreed state or result at the persistence boundary.",
            directContexts.Concat(resolvedContexts).Concat(touches));
    }

    private static T[] ResolveLinked<T>(IEnumerable<T> declarations, IEnumerable<string> links, Func<T, string> fullName)
    {
        // The scanner's links carry full names. A missing or duplicated declaration cannot back a suggestion.
        var linked = links.ToHashSet(StringComparer.Ordinal);
        return declarations.Where(d => linked.Contains(fullName(d)))
            .GroupBy(fullName, StringComparer.Ordinal).Where(g => g.Count() == 1)
            .Select(g => g.Single()).ToArray();
    }

    private static void AddSuggestion(
        List<SliceTestSuggestion> suggestions, CqrsRequest request, string boundary, string level, string purpose,
        IEnumerable<SliceTestEvidence> evidence)
        => AddTargetSuggestion(suggestions, request.FullName, boundary, level, purpose, evidence);

    private static void AddTargetSuggestion(
        List<SliceTestSuggestion> suggestions, string targetFullName, string boundary, string level, string purpose,
        IEnumerable<SliceTestEvidence> evidence)
    {
        var sources = evidence.OrderBy(e => e.Target, StringComparer.Ordinal)
            .ThenBy(e => e.Location?.File, StringComparer.Ordinal).ThenBy(e => e.Location?.Line).ToArray();
        if (sources.Length > 0)
            suggestions.Add(new(boundary, level, targetFullName, purpose, sources));
    }

    private static SliceTestEvidence Evidence(string kind, string target, string? project, SourceLocation? location)
        => new(kind, target, project, Location(location));

    private static JsonLocation? Location(SourceLocation? location)
        => location is null ? null : new JsonLocation { File = location.File, Line = location.Line, Column = location.Column };

    private static SliceTestStep[] TestSteps() =>
    [
        new("red", "Inspect existing tests and their assertions. Test dispatches are navigation hints. Add or refine one scenario test and verify it fails for the behavior you intend to change."),
        new("green", "Make the smallest production change that satisfies the failing assertion, then rerun the focused test."),
        new("refactor", "Improve names and boundaries while keeping the scenario passing, then run the affected test suite."),
    ];
}
