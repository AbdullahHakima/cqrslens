using System.Text.RegularExpressions;

namespace CqrsLens;

/// <summary>
/// Discovers gRPC service implementations (concrete classes inheriting generated <c>*Base</c>
/// types or declaring public RPC methods taking <c>ServerCallContext</c>) across non-test
/// projects, emitting one <see cref="SystemEntrypointInfo"/> per RPC method.
/// </summary>
internal static partial class GrpcSystemScanner
{
    private const string GrpcMethod = "GRPC";

    [GeneratedRegex(
        @"\bpublic\s+(?:(?<ovr>override|virtual)\s+|(?:async|sealed|new)\s+)*(?<ret>[^\(\{;=]+?)\s+(?<method>[A-Za-z_]\w*)\s*\(",
        RegexOptions.Singleline,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex PublicRpcMethodRegex();

    [GeneratedRegex(@"\bstatic\b", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
    private static partial Regex StaticKeywordRegex();

    private static readonly HashSet<string> NonGrpcBaseNames = new(StringComparer.Ordinal)
    {
        "ControllerBase", "Controller", "ApiController", "BaseController",
        "ComponentBase", "OwningComponentBase", "PageModel", "BackgroundService",
        "EndpointGroupBase", "EntityBase", "AggregateRoot", "BaseEntity",
        "DbContext", "IdentityDbContext", "Migration", "AbstractValidator",
        "ViewModelBase", "BaseViewModel",
    };

    internal static void Discover(SystemAnalysisContext context, List<SystemEntrypointInfo> target)
    {
        foreach (var (file, scan, mask) in context.ParsedFiles)
        {
            if (context.IsTestProject(file.ProjectName))
                continue;

            var fileHasServerCallContext = file.Code.Contains("ServerCallContext", StringComparison.Ordinal);

            foreach (var decl in scan.Classes)
            {
                if (decl.IsAbstract ||
                    decl.BodySpan.End <= decl.BodySpan.Start ||
                    IsStaticDeclaration(file.Code, mask, decl.DeclarationIndex))
                {
                    continue;
                }

                var hasDottedGrpcBase = decl.BaseTypeNames.Any(bt =>
                {
                    var simple = CSharpScanner.SimpleName(bt);
                    return bt.Contains('.', StringComparison.Ordinal) &&
                           simple.EndsWith("Base", StringComparison.Ordinal) &&
                           !NonGrpcBaseNames.Contains(simple);
                });

                if (!hasDottedGrpcBase && !fileHasServerCallContext)
                    continue;

                var bodyEnd = Math.Min(file.Code.Length, decl.BodySpan.End);
                MatchCollection matches;
                try
                {
                    matches = PublicRpcMethodRegex().Matches(file.Code, decl.BodySpan.Start);
                }
                catch (RegexMatchTimeoutException)
                {
                    continue;
                }

                foreach (Match match in matches)
                {
                    if (match.Index >= bodyEnd)
                        break;
                    if (!mask.IsCode(match.Index))
                        continue;
                    if (!ReferenceEquals(DeepSliceSyntax.InnermostClass(scan.Classes, match.Index), decl))
                        continue;

                    var methodName = match.Groups["method"].Value;
                    if (string.Equals(methodName, decl.Name, StringComparison.Ordinal) ||
                        methodName is "ToString" or "Equals" or "GetHashCode" or "Dispose" or "DisposeAsync")
                        continue;

                    var retType = match.Groups["ret"].Value;
                    if (ContainsStaticModifier(retType))
                        continue;

                    var openParen = match.Index + match.Length - 1;
                    var closeParen = DeepSliceSyntax.FindMatchingParen(file.Code, mask, openParen);
                    if (closeParen < 0 || closeParen >= bodyEnd)
                        continue;

                    var paramsCode = mask.CodeOnly(openParen + 1, Math.Max(0, closeParen - openParen - 1));
                    if (IsExtensionMethod(paramsCode))
                        continue;

                    var hasServerCallContextParam = paramsCode.Contains("ServerCallContext", StringComparison.Ordinal);
                    var isOverride = match.Groups["ovr"].Success &&
                        string.Equals(match.Groups["ovr"].Value, "override", StringComparison.Ordinal);

                    if (!hasServerCallContextParam &&
                        !(hasDottedGrpcBase && isOverride && IsAsyncGrpcReturnType(retType) && !string.IsNullOrWhiteSpace(paramsCode)))
                    {
                        continue;
                    }

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

                    var trigger = $"{decl.Name}.{methodName}";
                    var entrypoint = new SystemEntrypointInfo
                    {
                        Name = trigger,
                        Kind = SystemEntrypointKind.Grpc,
                        ExecutionStyle = matchedRequests.Count > 0
                            ? SystemExecutionStyle.Cqrs
                            : SystemExecutionStyle.Direct,
                        Method = GrpcMethod,
                        RouteOrTrigger = trigger,
                        ProjectName = file.ProjectName,
                        Location = SourceLocation.Create(file.Path, context.Result.RootPath, file.Code, match.Index),
                        IsInferred = false,
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
                        scopeToMethodBody: true);

                    target.Add(entrypoint);
                }
            }
        }
    }

    private static readonly char[] DeclarationBoundaries = [';', '{', '}', ']'];

    private static bool IsStaticDeclaration(string code, SourceMask mask, int declarationIndex)
    {
        var start = Math.Max(0, declarationIndex - 300);
        var segment = mask.CodeOnly(start, declarationIndex - start);
        var boundary = segment.LastIndexOfAny(DeclarationBoundaries);
        if (boundary >= 0)
            segment = segment[(boundary + 1)..];

        try
        {
            return StaticKeywordRegex().IsMatch(segment);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static bool ContainsStaticModifier(string returnSegment)
    {
        try
        {
            return StaticKeywordRegex().IsMatch(returnSegment);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static bool IsExtensionMethod(string paramsCode)
    {
        var i = 0;
        while (i < paramsCode.Length)
        {
            while (i < paramsCode.Length && char.IsWhiteSpace(paramsCode[i]))
                i++;

            if (i < paramsCode.Length && paramsCode[i] == '[')
            {
                var depth = 0;
                while (i < paramsCode.Length)
                {
                    if (paramsCode[i] == '[')
                    {
                        depth++;
                    }
                    else if (paramsCode[i] == ']')
                    {
                        depth--;
                        i++;
                        if (depth == 0)
                            break;
                        continue;
                    }
                    i++;
                }
                continue;
            }
            break;
        }

        return paramsCode.AsSpan(i).StartsWith("this", StringComparison.Ordinal) &&
               i + 4 < paramsCode.Length &&
               char.IsWhiteSpace(paramsCode[i + 4]);
    }

    private static bool IsAsyncGrpcReturnType(string rawRet)
    {
        if (rawRet.Contains("void", StringComparison.Ordinal))
            return false;

        return rawRet.Contains("Task", StringComparison.Ordinal) ||
               rawRet.Contains("ValueTask", StringComparison.Ordinal) ||
               rawRet.Contains("IAsyncEnumerable", StringComparison.Ordinal);
    }
}
