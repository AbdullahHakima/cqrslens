namespace CqrsLens;

/// <summary>
/// Post-analysis architecture checks (issue: v1.3 rule checks, shipped early in 0.2.0):
/// missing/multiple handlers, Clean Architecture layer violations, circular project
/// dependencies, commands without validators, orphan validators, and orphan notifications.
/// Findings land in AnalysisResult.Checks and drive --strict.
/// </summary>
public static class ArchitectureChecker
{
    public const string MissingHandlerCode = "MissingHandler";
    public const string MultipleHandlersCode = "MultipleHandlers";
    public const string LayerViolationCode = "LayerViolation";
    public const string ApplicationLayerViolationCode = "ApplicationLayerViolation";
    public const string CircularProjectDependencyCode = "CircularProjectDependency";
    public const string CommandWithoutValidatorCode = "CommandWithoutValidator";
    public const string OrphanValidatorCode = "OrphanValidator";
    public const string OrphanNotificationCode = "OrphanNotification";
    public const string AmbiguousProjectNameCode = "AmbiguousProjectName";

    /// <summary>Layers a Domain project must never depend on.</summary>
    private static readonly string[] ForbiddenForDomain = { "Infrastructure", "Api", "Web", "UI" };

    /// <summary>Layers an Application project must never depend on.</summary>
    private static readonly string[] ForbiddenForApplication = { "Infrastructure", "Api", "Web", "UI" };

    public static List<CheckFinding> RunChecks(AnalysisResult result)
    {
        var findings = new List<CheckFinding>();

        var projectsByName = result.Projects
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var (name, projects) in projectsByName.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (projects.Count > 1)
            {
                findings.Add(new CheckFinding(
                    CheckSeverity.Warning,
                    AmbiguousProjectNameCode,
                    $"Project name `{name}` is used by {projects.Count} projects; layer checks skip references to it.",
                    Target: name,
                    Location: SourceLocation.FromLine(projects[0].FilePath, result.RootPath, 1, 1),
                    ProjectName: name));
            }
        }

        // (a) Requests with no handler -> error; requests with >1 handler -> error
        foreach (var request in result.Requests)
        {
            if (request.Handlers.Count == 0)
            {
                findings.Add(new CheckFinding(
                    CheckSeverity.Error,
                    MissingHandlerCode,
                    $"Request `{request.FullName}` has no handler.",
                    Target: request.FullName,
                    Location: request.Location,
                    ProjectName: request.ProjectName));
            }
            else if (request.Handlers.Count > 1)
            {
                findings.Add(new CheckFinding(
                    CheckSeverity.Error,
                    MultipleHandlersCode,
                    $"Request `{request.FullName}` has {request.Handlers.Count} handlers ({string.Join(", ", request.Handlers)}); a request should have a single handler.",
                    Target: request.FullName,
                    Location: request.Location,
                    ProjectName: request.ProjectName));
            }
        }

        // (b) Domain project referencing Infrastructure/Api/Web/UI -> error, and
        //     Application project referencing Infrastructure/Api/Web/UI -> error.
        // Tests are not production layers and may legitimately reference anything.
        foreach (var project in result.Projects)
        {
            var isDomain = string.Equals(project.Layer, "Domain", StringComparison.OrdinalIgnoreCase);
            var isApplication = string.Equals(project.Layer, "Application", StringComparison.OrdinalIgnoreCase);
            if (!isDomain && !isApplication)
                continue;

            var forbidden = isDomain ? ForbiddenForDomain : ForbiddenForApplication;
            var code = isDomain ? LayerViolationCode : ApplicationLayerViolationCode;
            var layerLabel = isDomain ? "Domain" : "Application";

            foreach (var reference in project.ReferenceNames)
            {
                // A duplicate project name makes the reference target ambiguous; do not
                // guess and report a false layer violation.
                if (projectsByName.TryGetValue(reference, out var targets) &&
                    targets.Count == 1 &&
                    forbidden.Contains(targets[0].Layer, StringComparer.OrdinalIgnoreCase))
                {
                    findings.Add(new CheckFinding(
                        CheckSeverity.Error,
                        code,
                        $"{layerLabel} project `{project.Name}` references {targets[0].Layer} project `{targets[0].Name}`.",
                        Target: $"{project.Name} -> {targets[0].Name}",
                        Location: SourceLocation.FromLine(project.FilePath, result.RootPath, 1, 1),
                        ProjectName: project.Name));
                }
            }
        }

        // (c) Circular project dependencies -> error
        DetectCircularProjectDependencies(result, projectsByName, findings);

        // (d) Command requests without any validator -> warning
        foreach (var request in result.Requests)
        {
            if (request.Kind == "Command" && request.Validators.Count == 0)
            {
                findings.Add(new CheckFinding(
                    CheckSeverity.Warning,
                    CommandWithoutValidatorCode,
                    $"Command `{request.FullName}` has no validator.",
                    Target: request.FullName,
                    Location: request.Location,
                    ProjectName: request.ProjectName));
            }
        }

        // (e) Validators whose target type does not link to any discovered request -> warning
        if (result.Validators.Count > 0)
        {
            var linkedValidators = new HashSet<string>(
                result.Requests.SelectMany(r => r.Validators),
                StringComparer.OrdinalIgnoreCase);
            var ambiguousTargets = result.Requests
                .GroupBy(r => r.Name, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var validator in result.Validators)
            {
                if (linkedValidators.Contains(validator.FullName) ||
                    linkedValidators.Contains(validator.Name) ||
                    ambiguousTargets.Contains(validator.TargetType))
                    continue;

                var targetDisplay = string.IsNullOrEmpty(validator.TargetNamespace)
                    ? validator.TargetType
                    : $"{validator.TargetNamespace}.{validator.TargetType}";
                findings.Add(new CheckFinding(
                    CheckSeverity.Warning,
                    OrphanValidatorCode,
                    $"Validator `{validator.FullName}` targets `{targetDisplay}`, which does not match any discovered CQRS request.",
                    Target: validator.FullName,
                    Location: validator.Location,
                    ProjectName: validator.ProjectName));
            }
        }

        // (f) Notifications (domain events) without any handler -> warning
        foreach (var notification in result.Notifications)
        {
            if (notification.Handlers.Count == 0)
            {
                findings.Add(new CheckFinding(
                    CheckSeverity.Warning,
                    OrphanNotificationCode,
                    $"Notification `{notification.FullName}` has no handler.",
                    Target: notification.FullName,
                    Location: notification.Location,
                    ProjectName: notification.ProjectName));
            }
        }

        Sort(findings);
        return findings;
    }

    private static void DetectCircularProjectDependencies(
        AnalysisResult result,
        IReadOnlyDictionary<string, List<ProjectInfo>> projectsByName,
        List<CheckFinding> findings)
    {
        var uniqueProjects = projectsByName
            .Where(kv => kv.Value.Count == 1)
            .ToDictionary(kv => kv.Key, kv => kv.Value[0], StringComparer.OrdinalIgnoreCase);

        var reportedCycles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var path = new List<string>();
        var onStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Dfs(string current)
        {
            path.Add(current);
            onStack.Add(current);

            if (uniqueProjects.TryGetValue(current, out var project))
            {
                foreach (var dep in project.ReferenceNames.OrderBy(r => r, StringComparer.OrdinalIgnoreCase))
                {
                    if (!uniqueProjects.TryGetValue(dep, out var target))
                        continue;

                    if (onStack.Contains(target.Name))
                    {
                        var idx = path.FindIndex(p => string.Equals(p, target.Name, StringComparison.OrdinalIgnoreCase));
                        if (idx >= 0)
                        {
                            var ring = path.Skip(idx).ToList();
                            var minIndex = 0;
                            for (var i = 1; i < ring.Count; i++)
                            {
                                if (string.Compare(ring[i], ring[minIndex], StringComparison.OrdinalIgnoreCase) < 0)
                                    minIndex = i;
                            }

                            var canonical = ring.Skip(minIndex)
                                .Concat(ring.Take(minIndex))
                                .ToList();
                            canonical.Add(canonical[0]);
                            var cycleKey = string.Join(" -> ", canonical);
                            if (reportedCycles.Add(cycleKey))
                            {
                                var owner = uniqueProjects[canonical[0]];
                                findings.Add(new CheckFinding(
                                    CheckSeverity.Error,
                                    CircularProjectDependencyCode,
                                    $"Circular project dependency detected: {cycleKey}.",
                                    Target: cycleKey,
                                    Location: SourceLocation.FromLine(owner.FilePath, result.RootPath, 1, 1),
                                    ProjectName: owner.Name));
                            }
                        }
                    }
                    else if (!path.Contains(target.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        Dfs(target.Name);
                    }
                }
            }

            onStack.Remove(current);
            path.RemoveAt(path.Count - 1);
        }

        foreach (var name in uniqueProjects.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
            Dfs(name);
    }

    /// <summary>Applies .cqrslens.json rule severities; null means the rule is off.</summary>
    public static List<CheckFinding> ApplyRuleSeverities(
        IEnumerable<CheckFinding> findings,
        IReadOnlyDictionary<string, CheckSeverity?> overrides)
    {
        var filtered = new List<CheckFinding>();
        foreach (var finding in findings)
        {
            var severity = finding.Severity;
            if (overrides.TryGetValue(finding.Code, out var configured))
            {
                if (configured is null)
                    continue;
                severity = configured.Value;
            }

            filtered.Add(finding with { Severity = severity });
        }

        Sort(filtered);
        return filtered;
    }

    public static bool HasErrors(IEnumerable<CheckFinding> findings)
        => findings.Any(f => f.Severity == CheckSeverity.Error);

    private static void Sort(List<CheckFinding> findings)
        => findings.Sort((a, b) =>
        {
            var bySeverity = b.Severity.CompareTo(a.Severity); // errors first
            if (bySeverity != 0) return bySeverity;
            var byCode = string.Compare(a.Code, b.Code, StringComparison.Ordinal);
            return byCode != 0 ? byCode : string.Compare(a.Message, b.Message, StringComparison.Ordinal);
        });
}
