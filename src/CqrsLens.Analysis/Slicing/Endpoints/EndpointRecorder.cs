namespace CqrsLens;

/// <summary>Adds an endpoint once and records its label on the target request.</summary>
internal static class EndpointRecorder
{
    public static void Record(AnalysisResult result, CqrsRequest request, EndpointInfo endpoint)
    {
        endpoint.TargetRequestFullName ??= request.FullName;
        if (!result.Endpoints.Any(existing =>
                string.Equals(existing.HttpMethod, endpoint.HttpMethod, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.Route, endpoint.Route, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.TargetRequestName, endpoint.TargetRequestName, StringComparison.Ordinal) &&
                string.Equals(existing.TargetRequestNamespace, endpoint.TargetRequestNamespace, StringComparison.Ordinal) &&
                string.Equals(existing.Name, endpoint.Name, StringComparison.Ordinal)))
        {
            result.Endpoints.Add(endpoint);
        }

        var label = endpoint.IsInferred
            ? $"{endpoint.DisplayLabel} _(inferred)_"
            : endpoint.DisplayLabel;
        if (!request.Entrypoints.Contains(label, StringComparer.Ordinal))
            request.Entrypoints.Add(label);
    }
}
