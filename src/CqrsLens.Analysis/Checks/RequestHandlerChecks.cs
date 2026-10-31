namespace CqrsLens;

/// <summary>
/// Reports requests that have no handler or more than one handler.
/// </summary>
internal static class RequestHandlerChecks
{
    internal static void Run(AnalysisResult result, List<CheckFinding> findings)
    {
        foreach (var request in result.Requests)
        {
            if (request.Handlers.Count == 0)
            {
                findings.Add(new CheckFinding(
                    CheckSeverity.Error,
                    ArchitectureChecker.MissingHandlerCode,
                    $"Request `{request.FullName}` has no handler.",
                    Target: request.FullName,
                    Location: request.Location,
                    ProjectName: request.ProjectName));
            }
            else if (request.Handlers.Count > 1)
            {
                findings.Add(new CheckFinding(
                    CheckSeverity.Error,
                    ArchitectureChecker.MultipleHandlersCode,
                    $"Request `{request.FullName}` has {request.Handlers.Count} handlers ({string.Join(", ", request.Handlers)}); a request should have a single handler.",
                    Target: request.FullName,
                    Location: request.Location,
                    ProjectName: request.ProjectName));
            }
        }
    }
}
