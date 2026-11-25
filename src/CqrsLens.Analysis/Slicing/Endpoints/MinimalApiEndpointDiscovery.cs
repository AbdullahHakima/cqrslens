using System.Text.RegularExpressions;

namespace CqrsLens;

/// <summary>Discovers minimal API endpoints, including EndpointGroup method groups.</summary>
internal static class MinimalApiEndpointDiscovery
{
    public static void Discover(
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

        var groupPrefixes = IndexGroupPrefixes(file, mask);
        var localMethods = IndexLocalMethods(file, mask);

        DiscoverMapCalls(file, scan, mask, requestsBySimple, groupPrefixes, localMethods, result);
        DiscoverMethodGroups(file, scan, mask, requestsBySimple, groupPrefixes, localMethods, result);
    }

    private static Dictionary<string, string> IndexGroupPrefixes(SourceFile file, SourceMask mask)
    {
        var groupPrefixes = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (Match match in ApiEndpointPatterns.MapGroup().Matches(file.Code))
            {
                if (!mask.IsCode(match.Index)) continue;
                groupPrefixes[match.Groups["var"].Value] = match.Groups["prefix"].Value;
            }
        }
        catch (RegexMatchTimeoutException)
        {
        }
        return groupPrefixes;
    }

    private static Dictionary<string, (string Params, string Body, int Index)> IndexLocalMethods(
        SourceFile file,
        SourceMask mask)
    {
        // Index local methods so method-group Minimal APIs (e.g.
        // groupBuilder.MapPost(CreateTodoItem) or api.MapPost("/route", CreateOrderAsync)) resolve.
        var localMethods = new Dictionary<string, (string Params, string Body, int Index)>(StringComparer.Ordinal);
        try
        {
            foreach (Match match in ApiEndpointPatterns.LocalMethodDeclaration().Matches(file.Code))
            {
                if (!mask.IsCode(match.Index)) continue;
                var name = match.Groups["method"].Value;
                if (name is "Map" or "Configure" or "if" or "for" or "foreach" or "while" or "switch" or "using" or "catch")
                    continue;
                var ret = match.Groups["ret"].Value;
                if (ret.Contains("class", StringComparison.Ordinal) ||
                    ret.Contains("record", StringComparison.Ordinal) ||
                    ret.Contains("struct", StringComparison.Ordinal) ||
                    ret.Contains("interface", StringComparison.Ordinal))
                    continue;

                var openParen = match.Index + match.Length - 1;
                var closeParen = DeepSliceSyntax.FindMatchingParen(file.Code, mask, openParen);
                if (closeParen < 0)
                    continue;

                var parameters = mask.CodeOnly(openParen + 1, Math.Max(0, closeParen - openParen - 1));
                var start = closeParen + 1;
                var end = DeepSliceSyntax.FindMethodEnd(file.Code, mask, start, file.Code.Length);
                var body = mask.CodeOnly(start, Math.Max(0, end - start));
                localMethods.TryAdd(name, (parameters, body, match.Index));
            }
        }
        catch (RegexMatchTimeoutException)
        {
        }
        return localMethods;
    }

    private static void DiscoverMapCalls(
        SourceFile file,
        CSharpScanner.FileScan scan,
        SourceMask mask,
        Dictionary<string, List<CqrsRequest>> requestsBySimple,
        Dictionary<string, string> groupPrefixes,
        Dictionary<string, (string Params, string Body, int Index)> localMethods,
        AnalysisResult result)
    {
        // Standard Minimal APIs: app.MapPost("/route", ...) and route-first method groups: api.MapPost("/route", HandlerAsync)
        foreach (Match match in ApiEndpointPatterns.MinimalApiMap().Matches(file.Code))
        {
            if (!mask.IsCode(match.Index)) continue;

            var targetVariable = match.Groups["target"].Value;
            var verb = match.Groups["verb"].Value.ToUpperInvariant();
            var route = match.Groups["route"].Value;
            var openParen = file.Code.IndexOf('(', match.Index);
            if (openParen < 0) continue;

            var closeParen = DeepSliceSyntax.FindMatchingParen(file.Code, mask, openParen);
            if (closeParen <= openParen) continue;

            var callContent = mask.CodeOnly(openParen + 1, closeParen - openParen - 1);
            var enclosingClass = DeepSliceSyntax.InnermostClass(scan.Classes, match.Index);
            var matchedRequests = EndpointRequestMatcher.FindReferencedRequests(
                callContent,
                callContent,
                requestsBySimple,
                file.ProjectName,
                scan.Usings,
                enclosingClass?.Namespace,
                enclosingClass?.ContainingTypes);

            var endpointName = $"Map{match.Groups["verb"].Value}";
            var locationIndex = match.Index;

            if (matchedRequests.Count == 0 && localMethods.Count > 0)
            {
                // callContent has string literals blanked by SourceMask.CodeOnly, which drops a bare
                // route literal from CSharpScanner.SplitTopLevel; slice after the matched route instead.
                var afterRouteStart = match.Index + match.Length;
                if (afterRouteStart < closeParen)
                {
                    var maskedAfterRoute = mask.CodeOnly(afterRouteStart, closeParen - afterRouteStart);
                    if (maskedAfterRoute.Contains(',', StringComparison.Ordinal))
                    {
                        var afterArgs = CSharpScanner.SplitTopLevel(maskedAfterRoute);
                        if (afterArgs.Count >= 1)
                        {
                            var rawHandlerArg = afterArgs[^1].Trim();
                            var dot = rawHandlerArg.LastIndexOf('.');
                            var handlerMethodName = dot >= 0 ? rawHandlerArg[(dot + 1)..].Trim() : rawHandlerArg;
                            if (localMethods.TryGetValue(handlerMethodName, out var targetMethod))
                            {
                                var targetOwner = DeepSliceSyntax.InnermostClass(scan.Classes, targetMethod.Index);
                                matchedRequests = EndpointRequestMatcher.FindReferencedRequests(
                                    targetMethod.Params,
                                    targetMethod.Body,
                                    requestsBySimple,
                                    file.ProjectName,
                                    scan.Usings,
                                    targetOwner?.Namespace ?? enclosingClass?.Namespace,
                                    targetOwner?.ContainingTypes ?? enclosingClass?.ContainingTypes);
                                if (matchedRequests.Count > 0)
                                {
                                    var ownerName = (targetOwner ?? enclosingClass)?.Name;
                                    endpointName = ownerName is not null ? $"{ownerName}.{handlerMethodName}" : handlerMethodName;
                                    locationIndex = targetMethod.Index;
                                }
                            }
                        }
                    }
                }
            }

            if (matchedRequests.Count == 0) continue;

            groupPrefixes.TryGetValue(targetVariable, out var prefix);
            var fullRoute = EndpointRoutes.Combine(prefix, route);
            var location = SourceLocation.Create(file.Path, result.RootPath, file.Code, locationIndex);

            foreach (var request in matchedRequests)
            {
                EndpointRecorder.Record(
                    result,
                    request,
                    new EndpointInfo
                    {
                        Name = endpointName,
                        HttpMethod = verb,
                        Route = fullRoute,
                        TargetRequestName = request.Name,
                        TargetRequestNamespace = request.Namespace,
                        ProjectName = file.ProjectName,
                        Location = location,
                    });
            }
        }
    }

    private static void DiscoverMethodGroups(
        SourceFile file,
        CSharpScanner.FileScan scan,
        SourceMask mask,
        Dictionary<string, List<CqrsRequest>> requestsBySimple,
        Dictionary<string, string> groupPrefixes,
        Dictionary<string, (string Params, string Body, int Index)> localMethods,
        AnalysisResult result)
    {
        // Method-Group EndpointGroup Minimal APIs: groupBuilder.MapPost(CreateTodoItem)
        // or groupBuilder.MapPut(UpdateTodoItem, "{id}")
        foreach (Match match in ApiEndpointPatterns.MethodGroupMinimalApi().Matches(file.Code))
        {
            if (!mask.IsCode(match.Index)) continue;

            var handlerMethodName = match.Groups["handler"].Value;
            if (!localMethods.TryGetValue(handlerMethodName, out var targetMethod))
                continue;

            var matchedRequests = EndpointRequestMatcher.FindReferencedRequests(
                targetMethod.Params,
                targetMethod.Body,
                requestsBySimple,
                file.ProjectName,
                scan.Usings,
                DeepSliceSyntax.InnermostClass(scan.Classes, targetMethod.Index)?.Namespace);

            if (matchedRequests.Count == 0) continue;

            var verb = match.Groups["verb"].Value.ToUpperInvariant();
            var subRoute = match.Groups["route"].Success ? match.Groups["route"].Value : "";
            var enclosingClass = DeepSliceSyntax.InnermostClass(scan.Classes, match.Index);
            var targetVariable = match.Groups["target"].Value;
            var routeInferred = false;
            if (!groupPrefixes.TryGetValue(targetVariable, out var prefix) && enclosingClass is not null)
            {
                // EndpointGroupBase convention (`app.MapGroup(this)` => /api/{ClassName}); not read from source.
                prefix = $"/api/{enclosingClass.Name}";
                routeInferred = true;
            }

            var fullRoute = EndpointRoutes.Combine(prefix, subRoute);
            var endpointName = enclosingClass is not null
                ? $"{enclosingClass.Name}.{handlerMethodName}"
                : handlerMethodName;
            var location = SourceLocation.Create(
                file.Path, result.RootPath, file.Code, targetMethod.Index);

            foreach (var request in matchedRequests)
            {
                EndpointRecorder.Record(
                    result,
                    request,
                    new EndpointInfo
                    {
                        Name = endpointName,
                        HttpMethod = verb,
                        Route = fullRoute,
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
