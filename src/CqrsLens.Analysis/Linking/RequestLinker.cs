namespace CqrsLens;

/// <summary>
/// Connects handlers and validators to the requests they serve. Edges resolve by
/// fully-qualified name first; a simple-name match is used only when exactly one
/// request carries that name, so same-named requests in different namespaces never
/// share links.
/// </summary>
internal static class RequestLinker
{
    public static void Link(AnalysisResult result)
    {
        var index = new LinkIndex<CqrsRequest>(
            result.Requests,
            request => request.FullName,
            request => request.Name,
            request => request.ProjectName);

        LinkHandlers(result, index);
        LinkValidators(result, index);

        foreach (var request in result.Requests)
        {
            request.Handlers.Sort(StringComparer.OrdinalIgnoreCase);
            request.Validators.Sort(StringComparer.OrdinalIgnoreCase);
        }

        result.Requests.Sort((a, b) =>
        {
            var byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : string.Compare(a.FullName, b.FullName, StringComparison.Ordinal);
        });
    }

    private static void LinkHandlers(AnalysisResult result, LinkIndex<CqrsRequest> index)
    {
        foreach (var handler in result.Handlers.OrderBy(h => h.Name, StringComparer.OrdinalIgnoreCase))
        {
            var request = index.Resolve(
                handler.RequestName,
                handler.RequestNamespace,
                handler.Namespace,
                handler.ImportedNamespaces,
                handler.ProjectName);
            if (request is null)
            {
                LinkDiagnostics.RecordAmbiguous(
                    result,
                    "Handler",
                    handler.FullName,
                    handler.RequestName,
                    handler.Location,
                    handler.ProjectName,
                    index.SimpleMatches(handler.RequestName).Select(match => match.FullName).ToList());
                continue;
            }

            // Text mode only knows a written namespace when the generic argument was
            // qualified. Once linking has identified the real request, retain that
            // identity so text and Roslyn results describe the same shape.
            handler.RequestNamespace ??= request.Namespace;
            if (!request.Handlers.Contains(handler.FullName))
                request.Handlers.Add(handler.FullName);
        }
    }

    private static void LinkValidators(AnalysisResult result, LinkIndex<CqrsRequest> index)
    {
        foreach (var validator in result.Validators.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase))
        {
            var request = index.Resolve(
                validator.TargetType,
                validator.TargetNamespace,
                validator.Namespace,
                validator.ImportedNamespaces,
                validator.ProjectName);
            if (request is null)
            {
                LinkDiagnostics.RecordAmbiguous(
                    result,
                    "Validator",
                    validator.FullName,
                    validator.TargetType,
                    validator.Location,
                    validator.ProjectName,
                    index.SimpleMatches(validator.TargetType).Select(match => match.FullName).ToList());
                continue;
            }

            validator.TargetNamespace ??= request.Namespace;
            if (!request.Validators.Contains(validator.FullName))
                request.Validators.Add(validator.FullName);
        }
    }
}
