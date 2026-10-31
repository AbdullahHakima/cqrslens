namespace CqrsLens;

/// <summary>
/// Reports commands without validators and validators whose target is not a request.
/// </summary>
internal static class RequestValidatorChecks
{
    internal static void Run(AnalysisResult result, List<CheckFinding> findings)
    {
        ReportCommandsWithoutValidators(result, findings);
        ReportOrphanValidators(result, findings);
    }

    private static void ReportCommandsWithoutValidators(AnalysisResult result, List<CheckFinding> findings)
    {
        foreach (var request in result.Requests)
        {
            if (request.Kind != "Command" || request.Validators.Count > 0)
                continue;

            findings.Add(new CheckFinding(
                CheckSeverity.Warning,
                ArchitectureChecker.CommandWithoutValidatorCode,
                $"Command `{request.FullName}` has no validator.",
                Target: request.FullName,
                Location: request.Location,
                ProjectName: request.ProjectName));
        }
    }

    private static void ReportOrphanValidators(AnalysisResult result, List<CheckFinding> findings)
    {
        if (result.Validators.Count == 0)
            return;

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
                ambiguousTargets.Contains(validator.TargetType) ||
                result.NonConcreteRequestNames.Contains(validator.TargetType))
                continue;

            var targetDisplay = string.IsNullOrEmpty(validator.TargetNamespace)
                ? validator.TargetType
                : $"{validator.TargetNamespace}.{validator.TargetType}";
            findings.Add(new CheckFinding(
                CheckSeverity.Warning,
                ArchitectureChecker.OrphanValidatorCode,
                $"Validator `{validator.FullName}` targets `{targetDisplay}`, which does not match any discovered CQRS request.",
                Target: validator.FullName,
                Location: validator.Location,
                ProjectName: validator.ProjectName));
        }
    }
}
