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

    /// <summary>Layers a Domain project must never depend on.</summary>
    private static readonly string[] ForbiddenForDomain = { "Infrastructure", "Api", "Web" };

    public static List<CheckFinding> RunChecks(AnalysisResult result)
    {
        var findings = new List<CheckFinding>();

        var projectByName = result.Projects
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

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

        // (b) Domain project referencing Infrastructure/Api/Web -> error
        foreach (var project in result.Projects)
        {
            if (!string.Equals(project.Layer, "Domain", StringComparison.OrdinalIgnoreCase))
                continue;
            foreach (var reference in project.ReferenceNames)
            {
                if (projectByName.TryGetValue(reference, out var target) &&
                    ForbiddenForDomain.Contains(target.Layer, StringComparer.OrdinalIgnoreCase))
                {
                    findings.Add(new CheckFinding(
                        CheckSeverity.Error,
                        LayerViolationCode,
                        $"Domain project `{project.Name}` references {target.Layer} project `{target.Name}`."));
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

        findings.Sort((a, b) =>
        {
            var bySeverity = b.Severity.CompareTo(a.Severity); // errors first
            if (bySeverity != 0) return bySeverity;
            var byCode = string.Compare(a.Code, b.Code, StringComparison.Ordinal);
            return byCode != 0 ? byCode : string.Compare(a.Message, b.Message, StringComparison.Ordinal);
        });
        return findings;
    }

    public static bool HasErrors(IEnumerable<CheckFinding> findings)
        => findings.Any(f => f.Severity == CheckSeverity.Error);
}
