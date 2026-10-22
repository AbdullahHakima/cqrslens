namespace CqrsLens;

/// <summary>
/// Post-analysis architecture checks (issue: v1.3 rule checks, shipped early in 0.2.0):
/// missing handlers, Clean Architecture layer violations, and commands without
/// validators. Findings land in AnalysisResult.Checks and drive --strict.
/// </summary>
public static class ArchitectureChecker
{
    public const string MissingHandlerCode = "MissingHandler";
    public const string LayerViolationCode = "LayerViolation";
    public const string CommandWithoutValidatorCode = "CommandWithoutValidator";
    public const string AmbiguousProjectNameCode = "AmbiguousProjectName";

    /// <summary>Layers a Domain project must never depend on.</summary>
    private static readonly string[] ForbiddenForDomain = { "Infrastructure", "Api", "Web" };

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
                    $"Project name `{name}` is used by {projects.Count} projects; layer checks skip references to it."));
            }
        }

        // (a) Requests with no handler -> error
        foreach (var request in result.Requests)
        {
            if (request.Handlers.Count == 0)
            {
                findings.Add(new CheckFinding(
                    CheckSeverity.Error,
                    MissingHandlerCode,
                    $"Request `{request.FullName}` has no handler."));
            }
        }

        // (b) Domain project referencing Infrastructure/Api/Web -> error.
        // Tests are not production layers and may legitimately reference anything.
        foreach (var project in result.Projects)
        {
            if (!string.Equals(project.Layer, "Domain", StringComparison.OrdinalIgnoreCase))
                continue;
            foreach (var reference in project.ReferenceNames)
            {
                // A duplicate project name makes the reference target ambiguous; do not
                // guess and report a false layer violation.
                if (projectsByName.TryGetValue(reference, out var targets) &&
                    targets.Count == 1 &&
                    ForbiddenForDomain.Contains(targets[0].Layer, StringComparer.OrdinalIgnoreCase))
                {
                    findings.Add(new CheckFinding(
                        CheckSeverity.Error,
                        LayerViolationCode,
                        $"Domain project `{project.Name}` references {targets[0].Layer} project `{targets[0].Name}`."));
                }
            }
        }

        // (c) Command requests without any validator -> warning
        foreach (var request in result.Requests)
        {
            if (request.Kind == "Command" && request.Validators.Count == 0)
            {
                findings.Add(new CheckFinding(
                    CheckSeverity.Warning,
                    CommandWithoutValidatorCode,
                    $"Command `{request.FullName}` has no validator."));
            }
        }

        Sort(findings);
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
