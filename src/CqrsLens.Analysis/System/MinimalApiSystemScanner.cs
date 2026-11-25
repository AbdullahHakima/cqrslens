using System.Text.RegularExpressions;

namespace CqrsLens;

/// <summary>
/// Discovers all Minimal API endpoints (<c>MapGet</c>/<c>MapPost</c>/<c>MapPut</c>/
/// <c>MapDelete</c>/<c>MapPatch</c>) across non-test projects, including chained/nested
/// <c>MapGroup</c> prefixes, inline lambdas, route-first method groups, and EndpointGroup
/// conventions, regardless of whether MediatR/CQRS requests are used.
/// </summary>
internal static partial class MinimalApiSystemScanner
{
    [GeneratedRegex(
        @"(?:(?<target>[A-Za-z_]\w*)|\))\s*\.\s*Map(?<verb>Get|Post|Put|Delete|Patch)\s*\(\s*@?""(?<route>[^""]*)""",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex RouteFirstMapRegex();

    [GeneratedRegex(
        @"(?<target>[A-Za-z_]\w*)\s*\.\s*Map(?<verb>Get|Post|Put|Delete|Patch)\s*\(\s*(?<handler>[A-Za-z_]\w*)\s*(?:,\s*@?""(?<route>[^""]*)"")?\s*\)",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex HandlerFirstMapRegex();

    [GeneratedRegex(
        @"(?:\b(?:var|RouteGroupBuilder|IEndpointRouteBuilder)\s+)?(?<var>[A-Za-z_]\w*)\s*=\s*(?<rhs>[^;]{1,600}?)\.MapGroup\s*\(\s*@?""(?<prefix>[^""]*)""\s*\)",
        RegexOptions.Singleline,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex MapGroupAssignmentRegex();

    [GeneratedRegex(
        @"\.MapGroup\s*\(\s*@?""(?<prefix>[^""]*)""\s*\)",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex InlineMapGroupRegex();

    [GeneratedRegex(
        @"(?:(?:public|private|protected|internal|static|async)\s+)+(?<ret>[^\(\{;=]+?)\s+(?<method>[A-Za-z_]\w*)\s*\(",
        RegexOptions.Singleline,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex MethodDeclRegex();

    [GeneratedRegex(
        @"^[ \t]*(?:(?:static|async)\s+)*(?<ret>[A-Za-z_][\w.<>,\[\]\?]*(?:\s+[A-Za-z_][\w.<>,\[\]\?]*){0,2})\s+(?<method>[A-Za-z_]\w*)\s*\(",
        RegexOptions.Multiline,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex TopLevelLocalFunctionRegex();

    private static readonly HashSet<string> ControlKeywords = new(StringComparer.Ordinal)
    {
        "if", "for", "foreach", "while", "switch", "using", "catch", "lock",
        "return", "await", "throw", "yield", "else", "var", "new", "typeof", "nameof",
    };

    private sealed record MethodInfo(
        string Name,
        string Params,
        string Body,
        int BodyStart,
        int BodyEnd,
        int Index,
        CSharpScanner.ClassDecl? OwnerClass,
        SourceFile File,
        CSharpScanner.FileScan Scan,
        SourceMask Mask);

    private sealed record GroupBinding(string Variable, string Prefix, int Index, int ScopeStart, int ScopeEnd);

    internal static void Discover(SystemAnalysisContext context, List<SystemEntrypointInfo> target)
    {
        var candidateFiles = new List<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)>();
        foreach (var entry in context.ParsedFiles)
        {
            if (!context.IsTestProject(entry.File.ProjectName) && ContainsMinimalApiVerb(entry.File.Code))
                candidateFiles.Add(entry);
        }

        if (candidateFiles.Count == 0)
            return;

        Dictionary<(string Project, string ClassName, string MethodName), MethodInfo>? projectMethods = null;
        Dictionary<(string Project, string ClassName, string MethodName), MethodInfo> GetProjectMethods()
            => projectMethods ??= BuildProjectMethodIndex(context);

        foreach (var (file, scan, mask) in candidateFiles)
        {
            var groupBindings = IndexGroupBindings(file, scan, mask);
            var localMethods = IndexFileMethods(file, scan, mask);
            var matchedOffsets = new HashSet<int>();

            DiscoverRouteFirstCalls(
                context, file, scan, mask, groupBindings, localMethods, GetProjectMethods, matchedOffsets, target);
            DiscoverHandlerFirstCalls(
                context, file, scan, mask, groupBindings, localMethods, matchedOffsets, target);
        }
    }

    private static bool ContainsMinimalApiVerb(string code)
        => code.Contains("MapGet", StringComparison.Ordinal) ||
           code.Contains("MapPost", StringComparison.Ordinal) ||
           code.Contains("MapPut", StringComparison.Ordinal) ||
           code.Contains("MapDelete", StringComparison.Ordinal) ||
           code.Contains("MapPatch", StringComparison.Ordinal);

    private static Dictionary<(string Project, string ClassName, string MethodName), MethodInfo> BuildProjectMethodIndex(
        SystemAnalysisContext context)
    {
        var map = new Dictionary<(string, string, string), MethodInfo>();
        foreach (var (file, scan, mask) in context.ParsedFiles)
        {
            if (context.IsTestProject(file.ProjectName))
                continue;
            if (scan.Classes.Count == 0)
                continue;

            var project = file.ProjectName ?? string.Empty;
            foreach (var method in ExtractClassMethods(file, scan, mask))
            {
                if (method.OwnerClass is not null)
                    map.TryAdd((project, method.OwnerClass.Name, method.Name), method);
            }
        }
        return map;
    }

    private static List<GroupBinding> IndexGroupBindings(
        SourceFile file,
        CSharpScanner.FileScan scan,
        SourceMask mask)
    {
        var bindings = new List<GroupBinding>();
        try
        {
            foreach (Match m in MapGroupAssignmentRegex().Matches(file.Code))
            {
                if (!mask.IsCode(m.Index))
                    continue;

                var varName = m.Groups["var"].Value;
                var prefix = m.Groups["prefix"].Value;
                var rhs = m.Groups["rhs"].Value.Trim();

                var ownerClass = DeepSliceSyntax.InnermostClass(scan.Classes, m.Index);
                var scopeStart = ownerClass?.BodySpan.Start ?? 0;
                var scopeEnd = ownerClass is not null
                    ? Math.Min(file.Code.Length, ownerClass.BodySpan.End)
                    : file.Code.Length;

                // Resolve chained parent group variable: `var v1 = api.MapGroup("/v1")`
                var firstDot = rhs.IndexOf('.');
                if (firstDot > 0)
                {
                    var parentVar = rhs[..firstDot].Trim();
                    if (TryResolveGroupPrefix(bindings, parentVar, m.Index, out var parentPrefix))
                        prefix = EndpointRoutes.Combine(parentPrefix, prefix);
                }

                bindings.Add(new GroupBinding(varName, prefix, m.Index, scopeStart, scopeEnd));
            }
        }
        catch (RegexMatchTimeoutException)
        {
        }
        return bindings;
    }

    private static bool TryResolveGroupPrefix(
        List<GroupBinding> bindings,
        string variableName,
        int atIndex,
        out string prefix)
    {
        for (var i = bindings.Count - 1; i >= 0; i--)
        {
            var b = bindings[i];
            if (b.Index < atIndex &&
                atIndex >= b.ScopeStart &&
                atIndex <= b.ScopeEnd &&
                string.Equals(b.Variable, variableName, StringComparison.Ordinal))
            {
                prefix = b.Prefix;
                return true;
            }
        }

        prefix = string.Empty;
        return false;
    }

    private static Dictionary<string, List<MethodInfo>> IndexFileMethods(
        SourceFile file,
        CSharpScanner.FileScan scan,
        SourceMask mask)
    {
        var map = new Dictionary<string, List<MethodInfo>>(StringComparer.Ordinal);
        foreach (var method in ExtractClassMethods(file, scan, mask))
        {
            if (!map.TryGetValue(method.Name, out var list))
                map[method.Name] = list = new List<MethodInfo>();
            list.Add(method);
        }

        // Also capture top-level local functions without access modifiers (e.g. `Task<IResult> GetItems(...)`).
        if (scan.Classes.Count == 0 ||
            string.Equals(Path.GetFileName(file.Path), "Program.cs", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                foreach (Match m in TopLevelLocalFunctionRegex().Matches(file.Code))
                {
                    if (!mask.IsCode(m.Index))
                        continue;
                    if (DeepSliceSyntax.InnermostClass(scan.Classes, m.Index) is not null)
                        continue;

                    var name = m.Groups["method"].Value;
                    var ret = m.Groups["ret"].Value.Trim();
                    if (ControlKeywords.Contains(name) || ControlKeywords.Contains(ret))
                        continue;
                    if (ret.Contains('=', StringComparison.Ordinal) ||
                        ret.Contains(';', StringComparison.Ordinal) ||
                        ret.Contains("return ", StringComparison.Ordinal))
                        continue;

                    var openParen = m.Index + m.Length - 1;
                    var closeParen = DeepSliceSyntax.FindMatchingParen(file.Code, mask, openParen);
                    if (closeParen < 0)
                        continue;

                    var nextCode = NextNonWhitespaceChar(file.Code, mask, closeParen + 1);
                    if (nextCode is not ('{' or '='))
                        continue;

                    var parameters = mask.CodeOnly(openParen + 1, Math.Max(0, closeParen - openParen - 1));
                    var start = closeParen + 1;
                    var end = DeepSliceSyntax.FindMethodEnd(file.Code, mask, start, file.Code.Length);
                    var body = mask.CodeOnly(start, Math.Max(0, end - start));

                    var info = new MethodInfo(name, parameters, body, start, end, m.Index, null, file, scan, mask);
                    if (!map.TryGetValue(name, out var list))
                        map[name] = list = new List<MethodInfo>();
                    if (!list.Any(existing => existing.Index == info.Index))
                        list.Add(info);
                }
            }
            catch (RegexMatchTimeoutException)
            {
            }
        }

        return map;
    }

    private static List<MethodInfo> ExtractClassMethods(
        SourceFile file,
        CSharpScanner.FileScan scan,
        SourceMask mask)
    {
        var list = new List<MethodInfo>();
        try
        {
            foreach (Match m in MethodDeclRegex().Matches(file.Code))
            {
                if (!mask.IsCode(m.Index))
                    continue;
                var name = m.Groups["method"].Value;
                if (name is "Map" or "Configure" || ControlKeywords.Contains(name))
                    continue;
                var ret = m.Groups["ret"].Value;
                if (ret.Contains("class", StringComparison.Ordinal) ||
                    ret.Contains("record", StringComparison.Ordinal) ||
                    ret.Contains("struct", StringComparison.Ordinal) ||
                    ret.Contains("interface", StringComparison.Ordinal))
                    continue;

                var openParen = m.Index + m.Length - 1;
                var closeParen = DeepSliceSyntax.FindMatchingParen(file.Code, mask, openParen);
                if (closeParen < 0)
                    continue;

                var parameters = mask.CodeOnly(openParen + 1, Math.Max(0, closeParen - openParen - 1));
                var start = closeParen + 1;
                var owner = DeepSliceSyntax.InnermostClass(scan.Classes, m.Index);
                var maxEnd = owner is not null ? Math.Min(file.Code.Length, owner.BodySpan.End) : file.Code.Length;
                var end = DeepSliceSyntax.FindMethodEnd(file.Code, mask, start, maxEnd);
                var body = mask.CodeOnly(start, Math.Max(0, end - start));

                list.Add(new MethodInfo(name, parameters, body, start, end, m.Index, owner, file, scan, mask));
            }
        }
        catch (RegexMatchTimeoutException)
        {
        }
        return list;
    }

    private static void DiscoverRouteFirstCalls(
        SystemAnalysisContext context,
        SourceFile file,
        CSharpScanner.FileScan scan,
        SourceMask mask,
        List<GroupBinding> groupBindings,
        Dictionary<string, List<MethodInfo>> localMethods,
        Func<Dictionary<(string Project, string ClassName, string MethodName), MethodInfo>> getProjectMethods,
        HashSet<int> matchedOffsets,
        List<SystemEntrypointInfo> target)
    {
        MatchCollection matches;
        try
        {
            matches = RouteFirstMapRegex().Matches(file.Code);
        }
        catch (RegexMatchTimeoutException)
        {
            return;
        }

        foreach (Match match in matches)
        {
            if (!mask.IsCode(match.Groups["verb"].Index))
                continue;

            var verbRaw = match.Groups["verb"].Value;
            var verb = verbRaw.ToUpperInvariant();
            var route = match.Groups["route"].Value;
            var targetVariable = match.Groups["target"].Success ? match.Groups["target"].Value : string.Empty;

            var openParen = file.Code.IndexOf('(', match.Index);
            if (openParen < 0)
                continue;
            var closeParen = DeepSliceSyntax.FindMatchingParen(file.Code, mask, openParen);
            if (closeParen <= openParen)
                continue;

            matchedOffsets.Add(match.Index);

            var prefix = ResolveRoutePrefix(file.Code, mask, groupBindings, targetVariable, match.Index);
            var fullRoute = EndpointRoutes.Combine(prefix, route);
            var enclosingClass = DeepSliceSyntax.InnermostClass(scan.Classes, match.Index);

            var afterRouteStart = match.Index + match.Length;
            var handlerArg = ExtractSecondArgumentSpan(file.Code, mask, afterRouteStart, closeParen);

            string endpointName = enclosingClass is not null
                ? $"{enclosingClass.Name}.Map{verbRaw}"
                : $"Map{verbRaw}";
            var locationIndex = match.Index;
            var traceFile = file;
            var traceScan = scan;
            var traceMask = mask;
            var traceOwner = enclosingClass;
            var paramsCode = string.Empty;
            var bodyCode = string.Empty;
            var bodyStart = match.Index;
            var bodyEnd = closeParen;

            if (handlerArg.Start < handlerArg.End)
            {
                if (TryParseInlineLambda(file.Code, mask, handlerArg.Start, handlerArg.End,
                        out var lambdaParams, out var lambdaBody, out var lStart, out var lEnd))
                {
                    paramsCode = lambdaParams;
                    bodyCode = lambdaBody;
                    bodyStart = lStart;
                    bodyEnd = lEnd;
                }
                else
                {
                    var rawHandler = mask.CodeOnly(handlerArg.Start, handlerArg.End - handlerArg.Start).Trim();
                    var resolvedMethod = ResolveMethodGroup(
                        rawHandler, enclosingClass, file.ProjectName ?? string.Empty, localMethods, getProjectMethods);
                    if (resolvedMethod is not null)
                    {
                        var ownerName = resolvedMethod.OwnerClass?.Name ?? enclosingClass?.Name;
                        endpointName = ownerName is not null
                            ? $"{ownerName}.{resolvedMethod.Name}"
                            : resolvedMethod.Name;
                        locationIndex = resolvedMethod.Index;
                        traceFile = resolvedMethod.File;
                        traceScan = resolvedMethod.Scan;
                        traceMask = resolvedMethod.Mask;
                        traceOwner = resolvedMethod.OwnerClass;
                        paramsCode = resolvedMethod.Params;
                        bodyCode = resolvedMethod.Body;
                        bodyStart = resolvedMethod.BodyStart;
                        bodyEnd = resolvedMethod.BodyEnd;
                    }
                    else
                    {
                        bodyCode = mask.CodeOnly(openParen + 1, closeParen - openParen - 1);
                        paramsCode = bodyCode;
                    }
                }
            }

            var matchedRequests = context.RequestsBySimple.Count > 0
                ? EndpointRequestMatcher.FindReferencedRequests(
                    paramsCode,
                    bodyCode,
                    context.RequestsBySimple,
                    traceFile.ProjectName,
                    traceScan.Usings,
                    traceOwner?.Namespace ?? traceScan.Namespace,
                    traceOwner?.ContainingTypes)
                : new List<CqrsRequest>();

            var entrypoint = new SystemEntrypointInfo
            {
                Name = endpointName,
                Kind = SystemEntrypointKind.Http,
                ExecutionStyle = matchedRequests.Count > 0
                    ? SystemExecutionStyle.Cqrs
                    : SystemExecutionStyle.Direct,
                Method = verb,
                RouteOrTrigger = fullRoute,
                ProjectName = file.ProjectName,
                Location = SourceLocation.Create(traceFile.Path, context.Result.RootPath, traceFile.Code, locationIndex),
                IsInferred = false,
            };

            SystemDependencyTracer.Trace(
                context,
                traceFile,
                traceScan,
                traceMask,
                traceOwner,
                paramsCode,
                bodyCode,
                bodyStart,
                bodyEnd,
                entrypoint,
                matchedRequests);

            target.Add(entrypoint);
        }
    }

    private static void DiscoverHandlerFirstCalls(
        SystemAnalysisContext context,
        SourceFile file,
        CSharpScanner.FileScan scan,
        SourceMask mask,
        List<GroupBinding> groupBindings,
        Dictionary<string, List<MethodInfo>> localMethods,
        HashSet<int> matchedOffsets,
        List<SystemEntrypointInfo> target)
    {
        MatchCollection matches;
        try
        {
            matches = HandlerFirstMapRegex().Matches(file.Code);
        }
        catch (RegexMatchTimeoutException)
        {
            return;
        }

        foreach (Match match in matches)
        {
            if (matchedOffsets.Contains(match.Index) || !mask.IsCode(match.Index))
                continue;

            var handlerMethodName = match.Groups["handler"].Value;
            var enclosingClass = DeepSliceSyntax.InnermostClass(scan.Classes, match.Index);
            var targetMethod = PickLocalMethod(localMethods, handlerMethodName, enclosingClass);
            if (targetMethod is null)
                continue;

            var verb = match.Groups["verb"].Value.ToUpperInvariant();
            var subRoute = match.Groups["route"].Success ? match.Groups["route"].Value : string.Empty;
            var targetVariable = match.Groups["target"].Value;

            var routeInferred = false;
            if (!TryResolveGroupPrefix(groupBindings, targetVariable, match.Index, out var prefix) &&
                enclosingClass is not null)
            {
                prefix = $"/api/{enclosingClass.Name}";
                routeInferred = true;
            }

            var fullRoute = EndpointRoutes.Combine(prefix, subRoute);
            var endpointName = enclosingClass is not null
                ? $"{enclosingClass.Name}.{handlerMethodName}"
                : handlerMethodName;

            var matchedRequests = context.RequestsBySimple.Count > 0
                ? EndpointRequestMatcher.FindReferencedRequests(
                    targetMethod.Params,
                    targetMethod.Body,
                    context.RequestsBySimple,
                    file.ProjectName,
                    scan.Usings,
                    targetMethod.OwnerClass?.Namespace ?? enclosingClass?.Namespace,
                    targetMethod.OwnerClass?.ContainingTypes ?? enclosingClass?.ContainingTypes)
                : new List<CqrsRequest>();

            var entrypoint = new SystemEntrypointInfo
            {
                Name = endpointName,
                Kind = SystemEntrypointKind.Http,
                ExecutionStyle = matchedRequests.Count > 0
                    ? SystemExecutionStyle.Cqrs
                    : SystemExecutionStyle.Direct,
                Method = verb,
                RouteOrTrigger = fullRoute,
                ProjectName = file.ProjectName,
                Location = SourceLocation.Create(file.Path, context.Result.RootPath, file.Code, targetMethod.Index),
                IsInferred = routeInferred,
            };

            SystemDependencyTracer.Trace(
                context,
                targetMethod.File,
                targetMethod.Scan,
                targetMethod.Mask,
                targetMethod.OwnerClass,
                targetMethod.Params,
                targetMethod.Body,
                targetMethod.BodyStart,
                targetMethod.BodyEnd,
                entrypoint,
                matchedRequests);

            target.Add(entrypoint);
        }
    }

    private static string? ResolveRoutePrefix(
        string code,
        SourceMask mask,
        List<GroupBinding> groupBindings,
        string targetVariable,
        int callIndex)
    {
        if (targetVariable.Length > 0 &&
            TryResolveGroupPrefix(groupBindings, targetVariable, callIndex, out var prefix))
            return prefix;

        // Check inline fluent `.MapGroup("/prefix")...MapGet(...)` in the same statement.
        var stmtStart = Math.Max(0, callIndex - 600);
        for (var i = callIndex - 1; i >= stmtStart; i--)
        {
            if (mask.IsCode(i) && code[i] is ';' or '{' or '}')
            {
                stmtStart = i + 1;
                break;
            }
        }

        var statementPrefix = code[stmtStart..callIndex];
        try
        {
            string? combined = null;
            var trimmedStmt = statementPrefix.TrimStart();
            var firstDot = trimmedStmt.IndexOf('.');
            if (firstDot > 0)
            {
                var rootVar = trimmedStmt[..firstDot].Trim();
                if (TryResolveGroupPrefix(groupBindings, rootVar, callIndex, out var rootPrefix))
                    combined = rootPrefix;
            }

            foreach (Match m in InlineMapGroupRegex().Matches(statementPrefix))
            {
                if (!mask.IsCode(stmtStart + m.Index))
                    continue;
                combined = combined is null
                    ? m.Groups["prefix"].Value
                    : EndpointRoutes.Combine(combined, m.Groups["prefix"].Value);
            }
            return combined;
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    private static (int Start, int End) ExtractSecondArgumentSpan(
        string code,
        SourceMask mask,
        int afterRouteStart,
        int closeParen)
    {
        var depth = 0;
        var argStart = -1;
        for (var i = afterRouteStart; i < closeParen; i++)
        {
            if (!mask.IsCode(i))
                continue;
            var c = code[i];
            if (c is '(' or '[' or '{')
                depth++;
            else if (c is ')' or ']' or '}')
            {
                if (depth > 0)
                    depth--;
            }
            else if (c == ',' && depth == 0)
            {
                if (argStart < 0)
                    argStart = i + 1;
                else
                    return (argStart, i);
            }
        }

        return argStart >= 0 ? (argStart, closeParen) : (0, 0);
    }

    private static bool TryParseInlineLambda(
        string code,
        SourceMask mask,
        int start,
        int end,
        out string parameters,
        out string body,
        out int bodyStart,
        out int bodyEnd)
    {
        parameters = string.Empty;
        body = string.Empty;
        bodyStart = start;
        bodyEnd = end;

        var arrowIndex = FindTopLevelArrow(code, mask, start, end);
        if (arrowIndex < 0)
            return false;

        var openParen = -1;
        var closeParen = -1;
        for (var i = start; i < arrowIndex; i++)
        {
            if (!mask.IsCode(i))
                continue;
            if (code[i] == '(')
            {
                openParen = i;
                closeParen = DeepSliceSyntax.FindMatchingParen(code, mask, openParen);
                break;
            }
        }

        if (openParen >= 0 && closeParen > openParen && closeParen < arrowIndex)
            parameters = mask.CodeOnly(openParen + 1, closeParen - openParen - 1);

        bodyStart = arrowIndex + 2;
        bodyEnd = end;
        body = bodyEnd > bodyStart ? mask.CodeOnly(bodyStart, bodyEnd - bodyStart) : string.Empty;
        return true;
    }

    private static int FindTopLevelArrow(string code, SourceMask mask, int start, int end)
    {
        var parenDepth = 0;
        var braceDepth = 0;
        var bracketDepth = 0;
        for (var i = start; i + 1 < end; i++)
        {
            if (!mask.IsCode(i))
                continue;
            var c = code[i];
            switch (c)
            {
                case '(': parenDepth++; break;
                case ')': if (parenDepth > 0) parenDepth--; break;
                case '{': braceDepth++; break;
                case '}': if (braceDepth > 0) braceDepth--; break;
                case '[': bracketDepth++; break;
                case ']': if (bracketDepth > 0) bracketDepth--; break;
                case '=' when code[i + 1] == '>' && parenDepth == 0 && braceDepth == 0 && bracketDepth == 0:
                    return i;
            }
        }
        return -1;
    }

    private static MethodInfo? ResolveMethodGroup(
        string rawHandler,
        CSharpScanner.ClassDecl? enclosingClass,
        string projectName,
        Dictionary<string, List<MethodInfo>> localMethods,
        Func<Dictionary<(string Project, string ClassName, string MethodName), MethodInfo>> getProjectMethods)
    {
        if (string.IsNullOrWhiteSpace(rawHandler))
            return null;

        var expr = rawHandler.Trim();
        var dot = expr.LastIndexOf('.');
        if (dot > 0)
        {
            var ownerPart = expr[..dot].Trim();
            var methodPart = expr[(dot + 1)..].Trim();
            var ownerSimple = CSharpScanner.SimpleName(ownerPart);
            if (enclosingClass is not null &&
                string.Equals(ownerSimple, enclosingClass.Name, StringComparison.Ordinal))
            {
                return PickLocalMethod(localMethods, methodPart, enclosingClass);
            }

            if (PickLocalMethod(localMethods, methodPart, enclosingClass) is { } localMatch &&
                string.Equals(localMatch.OwnerClass?.Name, ownerSimple, StringComparison.Ordinal))
            {
                return localMatch;
            }

            if (getProjectMethods().TryGetValue((projectName, ownerSimple, methodPart), out var remote))
                return remote;

            return PickLocalMethod(localMethods, methodPart, enclosingClass);
        }

        return PickLocalMethod(localMethods, expr, enclosingClass);
    }

    private static MethodInfo? PickLocalMethod(
        Dictionary<string, List<MethodInfo>> localMethods,
        string methodName,
        CSharpScanner.ClassDecl? enclosingClass)
    {
        if (!localMethods.TryGetValue(methodName, out var candidates) || candidates.Count == 0)
            return null;
        if (enclosingClass is not null)
        {
            var sameClass = candidates.FirstOrDefault(m => ReferenceEquals(m.OwnerClass, enclosingClass));
            if (sameClass is not null)
                return sameClass;
        }
        return candidates[0];
    }

    private static char NextNonWhitespaceChar(string code, SourceMask mask, int start)
    {
        for (var i = start; i < code.Length; i++)
        {
            if (!mask.IsCode(i))
                continue;
            if (!char.IsWhiteSpace(code[i]))
                return code[i];
        }
        return '\0';
    }
}
