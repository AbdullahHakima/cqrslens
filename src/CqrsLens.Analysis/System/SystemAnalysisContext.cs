namespace CqrsLens;

/// <summary>
/// Shared immutable lookup context reused across system-wide entrypoint discovery,
/// integration flow discovery, and service profile construction.
/// </summary>
internal sealed class SystemAnalysisContext
{
    private IReadOnlyList<PublishedEventHit>? _publishedEvents;
    private Dictionary<string, List<PublishedEventHit>>? _publishedEventsByFile;
    private readonly Dictionary<CSharpScanner.ClassDecl, Dictionary<string, string>> _classMemberMapCache = new();
    private readonly Dictionary<CSharpScanner.ClassDecl, List<string>> _classDepsCache = new();
    private readonly Dictionary<CSharpScanner.ClassDecl, CachedClassEffects> _classEffectsCache = new();
    private readonly Dictionary<CSharpScanner.ClassDecl, ClassMethodIndex> _classMethodIndexCache = new();
    private readonly Dictionary<CSharpScanner.ClassDecl, ClassDataAccess> _classDataAccessMap = new();
    private readonly Dictionary<string, string> _genericRepoStoreByProject = new(StringComparer.OrdinalIgnoreCase);

    private SystemAnalysisContext(
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        AnalysisResult result,
        TypeHierarchy hierarchy,
        SourcePathFilter? pathFilter,
        HashSet<string> testProjects,
        Dictionary<string, ProjectInfo> projectsByName,
        List<CSharpScanner.ClassDecl> classes,
        Dictionary<string, CSharpScanner.ClassDecl> classByFullName,
        Dictionary<string, List<CSharpScanner.ClassDecl>> classesBySimpleName,
        Dictionary<CSharpScanner.ClassDecl, (SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> fileByClass,
        Dictionary<string, DbContextInfo> dbContextsByFullName,
        Dictionary<string, DbContextInfo?> dbContextBySimpleCandidate,
        DbSetMappings dbSetMappings,
        Dictionary<string, Dictionary<string, string>> dbSetsByContextFullName,
        Dictionary<string, CqrsRequest> requestsByFullName,
        Dictionary<string, List<CqrsRequest>> requestsBySimple,
        Dictionary<string, List<HandlerInfo>> handlersByFullName,
        Dictionary<string, ServiceResolution> resolutionBySignature,
        Dictionary<string, ServiceResolution> openResolutionByIdentity,
        Dictionary<string, List<FlowEdge>> flowEdgesByFrom,
        Dictionary<string, List<ResolvedEdge>> resolvedEdgesByFrom,
        Dictionary<string, List<ServiceCallEdge>> serviceCallEdgesByFrom)
    {
        ParsedFiles = parsedFiles;
        Result = result;
        Hierarchy = hierarchy;
        PathFilter = pathFilter;
        TestProjects = testProjects;
        ProjectsByName = projectsByName;
        Classes = classes;
        ClassByFullName = classByFullName;
        ClassesBySimpleName = classesBySimpleName;
        FileByClass = fileByClass;
        DbContextsByFullName = dbContextsByFullName;
        DbContextBySimpleCandidate = dbContextBySimpleCandidate;
        DbSetMappings = dbSetMappings;
        DbSetsByContextFullName = dbSetsByContextFullName;
        RequestsByFullName = requestsByFullName;
        RequestsBySimple = requestsBySimple;
        HandlersByFullName = handlersByFullName;
        ResolutionBySignature = resolutionBySignature;
        OpenResolutionByIdentity = openResolutionByIdentity;
        FlowEdgesByFrom = flowEdgesByFrom;
        ResolvedEdgesByFrom = resolvedEdgesByFrom;
        ServiceCallEdgesByFrom = serviceCallEdgesByFrom;
    }

    public IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> ParsedFiles { get; }
    public AnalysisResult Result { get; }
    public TypeHierarchy Hierarchy { get; }
    public SourcePathFilter? PathFilter { get; }
    public HashSet<string> TestProjects { get; }
    public Dictionary<string, ProjectInfo> ProjectsByName { get; }
    public List<CSharpScanner.ClassDecl> Classes { get; }
    public Dictionary<string, CSharpScanner.ClassDecl> ClassByFullName { get; }
    public Dictionary<string, List<CSharpScanner.ClassDecl>> ClassesBySimpleName { get; }
    public Dictionary<CSharpScanner.ClassDecl, (SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> FileByClass { get; }
    public Dictionary<string, DbContextInfo> DbContextsByFullName { get; }
    public Dictionary<string, DbContextInfo?> DbContextBySimpleCandidate { get; }
    public DbSetMappings DbSetMappings { get; }
    public Dictionary<string, Dictionary<string, string>> DbSetsByContextFullName { get; }
    public Dictionary<string, CqrsRequest> RequestsByFullName { get; }
    public Dictionary<string, List<CqrsRequest>> RequestsBySimple { get; }
    public Dictionary<string, List<HandlerInfo>> HandlersByFullName { get; }
    public Dictionary<string, ServiceResolution> ResolutionBySignature { get; }
    public Dictionary<string, ServiceResolution> OpenResolutionByIdentity { get; }
    public Dictionary<string, List<FlowEdge>> FlowEdgesByFrom { get; }
    public Dictionary<string, List<ResolvedEdge>> ResolvedEdgesByFrom { get; }
    public Dictionary<string, List<ServiceCallEdge>> ServiceCallEdgesByFrom { get; }

    public IReadOnlyList<PublishedEventHit> GetPublishedEvents()
        => _publishedEvents ??= SystemIntegrationEventScanner.ScanAll(this);

    public IReadOnlyList<PublishedEventHit> GetPublishedEventsForFile(string filePath)
    {
        if (_publishedEventsByFile is null)
        {
            var map = new Dictionary<string, List<PublishedEventHit>>(StringComparer.OrdinalIgnoreCase);
            foreach (var hit in GetPublishedEvents())
            {
                if (!map.TryGetValue(hit.FilePath, out var list))
                    map[hit.FilePath] = list = new List<PublishedEventHit>();
                list.Add(hit);
            }
            _publishedEventsByFile = map;
        }

        return _publishedEventsByFile.TryGetValue(filePath, out var hits)
            ? hits
            : Array.Empty<PublishedEventHit>();
    }

    public Dictionary<string, string> GetClassMemberMap(
        CSharpScanner.ClassDecl decl,
        string code,
        SourceMask mask,
        List<CSharpScanner.ClassDecl> classes)
    {
        if (_classMemberMapCache.TryGetValue(decl, out var cached))
            return cached;

        var computed = SystemIntegrationEventScanner.BuildClassMemberMap(decl, code, mask, classes);
        _classMemberMapCache[decl] = computed;
        return computed;
    }

    public List<string> GetClassDependencies(
        CSharpScanner.ClassDecl decl,
        string code,
        SourceMask mask,
        List<CSharpScanner.ClassDecl> classes)
    {
        if (_classDepsCache.TryGetValue(decl, out var cached))
            return cached;

        var deps = new List<string>();
        DependencyExtractor.Extract(decl, code, mask, classes, deps);
        _classDepsCache[decl] = deps;
        return deps;
    }

    public bool TryGetCachedClassEffects(CSharpScanner.ClassDecl decl, out CachedClassEffects effects)
        => _classEffectsCache.TryGetValue(decl, out effects!);

    public void SetCachedClassEffects(CSharpScanner.ClassDecl decl, CachedClassEffects effects)
        => _classEffectsCache[decl] = effects;

    public ClassMethodIndex GetClassMethodIndex(
        CSharpScanner.ClassDecl decl,
        SourceFile file,
        CSharpScanner.FileScan scan,
        SourceMask mask)
    {
        if (_classMethodIndexCache.TryGetValue(decl, out var cached))
            return cached;

        var computed = ClassMethodIndex.Build(this, decl, file, scan, mask);
        _classMethodIndexCache[decl] = computed;
        return computed;
    }

    public ClassDataAccess? GetClassDataAccess(CSharpScanner.ClassDecl decl)
        => _classDataAccessMap.TryGetValue(decl, out var access) ? access : null;

    public ClassDataAccess GetOrCreateClassDataAccess(CSharpScanner.ClassDecl decl)
    {
        if (_classDataAccessMap.TryGetValue(decl, out var existing))
            return existing;

        existing = new ClassDataAccess();
        _classDataAccessMap[decl] = existing;
        return existing;
    }

    public string? GetGenericRepoStoreForProject(string? projectName)
        => !string.IsNullOrWhiteSpace(projectName) && _genericRepoStoreByProject.TryGetValue(projectName, out var store)
            ? store
            : null;

    public void SetGenericRepoStoreForProject(string projectName, string storeName)
        => _genericRepoStoreByProject[projectName] = storeName;

    public bool IsTestProject(string? projectName)
        => !string.IsNullOrWhiteSpace(projectName) && TestProjects.Contains(projectName);

    public static SystemAnalysisContext Create(
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        AnalysisResult result,
        TypeHierarchy? hierarchy = null,
        SourcePathFilter? pathFilter = null)
    {
        var testProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tp in TestProjectDiscovery.DiscoverTestProjects(result))
            testProjects.Add(tp.Name);

        var projectsByName = new Dictionary<string, ProjectInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in result.Projects)
        {
            projectsByName.TryAdd(p.Name, p);
            if (string.Equals(p.Layer, "Tests", StringComparison.OrdinalIgnoreCase))
                testProjects.Add(p.Name);
        }

        var classes = new List<CSharpScanner.ClassDecl>();
        var interfaces = new List<CSharpScanner.InterfaceDecl>();
        var classByFullName = new Dictionary<string, CSharpScanner.ClassDecl>(StringComparer.Ordinal);
        var classesBySimpleName = new Dictionary<string, List<CSharpScanner.ClassDecl>>(StringComparer.Ordinal);
        var fileByClass = new Dictionary<CSharpScanner.ClassDecl, (SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)>();

        foreach (var entry in parsedFiles)
        {
            foreach (var decl in entry.Scan.Classes)
            {
                classes.Add(decl);
                classByFullName.TryAdd(decl.FullName, decl);
                if (!classesBySimpleName.TryGetValue(decl.Name, out var list))
                    classesBySimpleName[decl.Name] = list = new List<CSharpScanner.ClassDecl>();
                list.Add(decl);
                fileByClass.TryAdd(decl, entry);
            }
            interfaces.AddRange(entry.Scan.InterfaceDecls);
        }

        hierarchy ??= new TypeHierarchy(classes, interfaces);

        var dbContextsByFullName = new Dictionary<string, DbContextInfo>(StringComparer.Ordinal);
        var dbContextBySimpleCandidate = new Dictionary<string, DbContextInfo?>(StringComparer.Ordinal);
        var dbSetsByContextFullName = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        foreach (var ctx in result.DbContexts)
        {
            dbContextsByFullName.TryAdd(ctx.FullName, ctx);
            if (!dbContextBySimpleCandidate.TryGetValue(ctx.Name, out var existing))
                dbContextBySimpleCandidate[ctx.Name] = ctx;
            else if (existing is not null && !string.Equals(existing.FullName, ctx.FullName, StringComparison.Ordinal))
                dbContextBySimpleCandidate[ctx.Name] = null;

            if (!dbSetsByContextFullName.TryGetValue(ctx.FullName, out var setMap))
                dbSetsByContextFullName[ctx.FullName] = setMap = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var set in ctx.DbSets)
            {
                var entitySimple = CSharpScanner.SimpleName(set.Entity);
                setMap.TryAdd(set.Property, $"{set.Property} ({entitySimple})");
            }
        }

        var mappingInput = parsedFiles is List<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> listInput
            ? listInput
            : parsedFiles.ToList();
        var dbSetMappings = DbSetMappingBuilder.Build(mappingInput, result);

        var requestsByFullName = new Dictionary<string, CqrsRequest>(StringComparer.Ordinal);
        var requestsBySimple = new Dictionary<string, List<CqrsRequest>>(StringComparer.Ordinal);
        foreach (var req in result.Requests)
        {
            requestsByFullName.TryAdd(req.FullName, req);
            if (!requestsBySimple.TryGetValue(req.Name, out var list))
                requestsBySimple[req.Name] = list = new List<CqrsRequest>();
            list.Add(req);
        }

        var handlersByFullName = new Dictionary<string, List<HandlerInfo>>(StringComparer.Ordinal);
        foreach (var h in result.Handlers)
        {
            if (!handlersByFullName.TryGetValue(h.FullName, out var list))
                handlersByFullName[h.FullName] = list = new List<HandlerInfo>();
            list.Add(h);
        }

        var resolutionBySignature = new Dictionary<string, ServiceResolution>(StringComparer.Ordinal);
        var openResolutionByIdentity = new Dictionary<string, ServiceResolution>(StringComparer.Ordinal);
        foreach (var sr in result.ServiceResolutions)
        {
            resolutionBySignature.TryAdd(sr.Interface, sr);
            if (sr.IsOpenGeneric)
                openResolutionByIdentity.TryAdd(sr.Identity, sr);
        }

        var flowEdgesByFrom = new Dictionary<string, List<FlowEdge>>(StringComparer.Ordinal);
        foreach (var edge in result.FlowEdges)
        {
            if (!flowEdgesByFrom.TryGetValue(edge.From, out var list))
                flowEdgesByFrom[edge.From] = list = new List<FlowEdge>();
            list.Add(edge);
        }

        var resolvedEdgesByFrom = new Dictionary<string, List<ResolvedEdge>>(StringComparer.Ordinal);
        foreach (var edge in result.ResolvedEdges)
        {
            if (!resolvedEdgesByFrom.TryGetValue(edge.From, out var list))
                resolvedEdgesByFrom[edge.From] = list = new List<ResolvedEdge>();
            list.Add(edge);
        }

        var serviceCallEdgesByFrom = new Dictionary<string, List<ServiceCallEdge>>(StringComparer.Ordinal);
        foreach (var edge in result.ServiceCallEdges)
        {
            if (!serviceCallEdgesByFrom.TryGetValue(edge.From, out var list))
                serviceCallEdgesByFrom[edge.From] = list = new List<ServiceCallEdge>();
            list.Add(edge);
        }

        return new SystemAnalysisContext(
            parsedFiles,
            result,
            hierarchy,
            pathFilter,
            testProjects,
            projectsByName,
            classes,
            classByFullName,
            classesBySimpleName,
            fileByClass,
            dbContextsByFullName,
            dbContextBySimpleCandidate,
            dbSetMappings,
            dbSetsByContextFullName,
            requestsByFullName,
            requestsBySimple,
            handlersByFullName,
            resolutionBySignature,
            openResolutionByIdentity,
            flowEdgesByFrom,
            resolvedEdgesByFrom,
            serviceCallEdgesByFrom);
    }

    public static void AddUniqueOrdinal(List<string> target, string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !target.Contains(value, StringComparer.Ordinal))
            target.Add(value);
    }
}
