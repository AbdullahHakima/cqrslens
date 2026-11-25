namespace CqrsLens;

/// <summary>
/// Resolves handler → DbContext dependencies through the service-resolution index, supporting:
/// <list type="bullet">
///   <item>Direct 1-hop DI abstractions (<c>IAppDbContext</c> → <c>AppDbContext</c>);</item>
///   <item><c>IDbContextFactory&lt;TContext&gt;</c> / <c>DbContextFactory&lt;TContext&gt;</c>;</item>
///   <item>Open-generic registrations/implementations (e.g. <c>IRepository&lt;&gt;</c> → <c>EfRepository&lt;&gt;</c>);</item>
///   <item>Bounded multi-hop chains (Handler → Service/Repository → ... → DbContext, max depth 4).</item>
/// </list>
/// Fail closed: unresolved interfaces, ambiguous interfaces, or conflicting multiple candidate
/// implementations stop traversal and produce no guessed edge.
/// </summary>
internal static class HandlerDbContextResolver
{
    private const int MaxServiceHops = 4;

    public static void Build(
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        AnalysisResult result,
        TypeHierarchy? hierarchy = null)
    {
        if (result.DbContexts.Count == 0 ||
            (result.Handlers.Count == 0 && result.NotificationHandlers.Count == 0))
            return;

        var contextsByFullName = new Dictionary<string, DbContextInfo>(StringComparer.Ordinal);
        var contextBySimpleCandidate = new Dictionary<string, DbContextInfo?>(StringComparer.Ordinal);
        foreach (var context in result.DbContexts)
        {
            contextsByFullName.TryAdd(context.FullName, context);
            if (!contextBySimpleCandidate.TryGetValue(context.Name, out var existing))
                contextBySimpleCandidate[context.Name] = context;
            else if (existing is not null && !string.Equals(existing.FullName, context.FullName, StringComparison.Ordinal))
                contextBySimpleCandidate[context.Name] = null;
        }
        if (contextsByFullName.Count == 0) return;

        var resolutionBySignature = new Dictionary<string, ServiceResolution>(StringComparer.Ordinal);
        var openResolutionByIdentity = new Dictionary<string, ServiceResolution>(StringComparer.Ordinal);
        foreach (var resolution in result.ServiceResolutions)
        {
            resolutionBySignature.TryAdd(resolution.Interface, resolution);
            if (resolution.IsOpenGeneric)
                openResolutionByIdentity.TryAdd(resolution.Identity, resolution);
        }

        var classes = new List<CSharpScanner.ClassDecl>();
        var interfaces = new List<CSharpScanner.InterfaceDecl>();
        var classByFullName = new Dictionary<string, CSharpScanner.ClassDecl>(StringComparer.Ordinal);
        var fileByClass = new Dictionary<CSharpScanner.ClassDecl, (SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)>();
        foreach (var entry in parsedFiles)
        {
            foreach (var declaration in entry.Scan.Classes)
            {
                classes.Add(declaration);
                classByFullName.TryAdd(declaration.FullName, declaration);
                fileByClass.TryAdd(declaration, entry);
            }
            interfaces.AddRange(entry.Scan.InterfaceDecls);
        }

        hierarchy ??= new TypeHierarchy(classes, interfaces);
        var depsCache = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        List<string> GetClassDependencies(CSharpScanner.ClassDecl decl)
        {
            if (depsCache.TryGetValue(decl.FullName, out var cached))
                return cached;

            var deps = new List<string>();
            if (fileByClass.TryGetValue(decl, out var entry))
                DependencyExtractor.Extract(decl, entry.File.Code, entry.Mask, entry.Scan.Classes, deps);
            depsCache[decl.FullName] = deps;
            return deps;
        }

        var edges = new List<ResolvedEdge>();

        foreach (var handler in EnumerateHandlers(result))
        {
            classByFullName.TryGetValue(handler.FullName, out var handlerDecl);
            var currentNamespace = handlerDecl?.Namespace ?? handler.Namespace;
            IReadOnlyList<UsingDirective> usings =
                handlerDecl?.Usings ?? (IReadOnlyList<UsingDirective>)Array.Empty<UsingDirective>();
            IReadOnlyList<string> containingTypes =
                handlerDecl?.ContainingTypes ?? (IReadOnlyList<string>)Array.Empty<string>();

            var directContexts = new HashSet<string>(StringComparer.Ordinal);
            foreach (var dep in handler.Dependencies)
            {
                if (ResolveDirectConcreteContext(dep, currentNamespace, usings, containingTypes,
                        hierarchy, contextsByFullName, contextBySimpleCandidate) is { } directCtx)
                {
                    directContexts.Add(directCtx.FullName);
                }
            }

            // BFS across service/repository classes so shortest unambiguous hop chains win.
            var queue = new Queue<(CSharpScanner.ClassDecl Class, List<string> Chain, int Depth)>();
            var queuedClasses = new HashSet<string>(StringComparer.Ordinal);

            foreach (var dependency in handler.Dependencies)
            {
                var step = ResolveDependencyStep(
                    dependency,
                    currentNamespace,
                    usings,
                    containingTypes,
                    depth: 0,
                    hierarchy,
                    contextsByFullName,
                    contextBySimpleCandidate,
                    resolutionBySignature,
                    openResolutionByIdentity,
                    classByFullName);

                if (step.Context is { } hitContext)
                {
                    edges.Add(new ResolvedEdge
                    {
                        Kind = FlowEdgeKind.HandlerToDbContextResolved,
                        From = handler.FullName,
                        Via = step.HopLabel!,
                        ViaChain = null,
                        To = hitContext.FullName,
                        ProjectName = hitContext.ProjectName,
                        Location = hitContext.Location,
                    });
                    continue;
                }

                if (step.NextClass is { } nextClass && queuedClasses.Add(nextClass.FullName))
                {
                    var chain = new List<string>();
                    if (step.HopLabel is not null)
                        chain.Add(step.HopLabel);
                    chain.Add(nextClass.FullName);
                    queue.Enqueue((nextClass, chain, 1));
                }
            }

            while (queue.Count > 0)
            {
                var (serviceClass, chain, depth) = queue.Dequeue();
                var deps = GetClassDependencies(serviceClass);
                foreach (var dependency in deps)
                {
                    var step = ResolveDependencyStep(
                        dependency,
                        serviceClass.Namespace,
                        serviceClass.Usings,
                        serviceClass.ContainingTypes,
                        depth,
                        hierarchy,
                        contextsByFullName,
                        contextBySimpleCandidate,
                        resolutionBySignature,
                        openResolutionByIdentity,
                        classByFullName);

                    if (step.Context is { } hitContext)
                    {
                        if (directContexts.Contains(hitContext.FullName))
                            continue;

                        var fullChain = new List<string>(chain);
                        if (step.IncludeHopInChain && step.HopLabel is not null)
                            fullChain.Add(step.HopLabel);

                        edges.Add(new ResolvedEdge
                        {
                            Kind = FlowEdgeKind.HandlerToDbContextResolved,
                            From = handler.FullName,
                            Via = fullChain[0],
                            ViaChain = fullChain.Count > 1 ? fullChain : null,
                            To = hitContext.FullName,
                            ProjectName = hitContext.ProjectName,
                            Location = hitContext.Location,
                        });
                        continue;
                    }

                    if (depth < MaxServiceHops &&
                        step.NextClass is { } nextClass &&
                        queuedClasses.Add(nextClass.FullName))
                    {
                        var nextChain = new List<string>(chain);
                        if (step.HopLabel is not null)
                            nextChain.Add(step.HopLabel);
                        nextChain.Add(nextClass.FullName);
                        queue.Enqueue((nextClass, nextChain, depth + 1));
                    }
                }
            }
        }

        result.ResolvedEdges.AddRange(edges
            .GroupBy(edge => (edge.Kind, edge.From, edge.To))
            .Select(group => group
                .OrderBy(edge => edge.ViaChain?.Count ?? 1)
                .ThenBy(edge => edge.Via, StringComparer.Ordinal)
                .ThenBy(edge => edge.ViaChain is null ? "" : string.Join("->", edge.ViaChain), StringComparer.Ordinal)
                .First())
            .OrderBy(edge => edge.From, StringComparer.Ordinal)
            .ThenBy(edge => edge.Via, StringComparer.Ordinal)
            .ThenBy(edge => edge.To, StringComparer.Ordinal));
    }

    private readonly record struct DependencyStepResult(
        DbContextInfo? Context,
        CSharpScanner.ClassDecl? NextClass,
        string? HopLabel,
        bool IncludeHopInChain);

    private static DependencyStepResult ResolveDependencyStep(
        string dependency,
        string currentNamespace,
        IReadOnlyList<UsingDirective> usings,
        IReadOnlyList<string> containingTypes,
        int depth,
        TypeHierarchy hierarchy,
        IReadOnlyDictionary<string, DbContextInfo> contextsByFullName,
        IReadOnlyDictionary<string, DbContextInfo?> contextBySimpleCandidate,
        IReadOnlyDictionary<string, ServiceResolution> resolutionBySignature,
        IReadOnlyDictionary<string, ServiceResolution> openResolutionByIdentity,
        IReadOnlyDictionary<string, CSharpScanner.ClassDecl> classByFullName)
    {
        var (name, arguments) = CSharpScanner.ParseTypeReference(dependency);
        if (name.Length == 0)
            return default;

        var simpleName = CSharpScanner.SimpleName(name);

        // Support EF Core IDbContextFactory<TContext> / DbContextFactory<TContext> (including interface TContext).
        if (simpleName is "IDbContextFactory" or "DbContextFactory" && arguments.Count == 1)
        {
            var inner = arguments[0];
            var innerCtx = ResolveDirectConcreteContext(
                inner, currentNamespace, usings, containingTypes, hierarchy, contextsByFullName, contextBySimpleCandidate);
            if (innerCtx is null)
            {
                var innerResolution = LookupInterfaceResolution(
                    inner, currentNamespace, usings, containingTypes,
                    hierarchy, resolutionBySignature, openResolutionByIdentity);
                if (innerResolution?.Implementation is not null)
                    contextsByFullName.TryGetValue(innerResolution.Implementation, out innerCtx);
            }

            if (innerCtx is not null)
                return new DependencyStepResult(innerCtx, null, $"{simpleName}<{innerCtx.Name}>", IncludeHopInChain: true);

            return default;
        }

        var resolution = LookupInterfaceResolution(
            dependency, currentNamespace, usings, containingTypes,
            hierarchy, resolutionBySignature, openResolutionByIdentity);
        if (resolution is not null)
        {
            if (resolution.Implementation is null)
                return default;

            if (contextsByFullName.TryGetValue(resolution.Implementation, out var context))
                return new DependencyStepResult(context, null, resolution.InterfaceName, IncludeHopInChain: true);

            if (classByFullName.TryGetValue(resolution.Implementation, out var implClass))
                return new DependencyStepResult(null, implClass, resolution.InterfaceName, IncludeHopInChain: false);

            return default;
        }

        // At depth 0 (handler itself), a direct concrete DbContext belongs in FlowEdges, not ResolvedEdges.
        // Concrete non-DbContext service classes injected directly can still lead to indirect DbContexts.
        var classDecl = hierarchy.TryResolveClass(
            name, currentNamespace, usings, containingTypes, arguments.Count, out _);
        if (classDecl is not null)
        {
            if (contextsByFullName.TryGetValue(classDecl.FullName, out var directContext))
            {
                return depth > 0
                    ? new DependencyStepResult(directContext, null, directContext.FullName, IncludeHopInChain: false)
                    : default;
            }

            return new DependencyStepResult(null, classDecl, null, IncludeHopInChain: false);
        }

        return default;
    }

    private static ServiceResolution? LookupInterfaceResolution(
        string typeReference,
        string currentNamespace,
        IReadOnlyList<UsingDirective> usings,
        IReadOnlyList<string> containingTypes,
        TypeHierarchy hierarchy,
        IReadOnlyDictionary<string, ServiceResolution> resolutionBySignature,
        IReadOnlyDictionary<string, ServiceResolution> openResolutionByIdentity)
    {
        var (name, arguments) = CSharpScanner.ParseTypeReference(typeReference);
        if (name.Length == 0)
            return null;

        var identity = hierarchy.ResolveInterfaceReference(
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

        if (resolutionBySignature.TryGetValue(signature, out var resolution))
            return resolution;

        if (arguments.Count > 0 && openResolutionByIdentity.TryGetValue(identity, out var openResolution))
            return openResolution;

        return null;
    }

    private static DbContextInfo? ResolveDirectConcreteContext(
        string typeReference,
        string currentNamespace,
        IReadOnlyList<UsingDirective> usings,
        IReadOnlyList<string> containingTypes,
        TypeHierarchy hierarchy,
        IReadOnlyDictionary<string, DbContextInfo> contextsByFullName,
        IReadOnlyDictionary<string, DbContextInfo?> contextBySimpleCandidate)
    {
        var (name, arguments) = CSharpScanner.ParseTypeReference(typeReference);
        if (name.Length == 0)
            return null;

        var classDecl = hierarchy.TryResolveClass(
            name, currentNamespace, usings, containingTypes, arguments.Count, out var ambiguous);
        if (ambiguous.Count > 0)
            return null;

        if (classDecl is not null &&
            contextsByFullName.TryGetValue(classDecl.FullName, out var resolved))
        {
            return resolved;
        }

        var simple = CSharpScanner.SimpleName(name);
        return contextBySimpleCandidate.TryGetValue(simple, out var uniqueBySimple)
            ? uniqueBySimple
            : null;
    }

    private static IEnumerable<HandlerDependencies> EnumerateHandlers(AnalysisResult result)
    {
        foreach (var handler in result.Handlers)
            yield return new HandlerDependencies(handler.FullName, handler.Namespace, handler.Dependencies);
        foreach (var handler in result.NotificationHandlers)
            yield return new HandlerDependencies(handler.FullName, handler.Namespace, handler.Dependencies);
    }

    private sealed record HandlerDependencies(
        string FullName,
        string Namespace,
        IReadOnlyList<string> Dependencies);
}
