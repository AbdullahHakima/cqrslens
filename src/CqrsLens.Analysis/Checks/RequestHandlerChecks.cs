namespace CqrsLens;

/// <summary>
/// Reports requests that have no handler or more than one handler.
/// </summary>
internal static class RequestHandlerChecks
{
    internal static void Run(AnalysisResult result, List<CheckFinding> findings)
    {
        var relBuffer = new List<SpineRelation>();
        var handlers = new List<string>();
        foreach (var request in result.Requests)
        {
            var reqId = request.SpineId ?? result.IdentityResolver.ResolveComponentId(
                request.ProjectName, request.Namespace, request.ContainingTypes, request.Name);
            result.Spine.CollectOutgoingRelationsUnordered(
                NodeReference.ForComponent(reqId), RelationKind.Serves, relBuffer);

            handlers.Clear();
            for (var i = 0; i < relBuffer.Count; i++)
            {
                var name = relBuffer[i].To.SymbolId?.FullName ?? relBuffer[i].To.Name;
                if (!handlers.Contains(name, StringComparer.Ordinal))
                    handlers.Add(name);
            }

            if (handlers.Count > 1)
                handlers.Sort(StringComparer.OrdinalIgnoreCase);

            if (handlers.Count == 0)
            {
                findings.Add(new CheckFinding(
                    CheckSeverity.Error,
                    ArchitectureChecker.MissingHandlerCode,
                    $"Request `{request.FullName}` has no handler.",
                    Target: request.FullName,
                    Location: request.Location,
                    ProjectName: request.ProjectName));
            }
            else if (handlers.Count > 1)
            {
                findings.Add(new CheckFinding(
                    CheckSeverity.Error,
                    ArchitectureChecker.MultipleHandlersCode,
                    $"Request `{request.FullName}` has {handlers.Count} handlers ({string.Join(", ", handlers)}); a request should have a single handler.",
                    Target: request.FullName,
                    Location: request.Location,
                    ProjectName: request.ProjectName));
            }
        }
    }
}
