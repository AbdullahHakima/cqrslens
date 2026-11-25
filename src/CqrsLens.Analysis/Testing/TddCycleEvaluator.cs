namespace CqrsLens;

/// <summary>
/// Evaluates the closed-loop TDD phase of a CQRS slice across its design spec,
/// static scan checks, discovered test references, and optional runtime TRX results.
/// </summary>
public static class TddCycleEvaluator
{
    private const string EvaluationScope =
        "Static CQRS design + architecture checks + static test discovery + runtime TRX execution gate. " +
        "Passing tests verify only the behaviors asserted by those tests.";

    private static readonly string[] TestNameSuffixes = { "Tests", "Test", "Specs", "Spec" };

    /// <summary>
    /// Evaluates <paramref name="design"/> against <paramref name="result"/> and optional
    /// <paramref name="runtimeResults"/>, returning a fail-closed TDD phase and next actions.
    /// </summary>
    public static TddCycleEvaluation Evaluate(
        AnalysisResult result,
        SliceDesign design,
        TrxTestSummary? runtimeResults = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(design);

        var designEval = SliceDesignEvaluator.Evaluate(result, design);
        var ownerProject = ResolveProject(result, design.Project);
        var matchedRequest = design.IsCqrsKind ? ResolveRequest(result, design, ownerProject) : null;
        var sliceProjectName = ownerProject?.Name
            ?? matchedRequest?.ProjectName
            ?? (!design.Project.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ? design.Project : null);

        IReadOnlyList<TestMethodReference> discoveredTests;
        if (design.IsCqrsKind)
        {
            if (matchedRequest is not null)
            {
                discoveredTests = TestProjectDiscovery.FindTestReferencesForSlice(result, matchedRequest);
            }
            else if (designEval.Status == SliceDesignEvaluator.Missing)
            {
                var synthetic = BuildSyntheticRequest(design, ownerProject);
                discoveredTests = TestProjectDiscovery.FindTestReferencesForSlice(result, synthetic);
            }
            else
            {
                discoveredTests = Array.Empty<TestMethodReference>();
            }
        }
        else if (designEval.Status is SliceDesignEvaluator.Matched or SliceDesignEvaluator.Missing or SliceDesignEvaluator.Mismatch)
        {
            discoveredTests = TestProjectDiscovery.FindTestReferencesForTarget(result, design.Request, sliceProjectName);
            if (discoveredTests.Count == 0 &&
                design.Endpoint is { Route: { Length: > 1 } epRoute } &&
                epRoute.StartsWith('/'))
            {
                discoveredTests = TestProjectDiscovery.FindTestReferencesForTarget(result, epRoute, sliceProjectName);
            }
        }
        else
        {
            discoveredTests = Array.Empty<TestMethodReference>();
        }

        var matchedEntrypoint = design.IsCqrsKind
            ? null
            : ResolveEntrypoint(result, design, sliceProjectName);

        var sliceChecks = CollectSliceChecks(result, design, matchedRequest, matchedEntrypoint, sliceProjectName);
        var activeChecks = sliceChecks
            .Where(c => !result.BaselineApplied || !c.IsKnown)
            .ToArray();
        var sliceErrors = activeChecks.Count(c => c.Severity == CheckSeverity.Error);
        var sliceWarnings = activeChecks.Count(c => c.Severity == CheckSeverity.Warning);

        var targetSimpleNames = CollectTargetSimpleNames(design, matchedRequest, matchedEntrypoint);
        var matchingPassed = new List<string>();
        var matchingFailed = new List<string>();

        if (runtimeResults is not null)
        {
            var seenPassed = new HashSet<string>(StringComparer.Ordinal);
            var seenFailed = new HashSet<string>(StringComparer.Ordinal);

            foreach (var unit in runtimeResults.Results)
            {
                if (!MatchesSlice(unit, discoveredTests, targetSimpleNames))
                    continue;

                if (unit.Outcome == "passed")
                {
                    if (seenPassed.Add(unit.TestName))
                        matchingPassed.Add(unit.TestName);
                }
                else if (unit.Outcome == "failed")
                {
                    if (seenFailed.Add(unit.TestName))
                        matchingFailed.Add(unit.TestName);
                }
            }
        }

        string phase;
        if (designEval.Status == SliceDesignEvaluator.NotDeterminable || !result.CanRunStrictChecks)
        {
            phase = "inconclusive";
        }
        else if (designEval.Status == SliceDesignEvaluator.Mismatch || sliceErrors > 0)
        {
            phase = "drifted";
        }
        else if (matchingFailed.Count > 0)
        {
            phase = "red";
        }
        else if (designEval.Status == SliceDesignEvaluator.Missing)
        {
            phase = "spec_draft";
        }
        else if (runtimeResults is null ||
                 discoveredTests.Count == 0 ||
                 matchingPassed.Count == 0 ||
                 sliceWarnings > 0 ||
                 runtimeResults.Failed > 0)
        {
            phase = "green_incomplete";
        }
        else
        {
            phase = "verified";
        }

        var nextActions = BuildNextActions(
            phase,
            result,
            designEval,
            activeChecks,
            discoveredTests,
            runtimeResults,
            matchingPassed,
            matchingFailed);

        return new TddCycleEvaluation(
            phase,
            designEval.Status,
            discoveredTests.Count > 0,
            runtimeResults is not null,
            runtimeResults?.Passed ?? 0,
            runtimeResults?.Failed ?? 0,
            runtimeResults?.Skipped ?? 0,
            matchingPassed,
            matchingFailed,
            designEval.Checks,
            sliceChecks,
            discoveredTests,
            nextActions,
            EvaluationScope);
    }

    private static ProjectInfo? ResolveProject(AnalysisResult result, string expectedProject)
    {
        var normalized = expectedProject.Replace('\\', '/');
        if (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];

        var matches = result.Projects
            .Where(p => p.Name.Equals(expectedProject, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(
                            SourceLocation.FromLine(p.FilePath, result.RootPath, 1, 1)?.File,
                            normalized,
                            StringComparison.Ordinal))
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static CqrsRequest? ResolveRequest(
        AnalysisResult result,
        SliceDesign design,
        ProjectInfo? ownerProject)
    {
        var candidates = result.Requests
            .Where(r => string.Equals(r.FullName, design.Request, StringComparison.Ordinal))
            .ToArray();
        if (candidates.Length == 0)
            return null;

        if (ownerProject is not null)
        {
            var owned = candidates
                .Where(r => IsOwnedBy(result, r, ownerProject))
                .ToArray();
            if (owned.Length == 1)
                return owned[0];
            return null;
        }

        return candidates.Length == 1 ? candidates[0] : null;
    }

    private static bool IsOwnedBy(AnalysisResult result, CqrsRequest request, ProjectInfo project)
    {
        if (request.Location is { } location)
        {
            var sourcePath = Path.Combine(result.RootPath, location.File.Replace('/', Path.DirectorySeparatorChar));
            var located = ProjectLocator.FindOwningProject(result.Projects, sourcePath);
            if (located is not null)
                return located == project;
        }

        return string.Equals(request.ProjectName, project.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static CqrsRequest BuildSyntheticRequest(SliceDesign design, ProjectInfo? ownerProject)
    {
        var request = new CqrsRequest
        {
            Name = CSharpScanner.SimpleName(design.Request),
            Namespace = CSharpScanner.NamespaceOf(design.Request),
            ProjectName = ownerProject?.Name ?? design.Project,
        };
        if (design.Handler is not null)
            request.Handlers.Add(design.Handler);
        if (design.Validator is not null)
            request.Validators.Add(design.Validator);
        return request;
    }

    private static SystemEntrypointInfo? ResolveEntrypoint(
        AnalysisResult result,
        SliceDesign design,
        string? sliceProjectName)
    {
        var matches = result.SystemEntrypoints
            .Where(e => string.Equals(e.Name, design.Request, StringComparison.Ordinal) ||
                        string.Equals(e.RouteOrTrigger, design.Request, StringComparison.Ordinal) ||
                        string.Equals(e.DisplayLabel, design.Request, StringComparison.Ordinal) ||
                        string.Equals($"{e.Name} ({e.RouteOrTrigger})", design.Request, StringComparison.Ordinal))
            .Where(e => string.IsNullOrEmpty(sliceProjectName) ||
                        string.Equals(e.ProjectName, sliceProjectName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static IReadOnlyList<CheckFinding> CollectSliceChecks(
        AnalysisResult result,
        SliceDesign design,
        CqrsRequest? request,
        SystemEntrypointInfo? entrypoint,
        string? sliceProjectName)
    {
        var targets = new HashSet<string>(StringComparer.Ordinal)
        {
            design.Request,
        };
        if (design.Handler is not null)
            targets.Add(design.Handler);
        if (design.Validator is not null)
            targets.Add(design.Validator);

        if (request is not null)
        {
            targets.Add(request.FullName);
            foreach (var h in request.Handlers)
                targets.Add(h);
            foreach (var v in request.Validators)
                targets.Add(v);
            foreach (var n in request.PublishedNotifications)
                targets.Add(n);
            foreach (var d in request.DispatchedRequests)
                targets.Add(d);

            var linkedHandlers = result.Handlers
                .Where(h => request.Handlers.Contains(h.FullName, StringComparer.Ordinal));
            foreach (var handler in linkedHandlers)
            {
                foreach (var n in handler.PublishedNotifications)
                    targets.Add(n);
                foreach (var d in handler.DispatchedRequests)
                    targets.Add(d);
            }
        }

        if (entrypoint is not null)
        {
            targets.Add(entrypoint.Name);
            if (!string.IsNullOrWhiteSpace(entrypoint.RouteOrTrigger))
                targets.Add(entrypoint.RouteOrTrigger);
            foreach (var reqFull in entrypoint.TargetRequests)
                targets.Add(reqFull);
            foreach (var msg in entrypoint.PublishedMessages)
                targets.Add(msg);
            foreach (var rawSvc in entrypoint.CalledServices)
            {
                var arrow = rawSvc.IndexOf(" -> ", StringComparison.Ordinal);
                if (arrow >= 0)
                {
                    targets.Add(rawSvc[..arrow].Trim());
                    targets.Add(rawSvc[(arrow + 4)..].Trim());
                }
                else
                {
                    targets.Add(rawSvc.Trim());
                }
            }
        }

        var findings = new List<CheckFinding>();
        foreach (var finding in result.Checks)
        {
            if (finding.Target is not null && targets.Contains(finding.Target))
            {
                findings.Add(finding);
                continue;
            }

            if (finding.Code == ArchitectureChecker.CircularServiceDependencyCode &&
                finding.Target is not null &&
                MatchesServiceCycleTarget(finding.Target, targets))
            {
                findings.Add(finding);
                continue;
            }

            if ((finding.Code is ArchitectureChecker.LayerViolationCode or
                    ArchitectureChecker.ApplicationLayerViolationCode or
                    ArchitectureChecker.CircularProjectDependencyCode or
                    ArchitectureChecker.AmbiguousProjectNameCode) &&
                !string.IsNullOrEmpty(sliceProjectName) &&
                string.Equals(finding.ProjectName, sliceProjectName, StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(finding);
            }
        }

        return findings;
    }

    private static bool MatchesServiceCycleTarget(string cycleTarget, HashSet<string> sliceTargets)
    {
        foreach (var part in cycleTarget.Split(" -> ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (sliceTargets.Contains(part))
                return true;

            var simplePart = CSharpScanner.SimpleName(part);
            foreach (var st in sliceTargets)
            {
                if (string.Equals(CSharpScanner.SimpleName(st), simplePart, StringComparison.Ordinal))
                    return true;
            }
        }
        return false;
    }

    private static HashSet<string> CollectTargetSimpleNames(
        SliceDesign design,
        CqrsRequest? request,
        SystemEntrypointInfo? entrypoint)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (design.IsCqrsKind)
        {
            AddSimple(names, design.Request);
            if (design.Handler is not null)
                AddSimple(names, design.Handler);
            if (design.Validator is not null)
                AddSimple(names, design.Validator);

            if (request is not null)
            {
                AddSimple(names, request.FullName);
                foreach (var h in request.Handlers)
                    AddSimple(names, h);
                foreach (var v in request.Validators)
                    AddSimple(names, v);
            }

            return names;
        }

        if (!design.Request.Contains(' ', StringComparison.Ordinal) && !design.Request.StartsWith('/'))
        {
            var dot = design.Request.LastIndexOf('.');
            if (dot > 0 && dot < design.Request.Length - 1 &&
                design.Kind is "Endpoint" or "Grpc")
            {
                var left = CSharpScanner.SimpleName(design.Request[..dot]);
                var right = design.Request[(dot + 1)..];
                if (!TestProjectDiscovery.IsTrivialTargetToken(left) && left.Length >= 3)
                    names.Add(left);
                if (!TestProjectDiscovery.IsTrivialTargetToken(right) && right.Length >= 4)
                    names.Add(right);
            }
            else
            {
                var simple = CSharpScanner.SimpleName(design.Request.Trim());
                if (!TestProjectDiscovery.IsTrivialTargetToken(simple))
                    names.Add(simple);
            }
        }

        if (entrypoint is not null)
        {
            if (!entrypoint.Name.Contains(' ', StringComparison.Ordinal) &&
                entrypoint.Name is not ("MapGet" or "MapPost" or "MapPut" or "MapDelete" or "MapPatch"))
            {
                var dot = entrypoint.Name.LastIndexOf('.');
                if (dot > 0 && dot < entrypoint.Name.Length - 1)
                {
                    var left = CSharpScanner.SimpleName(entrypoint.Name[..dot]);
                    var right = entrypoint.Name[(dot + 1)..];
                    if (!TestProjectDiscovery.IsTrivialTargetToken(left) && left.Length >= 3)
                        names.Add(left);
                    if (!TestProjectDiscovery.IsTrivialTargetToken(right) && right.Length >= 4)
                        names.Add(right);
                }
                else if (!TestProjectDiscovery.IsTrivialTargetToken(entrypoint.Name))
                {
                    names.Add(entrypoint.Name);
                }
            }

            if (entrypoint.Kind == SystemEntrypointKind.MessageConsumer &&
                !string.IsNullOrWhiteSpace(entrypoint.RouteOrTrigger) &&
                !entrypoint.RouteOrTrigger.Contains(' ', StringComparison.Ordinal) &&
                !entrypoint.RouteOrTrigger.Contains('/', StringComparison.Ordinal))
            {
                var msgSimple = CSharpScanner.SimpleName(entrypoint.RouteOrTrigger);
                if (!TestProjectDiscovery.IsTrivialTargetToken(msgSimple))
                    names.Add(msgSimple);
            }
        }

        return names;
    }

    private static void AddSimple(HashSet<string> set, string fullOrSimple)
    {
        var simple = CSharpScanner.SimpleName(fullOrSimple.Trim());
        if (simple.Length > 0)
            set.Add(simple);
    }

    private static bool MatchesSlice(
        TrxUnitTestResult unit,
        IReadOnlyList<TestMethodReference> discoveredTests,
        HashSet<string> targetSimpleNames)
    {
        var raw = unit.TestName.Trim();
        if (raw.Length == 0)
            return false;

        var paren = raw.IndexOf('(');
        var baseName = paren > 0 ? raw[..paren].TrimEnd() : raw;
        if (baseName.Length == 0)
            return false;

        var lastDot = baseName.LastIndexOf('.');
        var qualifier = lastDot > 0 ? baseName[..lastDot] : string.Empty;
        var methodPart = lastDot >= 0 ? baseName[(lastDot + 1)..] : baseName;

        foreach (var testRef in discoveredTests)
        {
            if (testRef.MethodName == "<dispatch>")
            {
                if (!string.IsNullOrEmpty(testRef.ClassName))
                {
                    var simpleClass = CSharpScanner.SimpleName(testRef.ClassName);
                    if (qualifier.Equals(testRef.ClassName, StringComparison.OrdinalIgnoreCase) ||
                        qualifier.Equals(simpleClass, StringComparison.OrdinalIgnoreCase) ||
                        qualifier.EndsWith("." + simpleClass, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                continue;
            }

            if (!string.Equals(methodPart, testRef.MethodName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (qualifier.Length == 0)
                return true;

            var simpleClassName = CSharpScanner.SimpleName(testRef.ClassName);
            if (string.Equals(qualifier, testRef.ClassName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(qualifier, simpleClassName, StringComparison.OrdinalIgnoreCase) ||
                qualifier.EndsWith("." + simpleClassName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (var target in targetSimpleNames)
        {
            if (MatchesTargetToken(baseName, target))
                return true;
        }

        return false;
    }

    private static bool MatchesTargetToken(string testName, string targetSimpleName)
    {
        var index = 0;
        while (index < testName.Length)
        {
            index = testName.IndexOf(targetSimpleName, index, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return false;

            var leftOk = index == 0 || !IsIdentChar(testName[index - 1]);
            var afterIndex = index + targetSimpleName.Length;
            if (leftOk && IsValidTokenEnd(testName, afterIndex))
                return true;

            index += targetSimpleName.Length;
        }

        return false;
    }

    private static bool IsValidTokenEnd(string text, int afterIndex)
    {
        if (afterIndex >= text.Length || !IsIdentChar(text[afterIndex]))
            return true;

        foreach (var suffix in TestNameSuffixes)
        {
            if (afterIndex + suffix.Length <= text.Length &&
                string.Compare(text, afterIndex, suffix, 0, suffix.Length, StringComparison.OrdinalIgnoreCase) == 0)
            {
                var afterSuffix = afterIndex + suffix.Length;
                if (afterSuffix >= text.Length || !IsIdentChar(text[afterSuffix]))
                    return true;
            }
        }

        return false;
    }

    private static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static IReadOnlyList<string> BuildNextActions(
        string phase,
        AnalysisResult result,
        SliceDesignEvaluation designEval,
        CheckFinding[] activeChecks,
        IReadOnlyList<TestMethodReference> discoveredTests,
        TrxTestSummary? runtimeResults,
        List<string> matchingPassed,
        List<string> matchingFailed)
    {
        var actions = new List<string>();

        switch (phase)
        {
            case "inconclusive":
            {
                if (!result.CanRunStrictChecks)
                {
                    var codes = string.Join(", ",
                        result.Diagnostics.Select(d => d.Code).Distinct(StringComparer.Ordinal));
                    actions.Add($"Resolve blocking scan diagnostics ({codes}) and re-run analysis without source filters.");
                }

                var unclearFields = designEval.Checks
                    .Where(c => c.Status == SliceDesignEvaluator.NotDeterminable)
                    .Select(c => c.Field)
                    .ToArray();
                if (unclearFields.Length > 0)
                {
                    actions.Add($"Resolve `not_determinable` slice design check(s): {string.Join(", ", unclearFields)}.");
                }

                if (actions.Count == 0)
                    actions.Add("Ensure a complete, unambiguous static scan before evaluating the TDD cycle.");
                break;
            }

            case "drifted":
            {
                var mismatches = designEval.Checks
                    .Where(c => c.Status == SliceDesignEvaluator.Mismatch)
                    .Select(c => c.Field)
                    .ToArray();
                if (mismatches.Length > 0)
                {
                    actions.Add($"Reconcile mismatched slice design field(s): {string.Join(", ", mismatches)}.");
                }

                var errorChecks = activeChecks
                    .Where(c => c.Severity == CheckSeverity.Error)
                    .Select(c => c.Code)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                if (errorChecks.Length > 0)
                {
                    actions.Add($"Fix active slice architecture error(s): {string.Join(", ", errorChecks)}.");
                }
                break;
            }

            case "red":
            {
                actions.Add($"Fix failing slice test(s): {string.Join(", ", matchingFailed)}.");
                actions.Add(designEval.Status == SliceDesignEvaluator.Missing
                    ? "Implement the slice contract specified in the design so the failing test(s) pass, then re-run TRX tests."
                    : "Make the minimal code or test fix to turn the failing test(s) green, then re-run and supply a fresh TRX.");
                break;
            }

            case "spec_draft":
            {
                if (discoveredTests.Count == 0)
                {
                    actions.Add("Write at least one failing test covering the slice scenarios before implementing production code (TDD Red).");
                }
                else if (runtimeResults is null)
                {
                    actions.Add("Run the discovered test(s) with `--logger trx` to verify the Red phase before implementing the slice.");
                }

                actions.Add(designEval.Design.IsCqrsKind
                    ? "Implement the slice request, handler, and optional validator/endpoint declared in the design."
                    : "Implement the slice operation or service declared in the design.");
                if (runtimeResults is { Failed: > 0 })
                {
                    actions.Add($"Note: {runtimeResults.Failed} test(s) outside this slice failed in the supplied TRX.");
                }
                break;
            }

            case "green_incomplete":
            {
                if (discoveredTests.Count == 0)
                {
                    actions.Add(designEval.Design.IsCqrsKind
                        ? "Add static test references targeting this slice (request, handler, or validator) in a test project."
                        : "Add static test references targeting this slice (entrypoint, worker, consumer, or service) in a test project.");
                }

                if (runtimeResults is null)
                {
                    actions.Add("Run `dotnet test --logger trx` and pass the TRX summary to verify runtime execution.");
                }
                else
                {
                    if (matchingPassed.Count == 0)
                        actions.Add("No passing test in the TRX matched this slice; ensure the slice's tests execute and report results.");
                    if (runtimeResults.Failed > 0)
                        actions.Add($"Note: {runtimeResults.Failed} test(s) outside this slice failed in the TRX; resolve all failures for a verified green suite.");
                }

                var warnings = activeChecks
                    .Where(c => c.Severity == CheckSeverity.Warning)
                    .Select(c => c.Code)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                if (warnings.Length > 0)
                    actions.Add($"Address slice-scoped architecture warning(s): {string.Join(", ", warnings)}.");
                break;
            }

            case "verified":
            {
                actions.Add("Refactor cleanly while preserving all matched design checks and passing tests.");
                actions.Add("Re-run tests and slice verification after any refactoring.");
                break;
            }
        }

        return actions;
    }
}
