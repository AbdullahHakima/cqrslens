using System.Runtime.CompilerServices;

namespace CqrsLens;

/// <summary>
/// Stage 2 of the service-resolution work: method-body call edges between scanned classes.
///
/// A <em>source</em> is a request/notification handler, or a service — a concrete class that is
/// the chosen implementation of at least one interface in the Stage 1 index and is injected as
/// a constructor dependency into at least one scanned class. An <em>injected member</em> is a
/// field, property or constructor parameter whose declared type comes from the source's
/// constructor signature. A <em>call edge</em> is an invocation whose target expression is
/// exactly such a member; the target resolves through the Stage 1 index when the member is an
/// interface, or is the concrete class itself when the member is a scanned class.
///
/// Fail closed: property chains, locals, parameters, <c>new</c>-constructed targets, property
/// access, statics, <c>base</c>/<c>this</c>, DbContext targets, self-calls and
/// unresolved/ambiguous interfaces produce no edge.
/// </summary>
internal static class ServiceCallEdgeResolver
{

    public static void Build(
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        AnalysisResult result,
        TypeHierarchy? hierarchy = null)
        => BuildCore(parsedFiles, catalog: null, result, hierarchy, lineIndices: null);

    public static void BuildWithCatalog(
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        ScannedTypeCatalog catalog,
        AnalysisResult result,
        TypeHierarchy? hierarchy,
        IReadOnlyDictionary<string, SourceLineIndex> lineIndices)
        => BuildCore(parsedFiles, catalog, result, hierarchy, lineIndices);

    private static void BuildCore(
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        ScannedTypeCatalog? catalog,
        AnalysisResult result,
        TypeHierarchy? hierarchy,
        IReadOnlyDictionary<string, SourceLineIndex>? lineIndices)
    {
        if (parsedFiles.Count == 0 ||
            (result.Handlers.Count == 0 && result.NotificationHandlers.Count == 0 && result.ServiceResolutions.Count == 0))
        {
            return;
        }

        BuildCoreActive(parsedFiles, catalog, result, hierarchy, lineIndices);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void BuildCoreActive(
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        ScannedTypeCatalog? catalog,
        AnalysisResult result,
        TypeHierarchy? hierarchy,
        IReadOnlyDictionary<string, SourceLineIndex>? lineIndices)
    {
        lineIndices ??= SourceLineIndex.BuildLookup(parsedFiles, result.RootPath);

        var ordered = EnsureSortedByPath(parsedFiles);

        var classes = hierarchy is null ? new List<CSharpScanner.ClassDecl>() : null;
        var interfaces = hierarchy is null ? new List<CSharpScanner.InterfaceDecl>() : null;
        var classByFullName = new Dictionary<string, CSharpScanner.ClassDecl>(StringComparer.Ordinal);
        for (var i = 0; i < ordered.Count; i++)
        {
            var scan = ordered[i].Scan;
            var scanClasses = scan.Classes;
            for (var c = 0; c < scanClasses.Count; c++)
            {
                var declaration = scanClasses[c];
                classes?.Add(declaration);
                classByFullName.TryAdd(declaration.FullName, declaration);
            }
            interfaces?.AddRange(scan.InterfaceDecls);
        }
        if (classByFullName.Count == 0) return;

        // Reuse the shared hierarchy and its warmed resolver cache when the caller provides one.
        hierarchy ??= new TypeHierarchy(classes!, interfaces!);

        var resolutionBySignature = new Dictionary<string, ServiceResolution>(result.ServiceResolutions.Count, StringComparer.Ordinal);
        var interfaceLocationByName = new Dictionary<string, SourceLocation?>(result.ServiceResolutions.Count, StringComparer.Ordinal);
        foreach (var resolution in result.ServiceResolutions)
        {
            resolutionBySignature.TryAdd(resolution.Interface, resolution);
            interfaceLocationByName.TryAdd(resolution.InterfaceName, resolution.InterfaceLocation);
        }

        var contexts = new HashSet<string>(result.DbContexts.Count, StringComparer.Ordinal);
        foreach (var context in result.DbContexts)
            contexts.Add(context.FullName);

        // Source classes: the handler inventory plus services (chosen implementations injected
        // into at least one scanned class).
        var sourceFullNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var handler in result.Handlers)
            sourceFullNames.Add(handler.FullName);
        foreach (var handler in result.NotificationHandlers)
            sourceFullNames.Add(handler.FullName);

        if (resolutionBySignature.Count > 0)
            AddServices(ordered, hierarchy, resolutionBySignature, classByFullName, sourceFullNames);

        // A handler that is not a scanned class produces no body to scan.
        sourceFullNames.RemoveWhere(name => !classByFullName.ContainsKey(name));
        if (sourceFullNames.Count == 0) return;

        var collected = new List<(string FromFullName, string? FromProject, string Via, string Method, string To, SourceLocation? Location, SourceLocation? IfaceLocation)>();
        var membersBuffer = new List<(string Type, string Name)>();
        var resolvedByMember = new Dictionary<string, (string Via, string To)>(StringComparer.Ordinal);
        var hitsBuffer = new List<MemberInvocationHit<(string Via, string To)>>();

        foreach (var parsed in ordered)
        {
            var lineIndex = lineIndices.GetValueOrDefault(parsed.File.Path);
            // Only classes that are sources need their bodies scanned.
            foreach (var declaration in parsed.Scan.Classes)
            {
                if (!sourceFullNames.Contains(declaration.FullName)) continue;
                ScanClass(
                    parsed,
                    declaration,
                    lineIndex,
                    hierarchy,
                    resolutionBySignature,
                    contexts,
                    membersBuffer,
                    resolvedByMember,
                    hitsBuffer,
                    collected);
            }
        }

        if (collected.Count == 0) return;

        EmitCollectedEdges(collected, catalog, hierarchy, interfaceLocationByName, result);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void EmitCollectedEdges(
        List<(string FromFullName, string? FromProject, string Via, string Method, string To, SourceLocation? Location, SourceLocation? IfaceLocation)> collected,
        ScannedTypeCatalog? catalog,
        TypeHierarchy hierarchy,
        Dictionary<string, SourceLocation?> interfaceLocationByName,
        AnalysisResult result)
    {
        if (catalog is not null)
        {
            ServiceAndRepositoryRoleRules.ApplyServiceCallTargets(
                collected.Select(c => (c.Via, c.To, c.FromProject, c.Location)),
                catalog,
                hierarchy,
                result);
        }

        var resolver = result.IdentityResolver;
        var fromCache = new Dictionary<(string From, string? Project), NodeReference>();
        var viaCache = new Dictionary<(string Via, string To), NodeReference>();
        var toCache = new Dictionary<(string To, string Method), NodeReference>();

        foreach (var edge in collected)
        {
            var fromKey = (edge.FromFullName, edge.FromProject);
            if (!fromCache.TryGetValue(fromKey, out var fromRef))
            {
                fromRef = resolver.ResolveTypeOrComponentRef(edge.FromFullName, edge.FromProject);
                fromCache[fromKey] = fromRef;
            }

            var viaKey = (edge.Via, edge.To);
            var isConcrete = string.Equals(edge.Via, edge.To, StringComparison.Ordinal);
            if (!viaCache.TryGetValue(viaKey, out var viaRef))
            {
                SourceLocation? ifaceLocation = null;
                if (!isConcrete)
                    interfaceLocationByName.TryGetValue(edge.Via, out ifaceLocation);
                viaRef = isConcrete
                    ? resolver.ResolveTypeOrComponentRef(edge.Via)
                    : resolver.ResolveInterfaceRef(edge.Via, ifaceLocation);
                viaCache[viaKey] = viaRef;
            }

            var toKey = (edge.To, edge.Method);
            if (!toCache.TryGetValue(toKey, out var toRef))
            {
                toRef = resolver.ResolveMethodTargetRef(edge.To, edge.Method);
                toCache[toKey] = toRef;
            }

            result.Spine.AddOrMergeRelation(
                RelationKind.Calls,
                fromRef,
                toRef,
                SpineIdentityResolver.SyntheticOccurrence(
                    EvidenceRule.InjectedMemberInvocation,
                    EvidenceOrigin.CallSite,
                    edge.Location,
                    isConcrete ? EvidenceStrength.Proven : EvidenceStrength.Resolved,
                    via: [viaRef],
                    scopeProjectName: edge.FromProject),
                method: edge.Method);
        }
    }

    private static IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> EnsureSortedByPath(
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles)
    {
        for (var i = 1; i < parsedFiles.Count; i++)
        {
            if (StringComparer.OrdinalIgnoreCase.Compare(parsedFiles[i - 1].File.Path, parsedFiles[i].File.Path) > 0)
            {
                return parsedFiles
                    .OrderBy(f => f.File.Path, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        return parsedFiles;
    }

    /// <summary>
    /// A service is a concrete class that is the chosen implementation of an interface in the
    /// index and is injected as a constructor dependency into at least one scanned class.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AddServices(
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        TypeHierarchy hierarchy,
        IReadOnlyDictionary<string, ServiceResolution> resolutionBySignature,
        IReadOnlyDictionary<string, CSharpScanner.ClassDecl> classByFullName,
        HashSet<string> sourceFullNames)
    {
        if (resolutionBySignature.Count == 0) return;

        var injected = new HashSet<string>(StringComparer.Ordinal);
        var dependencies = new List<string>();
        foreach (var (file, scan, mask) in parsedFiles)
        {
            foreach (var declaration in scan.Classes)
            {
                dependencies.Clear();
                DependencyExtractor.Extract(declaration, file.Code, mask, scan.Classes, dependencies);
                foreach (var dependency in dependencies)
                {
                    var signature = SignatureFor(hierarchy, dependency, declaration, resolveFallback: true);
                    if (signature is not null) injected.Add(signature);
                }
            }
        }

        if (injected.Count == 0) return;

        foreach (var resolution in resolutionBySignature.Values)
        {
            if (resolution.Implementation is null) continue;
            if (!injected.Contains(resolution.Interface)) continue;
            if (!classByFullName.ContainsKey(resolution.Implementation)) continue;
            sourceFullNames.Add(resolution.Implementation);
        }
    }

    private static void ScanClass(
        (SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask) parsed,
        CSharpScanner.ClassDecl declaration,
        SourceLineIndex? lineIndex,
        TypeHierarchy hierarchy,
        IReadOnlyDictionary<string, ServiceResolution> resolutionBySignature,
        IReadOnlySet<string> contexts,
        List<(string Type, string Name)> members,
        Dictionary<string, (string Via, string To)> resolvedByMember,
        List<MemberInvocationHit<(string Via, string To)>> hitsBuffer,
        List<(string FromFullName, string? FromProject, string Via, string Method, string To, SourceLocation? Location, SourceLocation? IfaceLocation)> collected)
    {
        members.Clear();
        DependencyExtractor.ExtractParameters(declaration, parsed.File.Code, parsed.Mask, parsed.Scan.Classes, members);
        if (members.Count == 0) return;

        var constructorTypes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (type, _) in members)
            constructorTypes.Add(Normalize(type));

        ReceiverTypeMapBuilder.CollectClassLevelDeclarations(
            declaration, parsed.File.Code, parsed.Mask, parsed.Scan.Classes, members);
        foreach (var property in declaration.Properties)
        {
            if (property.Name.Length == 0) continue;
            var norm = Normalize(property.Type);
            if (!members.Contains((norm, property.Name)))
                members.Add((norm, property.Name));
        }

        // Pre-resolve every injected member's DI target once per class rather than per call site.
        resolvedByMember.Clear();
        foreach (var (type, name) in members)
        {
            if (name.Length == 0 || type.Length == 0) continue;
            if (!constructorTypes.Contains(type)) continue;
            if (resolvedByMember.ContainsKey(name)) continue;

            if (TryResolveTarget(type, declaration, hierarchy, resolutionBySignature, contexts, out var via, out var to) &&
                !string.Equals(to, declaration.FullName, StringComparison.Ordinal))
            {
                resolvedByMember[name] = (via, to);
            }
        }
        if (resolvedByMember.Count == 0) return;

        var code = parsed.File.Code;
        var bodyEnd = Math.Min(code.Length, declaration.BodySpan.End);
        hitsBuffer.Clear();
        MemberInvocationScanner.FindMatches(
            code, parsed.Mask, declaration.BodySpan.Start, bodyEnd, resolvedByMember, hitsBuffer);

        var multiClass = parsed.Scan.Classes.Count > 1;
        foreach (var hit in hitsBuffer)
        {
            if (multiClass &&
                !ReferenceEquals(DeepSliceSyntax.InnermostClass(parsed.Scan.Classes, hit.Index), declaration))
            {
                continue;
            }

            if (hit.Method.Length == 0) continue;

            var location = lineIndex is not null
                ? lineIndex.Create(hit.Index)
                : SourceLocation.Create(parsed.File.Path, "", code, hit.Index);
            collected.Add((declaration.FullName, parsed.File.ProjectName, hit.Target.Via, hit.Method, hit.Target.To, location, null));
        }
    }

    private static bool TryResolveTarget(
        string memberType,
        CSharpScanner.ClassDecl declaring,
        TypeHierarchy hierarchy,
        IReadOnlyDictionary<string, ServiceResolution> resolutionBySignature,
        IReadOnlySet<string> contexts,
        out string via,
        out string to)
    {
        via = "";
        to = "";

        var (name, arguments) = CSharpScanner.ParseTypeReference(memberType);
        if (name.Length == 0) return false;

        var identity = hierarchy.ResolveInterfaceReference(
            name, declaring.Namespace, declaring.Usings, declaring.ContainingTypes, arguments.Count, out _);
        if (identity is null)
        {
            // An interface declared outside the scan keeps its simple name, exactly as the
            // Stage 1 index records a registration for an external interface.
            identity = CSharpScanner.SimpleName(name);
            if (arguments.Count > 0 && !identity.EndsWith("`" + arguments.Count, StringComparison.Ordinal))
                identity = CSharpScanner.TypeIdentity(identity, arguments.Count);
        }

        var signature = arguments.Count == 0
            ? identity
            : identity + "<" + string.Join(", ", arguments) + ">";

        if (resolutionBySignature.TryGetValue(signature, out var resolution))
        {
            // Resolved through the index: ambiguous or unresolved yields no edge, and a
            // DbContext implementation is already represented by the Stage 1 dependency edges.
            if (resolution.Implementation is null) return false;
            if (contexts.Contains(resolution.Implementation)) return false;
            via = resolution.InterfaceName;
            to = resolution.Implementation;
            return true;
        }

        // Not an index interface: a concrete class declared in the scan is a direct edge.
        var classDecl = hierarchy.TryResolveClass(
            name, declaring.Namespace, declaring.Usings, declaring.ContainingTypes, arguments.Count, out _);
        if (classDecl is null) return false;
        if (contexts.Contains(classDecl.FullName)) return false;
        via = classDecl.FullName;
        to = classDecl.FullName;
        return true;
    }

    /// <summary>
    /// The interface signature a written type resolves to, mirroring the Stage 1 index keying:
    /// resolved identity when the interface is in the scan, otherwise the simple name (with an
    /// arity suffix when generic).
    /// </summary>
    private static string? SignatureFor(
        TypeHierarchy hierarchy,
        string writtenType,
        CSharpScanner.ClassDecl declaring,
        bool resolveFallback)
    {
        var (name, arguments) = CSharpScanner.ParseTypeReference(writtenType);
        if (name.Length == 0) return null;

        var identity = hierarchy.ResolveInterfaceReference(
            name, declaring.Namespace, declaring.Usings, declaring.ContainingTypes, arguments.Count, out _);
        if (identity is null)
        {
            if (!resolveFallback) return null;
            identity = CSharpScanner.SimpleName(name);
            if (arguments.Count > 0 && !identity.EndsWith("`" + arguments.Count, StringComparison.Ordinal))
                identity = CSharpScanner.TypeIdentity(identity, arguments.Count);
        }

        return arguments.Count == 0
            ? identity
            : identity + "<" + string.Join(", ", arguments) + ">";
    }

    private static string Normalize(string type) => MemberInvocationScanner.CollapseWhitespace(type);
}
