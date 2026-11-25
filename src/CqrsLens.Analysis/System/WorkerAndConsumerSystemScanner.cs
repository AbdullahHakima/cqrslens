using System.Text.RegularExpressions;

namespace CqrsLens;

/// <summary>
/// Discovers background workers (<c>BackgroundService</c>, <c>IHostedService</c>,
/// <c>IHostedLifecycleService</c>, <c>IJob</c>) and message/integration-event consumers
/// (<c>IIntegrationEventHandler&lt;T&gt;</c>, <c>IConsumer&lt;T&gt;</c>,
/// <c>IHandleMessages&lt;T&gt;</c>, <c>IEventHandler&lt;T&gt;</c>, and <c>[CapSubscribe]</c>)
/// across non-test projects.
/// </summary>
internal static partial class WorkerAndConsumerSystemScanner
{
    private const string WorkerMethod = "WORKER";
    private const string ConsumeMethod = "CONSUME";

    private static readonly string[] ConsumerInterfaces =
    {
        "IIntegrationEventHandler",
        "IConsumer",
        "IHandleMessages",
        "IEventHandler",
    };

    private static readonly HashSet<string> WorkerBaseTypes = new(StringComparer.Ordinal)
    {
        "BackgroundService",
        "IHostedService",
        "IHostedLifecycleService",
        "IJob",
    };

    [GeneratedRegex(
        @"(?<attrs>(?:\[\s*[^\[\]]*\]\s*)+)(?:(?:public|protected|internal|private|static|virtual|override|async|sealed|new)\s+)*[^\(\{;=]+?\s+(?<method>[A-Za-z_]\w*)\s*\(",
        RegexOptions.Singleline,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex AttributedMethodRegex();

    [GeneratedRegex(
        @"\[CapSubscribe\s*\(\s*(?:@?""(?<topic>[^""]+)""|(?<sym>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*))\s*(?:,|\))",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex CapSubscribeAttrRegex();

    internal static void Discover(SystemAnalysisContext context, List<SystemEntrypointInfo> target)
    {
        var hasCustomConsumerInterfaces = ConsumerInterfaces.Any(ci => context.Hierarchy.HasInterface(ci));
        var workerCache = new Dictionary<CSharpScanner.ClassDecl, bool>();

        foreach (var (file, scan, mask) in context.ParsedFiles)
        {
            if (context.IsTestProject(file.ProjectName))
                continue;

            var hasCapSubscribe = file.Code.Contains("CapSubscribe", StringComparison.Ordinal);

            foreach (var decl in scan.Classes)
            {
                if (decl.IsAbstract || decl.BodySpan.End <= decl.BodySpan.Start)
                    continue;

                var hasBase = !string.IsNullOrWhiteSpace(decl.BaseList);
                var isWorker = hasBase && IsWorkerClass(context, decl, workerCache);
                var consumedMessages = hasBase
                    ? CollectConsumedMessageTypes(context, decl, hasCustomConsumerInterfaces)
                    : Array.Empty<string>();

                var bodyEnd = Math.Min(file.Code.Length, decl.BodySpan.End);
                string? maskedBody = null;
                if (isWorker || consumedMessages.Count > 0)
                {
                    maskedBody = mask.CodeOnly(decl.BodySpan.Start, bodyEnd - decl.BodySpan.Start);
                    if (isWorker)
                        EmitWorker(context, file, scan, mask, decl, maskedBody, bodyEnd, target);

                    foreach (var messageType in consumedMessages)
                    {
                        EmitMessageConsumer(
                            context, file, scan, mask, decl, decl.Name, messageType,
                            decl.PrimaryConstructorParameters ?? string.Empty,
                            maskedBody, decl.BodySpan.Start, bodyEnd,
                            SourceLocation.Create(file.Path, context.Result.RootPath, file.Code, decl.NameIndex),
                            target);
                    }
                }

                if (hasCapSubscribe)
                {
                    DiscoverCapSubscribers(context, file, scan, mask, decl, bodyEnd, target);
                }
            }
        }
    }

    private static void EmitWorker(
        SystemAnalysisContext context,
        SourceFile file,
        CSharpScanner.FileScan scan,
        SourceMask mask,
        CSharpScanner.ClassDecl decl,
        string maskedBody,
        int bodyEnd,
        List<SystemEntrypointInfo> target)
    {
        var matchedRequests = context.RequestsBySimple.Count > 0
            ? EndpointRequestMatcher.FindReferencedRequests(
                decl.PrimaryConstructorParameters ?? string.Empty,
                maskedBody,
                context.RequestsBySimple,
                file.ProjectName,
                scan.Usings,
                decl.Namespace,
                decl.ContainingTypes)
            : new List<CqrsRequest>();

        var entrypoint = new SystemEntrypointInfo
        {
            Name = decl.Name,
            Kind = SystemEntrypointKind.Worker,
            ExecutionStyle = SystemExecutionStyle.Worker,
            Method = WorkerMethod,
            RouteOrTrigger = decl.Name,
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

    private static void EmitMessageConsumer(
        SystemAnalysisContext context,
        SourceFile file,
        CSharpScanner.FileScan scan,
        SourceMask mask,
        CSharpScanner.ClassDecl decl,
        string name,
        string trigger,
        string paramsCode,
        string bodyCode,
        int spanStart,
        int spanEnd,
        SourceLocation? location,
        List<SystemEntrypointInfo> target)
    {
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

        var entrypoint = new SystemEntrypointInfo
        {
            Name = name,
            Kind = SystemEntrypointKind.MessageConsumer,
            ExecutionStyle = SystemExecutionStyle.EventConsumer,
            Method = ConsumeMethod,
            RouteOrTrigger = trigger,
            ProjectName = file.ProjectName,
            Location = location,
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
            spanStart,
            spanEnd,
            entrypoint,
            matchedRequests);

        target.Add(entrypoint);
    }

    private static void DiscoverCapSubscribers(
        SystemAnalysisContext context,
        SourceFile file,
        CSharpScanner.FileScan scan,
        SourceMask mask,
        CSharpScanner.ClassDecl decl,
        int bodyEnd,
        List<SystemEntrypointInfo> target)
    {
        MatchCollection matches;
        try
        {
            matches = AttributedMethodRegex().Matches(file.Code, decl.BodySpan.Start);
        }
        catch (RegexMatchTimeoutException)
        {
            return;
        }

        foreach (Match match in matches)
        {
            if (match.Index >= bodyEnd)
                break;
            if (!mask.IsCode(match.Index))
                continue;
            if (!ReferenceEquals(DeepSliceSyntax.InnermostClass(scan.Classes, match.Index), decl))
                continue;

            var attrs = match.Groups["attrs"].Value;
            Match capMatch;
            try
            {
                capMatch = CapSubscribeAttrRegex().Match(attrs);
            }
            catch (RegexMatchTimeoutException)
            {
                continue;
            }

            if (!capMatch.Success)
                continue;

            var topic = capMatch.Groups["topic"].Success
                ? capMatch.Groups["topic"].Value
                : capMatch.Groups["sym"].Value;
            if (string.IsNullOrWhiteSpace(topic))
                continue;

            var methodName = match.Groups["method"].Value;
            var openParen = match.Index + match.Length - 1;
            var closeParen = DeepSliceSyntax.FindMatchingParen(file.Code, mask, openParen);
            if (closeParen < 0 || closeParen >= bodyEnd)
                continue;

            var paramsCode = mask.CodeOnly(openParen + 1, Math.Max(0, closeParen - openParen - 1));
            var methodStart = closeParen + 1;
            var methodEnd = DeepSliceSyntax.FindMethodEnd(file.Code, mask, methodStart, bodyEnd);
            var bodyCode = mask.CodeOnly(methodStart, Math.Max(0, methodEnd - methodStart));

            EmitMessageConsumer(
                context,
                file,
                scan,
                mask,
                decl,
                $"{decl.Name}.{methodName}",
                topic,
                paramsCode,
                bodyCode,
                methodStart,
                methodEnd,
                SourceLocation.Create(file.Path, context.Result.RootPath, file.Code, match.Index),
                target);
        }
    }

    private static bool IsWorkerClass(
        SystemAnalysisContext context,
        CSharpScanner.ClassDecl decl,
        Dictionary<CSharpScanner.ClassDecl, bool> cache)
    {
        if (cache.TryGetValue(decl, out var cached))
            return cached;

        cache[decl] = false;
        if (decl.BaseRawNames.Any(WorkerBaseTypes.Contains))
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
            if (resolved is not null && IsWorkerClass(context, resolved, cache))
            {
                cache[decl] = true;
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<string> CollectConsumedMessageTypes(
        SystemAnalysisContext context,
        CSharpScanner.ClassDecl decl,
        bool hasCustomConsumerInterfaces)
    {
        var messages = new List<string>();
        var bindings = GenericBindings.Empty;
        var current = decl;
        var visited = new HashSet<string>(StringComparer.Ordinal);

        for (var depth = 0; depth < 8 && current is not null; depth++)
        {
            foreach (var root in ConsumerInterfaces)
            {
                if (!current.BaseRawNames.Contains(root, StringComparer.Ordinal))
                    continue;

                foreach (var args in CSharpScanner.ExtractAllGenericArguments(current.BaseList, root))
                {
                    if (args.Count < 1)
                        continue;

                    var rawTarget = bindings.Count == 0
                        ? args[0]
                        : GenericBindings.Substitute(args[0], bindings);
                    var simple = CSharpScanner.UnqualifiedTypeName(rawTarget);
                    if (string.IsNullOrEmpty(simple) ||
                        decl.TypeParameters.Contains(simple, StringComparer.Ordinal) ||
                        current.TypeParameters.Contains(simple, StringComparer.Ordinal))
                    {
                        continue;
                    }

                    if (!messages.Contains(simple, StringComparer.Ordinal))
                        messages.Add(simple);
                }
            }

            if (!visited.Add(current.FullName))
                break;

            CSharpScanner.ClassDecl? next = null;
            IReadOnlyDictionary<string, string> nextBindings = GenericBindings.Empty;
            foreach (var part in CSharpScanner.SplitTopLevel(current.BaseList))
            {
                var (baseName, rawArgs) = CSharpScanner.ParseTypeReference(part);
                if (!context.ClassesBySimpleName.ContainsKey(CSharpScanner.SimpleName(baseName)))
                    continue;

                var substitutedArgs = bindings.Count == 0
                    ? rawArgs
                    : rawArgs.Select(a => GenericBindings.Substitute(a, bindings)).ToList();

                var resolved = context.Hierarchy.TryResolveClass(
                    baseName, current.Namespace, current.Usings, current.ContainingTypes, substitutedArgs.Count, out _);
                if (resolved is not null)
                {
                    next = resolved;
                    nextBindings = GenericBindings.Bind(resolved.TypeParameters, substitutedArgs);
                    break;
                }
            }

            current = next;
            bindings = nextBindings;
        }

        // Only walk transitive interface hierarchies if any consumer interface is declared in source.
        if (hasCustomConsumerInterfaces)
        {
            var implemented = new List<ImplementedInterface>();
            context.Hierarchy.CollectImplementedInterfaces(decl, implemented);
            foreach (var impl in implemented)
            {
                if (impl.IsOpenGeneric || impl.Arguments.Count < 1)
                    continue;
                var ifaceSimple = CSharpScanner.SimpleNameFromIdentity(impl.Identity);
                if (!ConsumerInterfaces.Contains(ifaceSimple, StringComparer.Ordinal))
                    continue;
                var simple = CSharpScanner.UnqualifiedTypeName(impl.Arguments[0]);
                if (!string.IsNullOrEmpty(simple) &&
                    !decl.TypeParameters.Contains(simple, StringComparer.Ordinal) &&
                    !messages.Contains(simple, StringComparer.Ordinal))
                {
                    messages.Add(simple);
                }
            }
        }

        return messages;
    }
}
