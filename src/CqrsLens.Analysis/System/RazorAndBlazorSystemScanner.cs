using System.Security;
using System.Text.RegularExpressions;

namespace CqrsLens;

/// <summary>
/// Discovers Razor Pages (<c>PageModel</c> handlers and <c>.cshtml</c> <c>@page</c> routes)
/// and Blazor components (<c>.razor</c> <c>@page</c> and <c>.razor.cs</c>/<c>ComponentBase</c>
/// <c>[Route]</c>) across non-test projects, reading markup files safely via
/// <see cref="BoundedFileReader"/> and skipping reparse points.
/// </summary>
internal static partial class RazorAndBlazorSystemScanner
{
    private const string BlazorMethod = "BLAZOR";

    private static readonly EnumerationOptions SafeDiscoveryOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        ReturnSpecialDirectories = false,
    };

    [GeneratedRegex(
        @"\b(?:public|private|protected|internal|async|virtual|override|sealed|new)\s+(?:[^\(\{;=]+?\s+)?(?<method>On(?<verb>Get|Post|Put|Delete|Patch)[A-Za-z0-9_]*)\s*\(",
        RegexOptions.Singleline,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex PageHandlerMethodRegex();

    [GeneratedRegex(
        @"^\s*@page(?:\s+""(?<route>[^""]*)"")?\s*(?://.*)?$",
        RegexOptions.Multiline,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex PageDirectiveRegex();

    [GeneratedRegex(
        @"^\s*@using\s+(?<target>[^\r\n;]+)",
        RegexOptions.Multiline,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex RazorUsingRegex();

    [GeneratedRegex(
        @"^\s*@inject\s+(?<type>[^\s;]+)\s+(?<name>[A-Za-z_]\w*)",
        RegexOptions.Multiline,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex RazorInjectRegex();

    [GeneratedRegex(
        @"@(?:code|functions)\b[^{]*\{",
        RegexOptions.Singleline,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex CodeBlockHeaderRegex();

    [GeneratedRegex(
        @"\.\s*(?:Send|SendAsync|InvokeAsync)\s*\(\s*@?(?<arg>[A-Za-z_]\w*)\s*(?:,|\))",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex MemberDispatchArgRegex();

    internal static void Discover(SystemAnalysisContext context, List<SystemEntrypointInfo> target)
    {
        var handledCshtmlPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        DiscoverPageModels(context, handledCshtmlPaths, target);
        DiscoverStandaloneRazorPages(context, handledCshtmlPaths, target);
        DiscoverBlazorComponents(context, target);
    }

    private static void DiscoverPageModels(
        SystemAnalysisContext context,
        HashSet<string> handledCshtmlPaths,
        List<SystemEntrypointInfo> target)
    {
        foreach (var (file, scan, mask) in context.ParsedFiles)
        {
            if (context.IsTestProject(file.ProjectName))
                continue;
            if (!file.Code.Contains("PageModel", StringComparison.Ordinal))
                continue;

            foreach (var decl in scan.Classes)
            {
                if (decl.IsAbstract || decl.BodySpan.End <= decl.BodySpan.Start)
                    continue;

                if (!InheritsPageModel(context, decl))
                    continue;

                var cshtmlInjects = new List<string>();
                var route = ResolveRazorPageRoute(file.Path, decl, cshtmlInjects, out var resolvedCshtmlPath, out var routeInferred);
                var bodyEnd = Math.Min(file.Code.Length, decl.BodySpan.End);
                var memberTypes = CollectMemberTypes(decl, file.Code, mask, scan.Classes);

                MatchCollection matches;
                try
                {
                    matches = PageHandlerMethodRegex().Matches(file.Code, decl.BodySpan.Start);
                }
                catch (RegexMatchTimeoutException)
                {
                    continue;
                }

                var emittedForPage = false;
                foreach (Match match in matches)
                {
                    if (match.Index >= bodyEnd)
                        break;
                    if (!mask.IsCode(match.Index))
                        continue;
                    if (!ReferenceEquals(DeepSliceSyntax.InnermostClass(scan.Classes, match.Index), decl))
                        continue;

                    var methodName = match.Groups["method"].Value;
                    var verb = match.Groups["verb"].Value.ToUpperInvariant();
                    var openParen = match.Index + match.Length - 1;
                    var closeParen = DeepSliceSyntax.FindMatchingParen(file.Code, mask, openParen);
                    if (closeParen < 0 || closeParen >= bodyEnd)
                        continue;

                    var paramsCode = mask.CodeOnly(openParen + 1, Math.Max(0, closeParen - openParen - 1));
                    var methodStart = closeParen + 1;
                    var methodEnd = DeepSliceSyntax.FindMethodEnd(file.Code, mask, methodStart, bodyEnd);
                    var bodyCode = mask.CodeOnly(methodStart, Math.Max(0, methodEnd - methodStart));

                    var matchedRequests = context.RequestsBySimple.Count > 0
                        ? EndpointRequestMatcher.FindReferencedRequests(
                            paramsCode,
                            bodyCode,
                            context.RequestsBySimple,
                            file.ProjectName,
                            scan.Usings,
                            decl.Namespace,
                            decl.ContainingTypes)
                        : new List<CqrsRequest>();

                    AddMemberDispatchedRequests(
                        context,
                        bodyCode,
                        memberTypes,
                        file.ProjectName,
                        scan.Usings,
                        decl.Namespace,
                        decl.ContainingTypes,
                        matchedRequests);

                    var entrypoint = new SystemEntrypointInfo
                    {
                        Name = $"{decl.Name}.{methodName}",
                        Kind = SystemEntrypointKind.RazorPage,
                        ExecutionStyle = matchedRequests.Count > 0
                            ? SystemExecutionStyle.Cqrs
                            : SystemExecutionStyle.Direct,
                        Method = verb,
                        RouteOrTrigger = route,
                        ProjectName = file.ProjectName,
                        Location = SourceLocation.Create(file.Path, context.Result.RootPath, file.Code, match.Index),
                        IsInferred = routeInferred,
                    };

                    SystemDependencyTracer.Trace(
                        context,
                        file,
                        scan,
                        mask,
                        decl,
                        paramsCode,
                        bodyCode,
                        methodStart,
                        methodEnd,
                        entrypoint,
                        matchedRequests,
                        cshtmlInjects);

                    target.Add(entrypoint);
                    emittedForPage = true;
                }

                if (emittedForPage && resolvedCshtmlPath is not null)
                    handledCshtmlPaths.Add(resolvedCshtmlPath);
            }
        }
    }

    private static void DiscoverStandaloneRazorPages(
        SystemAnalysisContext context,
        HashSet<string> handledCshtmlPaths,
        List<SystemEntrypointInfo> target)
    {
        var root = context.Result.RootPath;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return;

        foreach (var cshtmlPath in FindCshtmlFiles(root))
        {
            if (handledCshtmlPaths.Contains(cshtmlPath))
                continue;

            if (context.PathFilter is not null)
            {
                var rel = Path.GetRelativePath(root, cshtmlPath).Replace(Path.DirectorySeparatorChar, '/');
                if (!context.PathFilter.IsIncluded(rel))
                    continue;
            }

            var ownerProject = ProjectLocator.FindOwningProject(context.Result.Projects, cshtmlPath)?.Name;
            if (context.IsTestProject(ownerProject))
                continue;

            string markup;
            try
            {
                markup = BoundedFileReader.ReadAllText(cshtmlPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or SecurityException)
            {
                continue;
            }

            if (!markup.Contains("@page", StringComparison.Ordinal))
                continue;

            Match directiveMatch;
            try
            {
                directiveMatch = PageDirectiveRegex().Match(markup);
            }
            catch (RegexMatchTimeoutException)
            {
                continue;
            }

            if (!directiveMatch.Success)
                continue;

            var explicitRoute = directiveMatch.Groups["route"].Success
                ? directiveMatch.Groups["route"].Value.Trim()
                : string.Empty;
            var inferred = explicitRoute.Length == 0;
            var route = inferred
                ? DerivePagesConventionRoute(cshtmlPath, Path.GetFileNameWithoutExtension(cshtmlPath))
                : EndpointRoutes.Normalize(explicitRoute);

            var injects = ExtractRazorInjects(markup);
            var entrypoint = new SystemEntrypointInfo
            {
                Name = Path.GetFileNameWithoutExtension(cshtmlPath),
                Kind = SystemEntrypointKind.RazorPage,
                ExecutionStyle = SystemExecutionStyle.Direct,
                Method = "GET",
                RouteOrTrigger = route,
                ProjectName = ownerProject,
                Location = SourceLocation.FromLine(cshtmlPath, root, 1, 1),
                IsInferred = inferred,
            };

            if (injects.Count > 0)
            {
                var usings = BuildCombinedUsings(markup, null);
                var syntheticScan = new CSharpScanner.FileScan { Namespace = string.Empty };
                syntheticScan.Usings.AddRange(usings);
                var syntheticFile = new SourceFile(cshtmlPath, string.Empty, ownerProject);
                SystemDependencyTracer.Trace(
                    context,
                    syntheticFile,
                    syntheticScan,
                    SourceMask.Build(string.Empty),
                    null,
                    string.Empty,
                    string.Empty,
                    0,
                    0,
                    entrypoint,
                    Array.Empty<CqrsRequest>(),
                    injects);
            }

            target.Add(entrypoint);
        }
    }

    private static void DiscoverBlazorComponents(SystemAnalysisContext context, List<SystemEntrypointInfo> target)
    {
        var filesByPath = context.ParsedFiles.ToDictionary(
            f => f.File.Path,
            f => f,
            StringComparer.OrdinalIgnoreCase);
        var handledCodeBehindPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var root = context.Result.RootPath;
        if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
        {
            foreach (var razorPath in ProjectFileDiscovery.FindRazorFiles(root))
            {
                if (context.PathFilter is not null)
                {
                    var rel = Path.GetRelativePath(root, razorPath).Replace(Path.DirectorySeparatorChar, '/');
                    if (!context.PathFilter.IsIncluded(rel))
                        continue;
                }

                var codeBehindPath = razorPath + ".cs";
                var hasCodeBehind = filesByPath.TryGetValue(codeBehindPath, out var codeBehind);
                if (hasCodeBehind)
                    handledCodeBehindPaths.Add(codeBehindPath);

                var ownerProject = hasCodeBehind
                    ? codeBehind.File.ProjectName
                    : ProjectLocator.FindOwningProject(context.Result.Projects, razorPath)?.Name;
                if (context.IsTestProject(ownerProject))
                    continue;

                string markup;
                try
                {
                    markup = BoundedFileReader.ReadAllText(razorPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or SecurityException)
                {
                    continue;
                }

                if (!markup.Contains("@page", StringComparison.Ordinal))
                    continue;

                List<string> routes;
                try
                {
                    routes = PageDirectiveRegex()
                        .Matches(markup)
                        .Where(m => m.Groups["route"].Success && !string.IsNullOrWhiteSpace(m.Groups["route"].Value))
                        .Select(m => EndpointRoutes.Normalize(m.Groups["route"].Value))
                        .Distinct(StringComparer.Ordinal)
                        .ToList();
                }
                catch (RegexMatchTimeoutException)
                {
                    continue;
                }

                if (routes.Count == 0)
                    continue;

                var usings = BuildCombinedUsings(markup, hasCodeBehind ? codeBehind.Scan.Usings : null);
                var injects = ExtractRazorInjects(markup);
                var matchedRequests = new List<CqrsRequest>();
                var componentName = Path.GetFileNameWithoutExtension(razorPath);
                var primaryLocation = SourceLocation.FromLine(razorPath, root, 1, 1);

                var combinedBlockCode = new List<string>();
                foreach (var block in ExtractCodeBlocks(markup))
                {
                    combinedBlockCode.Add(block);
                    if (context.RequestsBySimple.Count > 0)
                    {
                        var blockMask = SourceMask.Build(block);
                        var masked = blockMask.CodeOnly(0, block.Length);
                        var blockScan = CSharpScanner.ScanText(block, blockMask);
                        var memberTypes = CollectCodeBlockMembers(blockScan.Classes, block, blockMask);
                        CollectDispatchedOrCreatedRequests(
                            context,
                            masked,
                            memberTypes,
                            ownerProject,
                            usings,
                            hasCodeBehind ? codeBehind.Scan.Classes.FirstOrDefault()?.Namespace : null,
                            null,
                            matchedRequests);
                    }
                }

                CSharpScanner.ClassDecl? codeBehindDecl = null;
                if (hasCodeBehind)
                {
                    foreach (var decl in codeBehind.Scan.Classes)
                    {
                        if (decl.IsAbstract || decl.BodySpan.End <= decl.BodySpan.Start)
                            continue;

                        codeBehindDecl ??= decl;
                        var bodyEnd = Math.Min(codeBehind.File.Code.Length, decl.BodySpan.End);
                        var maskedBody = codeBehind.Mask.CodeOnly(decl.BodySpan.Start, bodyEnd - decl.BodySpan.Start);
                        var memberTypes = CollectMemberTypes(decl, codeBehind.File.Code, codeBehind.Mask, codeBehind.Scan.Classes);
                        if (context.RequestsBySimple.Count > 0)
                        {
                            CollectDispatchedOrCreatedRequests(
                                context,
                                maskedBody,
                                memberTypes,
                                codeBehind.File.ProjectName,
                                usings,
                                decl.Namespace,
                                decl.ContainingTypes,
                                matchedRequests);
                        }
                    }
                }

                foreach (var route in routes)
                {
                    var entrypoint = new SystemEntrypointInfo
                    {
                        Name = componentName,
                        Kind = SystemEntrypointKind.Blazor,
                        ExecutionStyle = matchedRequests.Count > 0
                            ? SystemExecutionStyle.Cqrs
                            : SystemExecutionStyle.Direct,
                        Method = BlazorMethod,
                        RouteOrTrigger = route,
                        ProjectName = ownerProject,
                        Location = primaryLocation,
                        IsInferred = false,
                    };

                    if (hasCodeBehind && codeBehindDecl is not null)
                    {
                        var bodyEnd = Math.Min(codeBehind.File.Code.Length, codeBehindDecl.BodySpan.End);
                        var maskedBody = codeBehind.Mask.CodeOnly(codeBehindDecl.BodySpan.Start, bodyEnd - codeBehindDecl.BodySpan.Start);
                        SystemDependencyTracer.Trace(
                            context,
                            codeBehind.File,
                            codeBehind.Scan,
                            codeBehind.Mask,
                            codeBehindDecl,
                            codeBehindDecl.PrimaryConstructorParameters ?? string.Empty,
                            maskedBody + "\n" + string.Join('\n', combinedBlockCode),
                            codeBehindDecl.BodySpan.Start,
                            bodyEnd,
                            entrypoint,
                            matchedRequests,
                            injects);
                    }
                    else
                    {
                        var syntheticScan = new CSharpScanner.FileScan
                        {
                            Namespace = codeBehindDecl?.Namespace ?? string.Empty,
                        };
                        syntheticScan.Usings.AddRange(usings);
                        var blockText = string.Join('\n', combinedBlockCode);
                        var syntheticFile = new SourceFile(razorPath, blockText, ownerProject);
                        SystemDependencyTracer.Trace(
                            context,
                            syntheticFile,
                            syntheticScan,
                            SourceMask.Build(blockText),
                            null,
                            string.Empty,
                            blockText,
                            0,
                            blockText.Length,
                            entrypoint,
                            matchedRequests,
                            injects);
                    }

                    target.Add(entrypoint);
                }
            }
        }

        // Standalone component .razor.cs / ComponentBase classes with [Route("...")]
        foreach (var (file, scan, mask) in context.ParsedFiles)
        {
            if (context.IsTestProject(file.ProjectName))
                continue;
            if (handledCodeBehindPaths.Contains(file.Path))
                continue;
            if (!file.Code.Contains("Route", StringComparison.Ordinal))
                continue;

            foreach (var decl in scan.Classes)
            {
                if (decl.IsAbstract || decl.BodySpan.End <= decl.BodySpan.Start)
                    continue;

                var isComponent = file.Path.EndsWith(".razor.cs", StringComparison.OrdinalIgnoreCase) ||
                                  decl.BaseRawNames.Any(b => b is "ComponentBase" or "OwningComponentBase");
                if (!isComponent)
                    continue;

                var route = ExtractComponentRouteAttribute(file.Code, mask, decl);
                if (string.IsNullOrEmpty(route))
                    continue;

                var bodyEnd = Math.Min(file.Code.Length, decl.BodySpan.End);
                var maskedBody = mask.CodeOnly(decl.BodySpan.Start, bodyEnd - decl.BodySpan.Start);
                var memberTypes = CollectMemberTypes(decl, file.Code, mask, scan.Classes);
                var matchedRequests = new List<CqrsRequest>();
                if (context.RequestsBySimple.Count > 0)
                {
                    CollectDispatchedOrCreatedRequests(
                        context,
                        maskedBody,
                        memberTypes,
                        file.ProjectName,
                        scan.Usings,
                        decl.Namespace,
                        decl.ContainingTypes,
                        matchedRequests);
                }

                var entrypoint = new SystemEntrypointInfo
                {
                    Name = decl.Name,
                    Kind = SystemEntrypointKind.Blazor,
                    ExecutionStyle = matchedRequests.Count > 0
                        ? SystemExecutionStyle.Cqrs
                        : SystemExecutionStyle.Direct,
                    Method = BlazorMethod,
                    RouteOrTrigger = route,
                    ProjectName = file.ProjectName,
                    Location = SourceLocation.Create(file.Path, context.Result.RootPath, file.Code, decl.NameIndex),
                    IsInferred = false,
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
    }

    private static bool InheritsPageModel(SystemAnalysisContext context, CSharpScanner.ClassDecl decl)
    {
        var current = decl;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        for (var depth = 0; depth < 8 && current is not null; depth++)
        {
            if (current.BaseRawNames.Any(b => b == "PageModel" || b.EndsWith("PageModel", StringComparison.Ordinal)))
                return true;
            if (!visited.Add(current.FullName))
                break;

            CSharpScanner.ClassDecl? next = null;
            foreach (var baseName in current.BaseTypeNames)
            {
                var resolved = context.Hierarchy.TryResolveClass(
                    baseName, current.Namespace, current.Usings, current.ContainingTypes, 0, out _);
                if (resolved is not null)
                {
                    next = resolved;
                    break;
                }
            }
            current = next;
        }
        return false;
    }

    private static string ResolveRazorPageRoute(
        string codeBehindPath,
        CSharpScanner.ClassDecl declaration,
        List<string> cshtmlInjects,
        out string? resolvedCshtmlPath,
        out bool inferred)
    {
        resolvedCshtmlPath = null;
        if (codeBehindPath.EndsWith(".cshtml.cs", StringComparison.OrdinalIgnoreCase))
        {
            var cshtmlPath = codeBehindPath[..^3];
            if (File.Exists(cshtmlPath))
            {
                resolvedCshtmlPath = cshtmlPath;
                try
                {
                    var cshtml = BoundedFileReader.ReadAllText(cshtmlPath);
                    cshtmlInjects.AddRange(ExtractRazorInjects(cshtml));
                    var match = PageDirectiveRegex().Match(cshtml);
                    if (match.Success && match.Groups["route"].Success && !string.IsNullOrWhiteSpace(match.Groups["route"].Value))
                    {
                        inferred = false;
                        return EndpointRoutes.Normalize(match.Groups["route"].Value);
                    }
                }
                catch (Exception ex) when (
                    ex is IOException or UnauthorizedAccessException or InvalidDataException or SecurityException or RegexMatchTimeoutException)
                {
                }
            }
        }

        inferred = true;
        var stem = declaration.Name.EndsWith("Model", StringComparison.Ordinal) && declaration.Name.Length > "Model".Length
            ? declaration.Name[..^"Model".Length]
            : declaration.Name;
        return DerivePagesConventionRoute(codeBehindPath, stem);
    }

    private static string DerivePagesConventionRoute(string path, string fallbackName)
    {
        var normalizedPath = path.Replace('\\', '/');
        const string pagesMarker = "/Pages/";
        var idx = normalizedPath.LastIndexOf(pagesMarker, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var rel = normalizedPath[(idx + pagesMarker.Length)..];
            if (rel.EndsWith(".cshtml.cs", StringComparison.OrdinalIgnoreCase))
                rel = rel[..^".cshtml.cs".Length];
            else if (rel.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase))
                rel = rel[..^".cshtml".Length];
            else if (rel.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                rel = rel[..^".cs".Length];

            if (string.Equals(rel, "Index", StringComparison.OrdinalIgnoreCase))
                return "/";
            if (rel.EndsWith("/Index", StringComparison.OrdinalIgnoreCase))
                rel = rel[..^"/Index".Length];

            return EndpointRoutes.Normalize(rel);
        }

        return EndpointRoutes.Normalize(fallbackName);
    }

    private static List<string> FindCshtmlFiles(string root)
    {
        var fullRoot = Path.GetFullPath(root);
        try
        {
            return Directory
                .EnumerateFiles(fullRoot, "*.cshtml", SafeDiscoveryOptions)
                .Where(path => !ProjectFileDiscovery.IsSkippedPath(Path.GetRelativePath(fullRoot, path)))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return new List<string>();
        }
    }

    private static List<string> ExtractRazorInjects(string markup)
    {
        var types = new List<string>();
        try
        {
            foreach (Match m in RazorInjectRegex().Matches(markup))
            {
                var type = m.Groups["type"].Value.Trim();
                if (type.Length > 0 && !types.Contains(type, StringComparer.Ordinal))
                    types.Add(type);
            }
        }
        catch (RegexMatchTimeoutException)
        {
        }
        return types;
    }

    private static List<UsingDirective> BuildCombinedUsings(
        string markup,
        IReadOnlyList<UsingDirective>? codeBehindUsings)
    {
        var usings = new List<UsingDirective>();
        if (codeBehindUsings is not null)
            usings.AddRange(codeBehindUsings);

        try
        {
            foreach (Match m in RazorUsingRegex().Matches(markup))
            {
                var raw = m.Groups["target"].Value.Trim();
                if (raw.Length == 0 || raw.StartsWith("static ", StringComparison.Ordinal))
                    continue;

                var eq = raw.IndexOf('=');
                if (eq > 0)
                {
                    var alias = raw[..eq].Trim();
                    var target = raw[(eq + 1)..].Trim();
                    if (alias.Length > 0 && target.Length > 0)
                        usings.Add(new UsingDirective(target, alias));
                }
                else
                {
                    usings.Add(new UsingDirective(raw));
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
        }

        return usings;
    }

    private static List<string> ExtractCodeBlocks(string markup)
    {
        var blocks = new List<string>();
        try
        {
            foreach (Match match in CodeBlockHeaderRegex().Matches(markup))
            {
                var openBrace = match.Index + match.Length - 1;
                if (openBrace < 0 || openBrace >= markup.Length)
                    continue;

                var tail = markup[openBrace..];
                var mask = SourceMask.Build(tail);
                var closeBrace = mask.FindMatchingBrace(0);
                if (closeBrace > 1)
                    blocks.Add(tail[1..closeBrace]);
            }
        }
        catch (RegexMatchTimeoutException)
        {
        }

        return blocks;
    }

    private static Dictionary<string, string> CollectCodeBlockMembers(
        List<CSharpScanner.ClassDecl> classes,
        string code,
        SourceMask mask)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var temp = new CSharpScanner.ClassDecl
        {
            Name = "__RazorComponent",
            Namespace = string.Empty,
            BaseList = string.Empty,
            BaseTypeNames = new List<string>(),
            BaseRawNames = new List<string>(),
            BodySpan = (0, code.Length),
        };
        var members = new List<(string Type, string Name)>();
        ReceiverTypeMapBuilder.CollectClassLevelDeclarations(temp, code, mask, classes, members);
        foreach (var (type, name) in members)
            map.TryAdd(name.TrimStart('@'), type);
        return map;
    }

    private static Dictionary<string, string> CollectMemberTypes(
        CSharpScanner.ClassDecl decl,
        string code,
        SourceMask mask,
        List<CSharpScanner.ClassDecl> classes)
    {
        var members = new List<(string Type, string Name)>();
        ReceiverTypeMapBuilder.CollectClassLevelDeclarations(decl, code, mask, classes, members);
        foreach (var prop in decl.Properties)
        {
            if (prop.Name.Length > 0 && prop.Type.Length > 0)
                members.Add((prop.Type, prop.Name));
        }

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (type, name) in members)
            map.TryAdd(name.TrimStart('@'), type);
        return map;
    }

    private static void AddMemberDispatchedRequests(
        SystemAnalysisContext context,
        string bodyCode,
        IReadOnlyDictionary<string, string> memberTypes,
        string? projectName,
        IReadOnlyList<UsingDirective> usings,
        string? callerNamespace,
        IReadOnlyList<string> containingTypes,
        List<CqrsRequest> matchedRequests)
    {
        if (memberTypes.Count == 0 || context.RequestsBySimple.Count == 0)
            return;

        try
        {
            foreach (Match dispatch in MemberDispatchArgRegex().Matches(bodyCode))
            {
                var arg = dispatch.Groups["arg"].Value;
                if (!memberTypes.TryGetValue(arg, out var memberType))
                    continue;

                var request = EndpointRequestMatcher.ResolveRequest(
                    memberType,
                    context.RequestsBySimple,
                    projectName,
                    usings,
                    callerNamespace,
                    containingTypes);
                if (request is not null && !matchedRequests.Contains(request))
                    matchedRequests.Add(request);
            }
        }
        catch (RegexMatchTimeoutException)
        {
        }
    }

    private static void CollectDispatchedOrCreatedRequests(
        SystemAnalysisContext context,
        string maskedCode,
        IReadOnlyDictionary<string, string> memberTypes,
        string? projectName,
        IReadOnlyList<UsingDirective> usings,
        string? callerNamespace,
        IReadOnlyList<string>? containingTypes,
        List<CqrsRequest> target)
    {
        void TryAddType(string typeRef)
        {
            var picked = EndpointRequestMatcher.ResolveRequest(
                typeRef, context.RequestsBySimple, projectName, usings, callerNamespace, containingTypes);
            if (picked is not null && !target.Contains(picked))
                target.Add(picked);
        }

        try
        {
            foreach (Match match in DeepSlicePatterns.ObjectCreationRegex().Matches(maskedCode))
                TryAddType(match.Groups["type"].Value);

            foreach (Match match in DeepSlicePatterns.GenericDispatchRegex().Matches(maskedCode))
                TryAddType(match.Groups["type"].Value);

            if (memberTypes.Count > 0)
            {
                foreach (Match match in MemberDispatchArgRegex().Matches(maskedCode))
                {
                    var arg = match.Groups["arg"].Value;
                    if (memberTypes.TryGetValue(arg, out var memberType))
                        TryAddType(memberType);
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
        }
    }

    private static string? ExtractComponentRouteAttribute(
        string code,
        SourceMask mask,
        CSharpScanner.ClassDecl declaration)
    {
        var start = Math.Max(0, declaration.DeclarationIndex - 500);
        for (var i = declaration.DeclarationIndex - 1; i >= start; i--)
        {
            if (mask.IsCode(i) && code[i] is ';' or '}')
            {
                start = i + 1;
                break;
            }
        }

        var prefix = code[start..declaration.DeclarationIndex];
        try
        {
            foreach (Match m in ApiEndpointPatterns.RouteAttribute().Matches(prefix))
            {
                if (mask.IsCode(start + m.Index))
                    return EndpointRoutes.Normalize(m.Groups["route"].Value);
            }
        }
        catch (RegexMatchTimeoutException)
        {
        }

        return null;
    }
}
