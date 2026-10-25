using System.Text.RegularExpressions;

namespace CqrsLens;

/// <summary>
/// Enriches linked CQRS shapes with deep vertical-slice metadata:
/// API entrypoints (Controllers, Minimal APIs, FastEndpoints), request payload properties,
/// handler constructor dependencies, touched EF Core DbSets/entities, and published notifications.
/// Runs after <see cref="Analyzer"/> shape discovery and linking so both text and Roslyn modes
/// produce identical deep slice traces.
/// </summary>
internal static partial class DeepSliceAnalyzer
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

    [GeneratedRegex(@"\b(?<verb>Get|Post|Put|Delete|Patch)\s*\(\s*@?""(?<route>[^""]*)""\s*\)")]
    private static partial Regex FastEndpointVerbRegex();

    [GeneratedRegex(@"\.\s*(?<prop>[A-Za-z_]\w*)\b")]
    private static partial Regex MemberAccessRegex();

    [GeneratedRegex(@"\.\s*Set\s*<\s*(?<entity>[A-Za-z_]\w*(?:\s*\.\s*[A-Za-z_]\w*)*)\s*>\s*\(")]
    private static partial Regex DbContextSetMethodRegex();

    [GeneratedRegex(@"\bnew\s+(?<type>(?:global::)?[A-Za-z_]\w*(?:\s*\.\s*[A-Za-z_]\w*)*)\s*[\(\{]")]
    private static partial Regex ObjectCreationRegex();

    [GeneratedRegex(@"\.\s*(?:Publish|Send|InvokeAsync)\s*<\s*(?<type>(?:global::)?[A-Za-z_]\w*(?:\s*\.\s*[A-Za-z_]\w*)*)\s*[,>]")]
    private static partial Regex GenericDispatchRegex();

    [GeneratedRegex(@"\b(?<type>(?:global::)?[A-Za-z_]\w*(?:\s*\.\s*[A-Za-z_]\w*)*)\s+@?[A-Za-z_]\w*\s*[,)=]")]
    private static partial Regex ParameterTypeRegex();

    public static void Analyze(IReadOnlyList<SourceFile> files, AnalysisResult result)
    {
        if (files.Count == 0 || result.Requests.Count == 0)
            return;

        var parsedFiles = new List<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)>(files.Count);
        foreach (var file in files)
        {
            try
            {
                var mask = SourceMask.Build(file.Code);
                var scan = CSharpScanner.ScanText(file.Code);
                parsedFiles.Add((file, scan, mask));
            }
            catch
            {
                // Ignore files that failed to parse; already recorded by main scanner.
            }
        }

        PopulateRequestProperties(parsedFiles, result);
        PopulateHandlerDeepSlice(parsedFiles, result);
        DiscoverApiEntrypoints(parsedFiles, result);
    }

    private static void PopulateRequestProperties(
        List<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        AnalysisResult result)
    {
        var requestsByFull = result.Requests
            .GroupBy(r => r.FullName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        foreach (var (file, scan, _) in parsedFiles)
        {
            foreach (var decl in scan.Classes)
            {
                if (!requestsByFull.TryGetValue(decl.FullName, out var candidates))
                    continue;

                var request = PickCandidate(candidates, file.ProjectName);
                if (request is null) continue;

                foreach (var prop in decl.Properties)
                {
                    if (!prop.IsPublic || prop.IsStatic) continue;
                    if (request.Properties.Any(p => string.Equals(p.Name, prop.Name, StringComparison.Ordinal)))
                        continue;
                    request.Properties.Add(new RequestPropertyInfo(prop.Name, prop.Type));
                }
            }
        }
    }

    private static void PopulateHandlerDeepSlice(
        List<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        AnalysisResult result)
    {
        // Map DbSet property names and entity simple names to "Property (Entity)".
        var dbSetsByProperty = new Dictionary<string, string>(StringComparer.Ordinal);
        var dbSetsByEntity = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var ctx in result.DbContexts)
        {
            foreach (var set in ctx.DbSets)
            {
                var entitySimple = CSharpScanner.SimpleName(set.Entity);
                var label = $"{set.Property} ({entitySimple})";
                dbSetsByProperty.TryAdd(set.Property, label);
                dbSetsByEntity.TryAdd(entitySimple, label);
            }
        }

        var notificationsBySimple = result.Notifications
            .GroupBy(n => n.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var requestsBySimple = result.Requests
            .GroupBy(r => r.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var handlersByFull = result.Handlers
            .GroupBy(h => h.FullName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        foreach (var (file, scan, mask) in parsedFiles)
        {
            foreach (var decl in scan.Classes)
            {
                if (!handlersByFull.TryGetValue(decl.FullName, out var candidates))
                    continue;

                var handler = PickHandlerCandidate(candidates, file.ProjectName);
                if (handler is null) continue;

                // 1. Extract constructor / primary-constructor dependencies
                ExtractDependencies(decl, file.Code, mask, scan.Classes, handler);

                if (decl.BodySpan.End <= decl.BodySpan.Start)
                    continue;

                // 2. Scan handler body for DbSets, Notifications, and Dispatched Requests
                var bodyStart = decl.BodySpan.Start;
                var bodyEnd = Math.Min(file.Code.Length, decl.BodySpan.End);

                if (dbSetsByProperty.Count > 0)
                {
                    foreach (Match m in MemberAccessRegex().Matches(file.Code, bodyStart))
                    {
                        if (m.Index >= bodyEnd) break;
                        if (!mask.IsCode(m.Index)) continue;
                        if (!ReferenceEquals(InnermostClass(scan.Classes, m.Index), decl)) continue;

                        var prop = m.Groups["prop"].Value;
                        if (dbSetsByProperty.TryGetValue(prop, out var label) &&
                            !handler.TouchedEntities.Contains(label, StringComparer.Ordinal))
                        {
                            handler.TouchedEntities.Add(label);
                        }
                    }

                    foreach (Match m in DbContextSetMethodRegex().Matches(file.Code, bodyStart))
                    {
                        if (m.Index >= bodyEnd) break;
                        if (!mask.IsCode(m.Index)) continue;
                        if (!ReferenceEquals(InnermostClass(scan.Classes, m.Index), decl)) continue;

                        var entitySimple = CSharpScanner.SimpleName(m.Groups["entity"].Value.Trim());
                        if (dbSetsByEntity.TryGetValue(entitySimple, out var label) &&
                            !handler.TouchedEntities.Contains(label, StringComparer.Ordinal))
                        {
                            handler.TouchedEntities.Add(label);
                        }
                    }
                }

                // Check object creations and generic Publish/Send calls inside the handler
                foreach (var typeRef in EnumerateDispatchedTypeRefs(file.Code, bodyStart, bodyEnd, mask, scan.Classes, decl))
                {
                    var simple = CSharpScanner.UnqualifiedTypeName(typeRef);
                    if (notificationsBySimple.TryGetValue(simple, out var notifHits))
                    {
                        var notif = PickNotification(notifHits, decl.Namespace, file.ProjectName);
                        if (notif is not null && !handler.PublishedNotifications.Contains(notif.FullName, StringComparer.Ordinal))
                            handler.PublishedNotifications.Add(notif.FullName);
                    }

                    if (requestsBySimple.TryGetValue(simple, out var reqHits))
                    {
                        var req = PickCandidate(reqHits, file.ProjectName);
                        if (req is not null &&
                            !string.Equals(req.Name, handler.RequestName, StringComparison.Ordinal) &&
                            !handler.DispatchedRequests.Contains(req.FullName, StringComparer.Ordinal))
                        {
                            handler.DispatchedRequests.Add(req.FullName);
                        }
                    }
                }
            }
        }

        // Roll up handler side-effects onto linked requests
        var handlerLookup = result.Handlers
            .GroupBy(h => h.FullName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        foreach (var request in result.Requests)
        {
            foreach (var handlerFullName in request.Handlers)
            {
                if (!handlerLookup.TryGetValue(handlerFullName, out var handlers))
                    continue;

                foreach (var h in handlers)
                {
                    foreach (var entity in h.TouchedEntities)
                    {
                        if (!request.TouchedEntities.Contains(entity, StringComparer.Ordinal))
                            request.TouchedEntities.Add(entity);
                    }
                    foreach (var notif in h.PublishedNotifications)
                    {
                        if (!request.PublishedNotifications.Contains(notif, StringComparer.Ordinal))
                            request.PublishedNotifications.Add(notif);
                    }
                    foreach (var dispatched in h.DispatchedRequests)
                    {
                        if (!request.DispatchedRequests.Contains(dispatched, StringComparer.Ordinal))
                            request.DispatchedRequests.Add(dispatched);
                    }
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateDispatchedTypeRefs(
        string code,
        int bodyStart,
        int bodyEnd,
        SourceMask mask,
        List<CSharpScanner.ClassDecl> classes,
        CSharpScanner.ClassDecl owner)
    {
        foreach (Match m in ObjectCreationRegex().Matches(code, bodyStart))
        {
            if (m.Index >= bodyEnd) break;
            if (!mask.IsCode(m.Index)) continue;
            if (!ReferenceEquals(InnermostClass(classes, m.Index), owner)) continue;
            yield return m.Groups["type"].Value;
        }

        foreach (Match m in GenericDispatchRegex().Matches(code, bodyStart))
        {
            if (m.Index >= bodyEnd) break;
            if (!mask.IsCode(m.Index)) continue;
            if (!ReferenceEquals(InnermostClass(classes, m.Index), owner)) continue;
            yield return m.Groups["type"].Value;
        }
    }

    private static void ExtractDependencies(
        CSharpScanner.ClassDecl decl,
        string code,
        SourceMask mask,
        List<CSharpScanner.ClassDecl> classes,
        HandlerInfo handler)
    {
        if (!string.IsNullOrWhiteSpace(decl.PrimaryConstructorParameters))
            AddParameterTypes(decl.PrimaryConstructorParameters, handler.Dependencies);

        if (decl.BodySpan.End <= decl.BodySpan.Start)
            return;

        var ctorPattern = new Regex(
            $@"\b(?:public|internal|protected|private)\s+{Regex.Escape(decl.Name)}\s*\((?<params>[^\)]*)\)",
            RegexOptions.Singleline);

        var bodyEnd = Math.Min(code.Length, decl.BodySpan.End);
        foreach (Match m in ctorPattern.Matches(code, decl.BodySpan.Start))
        {
            if (m.Index >= bodyEnd) break;
            if (!mask.IsCode(m.Index)) continue;
            if (!ReferenceEquals(InnermostClass(classes, m.Index), decl)) continue;

            AddParameterTypes(m.Groups["params"].Value, handler.Dependencies);
        }
    }

    private static void AddParameterTypes(string rawParameters, List<string> target)
    {
        foreach (var raw in CSharpScanner.SplitTopLevel(rawParameters))
        {
            var paramType = ExtractParameterType(raw);
            if (string.IsNullOrEmpty(paramType)) continue;
            if (!target.Contains(paramType, StringComparer.Ordinal))
                target.Add(paramType);
        }
    }

    private static string? ExtractParameterType(string rawParameter)
    {
        var parameter = rawParameter.Trim();
        if (parameter.Length == 0) return null;

        while (parameter.StartsWith('['))
        {
            var close = parameter.IndexOf(']');
            if (close < 0) break;
            parameter = parameter[(close + 1)..].TrimStart();
        }

        var equals = parameter.IndexOf('=');
        if (equals >= 0)
            parameter = parameter[..equals].TrimEnd();

        parameter = Regex.Replace(parameter, @"^(?:(?:params|in|ref|out|this)\s+)+", "").Trim();
        var match = Regex.Match(parameter, @"^(?<type>.+?)\s+@?[A-Za-z_]\w*$");
        if (!match.Success) return null;

        var type = Regex.Replace(match.Groups["type"].Value.Trim(), @"\s+", " ");
        return type.Length > 0 ? type : null;
    }

    private static void DiscoverApiEntrypoints(
        List<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        AnalysisResult result)
    {
        var requestsBySimple = result.Requests
            .GroupBy(r => r.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        foreach (var (file, scan, mask) in parsedFiles)
        {
            DiscoverControllerEndpoints(file, scan, mask, requestsBySimple, result);
            DiscoverMinimalApiEndpoints(file, mask, requestsBySimple, result);
            DiscoverFastEndpoints(file, scan, mask, requestsBySimple, result);
        }

        result.Endpoints.Sort((a, b) =>
        {
            var byRoute = string.Compare(a.Route, b.Route, StringComparison.OrdinalIgnoreCase);
            if (byRoute != 0) return byRoute;
            var byMethod = string.Compare(a.HttpMethod, b.HttpMethod, StringComparison.OrdinalIgnoreCase);
            return byMethod != 0 ? byMethod : string.Compare(a.Name, b.Name, StringComparison.Ordinal);
        });
    }

    private static void DiscoverControllerEndpoints(
        SourceFile file,
        CSharpScanner.FileScan scan,
        SourceMask mask,
        Dictionary<string, List<CqrsRequest>> requestsBySimple,
        AnalysisResult result)
    {
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
                if (!ReferenceEquals(InnermostClass(scan.Classes, m.Index), decl)) continue;

                var attrs = m.Groups["attrs"].Value;
                var verbMatch = HttpVerbAttributeRegex().Match(attrs);
                var routeMatch = RouteAttributeRegex().Match(attrs);
                if (!verbMatch.Success && !routeMatch.Success)
                    continue;

                var methodName = m.Groups["method"].Value;
                var parameters = m.Groups["params"].Value;
                var actionEnd = FindMethodEnd(file.Code, mask, m.Index + m.Length, bodyEnd);
                var actionBody = actionEnd > m.Index + m.Length
                    ? file.Code[(m.Index + m.Length)..actionEnd]
                    : "";

                var matchedRequests = FindReferencedRequests(
                    parameters,
                    actionBody,
                    requestsBySimple,
                    file.ProjectName);

                if (matchedRequests.Count == 0) continue;

                var subRoute = verbMatch.Success && verbMatch.Groups["route"].Success
                    ? verbMatch.Groups["route"].Value
                    : routeMatch.Success
                        ? routeMatch.Groups["route"].Value
                        : "";

                var fullRoute = CombineControllerRoute(classRoute, subRoute, controllerStem, methodName);
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
                        });
                }
            }
        }
    }

    private static void DiscoverMinimalApiEndpoints(
        SourceFile file,
        SourceMask mask,
        Dictionary<string, List<CqrsRequest>> requestsBySimple,
        AnalysisResult result)
    {
        var groupPrefixes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match gm in MapGroupRegex().Matches(file.Code))
        {
            if (!mask.IsCode(gm.Index)) continue;
            groupPrefixes[gm.Groups["var"].Value] = gm.Groups["prefix"].Value;
        }

        foreach (Match m in MinimalApiMapRegex().Matches(file.Code))
        {
            if (!mask.IsCode(m.Index)) continue;

            var targetVar = m.Groups["target"].Value;
            var verb = m.Groups["verb"].Value.ToUpperInvariant();
            var route = m.Groups["route"].Value;
            var openParen = file.Code.IndexOf('(', m.Index);
            if (openParen < 0) continue;

            var closeParen = FindMatchingParen(file.Code, mask, openParen);
            if (closeParen <= openParen) continue;

            var callContent = file.Code[(openParen + 1)..closeParen];
            var matchedRequests = FindReferencedRequests(
                callContent,
                callContent,
                requestsBySimple,
                file.ProjectName);

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
    }

    private static void DiscoverFastEndpoints(
        SourceFile file,
        CSharpScanner.FileScan scan,
        SourceMask mask,
        Dictionary<string, List<CqrsRequest>> requestsBySimple,
        AnalysisResult result)
    {
        foreach (var decl in scan.Classes)
        {
            if (decl.IsAbstract || decl.BodySpan.End <= decl.BodySpan.Start)
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
                    var picked = PickCandidate(hits, file.ProjectName);
                    if (picked is not null)
                        matchedRequests.Add(picked);
                }
            }

            if (matchedRequests.Count == 0 && decl.BaseRawNames.Any(b => b.StartsWith("Endpoint", StringComparison.Ordinal)))
            {
                matchedRequests.AddRange(FindReferencedRequests("", body, requestsBySimple, file.ProjectName));
            }

            if (matchedRequests.Count == 0) continue;

            var verbMatch = FastEndpointVerbRegex().Match(body);
            if (!verbMatch.Success || !mask.IsCode(decl.BodySpan.Start + verbMatch.Index))
                continue;

            var verb = verbMatch.Groups["verb"].Value.ToUpperInvariant();
            var route = NormalizeRoute(verbMatch.Groups["route"].Value);
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
                    });
            }
        }
    }

    private static void RecordEndpoint(AnalysisResult result, CqrsRequest request, EndpointInfo endpoint)
    {
        if (!result.Endpoints.Any(e =>
                string.Equals(e.HttpMethod, endpoint.HttpMethod, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(e.Route, endpoint.Route, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(e.TargetRequestName, endpoint.TargetRequestName, StringComparison.Ordinal) &&
                string.Equals(e.Name, endpoint.Name, StringComparison.Ordinal)))
        {
            result.Endpoints.Add(endpoint);
        }

        var label = endpoint.DisplayLabel;
        if (!request.Entrypoints.Contains(label, StringComparer.Ordinal))
            request.Entrypoints.Add(label);
    }

    private static List<CqrsRequest> FindReferencedRequests(
        string parametersSection,
        string bodySection,
        Dictionary<string, List<CqrsRequest>> requestsBySimple,
        string? projectName)
    {
        var results = new List<CqrsRequest>();

        void TryAdd(string typeRef)
        {
            var simple = CSharpScanner.UnqualifiedTypeName(typeRef);
            if (!requestsBySimple.TryGetValue(simple, out var hits))
                return;
            var picked = PickCandidate(hits, projectName);
            if (picked is not null && !results.Contains(picked))
                results.Add(picked);
        }

        // 1. Check parameter types
        if (!string.IsNullOrWhiteSpace(parametersSection))
        {
            foreach (var rawParam in CSharpScanner.SplitTopLevel(parametersSection))
            {
                var paramType = ExtractParameterType(rawParam);
                if (!string.IsNullOrEmpty(paramType))
                    TryAdd(paramType);
            }
        }

        // 2. Check object creation `new RequestType(` or `new RequestType {`
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

    private static int FindMethodEnd(string code, SourceMask mask, int afterParamsIndex, int maxEnd)
    {
        for (var i = afterParamsIndex; i < maxEnd; i++)
        {
            if (!mask.IsCode(i)) continue;
            if (code[i] == '{')
            {
                var close = mask.FindMatchingBrace(i);
                return close > i ? Math.Min(maxEnd, close + 1) : maxEnd;
            }
            if (code[i] == ';')
                return i + 1;
        }
        return maxEnd;
    }

    private static int FindMatchingParen(string code, SourceMask mask, int openParenIndex)
    {
        var depth = 0;
        for (var i = openParenIndex; i < code.Length; i++)
        {
            if (!mask.IsCode(i)) continue;
            if (code[i] == '(') depth++;
            else if (code[i] == ')')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    private static CSharpScanner.ClassDecl? InnermostClass(
        IReadOnlyList<CSharpScanner.ClassDecl> classes,
        int index)
    {
        CSharpScanner.ClassDecl? owner = null;
        foreach (var candidate in classes)
        {
            if (index < candidate.BodySpan.Start || index >= candidate.BodySpan.End)
                continue;
            if (owner is null || candidate.BodySpan.Start > owner.BodySpan.Start)
                owner = candidate;
        }
        return owner;
    }

    private static CqrsRequest? PickCandidate(List<CqrsRequest> candidates, string? projectName)
    {
        if (candidates.Count == 1) return candidates[0];
        if (!string.IsNullOrEmpty(projectName))
        {
            var sameProj = candidates.FirstOrDefault(
                c => string.Equals(c.ProjectName, projectName, StringComparison.OrdinalIgnoreCase));
            if (sameProj is not null) return sameProj;
        }
        return candidates[0];
    }

    private static HandlerInfo? PickHandlerCandidate(List<HandlerInfo> candidates, string? projectName)
    {
        if (candidates.Count == 1) return candidates[0];
        if (!string.IsNullOrEmpty(projectName))
        {
            var sameProj = candidates.FirstOrDefault(
                c => string.Equals(c.ProjectName, projectName, StringComparison.OrdinalIgnoreCase));
            if (sameProj is not null) return sameProj;
        }
        return candidates[0];
    }

    private static CqrsNotification? PickNotification(
        List<CqrsNotification> candidates,
        string? callerNamespace,
        string? projectName)
    {
        if (candidates.Count == 1) return candidates[0];
        if (!string.IsNullOrEmpty(callerNamespace))
        {
            var sameNs = candidates.FirstOrDefault(
                c => string.Equals(c.Namespace, callerNamespace, StringComparison.Ordinal));
            if (sameNs is not null) return sameNs;
        }
        if (!string.IsNullOrEmpty(projectName))
        {
            var sameProj = candidates.FirstOrDefault(
                c => string.Equals(c.ProjectName, projectName, StringComparison.OrdinalIgnoreCase));
            if (sameProj is not null) return sameProj;
        }
        return candidates[0];
    }
}
