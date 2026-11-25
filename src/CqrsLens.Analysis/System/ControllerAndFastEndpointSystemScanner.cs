using System.Text.RegularExpressions;

namespace CqrsLens;

/// <summary>
/// Discovers all ASP.NET Core MVC controller actions (<c>[Http*]</c>/<c>[Route]</c>) and
/// FastEndpoints subclasses across non-test projects, whether or not they dispatch CQRS requests.
/// </summary>
internal static partial class ControllerAndFastEndpointSystemScanner
{
    [GeneratedRegex(
        @"\[Http(?<verb>Get|Post|Put|Delete|Patch)(?:\s*\(\s*@?""(?<route>[^""]*)""\s*\))?\s*\]",
        RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex HttpVerbAttrRegex();

    [GeneratedRegex(
        @"\[NonAction(?:\s*\(\s*\))?\]",
        RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex NonActionAttrRegex();

    internal static void Discover(SystemAnalysisContext context, List<SystemEntrypointInfo> target)
    {
        Dictionary<string, string>? constStrings = null;
        Dictionary<string, string> GetConstStrings() => constStrings ??= EndpointRequestMatcher.BuildConstStringLookup(
            context.ParsedFiles as List<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)>
            ?? context.ParsedFiles.ToList());

        var controllerCache = new Dictionary<CSharpScanner.ClassDecl, bool>();

        foreach (var (file, scan, mask) in context.ParsedFiles)
        {
            if (context.IsTestProject(file.ProjectName))
                continue;

            DiscoverControllers(context, file, scan, mask, controllerCache, target);
            DiscoverFastEndpoints(context, file, scan, mask, GetConstStrings, target);
        }
    }

    private static void DiscoverControllers(
        SystemAnalysisContext context,
        SourceFile file,
        CSharpScanner.FileScan scan,
        SourceMask mask,
        Dictionary<CSharpScanner.ClassDecl, bool> controllerCache,
        List<SystemEntrypointInfo> target)
    {
        if (!file.Code.Contains("Controller", StringComparison.Ordinal) &&
            !file.Code.Contains("[Http", StringComparison.Ordinal) &&
            !file.Code.Contains("[Route", StringComparison.Ordinal))
        {
            return;
        }

        foreach (var decl in scan.Classes)
        {
            if (decl.IsAbstract || decl.BodySpan.End <= decl.BodySpan.Start)
                continue;

            var isController = decl.Name.EndsWith("Controller", StringComparison.Ordinal) ||
                               InheritsControllerBase(context, decl, controllerCache) ||
                               HasControllerAttribute(file.Code, decl.DeclarationIndex);
            if (!isController)
                continue;

            var classRoute = ExtractClassRoutePrefix(context, file.Code, mask, decl);
            var controllerStem = ControllerStem(decl.Name);
            var bodyEnd = Math.Min(file.Code.Length, decl.BodySpan.End);

            MatchCollection actionMatches;
            try
            {
                actionMatches = ApiEndpointPatterns.ControllerAction().Matches(file.Code, decl.BodySpan.Start);
            }
            catch (RegexMatchTimeoutException)
            {
                continue;
            }

            foreach (Match match in actionMatches)
            {
                if (match.Index >= bodyEnd)
                    break;
                if (!mask.IsCode(match.Index))
                    continue;
                if (!ReferenceEquals(DeepSliceSyntax.InnermostClass(scan.Classes, match.Index), decl))
                    continue;

                var attributes = match.Groups["attrs"].Value;
                MatchCollection verbMatches;
                Match routeMatch;
                try
                {
                    if (NonActionAttrRegex().IsMatch(attributes))
                        continue;

                    verbMatches = HttpVerbAttrRegex().Matches(attributes);
                    routeMatch = ApiEndpointPatterns.RouteAttribute().Match(attributes);
                }
                catch (RegexMatchTimeoutException)
                {
                    continue;
                }
                if (verbMatches.Count == 0 && !routeMatch.Success)
                    continue;

                var methodName = match.Groups["method"].Value;
                var openParen = match.Index + match.Length - 1;
                var closeParen = DeepSliceSyntax.FindMatchingParen(file.Code, mask, openParen);
                if (closeParen < 0 || closeParen >= bodyEnd)
                    continue;

                var actionParams = mask.CodeOnly(openParen + 1, Math.Max(0, closeParen - openParen - 1));
                var actionStart = closeParen + 1;
                var actionEnd = DeepSliceSyntax.FindMethodEnd(file.Code, mask, actionStart, bodyEnd);
                var actionBody = mask.CodeOnly(actionStart, Math.Max(0, actionEnd - actionStart));

                var matchedRequests = context.RequestsBySimple.Count > 0
                    ? EndpointRequestMatcher.FindReferencedRequests(
                        actionParams,
                        actionBody,
                        context.RequestsBySimple,
                        file.ProjectName,
                        scan.Usings,
                        decl.Namespace,
                        decl.ContainingTypes)
                    : new List<CqrsRequest>();

                var location = SourceLocation.Create(file.Path, context.Result.RootPath, file.Code, match.Index);

                if (verbMatches.Count > 0)
                {
                    foreach (Match vm in verbMatches)
                    {
                        var verb = vm.Groups["verb"].Value.ToUpperInvariant();
                        var subRoute = vm.Groups["route"].Success
                            ? vm.Groups["route"].Value
                            : (routeMatch.Success ? routeMatch.Groups["route"].Value : string.Empty);
                        EmitControllerEntrypoint(
                            context, file, scan, mask, decl, methodName, verb,
                            classRoute, subRoute, controllerStem, routeInferred: string.IsNullOrEmpty(classRoute) && string.IsNullOrEmpty(subRoute),
                            actionParams, actionBody, actionStart, actionEnd, location, matchedRequests, target);
                    }
                }
                else
                {
                    var subRoute = routeMatch.Groups["route"].Value;
                    var verb = InferVerbFromRequestsOrName(matchedRequests, methodName);
                    EmitControllerEntrypoint(
                        context, file, scan, mask, decl, methodName, verb,
                        classRoute, subRoute, controllerStem, routeInferred: true,
                        actionParams, actionBody, actionStart, actionEnd, location, matchedRequests, target);
                }
            }
        }
    }

    private static void EmitControllerEntrypoint(
        SystemAnalysisContext context,
        SourceFile file,
        CSharpScanner.FileScan scan,
        SourceMask mask,
        CSharpScanner.ClassDecl decl,
        string methodName,
        string verb,
        string classRoute,
        string subRoute,
        string controllerStem,
        bool routeInferred,
        string actionParams,
        string actionBody,
        int actionStart,
        int actionEnd,
        SourceLocation? location,
        List<CqrsRequest> matchedRequests,
        List<SystemEntrypointInfo> target)
    {
        var fullRoute = EndpointRoutes.ForController(classRoute, subRoute, controllerStem, methodName);
        var entrypoint = new SystemEntrypointInfo
        {
            Name = $"{decl.Name}.{methodName}",
            Kind = SystemEntrypointKind.Http,
            ExecutionStyle = matchedRequests.Count > 0
                ? SystemExecutionStyle.Cqrs
                : SystemExecutionStyle.Direct,
            Method = verb,
            RouteOrTrigger = fullRoute,
            ProjectName = file.ProjectName,
            Location = location,
            IsInferred = routeInferred,
        };

        SystemDependencyTracer.Trace(
            context,
            file,
            scan,
            mask,
            decl,
            actionParams,
            actionBody,
            actionStart,
            actionEnd,
            entrypoint,
            matchedRequests);

        target.Add(entrypoint);
    }

    private static void DiscoverFastEndpoints(
        SystemAnalysisContext context,
        SourceFile file,
        CSharpScanner.FileScan scan,
        SourceMask mask,
        Func<Dictionary<string, string>> getConstStrings,
        List<SystemEntrypointInfo> target)
    {
        foreach (var decl in scan.Classes)
        {
            if (decl.IsAbstract || decl.BodySpan.End <= decl.BodySpan.Start)
                continue;

            if (!decl.BaseRawNames.Any(IsFastEndpointBase))
                continue;

            var bodyEnd = Math.Min(file.Code.Length, decl.BodySpan.End);
            var body = file.Code[decl.BodySpan.Start..bodyEnd];

            Match verbMatch;
            try
            {
                verbMatch = ApiEndpointPatterns.FastEndpointVerb().Match(body);
            }
            catch (RegexMatchTimeoutException)
            {
                continue;
            }

            if (!verbMatch.Success || !mask.IsCode(decl.BodySpan.Start + verbMatch.Index))
                continue;

            var matchedRequests = new List<CqrsRequest>();
            if (context.RequestsBySimple.Count > 0)
            {
                var endpointArguments = CSharpScanner.ExtractAllGenericArguments(decl.BaseList, "Endpoint")
                    .Concat(CSharpScanner.ExtractAllGenericArguments(decl.BaseList, "EndpointWithoutRequest"))
                    .Concat(CSharpScanner.ExtractAllGenericArguments(decl.BaseList, "EndpointWithMapper"))
                    .FirstOrDefault();

                if (endpointArguments is { Count: >= 1 })
                {
                    var picked = EndpointRequestMatcher.ResolveRequest(
                        endpointArguments[0], context.RequestsBySimple, file.ProjectName, scan.Usings, decl.Namespace);
                    if (picked is not null)
                        matchedRequests.Add(picked);
                }

                if (matchedRequests.Count == 0)
                {
                    matchedRequests.AddRange(EndpointRequestMatcher.FindReferencedRequests(
                        string.Empty,
                        mask.CodeOnly(decl.BodySpan.Start, body.Length),
                        context.RequestsBySimple,
                        file.ProjectName,
                        scan.Usings,
                        decl.Namespace));
                }
            }

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
                routeInferred = !getConstStrings().TryGetValue(constRef, out var resolvedConst);
                rawRoute = routeInferred ? string.Empty : resolvedConst!;
            }

            var route = routeInferred && rawRoute.Length == 0 ? string.Empty : EndpointRoutes.Normalize(rawRoute);
            var maskedBody = mask.CodeOnly(decl.BodySpan.Start, body.Length);

            var entrypoint = new SystemEntrypointInfo
            {
                Name = decl.Name,
                Kind = SystemEntrypointKind.Http,
                ExecutionStyle = matchedRequests.Count > 0
                    ? SystemExecutionStyle.Cqrs
                    : SystemExecutionStyle.Direct,
                Method = verb,
                RouteOrTrigger = route,
                ProjectName = file.ProjectName,
                Location = SourceLocation.Create(file.Path, context.Result.RootPath, file.Code, decl.BodySpan.Start),
                IsInferred = routeInferred,
            };

            SystemDependencyTracer.Trace(
                context,
                file,
                scan,
                mask,
                decl,
                decl.PrimaryConstructorParameters ?? string.Empty,
                maskedBody,
                decl.BodySpan.Start,
                bodyEnd,
                entrypoint,
                matchedRequests);

            target.Add(entrypoint);
        }
    }

    private static bool IsFastEndpointBase(string baseName)
        => baseName is "Endpoint" or "EndpointWithoutRequest" or "EndpointWithMapper";

    private static bool InheritsControllerBase(
        SystemAnalysisContext context,
        CSharpScanner.ClassDecl decl,
        Dictionary<CSharpScanner.ClassDecl, bool> cache)
    {
        if (cache.TryGetValue(decl, out var cached))
            return cached;

        cache[decl] = false;
        if (decl.BaseRawNames.Any(b => b is "ControllerBase" or "Controller" or "ApiController" or "BaseController"))
        {
            cache[decl] = true;
            return true;
        }

        foreach (var baseName in decl.BaseTypeNames)
        {
            var simple = CSharpScanner.SimpleName(baseName);
            if (!context.ClassesBySimpleName.ContainsKey(simple))
                continue;

            var resolved = context.Hierarchy.TryResolveClass(
                baseName, decl.Namespace, decl.Usings, decl.ContainingTypes, 0, out _);
            if (resolved is not null && InheritsControllerBase(context, resolved, cache))
            {
                cache[decl] = true;
                return true;
            }
        }

        return false;
    }

    private static bool HasControllerAttribute(string code, int declarationIndex)
    {
        var start = Math.Max(0, declarationIndex - 400);
        var prefix = code[start..declarationIndex];
        var lastBoundary = prefix.LastIndexOfAny([';', '}']);
        if (lastBoundary >= 0)
            prefix = prefix[(lastBoundary + 1)..];
        return prefix.Contains("ApiController", StringComparison.Ordinal) ||
               prefix.Contains("[Controller", StringComparison.Ordinal);
    }

    private static string ExtractClassRoutePrefix(
        SystemAnalysisContext context,
        string code,
        SourceMask mask,
        CSharpScanner.ClassDecl declaration)
    {
        var route = ExtractRouteAttributeFromDeclaration(code, mask, declaration);
        if (route.Length > 0)
            return route;

        foreach (var baseName in declaration.BaseTypeNames)
        {
            var baseDecl = context.Hierarchy.TryResolveClass(
                baseName, declaration.Namespace, declaration.Usings, declaration.ContainingTypes, 0, out _);
            if (baseDecl is not null && context.FileByClass.TryGetValue(baseDecl, out var baseEntry))
            {
                var baseRoute = ExtractRouteAttributeFromDeclaration(baseEntry.File.Code, baseEntry.Mask, baseDecl);
                if (baseRoute.Length > 0)
                    return baseRoute;
            }
        }

        return string.Empty;
    }

    private static string ExtractRouteAttributeFromDeclaration(
        string code,
        SourceMask mask,
        CSharpScanner.ClassDecl declaration)
    {
        var start = Math.Max(0, declaration.DeclarationIndex - 500);
        for (var index = declaration.DeclarationIndex - 1; index >= start; index--)
        {
            if (mask.IsCode(index) && code[index] is ';' or '}')
            {
                start = index + 1;
                break;
            }
        }

        var prefix = code[start..declaration.DeclarationIndex];
        try
        {
            foreach (Match match in ApiEndpointPatterns.RouteAttribute().Matches(prefix))
            {
                if (mask.IsCode(start + match.Index))
                    return match.Groups["route"].Value;
            }
        }
        catch (RegexMatchTimeoutException)
        {
        }
        return string.Empty;
    }

    private static string InferVerbFromRequestsOrName(List<CqrsRequest> matchedRequests, string methodName)
    {
        if (matchedRequests.Count > 0)
            return string.Equals(matchedRequests[0].Kind, "Query", StringComparison.Ordinal) ? "GET" : "POST";

        if (methodName.StartsWith("Get", StringComparison.OrdinalIgnoreCase) ||
            methodName.StartsWith("List", StringComparison.OrdinalIgnoreCase) ||
            methodName.StartsWith("Find", StringComparison.OrdinalIgnoreCase) ||
            methodName.StartsWith("Search", StringComparison.OrdinalIgnoreCase) ||
            methodName.StartsWith("Index", StringComparison.OrdinalIgnoreCase) ||
            methodName.StartsWith("Details", StringComparison.OrdinalIgnoreCase))
            return "GET";

        if (methodName.StartsWith("Delete", StringComparison.OrdinalIgnoreCase) ||
            methodName.StartsWith("Remove", StringComparison.OrdinalIgnoreCase))
            return "DELETE";

        if (methodName.StartsWith("Put", StringComparison.OrdinalIgnoreCase) ||
            methodName.StartsWith("Update", StringComparison.OrdinalIgnoreCase) ||
            methodName.StartsWith("Edit", StringComparison.OrdinalIgnoreCase))
            return "PUT";

        if (methodName.StartsWith("Patch", StringComparison.OrdinalIgnoreCase))
            return "PATCH";

        return "POST";
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
