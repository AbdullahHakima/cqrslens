namespace CqrsLens;

/// <summary>Adds an endpoint once and records its label on the target request.</summary>
internal static class EndpointRecorder
{
    public static void Record(AnalysisResult result, CqrsRequest request, EndpointInfo endpoint)
    {
        endpoint.TargetRequestFullName ??= request.FullName;
        var existing = result.Endpoints.FirstOrDefault(e =>
            string.Equals(e.HttpMethod, endpoint.HttpMethod, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.Route, endpoint.Route, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.TargetRequestName, endpoint.TargetRequestName, StringComparison.Ordinal) &&
            string.Equals(e.TargetRequestNamespace, endpoint.TargetRequestNamespace, StringComparison.Ordinal) &&
            string.Equals(e.Name, endpoint.Name, StringComparison.Ordinal));

        if (existing is null)
        {
            result.Endpoints.Add(endpoint);
            existing = endpoint;
        }

        if (existing.SpineId is not null)
        {
            var reqId = request.SpineId ?? result.IdentityResolver.ResolveComponentId(
                request.ProjectName, request.Namespace, request.ContainingTypes, request.Name);
            result.Spine.AddOrMergeRelation(
                RelationKind.Dispatches,
                NodeReference.ForEndpoint(existing.SpineId),
                NodeReference.ForComponent(reqId),
                SpineIdentityResolver.SyntheticOccurrence(
                    EvidenceRule.EndpointDispatch,
                    EvidenceOrigin.MappingOrAttribute,
                    existing.Location,
                    existing.IsInferred ? EvidenceStrength.Inferred : EvidenceStrength.Proven,
                    scopeProjectName: existing.ProjectName));
        }
    }
}
