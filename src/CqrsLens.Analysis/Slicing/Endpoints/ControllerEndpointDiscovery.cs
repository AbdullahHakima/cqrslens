using System.Text.RegularExpressions;

namespace CqrsLens;

/// <summary>Discovers MVC controller actions and links them to dispatched requests.</summary>
internal static class ControllerEndpointDiscovery
{
    public static void Discover(
        SourceFile file,
        CSharpScanner.FileScan scan,
        SourceMask mask,
        Dictionary<string, List<CqrsRequest>> requestsBySimple,
        AnalysisResult result)
    {
        if (!file.Code.Contains("Controller", StringComparison.Ordinal) &&
            !file.Code.Contains("[Http", StringComparison.Ordinal) &&
            !file.Code.Contains("[Route", StringComparison.Ordinal))
            return;

        foreach (var declaration in scan.Classes)
        {
            if (declaration.IsAbstract || declaration.BodySpan.End <= declaration.BodySpan.Start)
                continue;

            var isController = declaration.Name.EndsWith("Controller", StringComparison.Ordinal) ||
                               declaration.BaseRawNames.Any(b => b is "ControllerBase" or "Controller" or "ApiController" or "BaseController") ||
                               HasControllerAttribute(file.Code, declaration.DeclarationIndex);
            if (!isController) continue;

            var classRoute = ExtractClassRoutePrefix(file.Code, declaration);
            var controllerStem = ControllerStem(declaration.Name);
            var bodyEnd = Math.Min(file.Code.Length, declaration.BodySpan.End);

            foreach (Match match in ApiEndpointPatterns.ControllerAction().Matches(
                         file.Code, declaration.BodySpan.Start))
            {
                if (match.Index >= bodyEnd) break;
                if (!mask.IsCode(match.Index)) continue;
                if (!ReferenceEquals(DeepSliceSyntax.InnermostClass(scan.Classes, match.Index), declaration))
                    continue;

                var attributes = match.Groups["attrs"].Value;
                var verbMatch = ApiEndpointPatterns.HttpVerbAttribute().Match(attributes);
                var routeMatch = ApiEndpointPatterns.RouteAttribute().Match(attributes);
                if (!verbMatch.Success && !routeMatch.Success)
                    continue;

                var methodName = match.Groups["method"].Value;
                var parameters = match.Groups["params"].Value;
                var actionEnd = DeepSliceSyntax.FindMethodEnd(
                    file.Code, mask, match.Index + match.Length, bodyEnd);
                var actionBody = actionEnd > match.Index + match.Length
                    ? file.Code[(match.Index + match.Length)..actionEnd]
                    : "";

                var matchedRequests = EndpointRequestMatcher.FindReferencedRequests(
                    parameters,
                    actionBody,
                    requestsBySimple,
                    file.ProjectName,
                    scan.Usings);

                if (matchedRequests.Count == 0) continue;

                var subRoute = verbMatch.Success && verbMatch.Groups["route"].Success
                    ? verbMatch.Groups["route"].Value
                    : routeMatch.Success
                        ? routeMatch.Groups["route"].Value
                        : "";

                var fullRoute = EndpointRoutes.ForController(
                    classRoute, subRoute, controllerStem, methodName);
                var routeInferred = string.IsNullOrEmpty(classRoute) && string.IsNullOrEmpty(subRoute);
                var location = SourceLocation.Create(file.Path, result.RootPath, file.Code, match.Index);

                foreach (var request in matchedRequests)
                {
                    var verb = verbMatch.Success
                        ? verbMatch.Groups["verb"].Value.ToUpperInvariant()
                        : string.Equals(request.Kind, "Query", StringComparison.Ordinal) ? "GET" : "POST";

                    EndpointRecorder.Record(
                        result,
                        request,
                        new EndpointInfo
                        {
                            Name = $"{declaration.Name}.{methodName}",
                            HttpMethod = verb,
                            Route = fullRoute,
                            TargetRequestName = request.Name,
                            TargetRequestNamespace = request.Namespace,
                            ProjectName = file.ProjectName,
                            Location = location,
                            IsInferred = !verbMatch.Success || routeInferred,
                        });
                }
            }
        }
    }

    private static bool HasControllerAttribute(string code, int declarationIndex)
    {
        var start = Math.Max(0, declarationIndex - 400);
        var prefix = code[start..declarationIndex];
        var lastBoundary = prefix.LastIndexOfAny(new[] { ';', '}' });
        if (lastBoundary >= 0)
            prefix = prefix[(lastBoundary + 1)..];
        return prefix.Contains("ApiController", StringComparison.Ordinal) ||
               prefix.Contains("[Controller", StringComparison.Ordinal);
    }

    private static string ExtractClassRoutePrefix(string code, CSharpScanner.ClassDecl declaration)
    {
        var start = Math.Max(0, declaration.DeclarationIndex - 500);
        var prefix = code[start..declaration.DeclarationIndex];
        var lastBoundary = prefix.LastIndexOfAny(new[] { ';', '}' });
        if (lastBoundary >= 0)
            prefix = prefix[(lastBoundary + 1)..];

        var match = ApiEndpointPatterns.RouteAttribute().Match(prefix);
        return match.Success ? match.Groups["route"].Value : "";
    }

    private static string ControllerStem(string className)
    {
        var stem = className.EndsWith("Controller", StringComparison.OrdinalIgnoreCase) &&
                   className.Length > "Controller".Length
            ? className[..^"Controller".Length]
            : className;
        return stem.ToLowerInvariant();
    }
}
