using System.Text.RegularExpressions;
using static CqrsLens.DeepSlicePatterns;

namespace CqrsLens;

/// <summary>
/// Propagates touched DbSet/entity labels to handlers across multi-hop unambiguous DI service
/// and repository chains (max depth 4), failing closed on ambiguous or unresolved hops and
/// never widening generic parameters across unrelated entity types.
/// </summary>
internal static class TransitiveEntityLineageResolver
{
    private const int MaxServiceHops = 4;

    public static void Resolve(
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        AnalysisResult result,
        TypeHierarchy? hierarchy = null)
    {
        if (result.DbContexts.Count == 0 ||
            (result.Handlers.Count == 0 && result.NotificationHandlers.Count == 0))
            return;

        var mappingInput = parsedFiles
            .Select(p => (p.File, p.Scan, p.Mask))
            .ToList();
        var mappings = DbSetMappingBuilder.Build(mappingInput, result);
        if (mappings.DbSetsByProperty.Count == 0 && mappings.DbSetsByEntity.Count == 0)
            return;

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
        var entryByClass = new Dictionary<CSharpScanner.ClassDecl, (SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)>();
        foreach (var entry in parsedFiles)
        {
            foreach (var decl in entry.Scan.Classes)
            {
                classes.Add(decl);
                classByFullName.TryAdd(decl.FullName, decl);
                entryByClass.TryAdd(decl, entry);
            }
            interfaces.AddRange(entry.Scan.InterfaceDecls);
        }

        hierarchy ??= new TypeHierarchy(classes, interfaces);

        var handlerFullNames = new HashSet<string>(
            result.Handlers.Select(h => h.FullName)
                .Concat(result.NotificationHandlers.Select(nh => nh.FullName)),
            StringComparer.Ordinal);
        var dbContextFullNames = new HashSet<string>(
            result.DbContexts.Select(c => c.FullName),
            StringComparer.Ordinal);

        // Compute direct concrete entity touches + constructor deps per non-handler concrete class.
        var directEntitiesByClass = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var depsByClass = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var emptyRepoEntities = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var (file, scan, mask) in parsedFiles)
        {
            foreach (var decl in scan.Classes)
            {
                if (decl.IsAbstract ||
                    handlerFullNames.Contains(decl.FullName) ||
                    dbContextFullNames.Contains(decl.FullName))
                    continue;

                var deps = new List<string>();
                DependencyExtractor.Extract(decl, file.Code, mask, scan.Classes, deps);
                depsByClass[decl.FullName] = deps;

                var touched = new List<string>();
                void AddEntity(string label)
                {
                    if (!touched.Contains(label, StringComparer.Ordinal))
                        touched.Add(label);
                }

                // Generic/named repo signals only for non-open-generic classes and concrete entity names.
                foreach (var typeRef in deps.Concat(CSharpScanner.SplitTopLevel(decl.BaseList)))
                {
                    foreach (Match m in GenericRepositoryRegex().Matches(typeRef))
                    {
                        var entitySimple = CSharpScanner.SimpleName(m.Groups["entity"].Value.Trim());
                        if (!ReceiverTypeMapBuilder.IsEntityCandidate(entitySimple, decl, mappings.DbSetsByEntity))
                            continue;
                        if (mappings.DbSetsByEntity.TryGetValue(entitySimple, out var label))
                            AddEntity(label);
                    }
                }

                if (decl.TypeParameters.Count == 0)
                {
                    var named = NamedRepositoryRegex().Match(decl.Name);
                    if (named.Success &&
                        mappings.DbSetsByEntity.TryGetValue(named.Groups["entity"].Value, out var namedLabel))
                    {
                        AddEntity(namedLabel);
                    }
                }

                if (decl.BodySpan.End > decl.BodySpan.Start)
                {
                    var bodyStart = decl.BodySpan.Start;
                    var bodyEnd = Math.Min(file.Code.Length, decl.BodySpan.End);
                    Dictionary<string, string>? receiverTypes = null;

                    foreach (Match match in MemberAccessRegex().Matches(file.Code, bodyStart))
                    {
                        if (match.Index >= bodyEnd) break;
                        if (!mappings.DbSetsByProperty.TryGetValue(match.Groups["prop"].Value, out var label)) continue;
                        if (!mask.IsCode(match.Index)) continue;
                        if (!ReferenceEquals(DeepSliceSyntax.InnermostClass(scan.Classes, match.Index), decl)) continue;

                        receiverTypes ??= ReceiverTypeMapBuilder.Build(
                            decl, file.Code, mask, scan.Classes,
                            mappings.DbSetsByProperty, mappings.DbContextTypeNames, emptyRepoEntities);
                        if (ReceiverTypeMapBuilder.IsDbContextReceiver(
                                ReceiverTypeMapBuilder.GetReceiverIdentifier(file.Code, match.Index),
                                receiverTypes,
                                mappings.DbContextTypeNames))
                        {
                            AddEntity(label);
                        }
                    }

                    foreach (Match match in DbContextSetMethodRegex().Matches(file.Code, bodyStart))
                    {
                        if (match.Index >= bodyEnd) break;
                        if (!mask.IsCode(match.Index)) continue;
                        if (!ReferenceEquals(DeepSliceSyntax.InnermostClass(scan.Classes, match.Index), decl)) continue;

                        receiverTypes ??= ReceiverTypeMapBuilder.Build(
                            decl, file.Code, mask, scan.Classes,
                            mappings.DbSetsByProperty, mappings.DbContextTypeNames, emptyRepoEntities);
                        if (!ReceiverTypeMapBuilder.IsDbContextReceiver(
                                ReceiverTypeMapBuilder.GetReceiverIdentifier(file.Code, match.Index),
                                receiverTypes,
                                mappings.DbContextTypeNames))
                            continue;

                        var entitySimple = CSharpScanner.SimpleName(match.Groups["entity"].Value.Trim());
                        if (!ReceiverTypeMapBuilder.IsEntityCandidate(entitySimple, decl, mappings.DbSetsByEntity))
                            continue;
                        if (mappings.DbSetsByEntity.TryGetValue(entitySimple, out var label))
                            AddEntity(label);
                    }
                }

                if (touched.Count > 0)
                    directEntitiesByClass[decl.FullName] = touched;
            }
        }

        var changed = false;

        foreach (var handler in result.Handlers)
        {
            classByFullName.TryGetValue(handler.FullName, out var decl);
            if (EnrichHandlerTargets(
                    handler.Dependencies,
                    decl,
                    handler.Namespace,
                    handler.TouchedEntities,
                    hierarchy,
                    resolutionBySignature,
                    openResolutionByIdentity,
                    classByFullName,
                    depsByClass,
                    directEntitiesByClass,
                    mappings.DbSetsByEntity))
            {
                changed = true;
            }
        }

        foreach (var nh in result.NotificationHandlers)
        {
            classByFullName.TryGetValue(nh.FullName, out var decl);
            if (EnrichHandlerTargets(
                    nh.Dependencies,
                    decl,
                    nh.Namespace,
                    nh.TouchedEntities,
                    hierarchy,
                    resolutionBySignature,
                    openResolutionByIdentity,
                    classByFullName,
                    depsByClass,
                    directEntitiesByClass,
                    mappings.DbSetsByEntity))
            {
                changed = true;
            }
        }

        if (changed)
            HandlerEffectRollup.Apply(result);
    }

    private static bool EnrichHandlerTargets(
        IReadOnlyList<string> rootDependencies,
        CSharpScanner.ClassDecl? rootDecl,
        string rootNamespace,
        List<string> touchedEntities,
        TypeHierarchy hierarchy,
        IReadOnlyDictionary<string, ServiceResolution> resolutionBySignature,
        IReadOnlyDictionary<string, ServiceResolution> openResolutionByIdentity,
        IReadOnlyDictionary<string, CSharpScanner.ClassDecl> classByFullName,
        IReadOnlyDictionary<string, List<string>> depsByClass,
        IReadOnlyDictionary<string, List<string>> directEntitiesByClass,
        Dictionary<string, string> dbSetsByEntity)
    {
        if (rootDependencies.Count == 0)
            return false;

        var added = false;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<(CSharpScanner.ClassDecl Class, int Depth)>();

        void AddEntityLabel(string label)
        {
            if (!touchedEntities.Contains(label, StringComparer.Ordinal))
            {
                touchedEntities.Add(label);
                added = true;
            }
        }

        void ProcessDependencies(
            IReadOnlyList<string> deps,
            CSharpScanner.ClassDecl? scopeDecl,
            string currentNamespace,
            IReadOnlyList<UsingDirective> usings,
            IReadOnlyList<string> containingTypes,
            int nextDepth)
        {
            foreach (var dep in deps)
            {
                // Extract closed generic entity args along service/repo deps when valid concrete entities.
                if (scopeDecl is not null)
                {
                    foreach (Match m in GenericRepositoryRegex().Matches(dep))
                    {
                        var entitySimple = CSharpScanner.SimpleName(m.Groups["entity"].Value.Trim());
                        if (!ReceiverTypeMapBuilder.IsEntityCandidate(entitySimple, scopeDecl, dbSetsByEntity))
                            continue;
                        if (dbSetsByEntity.TryGetValue(entitySimple, out var entityLabel))
                            AddEntityLabel(entityLabel);
                    }
                }

                var targetClass = ResolveServiceClass(
                    dep, currentNamespace, usings, containingTypes,
                    hierarchy, resolutionBySignature, openResolutionByIdentity, classByFullName);
                if (targetClass is null || !visited.Add(targetClass.FullName))
                    continue;

                if (directEntitiesByClass.TryGetValue(targetClass.FullName, out var directLabels))
                {
                    foreach (var label in directLabels)
                        AddEntityLabel(label);
                }

                if (nextDepth <= MaxServiceHops)
                    queue.Enqueue((targetClass, nextDepth));
            }
        }

        IReadOnlyList<UsingDirective> rootUsings =
            rootDecl?.Usings ?? (IReadOnlyList<UsingDirective>)Array.Empty<UsingDirective>();
        IReadOnlyList<string> rootContaining =
            rootDecl?.ContainingTypes ?? (IReadOnlyList<string>)Array.Empty<string>();

        ProcessDependencies(rootDependencies, rootDecl, rootNamespace, rootUsings, rootContaining, nextDepth: 1);

        while (queue.Count > 0)
        {
            var (svc, depth) = queue.Dequeue();
            if (!depsByClass.TryGetValue(svc.FullName, out var svcDeps) || svcDeps.Count == 0)
                continue;

            ProcessDependencies(svcDeps, svc, svc.Namespace, svc.Usings, svc.ContainingTypes, nextDepth: depth + 1);
        }

        return added;
    }

    private static CSharpScanner.ClassDecl? ResolveServiceClass(
        string typeReference,
        string currentNamespace,
        IReadOnlyList<UsingDirective> usings,
        IReadOnlyList<string> containingTypes,
        TypeHierarchy hierarchy,
        IReadOnlyDictionary<string, ServiceResolution> resolutionBySignature,
        IReadOnlyDictionary<string, ServiceResolution> openResolutionByIdentity,
        IReadOnlyDictionary<string, CSharpScanner.ClassDecl> classByFullName)
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
            return resolution.Implementation is not null && classByFullName.TryGetValue(resolution.Implementation, out var impl)
                ? impl
                : null;

        if (arguments.Count > 0 && openResolutionByIdentity.TryGetValue(identity, out var openResolution))
            return openResolution.Implementation is not null && classByFullName.TryGetValue(openResolution.Implementation, out var openImpl)
                ? openImpl
                : null;

        return hierarchy.TryResolveClass(
            name, currentNamespace, usings, containingTypes, arguments.Count, out _);
    }
}
