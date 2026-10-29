using System.Text.RegularExpressions;

namespace CqrsLens;

/// <summary>Discovers FastEndpoints classes and their verb/route calls.</summary>
internal static class FastEndpointDiscovery
{
    public static void Discover(
        SourceFile file,
        CSharpScanner.FileScan scan,
        SourceMask mask,
        Dictionary<string, List<CqrsRequest>> requestsBySimple,
        Dictionary<string, string> constStrings,
        AnalysisResult result)
    {
        foreach (var declaration in scan.Classes)
        {
            if (declaration.IsAbstract || declaration.BodySpan.End <= declaration.BodySpan.Start)
                continue;

            // FastEndpoints declarations are the only classes that can match here.
            // Checking the base name first avoids two base-list parses plus a body
            // substring allocation for every unrelated class.
            if (!declaration.BaseRawNames.Any(b => b.StartsWith("Endpoint", StringComparison.Ordinal)))
                continue;

            var endpointArguments = CSharpScanner.ExtractAllGenericArguments(declaration.BaseList, "Endpoint")
                .Concat(CSharpScanner.ExtractAllGenericArguments(declaration.BaseList, "EndpointWithoutRequest"))
                .FirstOrDefault();

            var bodyEnd = Math.Min(file.Code.Length, declaration.BodySpan.End);
            var body = file.Code[declaration.BodySpan.Start..bodyEnd];

            var matchedRequests = new List<CqrsRequest>();
            if (endpointArguments is { Count: >= 1 })
            {
                var requestSimple = CSharpScanner.UnqualifiedTypeName(endpointArguments[0]);
                if (requestsBySimple.TryGetValue(requestSimple, out var hits))
                {
                    var picked = DeepSliceCandidateResolver.PickRequest(
                        hits, file.ProjectName, scan.Usings);
                    if (picked is not null)
                        matchedRequests.Add(picked);
                }
            }

            if (matchedRequests.Count == 0 &&
                declaration.BaseRawNames.Any(b => b.StartsWith("Endpoint", StringComparison.Ordinal)))
            {
                matchedRequests.AddRange(EndpointRequestMatcher.FindReferencedRequests(
                    "", body, requestsBySimple, file.ProjectName, scan.Usings));
            }

            if (matchedRequests.Count == 0) continue;

            var verbMatch = ApiEndpointPatterns.FastEndpointVerb().Match(body);
            if (!verbMatch.Success || !mask.IsCode(declaration.BodySpan.Start + verbMatch.Index))
                continue;

            var verb = verbMatch.Groups["verb"].Value.ToUpperInvariant();
            string rawRoute;
            var routeInferred = false;
            if (verbMatch.Groups["route"].Success)
            {
                rawRoute = verbMatch.Groups["route"].Value;
            }
            else
            {
                var constReference = verbMatch.Groups["constRef"].Value;
                routeInferred = !constStrings.TryGetValue(constReference, out var resolvedConst);
                // Do not turn an unresolved symbol into a fake literal route. An empty route
                // renders as just the HTTP verb and is marked inferred.
                rawRoute = routeInferred ? "" : resolvedConst!;
            }

            var route = routeInferred && rawRoute.Length == 0 ? "" : EndpointRoutes.Normalize(rawRoute);
            var location = SourceLocation.Create(
                file.Path, result.RootPath, file.Code, declaration.BodySpan.Start);

            foreach (var request in matchedRequests)
            {
                EndpointRecorder.Record(
                    result,
                    request,
                    new EndpointInfo
                    {
                        Name = declaration.Name,
                        HttpMethod = verb,
                        Route = route,
                        TargetRequestName = request.Name,
                        TargetRequestNamespace = request.Namespace,
                        ProjectName = file.ProjectName,
                        Location = location,
                        IsInferred = routeInferred,
                    });
            }
        }
    }
}
