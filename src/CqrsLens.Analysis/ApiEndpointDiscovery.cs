using System.Text.RegularExpressions;
using static CqrsLens.DeepSlicePatterns;

namespace CqrsLens;

/// <summary>
/// Discovers HTTP entrypoints and links them to the CQRS request they dispatch:
/// MVC controllers, minimal APIs, EndpointGroup method groups and FastEndpoints.
/// </summary>
internal static partial class ApiEndpointDiscovery
{
    [GeneratedRegex(@"\[Route\s*\(\s*@?""(?<route>[^""]*)""\s*\)\]", RegexOptions.IgnoreCase)]
    private static partial Regex RouteAttributeRegex();

    [GeneratedRegex(@"\[Http(?<verb>Get|Post|Put|Delete|Patch)(?:\s*\(\s*@?""(?<route>[^""]*)""\s*\))?\s*\]", RegexOptions.IgnoreCase)]
    private static partial Regex HttpVerbAttributeRegex();

    [GeneratedRegex(@"(?<attrs>(?:\[\s*[^\[\]]*\]\s*)+)(?:(?:public|protected|internal|virtual|override|async|sealed|new)\s+)+[^\(\{;=]+?\s+(?<method>[A-Za-z_]\w*)\s*\((?<params>[^\)]*)\)", RegexOptions.Singleline)]
    private static partial Regex ControllerActionRegex();

    [GeneratedRegex(@"\b(?<var>[A-Za-z_]\w*)\s*=\s*[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*\.MapGroup\s*\(\s*@?""(?<prefix>[^""]*)""\s*\)")]
    private static partial Regex MapGroupRegex();

    [GeneratedRegex(@"(?<target>[A-Za-z_]\w*)\s*\.\s*Map(?<verb>Get|Post|Put|Delete|Patch)\s*\(\s*@?""(?<route>[^""]*)""")]
    private static partial Regex MinimalApiMapRegex();

    // Matches Jason Taylor CleanArchitecture style: groupBuilder.MapPost(CreateTodoItem) or groupBuilder.MapPut(UpdateTodoItem, "{id}")
    [GeneratedRegex(@"(?<target>[A-Za-z_]\w*)\s*\.\s*Map(?<verb>Get|Post|Put|Delete|Patch)\s*\(\s*(?<handler>[A-Za-z_]\w*)\s*(?:,\s*@?""(?<route>[^""]*)"")?\s*\)")]
    private static partial Regex MethodGroupMinimalApiRegex();

    [GeneratedRegex(@"(?:(?:public|private|protected|internal|static|async)\s+)+[^\(\{;=]+?\s+(?<method>[A-Za-z_]\w*)\s*\((?<params>[^\)]*)\)", RegexOptions.Singleline)]
    private static partial Regex LocalMethodDeclarationRegex();

    // Matches FastEndpoints route calls: Post("/route") OR Post(CreateContributorRequest.Route)
    [GeneratedRegex(@"\b(?<verb>Get|Post|Put|Delete|Patch)\s*\(\s*(?:@?""(?<route>[^""]*)""|(?<constRef>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)+))\s*\)")]
    private static partial Regex FastEndpointVerbRegex();

    [GeneratedRegex(@"\bconst\s+string\s+(?<name>[A-Za-z_]\w*)\s*=\s*@?""(?<value>[^""]*)""")]
    private static partial Regex ConstStringRegex();

    internal static void Discover(
        List<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        AnalysisResult result)
    {
        var requestsBySimple = result.Requests
            .GroupBy(r => r.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        // Build constant string lookup for FastEndpoints route constants (e.g. CreateContributorRequest.Route)
        var constStrings = BuildConstStringLookup(parsedFiles);

        foreach (var (file, scan, mask) in parsedFiles)
        {
            DiscoverControllerEndpoints(file, scan, mask, requestsBySimple, result);
            DiscoverMinimalApiEndpoints(file, scan, mask, requestsBySimple, result);
            DiscoverFastEndpoints(file, scan, mask, requestsBySimple, constStrings, result);
        }

        result.Endpoints.Sort((a, b) =>
        {
            var byRoute = string.Compare(a.Route, b.Route, StringComparison.OrdinalIgnoreCase);
            if (byRoute != 0) return byRoute;
            var byMethod = string.Compare(a.HttpMethod, b.HttpMethod, StringComparison.OrdinalIgnoreCase);
            return byMethod != 0 ? byMethod : string.Compare(a.Name, b.Name, StringComparison.Ordinal);
        });
    }

    private static Dictionary<string, string> BuildConstStringLookup(
        List<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (file, scan, mask) in parsedFiles)
        {
            // FastEndpoints route constants are rare; avoid a regex pass over every
            // source file in large solutions. Every candidate contains `const`.
            if (!file.Code.Contains("const", StringComparison.Ordinal)) continue;

            foreach (var decl in scan.Classes)
            {
                if (decl.BodySpan.End <= decl.BodySpan.Start) continue;
                var bodyEnd = Math.Min(file.Code.Length, decl.BodySpan.End);
                foreach (Match m in ConstStringRegex().Matches(file.Code, decl.BodySpan.Start))
                {
                    if (m.Index >= bodyEnd) break;
                    if (!mask.IsCode(m.Index)) continue;
                    var constName = m.Groups["name"].Value;
                    var constVal = m.Groups["value"].Value;
                    map.TryAdd($"{decl.Name}.{constName}", constVal);
                    map.TryAdd($"{decl.FullName}.{constName}", constVal);
                }
            }
        }
        return map;
    }

    private static void DiscoverControllerEndpoints(
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

        foreach (var decl in scan.Classes)
        {
            if (decl.IsAbstract || decl.BodySpan.End <= decl.BodySpan.Start)
                continue;

            var isController = decl.Name.EndsWith("Controller", StringComparison.Ordinal) ||
                               decl.BaseRawNames.Any(b => b is "ControllerBase" or "Controller" or "ApiController" or "BaseController") ||
                               HasControllerAttribute(file.Code, decl.DeclarationIndex);
            if (!isController) continue;

            var classRoute = ExtractClassRoutePrefix(file.Code, decl);
            var controllerStem = ControllerStem(decl.Name);
            var bodyEnd = Math.Min(file.Code.Length, decl.BodySpan.End);

            foreach (Match m in ControllerActionRegex().Matches(file.Code, decl.BodySpan.Start))
            {
                if (m.Index >= bodyEnd) break;
                if (!mask.IsCode(m.Index)) continue;
                if (!ReferenceEquals(DeepSliceSyntax.InnermostClass(scan.Classes, m.Index), decl)) continue;

                var attrs = m.Groups["attrs"].Value;
                var verbMatch = HttpVerbAttributeRegex().Match(attrs);
                var routeMatch = RouteAttributeRegex().Match(attrs);
                if (!verbMatch.Success && !routeMatch.Success)
                    continue;

                var methodName = m.Groups["method"].Value;
                var parameters = m.Groups["params"].Value;
                var actionEnd = DeepSliceSyntax.FindMethodEnd(file.Code, mask, m.Index + m.Length, bodyEnd);
                var actionBody = actionEnd > m.Index + m.Length
                    ? file.Code[(m.Index + m.Length)..actionEnd]
                    : "";

                var matchedRequests = FindReferencedRequests(
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

                var fullRoute = CombineControllerRoute(classRoute, subRoute, controllerStem, methodName);
                var routeInferred = string.IsNullOrEmpty(classRoute) && string.IsNullOrEmpty(subRoute);
                var location = SourceLocation.Create(file.Path, result.RootPath, file.Code, m.Index);

                foreach (var req in matchedRequests)
                {
                    var verb = verbMatch.Success
                        ? verbMatch.Groups["verb"].Value.ToUpperInvariant()
                        : string.Equals(req.Kind, "Query", StringComparison.Ordinal) ? "GET" : "POST";

                    RecordEndpoint(
                        result,
                        req,
                        new EndpointInfo
                        {
                            Name = $"{decl.Name}.{methodName}",
                            HttpMethod = verb,
                            Route = fullRoute,
                            TargetRequestName = req.Name,
                            TargetRequestNamespace = req.Namespace,
                            ProjectName = file.ProjectName,
                            Location = location,
                            IsInferred = !verbMatch.Success || routeInferred,
                        });
                }
            }
        }
    }

    private static void DiscoverMinimalApiEndpoints(
        SourceFile file,
        CSharpScanner.FileScan scan,
        SourceMask mask,
        Dictionary<string, List<CqrsRequest>> requestsBySimple,
        AnalysisResult result)
    {
        // Every Minimal API form requires a `Map...` call. Skipping files without
        // one avoids three whole-file regex passes over domain/application modules.
        // Check the bare identifier so `app . MapGet(...)` is not missed.
        if (!file.Code.Contains("Map", StringComparison.Ordinal))
            return;

        var groupPrefixes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match gm in MapGroupRegex().Matches(file.Code))
        {
            if (!mask.IsCode(gm.Index)) continue;
            groupPrefixes[gm.Groups["var"].Value] = gm.Groups["prefix"].Value;
        }

        // Index local methods in the file so method-group Minimal APIs (e.g. groupBuilder.MapPost(CreateTodoItem)) can be resolved.
        var localMethods = new Dictionary<string, (string Params, string Body, int Index)>(StringComparer.Ordinal);
        foreach (Match mm in LocalMethodDeclarationRegex().Matches(file.Code))
        {
            if (!mask.IsCode(mm.Index)) continue;
            var mName = mm.Groups["method"].Value;
            if (mName is "Map" or "Configure" or "if" or "for" or "foreach" or "while" or "switch" or "using" or "catch")
                continue;
            var mParams = mm.Groups["params"].Value;
            var mEnd = DeepSliceSyntax.FindMethodEnd(file.Code, mask, mm.Index + mm.Length, file.Code.Length);
            var mBody = mEnd > mm.Index + mm.Length ? file.Code[(mm.Index + mm.Length)..mEnd] : "";
            localMethods.TryAdd(mName, (mParams, mBody, mm.Index));
        }

        // 1. Standard Minimal APIs: app.MapPost("/route", ...)
        foreach (Match m in MinimalApiMapRegex().Matches(file.Code))
        {
            if (!mask.IsCode(m.Index)) continue;

            var targetVar = m.Groups["target"].Value;
            var verb = m.Groups["verb"].Value.ToUpperInvariant();
            var route = m.Groups["route"].Value;
            var openParen = file.Code.IndexOf('(', m.Index);
            if (openParen < 0) continue;

            var closeParen = DeepSliceSyntax.FindMatchingParen(file.Code, mask, openParen);
            if (closeParen <= openParen) continue;

            var callContent = file.Code[(openParen + 1)..closeParen];
            var matchedRequests = FindReferencedRequests(
                callContent,
                callContent,
                requestsBySimple,
                file.ProjectName,
                scan.Usings);

            if (matchedRequests.Count == 0) continue;

            groupPrefixes.TryGetValue(targetVar, out var prefix);
            var fullRoute = CombineRoutes(prefix, route);
            var location = SourceLocation.Create(file.Path, result.RootPath, file.Code, m.Index);

            foreach (var req in matchedRequests)
            {
                RecordEndpoint(
                    result,
                    req,
                    new EndpointInfo
                    {
                        Name = $"Map{m.Groups["verb"].Value}",
                        HttpMethod = verb,
                        Route = fullRoute,
                        TargetRequestName = req.Name,
                        TargetRequestNamespace = req.Namespace,
                        ProjectName = file.ProjectName,
                        Location = location,
                    });
            }
        }

        // 2. Method-Group EndpointGroup Minimal APIs: groupBuilder.MapPost(CreateTodoItem) or groupBuilder.MapPut(UpdateTodoItem, "{id}")
        foreach (Match m in MethodGroupMinimalApiRegex().Matches(file.Code))
        {
            if (!mask.IsCode(m.Index)) continue;

            var handlerMethodName = m.Groups["handler"].Value;
            if (!localMethods.TryGetValue(handlerMethodName, out var targetMethod))
                continue;

            var matchedRequests = FindReferencedRequests(
                targetMethod.Params,
                targetMethod.Body,
                requestsBySimple,
                file.ProjectName,
                scan.Usings);

            if (matchedRequests.Count == 0) continue;

            var verb = m.Groups["verb"].Value.ToUpperInvariant();
            var subRoute = m.Groups["route"].Success ? m.Groups["route"].Value : "";
            var enclosingClass = DeepSliceSyntax.InnermostClass(scan.Classes, m.Index);
            var targetVar = m.Groups["target"].Value;
            var routeInferred = false;
            if (!groupPrefixes.TryGetValue(targetVar, out var prefix) && enclosingClass is not null)
            {
                // EndpointGroupBase convention (`app.MapGroup(this)` => /api/{ClassName}); not read from source.
                prefix = $"/api/{enclosingClass.Name}";
                routeInferred = true;
            }

            var fullRoute = CombineRoutes(prefix, subRoute);
            var endpointName = enclosingClass is not null
                ? $"{enclosingClass.Name}.{handlerMethodName}"
                : handlerMethodName;
            var location = SourceLocation.Create(file.Path, result.RootPath, file.Code, targetMethod.Index);

            foreach (var req in matchedRequests)
            {
                RecordEndpoint(
                    result,
                    req,
                    new EndpointInfo
                    {
                        Name = endpointName,
                        HttpMethod = verb,
                        Route = fullRoute,
                        TargetRequestName = req.Name,
                        TargetRequestNamespace = req.Namespace,
                        ProjectName = file.ProjectName,
                        Location = location,
                        IsInferred = routeInferred,
                    });
            }
        }
    }

    private static void DiscoverFastEndpoints(
        SourceFile file,
        CSharpScanner.FileScan scan,
        SourceMask mask,
        Dictionary<string, List<CqrsRequest>> requestsBySimple,
        Dictionary<string, string> constStrings,
        AnalysisResult result)
    {
        foreach (var decl in scan.Classes)
        {
            if (decl.IsAbstract || decl.BodySpan.End <= decl.BodySpan.Start)
                continue;

            // FastEndpoints declarations are the only classes that can match here.
            // Checking the base name first avoids two base-list parses plus a body
            // substring allocation for every unrelated class.
            if (!decl.BaseRawNames.Any(b => b.StartsWith("Endpoint", StringComparison.Ordinal)))
                continue;

            var endpointArgs = CSharpScanner.ExtractAllGenericArguments(decl.BaseList, "Endpoint")
                .Concat(CSharpScanner.ExtractAllGenericArguments(decl.BaseList, "EndpointWithoutRequest"))
                .FirstOrDefault();

            var bodyEnd = Math.Min(file.Code.Length, decl.BodySpan.End);
            var body = file.Code[decl.BodySpan.Start..bodyEnd];

            var matchedRequests = new List<CqrsRequest>();
            if (endpointArgs is { Count: >= 1 })
            {
                var reqSimple = CSharpScanner.UnqualifiedTypeName(endpointArgs[0]);
                if (requestsBySimple.TryGetValue(reqSimple, out var hits))
                {
                    var picked = DeepSliceCandidateResolver.PickRequest(hits, file.ProjectName, scan.Usings);
                    if (picked is not null)
                        matchedRequests.Add(picked);
                }
            }

            if (matchedRequests.Count == 0 && decl.BaseRawNames.Any(b => b.StartsWith("Endpoint", StringComparison.Ordinal)))
            {
                matchedRequests.AddRange(FindReferencedRequests("", body, requestsBySimple, file.ProjectName, scan.Usings));
            }

            if (matchedRequests.Count == 0) continue;

            var verbMatch = FastEndpointVerbRegex().Match(body);
            if (!verbMatch.Success || !mask.IsCode(decl.BodySpan.Start + verbMatch.Index))
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
                var constRef = verbMatch.Groups["constRef"].Value;
                routeInferred = !constStrings.TryGetValue(constRef, out var resolvedConst);
                // Do not turn an unresolved symbol into a fake literal route. An empty route
                // renders as just the HTTP verb and is marked inferred.
                rawRoute = routeInferred ? "" : resolvedConst!;
            }

            var route = routeInferred && rawRoute.Length == 0 ? "" : NormalizeRoute(rawRoute);
            var location = SourceLocation.Create(file.Path, result.RootPath, file.Code, decl.BodySpan.Start);

            foreach (var req in matchedRequests)
            {
                RecordEndpoint(
                    result,
                    req,
                    new EndpointInfo
                    {
                        Name = decl.Name,
                        HttpMethod = verb,
                        Route = route,
                        TargetRequestName = req.Name,
                        TargetRequestNamespace = req.Namespace,
                        ProjectName = file.ProjectName,
                        Location = location,
                        IsInferred = routeInferred,
                    });
            }
        }
    }

    private static void RecordEndpoint(AnalysisResult result, CqrsRequest request, EndpointInfo endpoint)
    {
        endpoint.TargetRequestFullName ??= request.FullName;
        if (!result.Endpoints.Any(e =>
                string.Equals(e.HttpMethod, endpoint.HttpMethod, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(e.Route, endpoint.Route, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(e.TargetRequestName, endpoint.TargetRequestName, StringComparison.Ordinal) &&
                string.Equals(e.TargetRequestNamespace, endpoint.TargetRequestNamespace, StringComparison.Ordinal) &&
                string.Equals(e.Name, endpoint.Name, StringComparison.Ordinal)))
        {
            result.Endpoints.Add(endpoint);
        }

        var label = endpoint.IsInferred
            ? $"{endpoint.DisplayLabel} _(inferred)_"
            : endpoint.DisplayLabel;
        if (!request.Entrypoints.Contains(label, StringComparer.Ordinal))
            request.Entrypoints.Add(label);
    }

    private static List<CqrsRequest> FindReferencedRequests(
        string parametersSection,
        string bodySection,
        Dictionary<string, List<CqrsRequest>> requestsBySimple,
        string? projectName,
        IReadOnlyList<UsingDirective>? usings)
    {
        var results = new List<CqrsRequest>();

        void TryAdd(string typeRef)
        {
            var simple = CSharpScanner.UnqualifiedTypeName(typeRef);
            if (!requestsBySimple.TryGetValue(simple, out var hits))
                return;
            var picked = DeepSliceCandidateResolver.PickRequest(hits, projectName, usings);
            if (picked is not null && !results.Contains(picked))
                results.Add(picked);
        }

        // 1. Check parameter types
        if (!string.IsNullOrWhiteSpace(parametersSection))
        {
            foreach (var rawParam in CSharpScanner.SplitTopLevel(parametersSection))
            {
                var paramType = DeepSliceSyntax.ExtractParameterType(rawParam);
                if (!string.IsNullOrEmpty(paramType))
                    TryAdd(paramType);
            }
        }

        // 2. Check parameter patterns and object creations `new RequestType(` or `new RequestType {`
        foreach (Match m in ParameterTypeRegex().Matches(bodySection))
            TryAdd(m.Groups["type"].Value);
        foreach (Match m in ObjectCreationRegex().Matches(bodySection))
            TryAdd(m.Groups["type"].Value);

        // 3. Check generic Send<RequestType> / InvokeAsync<RequestType>
        foreach (Match m in GenericDispatchRegex().Matches(bodySection))
            TryAdd(m.Groups["type"].Value);

        return results;
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

    private static string ExtractClassRoutePrefix(string code, CSharpScanner.ClassDecl decl)
    {
        var start = Math.Max(0, decl.DeclarationIndex - 500);
        var prefix = code[start..decl.DeclarationIndex];
        var lastBoundary = prefix.LastIndexOfAny(new[] { ';', '}' });
        if (lastBoundary >= 0)
            prefix = prefix[(lastBoundary + 1)..];

        var match = RouteAttributeRegex().Match(prefix);
        return match.Success ? match.Groups["route"].Value : "";
    }

    private static string ControllerStem(string className)
    {
        var stem = className.EndsWith("Controller", StringComparison.OrdinalIgnoreCase) && className.Length > "Controller".Length
            ? className[..^"Controller".Length]
            : className;
        return stem.ToLowerInvariant();
    }

    private static string CombineControllerRoute(string classRoute, string methodRoute, string controllerStem, string actionName)
    {
        string ReplaceTokens(string template)
        {
            var s = Regex.Replace(template, @"\[controller\]", controllerStem, RegexOptions.IgnoreCase);
            return Regex.Replace(s, @"\[action\]", actionName.ToLowerInvariant(), RegexOptions.IgnoreCase);
        }

        var prefix = ReplaceTokens(classRoute.Trim());
        var sub = ReplaceTokens(methodRoute.Trim());

        if (sub.StartsWith('/') || sub.StartsWith("~/"))
            return NormalizeRoute(sub.TrimStart('~'));

        if (string.IsNullOrEmpty(prefix) && string.IsNullOrEmpty(sub))
            return $"/{controllerStem}";

        return CombineRoutes(prefix, sub);
    }

    private static string CombineRoutes(string? prefix, string? route)
    {
        var p = (prefix ?? "").Trim().Trim('/');
        var r = (route ?? "").Trim().Trim('/');
        if (string.IsNullOrEmpty(p) && string.IsNullOrEmpty(r))
            return "/";
        if (string.IsNullOrEmpty(p))
            return "/" + r;
        if (string.IsNullOrEmpty(r))
            return "/" + p;
        return $"/{p}/{r}";
    }

    private static string NormalizeRoute(string route)
    {
        var trimmed = route.Trim();
        if (string.IsNullOrEmpty(trimmed)) return "/";
        return trimmed.StartsWith('/') ? trimmed : "/" + trimmed;
    }
}
