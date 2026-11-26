using System.Text.RegularExpressions;
using static CqrsLens.DeepSlicePatterns;

namespace CqrsLens;

/// <summary>
/// Traces downstream called services, touched DbContexts, touched entities, and published
/// messages for a discovered system entrypoint across CQRS handlers, direct parameter/constructor
/// dependencies, service locator calls, and memoized multi-hop DI chains.
/// </summary>
internal static partial class SystemDependencyTracer
{
    private const int MaxServiceHops = 4;

    [GeneratedRegex(
        @"\.\s*(?:GetRequiredService|GetService|GetRequiredKeyedService|GetKeyedService)\s*<\s*(?<type>(?:global::)?[A-Za-z_]\w*(?:\s*\.\s*[A-Za-z_]\w*)*(?:\s*<[^;{}()]*>)?)\s*>\s*\(",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex ServiceLocatorRegex();

    [GeneratedRegex(
        @"(?<![A-Za-z0-9_])new\s+(?<type>(?:global::)?[A-Za-z_]\w*(?:\s*\.\s*[A-Za-z_]\w*)*)\s*[\(\{]",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex ObjectCreateRegex();

    private static readonly HashSet<string> FrameworkSimpleTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "object", "string", "bool", "byte", "sbyte", "short", "ushort", "int", "uint",
        "long", "ulong", "float", "double", "decimal", "char", "nint", "nuint", "dynamic",
        "Guid", "DateTime", "DateTimeOffset", "DateOnly", "TimeOnly", "TimeSpan", "Uri",
        "CancellationToken", "HttpContext", "HttpRequest", "HttpResponse", "ClaimsPrincipal",
        "IFormFile", "IFormFileCollection", "IFormCollection", "Stream", "PipeReader",
        "ServerCallContext", "IServiceProvider", "IServiceScope", "IServiceScopeFactory",
        "IConfiguration", "IHostEnvironment", "IWebHostEnvironment", "IHostApplicationLifetime",
        "ILogger", "ILoggerFactory", "IOptions", "IOptionsSnapshot", "IOptionsMonitor",
        "IMediator", "ISender", "IPublisher", "IMessageBus", "IValidator",
        "NavigationManager", "IJSRuntime", "IDistributedCache", "IMemoryCache",
        "HttpClient", "LinkGenerator", "ProblemDetails", "ValidationProblemDetails",
        "IEndpointRouteBuilder", "WebApplication", "RouteGroupBuilder", "IApplicationBuilder",
        "IServiceCollection", "ModelStateDictionary", "TempDataDictionary", "ViewDataDictionary",
        "IEnumerable", "IReadOnlyList", "IReadOnlyCollection", "IList", "ICollection",
        "IDictionary", "IReadOnlyDictionary", "ISet", "IReadOnlySet", "IQueryable",
        "IOrderedQueryable", "IAsyncEnumerable", "IResult", "IActionResult",
        "IDisposable", "IAsyncDisposable", "IEquatable", "IComparable", "IFormatProvider",
        "IPrincipal", "IIdentity", "IHttpContextAccessor",
        "T", "TKey", "TValue", "TResult", "TResponse", "TRequest", "TEntity",
    };

    private static readonly string[] ServiceNameSuffixes =
    {
        "Service", "Services", "Repository", "Repositories", "Repo", "Store", "Manager",
        "Queries", "QueryService", "Client", "Gateway", "Handler", "Publisher", "Sender",
        "Provider", "Factory", "Helper", "Engine", "Processor", "Worker", "Accessor",
        "UnitOfWork", "EventBus", "Bus", "Context", "Adapter", "Proxy", "Facade",
    };

    private static readonly string[] DtoSuffixes =
    {
        "Dto", "Request", "Command", "Query", "Response", "Result", "Reply",
        "Model", "ViewModel", "Payload", "Args", "Options", "Settings", "Config",
    };

    internal static void Trace(
        SystemAnalysisContext context,
        SourceFile file,
        CSharpScanner.FileScan scan,
        SourceMask mask,
        CSharpScanner.ClassDecl? ownerClass,
        string parametersCode,
        string bodyCode,
        int bodySpanStart,
        int bodySpanEnd,
        SystemEntrypointInfo entrypoint,
        IReadOnlyList<CqrsRequest> matchedRequests,
        IReadOnlyList<string>? extraInjectedTypes = null,
        bool scopeToMethodBody = false)
    {
        foreach (var req in matchedRequests)
            SystemAnalysisContext.AddUniqueOrdinal(entrypoint.TargetRequests, req.FullName);

        if (matchedRequests.Count > 0 &&
            entrypoint.ExecutionStyle == SystemExecutionStyle.Direct)
        {
            entrypoint.ExecutionStyle = SystemExecutionStyle.Cqrs;
        }

        List<(string Body, int Start, int End)>? visitedHelpers = null;
        HashSet<string>? referencedIdentifiers = null;
        ClassMethodIndex? methodIndex = null;
        if (scopeToMethodBody && ownerClass is not null && !string.IsNullOrWhiteSpace(bodyCode))
        {
            methodIndex = context.GetClassMethodIndex(ownerClass, file, scan, mask);
            methodIndex.CollectScopedBodyAndIdentifiers(bodyCode, out referencedIdentifiers, out visitedHelpers);
        }

        var rootTypes = CollectRootDependencyTypes(
            context, ownerClass, file, mask, scan.Classes, parametersCode, bodyCode,
            extraInjectedTypes, matchedRequests, scopeToMethodBody, methodIndex, referencedIdentifiers, visitedHelpers);

        var currentNs = ownerClass?.Namespace ?? scan.Namespace;
        IReadOnlyList<string> containing = ownerClass?.ContainingTypes ?? (IReadOnlyList<string>)Array.Empty<string>();

        var visitedClasses = new HashSet<string>(StringComparer.Ordinal);
        if (ownerClass is not null)
            visitedClasses.Add(ownerClass.FullName);

        var visitedTypes = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<(CSharpScanner.ClassDecl Class, int Depth)>();

        foreach (var rootType in rootTypes)
        {
            ResolveTypeReference(
                context,
                rootType,
                currentNs,
                scan.Usings,
                containing,
                ownerClass,
                matchedRequests,
                depth: 0,
                visitedTypes,
                visitedClasses,
                entrypoint.CalledServices,
                entrypoint.TouchedDbContexts,
                entrypoint.TouchedDataStores,
                entrypoint.TouchedEntities,
                queue);
        }

        // Scan entrypoint's own method/lambda body for direct DbSet/entity touches & published events.
        CollectDirectBodyEffects(
            context,
            file,
            scan,
            mask,
            ownerClass,
            parametersCode,
            bodyCode,
            bodySpanStart,
            bodySpanEnd,
            entrypoint.TouchedDbContexts,
            entrypoint.TouchedDataStores,
            entrypoint.TouchedEntities,
            entrypoint.PublishedMessages,
            bodyAlreadyMasked: true);

        if (scopeToMethodBody)
        {
            DataStoreDiscovery.InspectMethodBody(
                context,
                file,
                scan,
                ownerClass,
                bodyCode,
                bodySpanStart,
                bodySpanEnd,
                entrypoint.TouchedDataStores,
                entrypoint.TouchedEntities);
        }
        else if (ownerClass is not null && context.GetClassDataAccess(ownerClass) is { } ownerAccess)
        {
            foreach (var ds in ownerAccess.DataStores)
                SystemAnalysisContext.AddUniqueOrdinal(entrypoint.TouchedDataStores, ds);
            foreach (var ent in ownerAccess.Entities)
                SystemAnalysisContext.AddUniqueOrdinal(entrypoint.TouchedEntities, ent);
        }

        var combinedBodyCode = bodyCode;
        if (visitedHelpers is { Count: > 0 })
        {
            foreach (var helper in visitedHelpers)
            {
                CollectDirectBodyEffects(
                    context,
                    file,
                    scan,
                    mask,
                    ownerClass,
                    string.Empty,
                    helper.Body,
                    helper.Start,
                    helper.End,
                    entrypoint.TouchedDbContexts,
                    entrypoint.TouchedDataStores,
                    entrypoint.TouchedEntities,
                    entrypoint.PublishedMessages,
                    bodyAlreadyMasked: true);

                DataStoreDiscovery.InspectMethodBody(
                    context,
                    file,
                    scan,
                    ownerClass,
                    helper.Body,
                    helper.Start,
                    helper.End,
                    entrypoint.TouchedDataStores,
                    entrypoint.TouchedEntities);
            }

            combinedBodyCode = bodyCode + "\n" + string.Join('\n', visitedHelpers.Select(h => h.Body));
        }

        // BFS over memoized per-class summaries (depth <= MaxServiceHops) so shared services
        // are analyzed at most once across the entire solution.
        while (queue.Count > 0)
        {
            var (svcDecl, depth) = queue.Dequeue();
            var summary = GetOrComputeClassEffects(context, svcDecl);

            foreach (var svc in summary.DirectCalledServices)
                SystemAnalysisContext.AddUniqueOrdinal(entrypoint.CalledServices, svc);
            foreach (var db in summary.DirectDbContexts)
                SystemAnalysisContext.AddUniqueOrdinal(entrypoint.TouchedDbContexts, db);
            foreach (var ds in summary.DirectDataStores)
                SystemAnalysisContext.AddUniqueOrdinal(entrypoint.TouchedDataStores, ds);
            foreach (var ent in summary.DirectEntities)
                SystemAnalysisContext.AddUniqueOrdinal(entrypoint.TouchedEntities, ent);
            foreach (var msg in summary.DirectPublishedMessages)
                SystemAnalysisContext.AddUniqueOrdinal(entrypoint.PublishedMessages, msg);

            if (depth < MaxServiceHops)
            {
                foreach (var nextDecl in summary.NextServiceClasses)
                {
                    if (visitedClasses.Add(nextDecl.FullName))
                        queue.Enqueue((nextDecl, depth + 1));
                }
            }
        }

        RollUpCqrsEffects(context, matchedRequests, entrypoint);
        CollectScopedDbContextSetTouches(context, combinedBodyCode, entrypoint);
        SortEntrypointLists(entrypoint);
    }

    internal static void PopulateServiceClassEffects(SystemAnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var result = context.Result;
        var excludedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in result.Requests)
            excludedNames.Add(r.FullName);
        foreach (var h in result.Handlers)
            excludedNames.Add(h.FullName);
        foreach (var nh in result.NotificationHandlers)
            excludedNames.Add(nh.FullName);
        foreach (var v in result.Validators)
            excludedNames.Add(v.FullName);
        foreach (var n in result.Notifications)
            excludedNames.Add(n.FullName);
        foreach (var db in result.DbContexts)
            excludedNames.Add(db.FullName);

        var candidateClasses = new HashSet<CSharpScanner.ClassDecl>();

        void AddCandidateByName(string? fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
                return;
            if (context.ClassByFullName.TryGetValue(fullName, out var decl))
                candidateClasses.Add(decl);
        }

        foreach (var sr in result.ServiceResolutions)
        {
            AddCandidateByName(sr.Implementation);
            foreach (var candidate in sr.Candidates)
            {
                if (candidate.DeclaredInScan)
                    AddCandidateByName(candidate.Type);
            }
        }

        foreach (var edge in result.ServiceCallEdges)
        {
            AddCandidateByName(edge.From);
            AddCandidateByName(edge.To);
        }

        foreach (var edge in result.ResolvedEdges)
        {
            AddCandidateByName(edge.From);
            AddCandidateByName(edge.Via);
        }

        foreach (var ep in result.SystemEntrypoints)
        {
            foreach (var rawSvc in ep.CalledServices)
            {
                var arrow = rawSvc.IndexOf(" -> ", StringComparison.Ordinal);
                AddCandidateByName(arrow >= 0 ? rawSvc[(arrow + 4)..].Trim() : rawSvc.Trim());
            }
        }

        foreach (var decl in context.Classes)
        {
            if (!decl.IsAbstract && IsServiceLikeClass(context, decl))
                candidateClasses.Add(decl);
        }

        var effectsByFullName = new Dictionary<string, (
            string? ProjectName,
            SourceLocation? Location,
            List<string> CalledServices,
            List<string> DbContexts,
            List<string> DataStores,
            List<string> Entities,
            List<string> PublishedMessages)>(StringComparer.Ordinal);

        foreach (var decl in candidateClasses
            .OrderBy(c => c.FullName, StringComparer.Ordinal)
            .ThenBy(c => c.NameIndex))
        {
            if (decl.IsAbstract || decl.TypeParameters.Count > 0)
                continue;
            if (excludedNames.Contains(decl.FullName))
                continue;
            if (!context.FileByClass.TryGetValue(decl, out var entry))
                continue;
            if (context.IsTestProject(entry.File.ProjectName))
                continue;

            var summary = GetOrComputeClassEffects(context, decl);
            if (summary.DirectCalledServices.Count == 0 &&
                summary.DirectDbContexts.Count == 0 &&
                summary.DirectDataStores.Count == 0 &&
                summary.DirectEntities.Count == 0 &&
                summary.DirectPublishedMessages.Count == 0)
            {
                continue;
            }

            if (!effectsByFullName.TryGetValue(decl.FullName, out var bucket))
            {
                var location = SourceLocation.Create(entry.File.Path, result.RootPath, entry.File.Code, decl.NameIndex);
                bucket = (
                    entry.File.ProjectName,
                    location,
                    new List<string>(),
                    new List<string>(),
                    new List<string>(),
                    new List<string>(),
                    new List<string>());
                effectsByFullName[decl.FullName] = bucket;
            }

            foreach (var svc in summary.DirectCalledServices)
                SystemAnalysisContext.AddUniqueOrdinal(bucket.CalledServices, svc);
            foreach (var db in summary.DirectDbContexts)
                SystemAnalysisContext.AddUniqueOrdinal(bucket.DbContexts, db);
            foreach (var ds in summary.DirectDataStores)
                SystemAnalysisContext.AddUniqueOrdinal(bucket.DataStores, ds);
            foreach (var ent in summary.DirectEntities)
                SystemAnalysisContext.AddUniqueOrdinal(bucket.Entities, ent);
            foreach (var msg in summary.DirectPublishedMessages)
                SystemAnalysisContext.AddUniqueOrdinal(bucket.PublishedMessages, msg);
        }

        foreach (var (fullName, bucket) in effectsByFullName.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            bucket.CalledServices.Sort(StringComparer.Ordinal);
            bucket.DbContexts.Sort(StringComparer.Ordinal);
            bucket.DataStores.Sort(StringComparer.Ordinal);
            bucket.Entities.Sort(StringComparer.Ordinal);
            bucket.PublishedMessages.Sort(StringComparer.Ordinal);

            result.ServiceClassEffects[fullName] = new ServiceClassEffect(
                bucket.ProjectName,
                bucket.Location,
                bucket.CalledServices,
                bucket.DbContexts,
                bucket.DataStores,
                bucket.Entities,
                bucket.PublishedMessages);
        }
    }

    private static CachedClassEffects GetOrComputeClassEffects(
        SystemAnalysisContext context,
        CSharpScanner.ClassDecl svcDecl)
    {
        if (context.TryGetCachedClassEffects(svcDecl, out var cached))
            return cached;

        var summary = new CachedClassEffects();
        // Register immediately to break cycles safely if re-entered.
        context.SetCachedClassEffects(svcDecl, summary);

        if (!context.FileByClass.TryGetValue(svcDecl, out var svcEntry))
            return summary;

        var svcDeps = new List<string>(
            context.GetClassDependencies(svcDecl, svcEntry.File.Code, svcEntry.Mask, svcEntry.Scan.Classes));
        foreach (var prop in svcDecl.Properties)
        {
            if (prop.Type.Length > 0 && !svcDeps.Contains(prop.Type, StringComparer.Ordinal))
                svcDeps.Add(prop.Type);
        }

        var hasBody = svcDecl.BodySpan.End > svcDecl.BodySpan.Start;
        var svcEnd = hasBody ? Math.Min(svcEntry.File.Code.Length, svcDecl.BodySpan.End) : 0;
        var svcBody = hasBody
            ? svcEntry.Mask.CodeOnly(svcDecl.BodySpan.Start, svcEnd - svcDecl.BodySpan.Start)
            : string.Empty;

        if (svcBody.Length > 0)
        {
            foreach (var located in ExtractServiceLocatorTypes(svcBody))
            {
                if (!svcDeps.Contains(located, StringComparer.Ordinal))
                    svcDeps.Add(located);
            }
        }

        var visitedTypes = new HashSet<string>(StringComparer.Ordinal);
        var visitedClasses = new HashSet<string>(StringComparer.Ordinal) { svcDecl.FullName };
        var nextQueue = new Queue<(CSharpScanner.ClassDecl Class, int Depth)>();

        foreach (var dep in svcDeps)
        {
            ResolveTypeReference(
                context,
                dep,
                svcDecl.Namespace,
                svcDecl.Usings,
                svcDecl.ContainingTypes,
                svcDecl,
                Array.Empty<CqrsRequest>(),
                depth: 0,
                visitedTypes,
                visitedClasses,
                summary.DirectCalledServices,
                summary.DirectDbContexts,
                summary.DirectDataStores,
                summary.DirectEntities,
                nextQueue);
        }

        if (context.GetClassDataAccess(svcDecl) is { } svcDataAccess)
        {
            foreach (var ds in svcDataAccess.DataStores)
                SystemAnalysisContext.AddUniqueOrdinal(summary.DirectDataStores, ds);
            foreach (var ent in svcDataAccess.Entities)
                SystemAnalysisContext.AddUniqueOrdinal(summary.DirectEntities, ent);
        }

        while (nextQueue.Count > 0)
        {
            var (nextClass, _) = nextQueue.Dequeue();
            if (!summary.NextServiceClasses.Contains(nextClass))
                summary.NextServiceClasses.Add(nextClass);
        }

        if (hasBody)
        {
            CollectDirectBodyEffects(
                context,
                svcEntry.File,
                svcEntry.Scan,
                svcEntry.Mask,
                svcDecl,
                svcDecl.PrimaryConstructorParameters ?? string.Empty,
                svcBody,
                svcDecl.BodySpan.Start,
                svcEnd,
                summary.DirectDbContexts,
                summary.DirectDataStores,
                summary.DirectEntities,
                summary.DirectPublishedMessages,
                bodyAlreadyMasked: true);
        }

        return summary;
    }

    private static void ResolveTypeReference(
        SystemAnalysisContext context,
        string typeRef,
        string scopeNs,
        IReadOnlyList<UsingDirective> scopeUsings,
        IReadOnlyList<string> scopeContaining,
        CSharpScanner.ClassDecl? ownerClass,
        IReadOnlyList<CqrsRequest> matchedRequests,
        int depth,
        HashSet<string> visitedTypes,
        HashSet<string> visitedClasses,
        List<string> calledServices,
        List<string> touchedDbContexts,
        List<string> touchedDataStores,
        List<string> touchedEntities,
        Queue<(CSharpScanner.ClassDecl Class, int Depth)> queue)
    {
        if (string.IsNullOrWhiteSpace(typeRef))
            return;

        var (name, args) = CSharpScanner.ParseTypeReference(typeRef);
        if (name.Length == 0)
            return;

        var simple = CSharpScanner.SimpleName(name);
        if (IsFrameworkOrRequestType(simple, matchedRequests))
            return;

        if (!visitedTypes.Add($"{scopeNs}|{typeRef}"))
            return;

        // IDbContextFactory<TContext>
        if (simple is "IDbContextFactory" or "DbContextFactory" && args.Count == 1)
        {
            var innerCtx = ResolveConcreteDbContext(context, args[0], scopeNs, scopeUsings, scopeContaining);
            if (innerCtx is not null)
            {
                SystemAnalysisContext.AddUniqueOrdinal(touchedDbContexts, innerCtx.FullName);
                return;
            }
        }

        // Direct concrete DbContext
        var directCtx = ResolveConcreteDbContext(context, typeRef, scopeNs, scopeUsings, scopeContaining);
        if (directCtx is not null)
        {
            SystemAnalysisContext.AddUniqueOrdinal(touchedDbContexts, directCtx.FullName);
            return;
        }

        // Generic repository dependency (e.g., IRepository<Order>) across EF Core or non-EF stores
        TryAddGenericRepoEntity(
            typeRef, ownerClass, context, scopeNs, scopeUsings, scopeContaining, touchedEntities, touchedDataStores);

        // Service resolution (interface -> concrete implementation)
        var resolution = LookupResolution(context, typeRef, scopeNs, scopeUsings, scopeContaining);
        if (resolution is not null)
        {
            if (resolution.Implementation is not null &&
                context.DbContextsByFullName.ContainsKey(resolution.Implementation))
            {
                SystemAnalysisContext.AddUniqueOrdinal(touchedDbContexts, resolution.Implementation);
                return;
            }

            var label = resolution.Implementation is not null
                ? $"{resolution.InterfaceName} -> {resolution.Implementation}"
                : resolution.InterfaceName;
            SystemAnalysisContext.AddUniqueOrdinal(calledServices, label);

            if (resolution.Implementation is not null &&
                depth < MaxServiceHops &&
                context.ClassByFullName.TryGetValue(resolution.Implementation, out var implDecl) &&
                visitedClasses.Add(implDecl.FullName))
            {
                queue.Enqueue((implDecl, depth + 1));
            }
            return;
        }

        // Concrete scanned class
        var classDecl = context.Hierarchy.TryResolveClass(
            name, scopeNs, scopeUsings, scopeContaining, args.Count, out _);
        if (classDecl is not null)
        {
            if (context.DbContextsByFullName.ContainsKey(classDecl.FullName))
            {
                SystemAnalysisContext.AddUniqueOrdinal(touchedDbContexts, classDecl.FullName);
                return;
            }

            if (!IsServiceLikeClass(context, classDecl))
                return;

            SystemAnalysisContext.AddUniqueOrdinal(calledServices, classDecl.FullName);
            if (depth < MaxServiceHops && visitedClasses.Add(classDecl.FullName))
                queue.Enqueue((classDecl, depth + 1));
            return;
        }

        // External / unscanned interface dependency (e.g. IEmailSender, IPaymentClient)
        if (simple.Length > 1 && simple[0] == 'I' && char.IsUpper(simple[1]))
        {
            var ifaceIdentity = context.Hierarchy.ResolveInterfaceReference(
                name, scopeNs, scopeUsings, scopeContaining, args.Count, out _);
            var label = ifaceIdentity is not null && ifaceIdentity.Contains('.', StringComparison.Ordinal)
                ? (ifaceIdentity.Contains('`', StringComparison.Ordinal)
                    ? ifaceIdentity[..ifaceIdentity.LastIndexOf('`')]
                    : ifaceIdentity)
                : name;
            SystemAnalysisContext.AddUniqueOrdinal(calledServices, label);
        }
    }

    private static List<string> CollectRootDependencyTypes(
        SystemAnalysisContext context,
        CSharpScanner.ClassDecl? ownerClass,
        SourceFile file,
        SourceMask mask,
        List<CSharpScanner.ClassDecl> classes,
        string parametersCode,
        string bodyCode,
        IReadOnlyList<string>? extraInjectedTypes,
        IReadOnlyList<CqrsRequest> matchedRequests,
        bool scopeToMethodBody,
        ClassMethodIndex? methodIndex,
        HashSet<string>? referencedIdentifiers,
        List<(string Body, int Start, int End)>? visitedHelpers)
    {
        var result = new List<string>();

        void Add(string? type)
        {
            if (string.IsNullOrWhiteSpace(type))
                return;
            var cleaned = type.Trim();
            if (!result.Contains(cleaned, StringComparer.Ordinal))
                result.Add(cleaned);
        }

        if (!string.IsNullOrWhiteSpace(parametersCode))
        {
            foreach (var rawParam in CSharpScanner.SplitTopLevel(parametersCode))
                Add(DeepSliceSyntax.ExtractParameterType(rawParam));
        }

        if (extraInjectedTypes is not null)
        {
            foreach (var extra in extraInjectedTypes)
                Add(extra);
        }

        foreach (var located in ExtractServiceLocatorTypes(bodyCode))
            Add(located);

        if (visitedHelpers is { Count: > 0 })
        {
            foreach (var helper in visitedHelpers)
            {
                foreach (var located in ExtractServiceLocatorTypes(helper.Body))
                    Add(located);
            }
        }

        if (ownerClass is not null)
        {
            if (scopeToMethodBody && methodIndex is not null && referencedIdentifiers is not null)
            {
                foreach (var (memberName, memberType) in methodIndex.MemberTypes)
                {
                    if (referencedIdentifiers.Contains(memberName))
                        Add(memberType);
                }

                // Fall back to small owner dependencies only if method-scoped extraction
                // found zero non-framework types (e.g. thin constructor-forwarding wrappers).
                var hasNonFrameworkRoot = result.Any(t =>
                    !IsFrameworkOrRequestType(CSharpScanner.UnqualifiedTypeName(t), matchedRequests));
                if (!hasNonFrameworkRoot)
                {
                    var classDeps = context.GetClassDependencies(ownerClass, file.Code, mask, classes);
                    if (classDeps.Count <= 3)
                    {
                        foreach (var dep in classDeps)
                            Add(dep);
                    }
                }
            }
            else
            {
                foreach (var dep in context.GetClassDependencies(ownerClass, file.Code, mask, classes))
                    Add(dep);

                foreach (var prop in ownerClass.Properties)
                {
                    if (prop.Attributes.Any(a => a.Contains("Inject", StringComparison.Ordinal)) ||
                        IsServicePropertyType(prop.Type, matchedRequests))
                    {
                        Add(prop.Type);
                    }
                }
            }
        }

        return result;
    }

    private static bool IsServicePropertyType(string type, IReadOnlyList<CqrsRequest> matchedRequests)
    {
        if (string.IsNullOrWhiteSpace(type))
            return false;
        var simple = CSharpScanner.UnqualifiedTypeName(type);
        if (IsFrameworkOrRequestType(simple, matchedRequests))
            return false;
        return (simple.Length > 1 && simple[0] == 'I' && char.IsUpper(simple[1])) ||
               simple.EndsWith("Context", StringComparison.Ordinal) ||
               simple.EndsWith("Service", StringComparison.Ordinal) ||
               simple.EndsWith("Repository", StringComparison.Ordinal);
    }

    private static IEnumerable<string> ExtractServiceLocatorTypes(string maskedBody)
    {
        if (!maskedBody.Contains("Service", StringComparison.Ordinal))
            yield break;

        MatchCollection matches;
        try
        {
            matches = ServiceLocatorRegex().Matches(maskedBody);
        }
        catch (RegexMatchTimeoutException)
        {
            yield break;
        }

        foreach (Match m in matches)
        {
            var type = m.Groups["type"].Value.Trim();
            if (type.Length > 0)
                yield return type;
        }
    }

    private static void CollectDirectBodyEffects(
        SystemAnalysisContext context,
        SourceFile file,
        CSharpScanner.FileScan scan,
        SourceMask mask,
        CSharpScanner.ClassDecl? ownerClass,
        string parametersCode,
        string bodyCode,
        int spanStart,
        int spanEnd,
        List<string> touchedDbContexts,
        List<string> touchedDataStores,
        List<string> touchedEntities,
        List<string> publishedMessages,
        bool bodyAlreadyMasked)
    {
        var scopeNs = ownerClass?.Namespace ?? scan.Namespace;
        IReadOnlyList<UsingDirective> scopeUsings = ownerClass?.Usings ?? scan.Usings;
        IReadOnlyList<string> scopeContaining = ownerClass?.ContainingTypes ?? (IReadOnlyList<string>)Array.Empty<string>();

        if (ownerClass is not null)
        {
            var namedMatch = NamedRepositoryRegex().Match(ownerClass.Name);
            if (namedMatch.Success &&
                context.DbSetMappings.DbSetsByEntity.TryGetValue(namedMatch.Groups["entity"].Value, out var stemLabel))
            {
                SystemAnalysisContext.AddUniqueOrdinal(touchedEntities, stemLabel);
            }

            foreach (var bt in CSharpScanner.SplitTopLevel(ownerClass.BaseList))
            {
                TryAddGenericRepoEntity(
                    bt, ownerClass, context, scopeNs, scopeUsings, scopeContaining, touchedEntities, touchedDataStores);
            }
        }

        var hasDbMappings = context.DbSetMappings.DbSetsByProperty.Count > 0 ||
                            context.DbSetMappings.DbSetsByEntity.Count > 0 ||
                            context.Result.DbContexts.Count > 0;
        if (hasDbMappings && bodyCode.Length > 0)
        {
            var localVars = BuildBodyReceiverTypes(
                context, ownerClass, file.Code, mask, scan.Classes, parametersCode, bodyCode, bodyAlreadyMasked);
            var currentNs = ownerClass?.Namespace ?? scan.Namespace;
            IReadOnlyList<string> containing = ownerClass?.ContainingTypes ?? (IReadOnlyList<string>)Array.Empty<string>();

            foreach (var (_, varType) in localVars)
            {
                var resolvedCtx = ResolveConcreteDbContext(context, varType, currentNs, scan.Usings, containing);
                if (resolvedCtx is not null)
                    SystemAnalysisContext.AddUniqueOrdinal(touchedDbContexts, resolvedCtx.FullName);
            }

            if (context.DbSetMappings.DbSetsByProperty.Count > 0 && bodyCode.Contains('.', StringComparison.Ordinal))
            {
                try
                {
                    foreach (Match m in MemberAccessRegex().Matches(bodyCode))
                    {
                        var prop = m.Groups["prop"].Value;
                        if (!context.DbSetMappings.DbSetsByProperty.TryGetValue(prop, out var label))
                            continue;
                        var receiver = ReceiverTypeMapBuilder.GetReceiverIdentifier(bodyCode, m.Index);
                        if (ReceiverTypeMapBuilder.IsDbContextReceiver(receiver, localVars, context.DbSetMappings.DbContextTypeNames))
                            SystemAnalysisContext.AddUniqueOrdinal(touchedEntities, label);
                    }
                }
                catch (RegexMatchTimeoutException)
                {
                }
            }

            if (context.DbSetMappings.DbSetsByEntity.Count > 0 && bodyCode.Contains(".Set", StringComparison.Ordinal))
            {
                try
                {
                    foreach (Match m in DbContextSetMethodRegex().Matches(bodyCode))
                    {
                        var receiver = ReceiverTypeMapBuilder.GetReceiverIdentifier(bodyCode, m.Index);
                        if (!ReceiverTypeMapBuilder.IsDbContextReceiver(receiver, localVars, context.DbSetMappings.DbContextTypeNames))
                            continue;
                        var entitySimple = CSharpScanner.SimpleName(m.Groups["entity"].Value.Trim());
                        if (context.DbSetMappings.DbSetsByEntity.TryGetValue(entitySimple, out var label))
                            SystemAnalysisContext.AddUniqueOrdinal(touchedEntities, label);
                    }
                }
                catch (RegexMatchTimeoutException)
                {
                }
            }
        }

        // O(1) per-file lookup for published integration events / domain events in this span.
        foreach (var hit in context.GetPublishedEventsForFile(file.Path))
        {
            if (hit.Offset >= spanStart && hit.Offset < spanEnd)
                SystemAnalysisContext.AddUniqueOrdinal(publishedMessages, hit.EventName);
        }

        // Created CQRS INotification types inside this body.
        if (context.Result.Notifications.Count > 0 && bodyCode.Contains("new", StringComparison.Ordinal))
        {
            try
            {
                foreach (Match m in ObjectCreateRegex().Matches(bodyCode))
                {
                    var simple = CSharpScanner.UnqualifiedTypeName(m.Groups["type"].Value);
                    var matching = context.Result.Notifications.Where(n => n.Name == simple).ToList();
                    if (matching.Count > 0)
                    {
                        var picked = DeepSliceCandidateResolver.PickNotification(
                            matching, ownerClass?.Namespace ?? scan.Namespace, file.ProjectName);
                        if (picked is not null)
                            SystemAnalysisContext.AddUniqueOrdinal(publishedMessages, picked.FullName);
                    }
                }
            }
            catch (RegexMatchTimeoutException)
            {
            }
        }
    }

    private static void CollectScopedDbContextSetTouches(
        SystemAnalysisContext context,
        string bodyCode,
        SystemEntrypointInfo entrypoint)
    {
        if (entrypoint.TouchedDbContexts.Count == 0 ||
            string.IsNullOrEmpty(bodyCode) ||
            !bodyCode.Contains('.', StringComparison.Ordinal))
        {
            return;
        }

        var candidateSets = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var ctxFullName in entrypoint.TouchedDbContexts)
        {
            if (!context.DbSetsByContextFullName.TryGetValue(ctxFullName, out var sets))
                continue;
            foreach (var (prop, label) in sets)
                candidateSets.TryAdd(prop, label);
        }

        if (candidateSets.Count == 0)
            return;

        try
        {
            foreach (Match m in MemberAccessRegex().Matches(bodyCode))
            {
                var prop = m.Groups["prop"].Value;
                if (candidateSets.TryGetValue(prop, out var label))
                    SystemAnalysisContext.AddUniqueOrdinal(entrypoint.TouchedEntities, label);
            }
        }
        catch (RegexMatchTimeoutException)
        {
        }
    }

    private static void RollUpCqrsEffects(
        SystemAnalysisContext context,
        IReadOnlyList<CqrsRequest> matchedRequests,
        SystemEntrypointInfo entrypoint)
    {
        foreach (var request in matchedRequests)
        {
            foreach (var entity in request.TouchedEntities)
                SystemAnalysisContext.AddUniqueOrdinal(entrypoint.TouchedEntities, entity);

            foreach (var pub in request.PublishedNotifications)
                SystemAnalysisContext.AddUniqueOrdinal(entrypoint.PublishedMessages, pub);

            foreach (var handlerFullName in request.Handlers)
            {
                if (!context.HandlersByFullName.TryGetValue(handlerFullName, out var handlers))
                    continue;

                foreach (var handler in handlers)
                {
                    context.ClassByFullName.TryGetValue(handler.FullName, out var handlerDecl);
                    IReadOnlyList<UsingDirective> handlerUsings = handlerDecl?.Usings
                        ?? (IReadOnlyList<UsingDirective>)Array.Empty<UsingDirective>();

                    foreach (var dep in handler.Dependencies)
                    {
                        var (depName, _) = CSharpScanner.ParseTypeReference(dep);
                        var depSimple = CSharpScanner.SimpleName(depName);
                        if (IsFrameworkOrRequestType(depSimple, matchedRequests))
                            continue;

                        var directCtx = ResolveConcreteDbContext(
                            context, dep, handler.Namespace, handlerUsings, handler.ContainingTypes);
                        if (directCtx is not null)
                        {
                            SystemAnalysisContext.AddUniqueOrdinal(entrypoint.TouchedDbContexts, directCtx.FullName);
                            continue;
                        }

                        var sr = LookupResolution(
                            context, dep, handler.Namespace, handlerUsings, handler.ContainingTypes);
                        if (sr is not null)
                        {
                            if (sr.Implementation is not null && context.DbContextsByFullName.ContainsKey(sr.Implementation))
                            {
                                SystemAnalysisContext.AddUniqueOrdinal(entrypoint.TouchedDbContexts, sr.Implementation);
                                continue;
                            }

                            var svcLabel = sr.Implementation is not null
                                ? $"{sr.InterfaceName} -> {sr.Implementation}"
                                : sr.InterfaceName;
                            SystemAnalysisContext.AddUniqueOrdinal(entrypoint.CalledServices, svcLabel);
                            continue;
                        }

                        if (!depSimple.EndsWith("DbContext", StringComparison.Ordinal))
                            SystemAnalysisContext.AddUniqueOrdinal(entrypoint.CalledServices, depName);
                    }

                    if (context.FlowEdgesByFrom.TryGetValue(handler.FullName, out var flowEdges))
                    {
                        foreach (var edge in flowEdges)
                        {
                            if (edge.Kind == FlowEdgeKind.HandlerToDbContext)
                                SystemAnalysisContext.AddUniqueOrdinal(entrypoint.TouchedDbContexts, edge.To);
                        }
                    }

                    if (context.ResolvedEdgesByFrom.TryGetValue(handler.FullName, out var resolvedEdges))
                    {
                        foreach (var edge in resolvedEdges)
                        {
                            if (edge.Kind == FlowEdgeKind.HandlerToDbContextResolved)
                                SystemAnalysisContext.AddUniqueOrdinal(entrypoint.TouchedDbContexts, edge.To);
                        }
                    }

                    if (context.ServiceCallEdgesByFrom.TryGetValue(handler.FullName, out var callEdges))
                    {
                        foreach (var edge in callEdges)
                        {
                            if (context.DbContextsByFullName.ContainsKey(edge.To))
                                continue;
                            var label = string.Equals(edge.Via, edge.To, StringComparison.Ordinal)
                                ? edge.To
                                : $"{edge.Via} -> {edge.To}";
                            SystemAnalysisContext.AddUniqueOrdinal(entrypoint.CalledServices, label);
                        }
                    }
                }
            }
        }
    }

    private static Dictionary<string, string> BuildBodyReceiverTypes(
        SystemAnalysisContext context,
        CSharpScanner.ClassDecl? ownerClass,
        string fileCode,
        SourceMask mask,
        List<CSharpScanner.ClassDecl> classes,
        string parametersCode,
        string bodyCode,
        bool bodyAlreadyMasked)
    {
        var baseMap = ownerClass is not null
            ? context.GetClassMemberMap(ownerClass, fileCode, mask, classes)
            : null;

        var map = baseMap is not null
            ? new Dictionary<string, string>(baseMap, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(parametersCode))
        {
            foreach (var rawParam in CSharpScanner.SplitTopLevel(parametersCode))
            {
                var pType = DeepSliceSyntax.ExtractParameterType(rawParam);
                var pName = DeepSliceSyntax.ExtractParameterName(rawParam);
                if (!string.IsNullOrEmpty(pType) && !string.IsNullOrEmpty(pName))
                    map[pName.TrimStart('@')] = pType;
            }
        }

        if (!string.IsNullOrWhiteSpace(bodyCode))
        {
            var bodyMask = bodyAlreadyMasked ? mask : SourceMask.Build(bodyCode);
            return SystemIntegrationEventScanner.BuildScopeVariableMap(
                bodyCode, bodyMask, 0, bodyCode.Length, map, alreadyMasked: bodyAlreadyMasked);
        }

        return map;
    }

    private static void TryAddGenericRepoEntity(
        string typeRef,
        CSharpScanner.ClassDecl? scopeDecl,
        SystemAnalysisContext context,
        string scopeNamespace,
        IReadOnlyList<UsingDirective> scopeUsings,
        IReadOnlyList<string> scopeContaining,
        List<string> touchedEntities,
        List<string> touchedDataStores)
    {
        if (!typeRef.Contains('<', StringComparison.Ordinal))
            return;

        if (context.DbSetMappings.DbSetsByEntity.Count > 0)
        {
            try
            {
                foreach (Match m in GenericRepositoryRegex().Matches(typeRef))
                {
                    var entitySimple = CSharpScanner.SimpleName(m.Groups["entity"].Value.Trim());
                    if (scopeDecl is not null &&
                        !ReceiverTypeMapBuilder.IsEntityCandidate(entitySimple, scopeDecl, context.DbSetMappings.DbSetsByEntity))
                        continue;
                    if (context.DbSetMappings.DbSetsByEntity.TryGetValue(entitySimple, out var label))
                        SystemAnalysisContext.AddUniqueOrdinal(touchedEntities, label);
                }
            }
            catch (RegexMatchTimeoutException)
            {
            }
        }

        if (DataStoreDiscovery.TryResolveNonEfRepositoryEntity(
                context, typeRef, scopeDecl, scopeNamespace, scopeUsings, scopeContaining,
                out var nonEfEntity, out var nonEfStore))
        {
            if (!string.IsNullOrEmpty(nonEfEntity))
                SystemAnalysisContext.AddUniqueOrdinal(touchedEntities, nonEfEntity);
            if (!string.IsNullOrEmpty(nonEfStore))
                SystemAnalysisContext.AddUniqueOrdinal(touchedDataStores, nonEfStore);
        }
    }

    internal static DbContextInfo? ResolveConcreteDbContext(
        SystemAnalysisContext context,
        string typeReference,
        string currentNamespace,
        IReadOnlyList<UsingDirective> usings,
        IReadOnlyList<string> containingTypes)
    {
        if (context.Result.DbContexts.Count == 0)
            return null;

        var (name, arguments) = CSharpScanner.ParseTypeReference(typeReference);
        if (name.Length == 0)
            return null;

        var classDecl = context.Hierarchy.TryResolveClass(
            name, currentNamespace, usings, containingTypes, arguments.Count, out var ambiguous);
        if (ambiguous.Count > 0)
            return null;

        if (classDecl is not null &&
            context.DbContextsByFullName.TryGetValue(classDecl.FullName, out var resolved))
        {
            return resolved;
        }

        var simple = CSharpScanner.SimpleName(name);
        return context.DbContextBySimpleCandidate.TryGetValue(simple, out var uniqueBySimple)
            ? uniqueBySimple
            : null;
    }

    internal static ServiceResolution? LookupResolution(
        SystemAnalysisContext context,
        string typeReference,
        string currentNamespace,
        IReadOnlyList<UsingDirective> usings,
        IReadOnlyList<string> containingTypes)
    {
        if (context.ResolutionBySignature.Count == 0 && context.OpenResolutionByIdentity.Count == 0)
            return null;

        var (name, arguments) = CSharpScanner.ParseTypeReference(typeReference);
        if (name.Length == 0)
            return null;

        var identity = context.Hierarchy.ResolveInterfaceReference(
            name, currentNamespace, usings, containingTypes, arguments.Count, out var ambiguous);
        if (ambiguous.Count > 0)
            return null;

        if (identity is null)
        {
            var simple = CSharpScanner.SimpleName(name);
            identity = arguments.Count > 0
                ? CSharpScanner.TypeIdentity(simple, arguments.Count)
                : simple;
        }

        var signature = arguments.Count == 0
            ? identity
            : identity + "<" + string.Join(", ", arguments) + ">";

        if (context.ResolutionBySignature.TryGetValue(signature, out var resolution))
            return resolution;

        if (arguments.Count > 0 && context.OpenResolutionByIdentity.TryGetValue(identity, out var openResolution))
            return openResolution;

        return null;
    }

    private static bool IsServiceLikeClass(SystemAnalysisContext context, CSharpScanner.ClassDecl decl)
    {
        if (decl.IsAbstract)
            return false;
        if (context.RequestsByFullName.ContainsKey(decl.FullName))
            return false;

        foreach (var suffix in DtoSuffixes)
        {
            if (decl.Name.EndsWith(suffix, StringComparison.Ordinal))
                return false;
        }

        foreach (var suffix in ServiceNameSuffixes)
        {
            if (decl.Name.EndsWith(suffix, StringComparison.Ordinal))
                return true;
        }

        if (context.FileByClass.TryGetValue(decl, out var entry))
        {
            var deps = context.GetClassDependencies(decl, entry.File.Code, entry.Mask, entry.Scan.Classes);
            foreach (var dep in deps)
            {
                var depSimple = CSharpScanner.UnqualifiedTypeName(dep);
                if (!IsFrameworkOrRequestType(depSimple, Array.Empty<CqrsRequest>()) &&
                    ((depSimple.Length > 1 && depSimple[0] == 'I' && char.IsUpper(depSimple[1])) ||
                     context.DbContextBySimpleCandidate.ContainsKey(depSimple) ||
                     ServiceNameSuffixes.Any(s => depSimple.EndsWith(s, StringComparison.Ordinal))))
                {
                    return true;
                }
            }
        }

        return decl.BaseRawNames.Any(b => b.Length > 1 && b[0] == 'I' && char.IsUpper(b[1]));
    }

    private static bool IsFrameworkOrRequestType(string simple, IReadOnlyList<CqrsRequest> matchedRequests)
    {
        if (simple.Length == 0 || FrameworkSimpleTypes.Contains(simple))
            return true;
        if (simple.StartsWith("ILogger", StringComparison.Ordinal) ||
            simple.StartsWith("IOptions", StringComparison.Ordinal))
            return true;

        foreach (var req in matchedRequests)
        {
            if (string.Equals(req.Name, simple, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static void SortEntrypointLists(SystemEntrypointInfo ep)
    {
        ep.TargetRequests.Sort(StringComparer.Ordinal);
        ep.CalledServices.Sort(StringComparer.Ordinal);
        ep.TouchedDbContexts.Sort(StringComparer.Ordinal);
        ep.TouchedDataStores.Sort(StringComparer.Ordinal);
        ep.TouchedEntities.Sort(StringComparer.Ordinal);
        ep.PublishedMessages.Sort(StringComparer.Ordinal);
    }
}
