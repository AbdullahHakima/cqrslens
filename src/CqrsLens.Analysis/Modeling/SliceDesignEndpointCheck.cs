using static CqrsLens.SliceDesignEvaluator;

namespace CqrsLens;

/// <summary>Checks an observed route association without inferring runtime dispatch.</summary>
internal static class SliceDesignEndpointCheck
{
    internal static SliceDesignCheck Evaluate(
        AnalysisResult result, string request, SliceDesignEndpoint endpoint, bool requestResolved,
        bool duplicateRequestName, string absenceStatus)
    {
        var expected = $"{endpoint.Method.ToUpperInvariant()} {endpoint.Route}";
        var observedRoute = result.Endpoints.Where(candidate =>
            candidate.HttpMethod.Equals(endpoint.Method, StringComparison.OrdinalIgnoreCase) &&
            candidate.Route.Equals(endpoint.Route, StringComparison.Ordinal)).ToArray();
        var requestEndpoints = result.Endpoints.Where(candidate =>
            candidate.TargetRequestFullName == request).ToArray();
        var explicitMatches = observedRoute.Where(candidate =>
            !candidate.IsInferred && candidate.TargetRequestFullName == request).ToArray();

        if (explicitMatches.Length > 0)
        {
            if (!requestResolved || duplicateRequestName)
                return Outcome(NotDeterminable, "The route is observed, but its link cannot be attributed to the selected request.", explicitMatches);

            return Outcome(Matched, "The scan links the declared route and method to the request.", explicitMatches);
        }

        var unclearRoute = observedRoute.Where(candidate =>
            candidate.IsInferred || string.IsNullOrEmpty(candidate.TargetRequestFullName)).ToArray();
        if (unclearRoute.Length > 0)
            return Outcome(NotDeterminable, "The route or its target was inferred or unresolved.", unclearRoute);

        if (observedRoute.Length > 0)
            return Outcome(Mismatch, "The scan links this route and method to a different request.", observedRoute);

        if (absenceStatus == NotDeterminable)
            return Outcome(NotDeterminable, "The route was not observed." + AbsenceNote(absenceStatus), requestEndpoints);

        if (requestEndpoints.Length == 0)
            return Outcome(Missing, "The scan found no association for the planned route and method.", []);

        if (!requestResolved || duplicateRequestName || requestEndpoints.All(candidate => candidate.IsInferred))
            return Outcome(NotDeterminable, "The scan lacks a uniquely attributed declared route for this request.", requestEndpoints);

        return Outcome(Mismatch, "The scan links the request to a different declared route or method.", requestEndpoints);

        SliceDesignCheck Outcome(string status, string message, EndpointInfo[] candidates) => Check(
            "endpoint", status, expected, candidates.Select(candidate =>
                $"{candidate.DisplayLabel} → {candidate.TargetRequestFullName ?? candidate.TargetRequestName}"),
            message, candidates.Select(candidate => candidate.Location).ToArray());
    }
}
