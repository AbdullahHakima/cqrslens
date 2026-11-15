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
            var reqId = request.SpineId ?? result.IdentityResolver.ResolveComponentId(
                request.ProjectName, request.Namespace, request.ContainingTypes, request.Name);
            var hasValidator = result.Spine.HasIncomingRelation(
                NodeReference.ForComponent(reqId), RelationKind.Validates);
            if (request.Kind != "Command" || hasValidator)
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

        // The check's premise is that validators attach to CQRS requests. A
        // solution that exposes no concrete request through any mediator shape has
        // no request model for a validator to be orphaned from: its
        // AbstractValidator<T> types validate view models, DTOs or domain types,
        // not requests. Flagging every one of them is noise, so orphan detection
        // only applies once a request inventory exists. A validator whose target
        // request was renamed or deleted in a MediatR-shaped solution is still an
        // orphan because such a solution keeps its other requests.
        if (result.Requests.Count == 0)
            return;

        var linkedValidators = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rel in result.Spine.UnindexedRelations)
        {
            if (rel.Kind == RelationKind.Validates)
                linkedValidators.Add(rel.From.SymbolId?.FullName ?? rel.From.Name);
        }
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
