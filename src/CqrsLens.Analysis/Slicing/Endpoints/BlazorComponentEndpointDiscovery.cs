using System.Security;
using System.Text.RegularExpressions;
using static CqrsLens.DeepSlicePatterns;

namespace CqrsLens;

/// <summary>
/// Discovers Blazor routed component entrypoints (<c>@page "..."</c> in <c>.razor</c> files,
/// plus standalone <c>ComponentBase</c> / <c>OwningComponentBase</c> classes with
/// <c>[Route("...")]</c>) and links them only when a request is explicitly dispatched or
/// instantiated inside component logic.
/// </summary>
internal static partial class BlazorComponentEndpointDiscovery
{
    private const string BlazorMethod = "BLAZOR";

    [GeneratedRegex(
        @"^\s*@page\s+""(?<route>[^""]+)""",
        RegexOptions.Multiline,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex PageDirectiveRegex();

    [GeneratedRegex(
        @"^\s*@using\s+(?<target>[^\r\n;]+)",
        RegexOptions.Multiline,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex RazorUsingDirectiveRegex();

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

    public static void Discover(
        List<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        Dictionary<string, List<CqrsRequest>> requestsBySimple,
        AnalysisResult result,
        SourcePathFilter? pathFilter = null)
    {
        var filesByPath = parsedFiles.ToDictionary(
            f => f.File.Path,
            f => f,
            StringComparer.OrdinalIgnoreCase);

        var handledCodeBehindPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(result.RootPath) && Directory.Exists(result.RootPath))
        {
            var razorFiles = ProjectFileDiscovery.FindRazorFiles(result.RootPath);
            foreach (var razorPath in razorFiles)
            {
                if (pathFilter is not null)
                {
                    var rel = Path.GetRelativePath(result.RootPath, razorPath).Replace(Path.DirectorySeparatorChar, '/');
                    if (!pathFilter.IsIncluded(rel))
                        continue;
                }

                string markup;
                try
                {
                    markup = BoundedFileReader.ReadAllText(razorPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or SecurityException)
                {
                    continue;
                }

                List<string> routes;
                try
                {
                    routes = PageDirectiveRegex()
                        .Matches(markup)
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

                var codeBehindPath = razorPath + ".cs";
                var hasCodeBehind = filesByPath.TryGetValue(codeBehindPath, out var codeBehind);
                if (hasCodeBehind)
                    handledCodeBehindPaths.Add(codeBehindPath);

                var usings = BuildCombinedUsings(markup, hasCodeBehind ? codeBehind.Scan.Usings : null);
                var ownerProject = hasCodeBehind
                    ? codeBehind.File.ProjectName
                    : ProjectLocator.FindOwningProject(result.Projects, razorPath)?.Name;

                var matchedRequests = new List<CqrsRequest>();
                foreach (var block in ExtractCodeBlocks(markup))
                {
                    var blockMask = SourceMask.Build(block);
                    var masked = blockMask.CodeOnly(0, block.Length);
                    var blockScan = CSharpScanner.ScanText(block, blockMask);
                    var memberTypes = CollectCodeBlockMembers(blockScan.Classes, block, blockMask);
                    CollectDispatchedOrCreatedRequests(
                        masked,
                        memberTypes,
                        requestsBySimple,
                        ownerProject,
                        usings,
                        callerNamespace: hasCodeBehind ? codeBehind.Scan.Classes.FirstOrDefault()?.Namespace : null,
                        containingTypes: null,
                        matchedRequests);
                }

                var primaryLocation = SourceLocation.FromLine(razorPath, result.RootPath, 1, 1);

                if (hasCodeBehind)
                {
                    foreach (var decl in codeBehind.Scan.Classes)
                    {
                        if (decl.IsAbstract || decl.BodySpan.End <= decl.BodySpan.Start)
                            continue;

                        var bodyEnd = Math.Min(codeBehind.File.Code.Length, decl.BodySpan.End);
                        var maskedBody = codeBehind.Mask.CodeOnly(decl.BodySpan.Start, bodyEnd - decl.BodySpan.Start);
                        var memberTypes = CollectClassMemberTypes(decl, codeBehind.File.Code, codeBehind.Mask, codeBehind.Scan.Classes);
                        CollectDispatchedOrCreatedRequests(
                            maskedBody,
                            memberTypes,
                            requestsBySimple,
                            codeBehind.File.ProjectName,
                            usings,
                            decl.Namespace,
                            decl.ContainingTypes,
                            matchedRequests);
                    }
                }

                if (matchedRequests.Count == 0)
                    continue;

                var componentName = Path.GetFileNameWithoutExtension(razorPath);
                foreach (var route in routes)
                {
                    foreach (var request in matchedRequests)
                    {
                        EndpointRecorder.Record(
                            result,
                            request,
                            new EndpointInfo
                            {
                                Name = componentName,
                                HttpMethod = BlazorMethod,
                                Route = route,
                                TargetRequestName = request.Name,
                                TargetRequestNamespace = request.Namespace,
                                ProjectName = ownerProject,
                                Location = primaryLocation,
                                IsInferred = false,
                            });
                    }
                }
            }
        }

        // Standalone component code-behind files with [Route("...")] and no @page in .razor.
        foreach (var (file, scan, mask) in parsedFiles)
        {
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
                var memberTypes = CollectClassMemberTypes(decl, file.Code, mask, scan.Classes);
                var matchedRequests = new List<CqrsRequest>();
                CollectDispatchedOrCreatedRequests(
                    maskedBody,
                    memberTypes,
                    requestsBySimple,
                    file.ProjectName,
                    scan.Usings,
                    decl.Namespace,
                    decl.ContainingTypes,
                    matchedRequests);

                if (matchedRequests.Count == 0)
                    continue;

                var location = SourceLocation.Create(file.Path, result.RootPath, file.Code, decl.NameIndex);
                foreach (var request in matchedRequests)
                {
                    EndpointRecorder.Record(
                        result,
                        request,
                        new EndpointInfo
                        {
                            Name = decl.Name,
                            HttpMethod = BlazorMethod,
                            Route = route,
                            TargetRequestName = request.Name,
                            TargetRequestNamespace = request.Namespace,
                            ProjectName = file.ProjectName,
                            Location = location,
                            IsInferred = false,
                        });
                }
            }
        }
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
            foreach (Match m in RazorUsingDirectiveRegex().Matches(markup))
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
            Namespace = "",
            BaseList = "",
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

    private static Dictionary<string, string> CollectClassMemberTypes(
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

    private static void CollectDispatchedOrCreatedRequests(
        string maskedCode,
        IReadOnlyDictionary<string, string> memberTypes,
        Dictionary<string, List<CqrsRequest>> requestsBySimple,
        string? projectName,
        IReadOnlyList<UsingDirective> usings,
        string? callerNamespace,
        IReadOnlyList<string>? containingTypes,
        List<CqrsRequest> target)
    {
        void TryAddType(string typeRef)
        {
            var picked = EndpointRequestMatcher.ResolveRequest(
                typeRef, requestsBySimple, projectName, usings, callerNamespace, containingTypes);
            if (picked is not null && !target.Contains(picked))
                target.Add(picked);
        }

        try
        {
            foreach (Match match in ObjectCreationRegex().Matches(maskedCode))
                TryAddType(match.Groups["type"].Value);

            foreach (Match match in GenericDispatchRegex().Matches(maskedCode))
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
