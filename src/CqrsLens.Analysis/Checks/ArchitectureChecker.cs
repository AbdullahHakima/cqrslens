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
    public const string UnreachableRequestCode = "UnreachableRequest";
    public const string UnpublishedNotificationCode = "UnpublishedNotification";

    public static List<CheckFinding> RunChecks(AnalysisResult result)
    {
        var findings = new List<CheckFinding>();

        var projectsByName = ProjectNameIndex.Build(result);
        ProjectNameIndex.ReportAmbiguousNames(result, projectsByName, findings);

        RequestHandlerChecks.Run(result, findings);
        ProjectLayerChecks.Run(result, projectsByName, findings);
        RequestValidatorChecks.Run(result, findings);
        NotificationChecks.Run(result, findings);
        FlowReachabilityChecks.Run(result, findings);

        CheckFindingOrdering.Sort(findings);
        return findings;
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

        CheckFindingOrdering.Sort(filtered);
        return filtered;
    }

    public static bool HasErrors(IEnumerable<CheckFinding> findings)
        => findings.Any(f => f.Severity == CheckSeverity.Error);
}
