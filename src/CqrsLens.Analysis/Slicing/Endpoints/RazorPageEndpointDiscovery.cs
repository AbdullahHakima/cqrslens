using System.Security;
using System.Text.RegularExpressions;

namespace CqrsLens;

/// <summary>
/// Discovers ASP.NET Core Razor Pages (<c>PageModel</c>) handler entrypoints
/// (<c>OnGet*</c>, <c>OnPost*</c>, <c>OnPut*</c>, <c>OnDelete*</c>, <c>OnPatch*</c>)
/// and links them to dispatched requests, including nested feature-slice contracts
/// and <c>[BindProperty]</c> / field dispatches.
/// </summary>
internal static partial class RazorPageEndpointDiscovery
{
    [GeneratedRegex(
        @"\b(?:public|private|protected|internal|async|virtual|override|sealed|new)\s+(?:[^\(\{;=]+?\s+)?(?<method>On(?<verb>Get|Post|Put|Delete|Patch)[A-Za-z0-9_]*)\s*\(",
        RegexOptions.Singleline,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex HandlerMethodRegex();

    [GeneratedRegex(
        @"\.\s*(?:Send|SendAsync|InvokeAsync)\s*\(\s*@?(?<arg>[A-Za-z_]\w*)\s*(?:,|\))",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex MemberDispatchArgRegex();

    [GeneratedRegex(
        @"^\s*@page\s+""(?<route>[^""]*)""",
        RegexOptions.Multiline,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex RazorPageDirectiveRegex();

    public static void Discover(
        SourceFile file,
        CSharpScanner.FileScan scan,
        SourceMask mask,
        Dictionary<string, List<CqrsRequest>> requestsBySimple,
        AnalysisResult result)
    {
        if (!file.Code.Contains("PageModel", StringComparison.Ordinal))
            return;

        foreach (var declaration in scan.Classes)
        {
            if (declaration.IsAbstract || declaration.BodySpan.End <= declaration.BodySpan.Start)
                continue;

            var isPageModel = declaration.BaseRawNames.Any(b =>
                b == "PageModel" || b.EndsWith("PageModel", StringComparison.Ordinal));
            if (!isPageModel)
                continue;

            var route = ResolveRoute(file, declaration, out var routeInferred);
            var bodyEnd = Math.Min(file.Code.Length, declaration.BodySpan.End);
            var memberTypes = CollectMemberTypes(declaration, file.Code, mask, scan.Classes);

            try
            {
                foreach (Match match in HandlerMethodRegex().Matches(file.Code, declaration.BodySpan.Start))
                {
                    if (match.Index >= bodyEnd) break;
                    if (!mask.IsCode(match.Index)) continue;
                    if (!ReferenceEquals(DeepSliceSyntax.InnermostClass(scan.Classes, match.Index), declaration))
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

                    var matchedRequests = EndpointRequestMatcher.FindReferencedRequests(
                        paramsCode,
                        bodyCode,
                        requestsBySimple,
                        file.ProjectName,
                        scan.Usings,
                        declaration.Namespace,
                        declaration.ContainingTypes);

                    AddMemberDispatchedRequests(
                        bodyCode,
                        memberTypes,
                        requestsBySimple,
                        file.ProjectName,
                        scan.Usings,
                        declaration.Namespace,
                        declaration.ContainingTypes,
                        matchedRequests);

                    if (matchedRequests.Count == 0)
                        continue;

                    var location = SourceLocation.Create(file.Path, result.RootPath, file.Code, match.Index);
                    foreach (var request in matchedRequests)
                    {
                        EndpointRecorder.Record(
                            result,
                            request,
                            new EndpointInfo
                            {
                                Name = $"{declaration.Name}.{methodName}",
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
            catch (RegexMatchTimeoutException)
            {
            }
        }
    }

    private static Dictionary<string, string> CollectMemberTypes(
        CSharpScanner.ClassDecl declaration,
        string code,
        SourceMask mask,
        List<CSharpScanner.ClassDecl> classes)
    {
        var members = new List<(string Type, string Name)>();
        ReceiverTypeMapBuilder.CollectClassLevelDeclarations(declaration, code, mask, classes, members);
        foreach (var prop in declaration.Properties)
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
        string bodyCode,
        IReadOnlyDictionary<string, string> memberTypes,
        Dictionary<string, List<CqrsRequest>> requestsBySimple,
        string? projectName,
        IReadOnlyList<UsingDirective> usings,
        string? callerNamespace,
        IReadOnlyList<string> containingTypes,
        List<CqrsRequest> matchedRequests)
    {
        if (memberTypes.Count == 0)
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
                    requestsBySimple,
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

    private static string ResolveRoute(
        SourceFile file,
        CSharpScanner.ClassDecl declaration,
        out bool inferred)
    {
        if (file.Path.EndsWith(".cshtml.cs", StringComparison.OrdinalIgnoreCase))
        {
            var cshtmlPath = file.Path[..^3];
            if (File.Exists(cshtmlPath))
            {
                try
                {
                    var cshtml = BoundedFileReader.ReadAllText(cshtmlPath);
                    var match = RazorPageDirectiveRegex().Match(cshtml);
                    if (match.Success)
                    {
                        inferred = false;
                        return EndpointRoutes.Normalize(match.Groups["route"].Value);
                    }
                }
                catch (Exception ex) when (
                    ex is IOException or UnauthorizedAccessException or InvalidDataException or SecurityException or RegexMatchTimeoutException)
                {
                    // Fall back to path-derived Razor Pages convention if .cshtml cannot be read safely.
                }
            }
        }

        inferred = true;
        var normalizedPath = file.Path.Replace('\\', '/');
        var pagesMarker = "/Pages/";
        var idx = normalizedPath.LastIndexOf(pagesMarker, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var rel = normalizedPath[(idx + pagesMarker.Length)..];
            if (rel.EndsWith(".cshtml.cs", StringComparison.OrdinalIgnoreCase))
                rel = rel[..^".cshtml.cs".Length];
            else if (rel.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                rel = rel[..^".cs".Length];

            if (string.Equals(rel, "Index", StringComparison.OrdinalIgnoreCase))
                return "/";
            if (rel.EndsWith("/Index", StringComparison.OrdinalIgnoreCase))
                rel = rel[..^"/Index".Length];

            return EndpointRoutes.Normalize(rel);
        }

        var stem = declaration.Name.EndsWith("Model", StringComparison.Ordinal) && declaration.Name.Length > "Model".Length
            ? declaration.Name[..^"Model".Length]
            : declaration.Name;
        return EndpointRoutes.Normalize(stem);
    }
}
