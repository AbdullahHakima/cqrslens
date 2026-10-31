namespace CqrsLens;

/// <summary>
/// Builds the EF Core entity model in text mode. DbSet types are roots; source
/// classes reached through navigation properties join the model only when a key
/// can be established (convention, [Key] or explicit Fluent HasKey). This keeps
/// value objects and unrelated domain classes out of the ER diagram.
/// </summary>
internal sealed class EfCoreTextModelBuilder
{
    private readonly TypeHierarchy _hierarchy;
    private readonly AnalysisResult _result;
    private readonly Dictionary<string, (CSharpScanner.ClassDecl Decl, SourceFile File)> _classes;
    private readonly EfKeyIndex _keys;
    private readonly IReadOnlyList<EfRelationshipConfiguration> _configuredRelationships;

    private EfCoreTextModelBuilder(
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan)> files,
        TypeHierarchy hierarchy,
        AnalysisResult result,
        EfFluentConfiguration fluent)
    {
        _hierarchy = hierarchy;
        _result = result;
        _keys = new EfKeyIndex(fluent);

        _classes = new Dictionary<string, (CSharpScanner.ClassDecl, SourceFile)>(StringComparer.Ordinal);
        foreach (var (file, scan) in files)
        {
            foreach (var decl in scan.Classes)
            {
                if (!_classes.ContainsKey(decl.FullName))
                    _classes[decl.FullName] = (decl, file);
            }
        }

        _configuredRelationships = fluent.Relationships;
    }

    public static void Build(
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan)> files,
        TypeHierarchy hierarchy,
        AnalysisResult result,
        IReadOnlyDictionary<string, SourceMask>? masks = null)
    {
        if (files.Count == 0 || result.DbContexts.Count == 0) return;

        var fluent = EfFluentConfigurationParser.ParseText(files, hierarchy, result, masks);
        var builder = new EfCoreTextModelBuilder(files, hierarchy, result, fluent);
        foreach (var context in result.DbContexts)
            builder.BuildContext(context);
    }

    private void BuildContext(DbContextInfo context)
    {
        var contextDecl = FindContextDeclaration(context);
        if (contextDecl is null) return;
        var contextFile = _classes[contextDecl.FullName].File;
        var roots = new HashSet<string>(StringComparer.Ordinal);
        var orderedRoots = new List<CSharpScanner.ClassDecl>();

        foreach (var dbSet in context.DbSets)
        {
            if (!TryResolveClassReference(
                    dbSet.Entity,
                    contextDecl,
                    contextFile,
                    out var entityDecl,
                    out _))
            {
                var stub = EfEntityStubFactory.Create(dbSet.Entity, context);
                if (stub is not null)
                {
                    context.Entities.Add(stub);
                    context.Entities.Sort(EfEntityOrdering.ByFullName);
                }
                continue;
            }

            dbSet.EntityFullName = entityDecl.FullName;
            if (roots.Add(entityDecl.FullName))
                orderedRoots.Add(entityDecl);
        }

        var entityDecls = new Dictionary<string, CSharpScanner.ClassDecl>(StringComparer.Ordinal);
        var queue = new Queue<CSharpScanner.ClassDecl>();
        foreach (var root in orderedRoots)
        {
            if (entityDecls.TryAdd(root.FullName, root))
                queue.Enqueue(root);
        }

        while (queue.Count > 0)
        {
            var declaration = queue.Dequeue();
            foreach (var (property, declaring) in MappedProperties(declaration))
            {
                if (!property.IsPublic || property.IsStatic) continue;
                if (EfAnnotations.HasAttribute(property.Attributes, "NotMapped") ||
                    EfAnnotations.HasAttribute(property.Attributes, "Ignore"))
                    continue;

                var target = ResolveNavigationTarget(declaring, property);
                if (target is null) continue;
                if (!roots.Contains(target.FullName) && GetKeyNames(target).Count == 0)
                    continue;
                if (entityDecls.TryAdd(target.FullName, target))
                    queue.Enqueue(target);
            }
        }

        var contextEntityNames = entityDecls.Keys.ToHashSet(StringComparer.Ordinal);
        var entities = new List<EntityInfo>(entityDecls.Count);
        foreach (var declaration in entityDecls.Values.OrderBy(e => e.FullName, StringComparer.Ordinal))
            entities.Add(CreateEntity(declaration, contextEntityNames));
        context.Entities.AddRange(entities);
        context.Entities.Sort(EfEntityOrdering.ByFullName);

        var relationshipConfigs = _configuredRelationships
            .Where(r => contextEntityNames.Contains(r.PrincipalEntity) &&
                        contextEntityNames.Contains(r.DependentEntity))
            .ToList();
        EfRelationshipBuilder.Build(context, relationshipConfigs, _result);
    }

    private CSharpScanner.ClassDecl? FindContextDeclaration(DbContextInfo context)
    {
        var fullName = TypeFullName.Compose(context.Namespace, context.ContainingTypes, context.Name);
        return _classes.TryGetValue(fullName, out var entry) ? entry.Decl : null;
    }

    private EntityInfo CreateEntity(
        CSharpScanner.ClassDecl declaration,
        IReadOnlySet<string> entityNames)
    {
        var mapped = new List<EfMappedProperty>();
        foreach (var (property, declaring) in MappedProperties(declaration))
        {
            if (!property.IsPublic || property.IsStatic) continue;
            if (EfAnnotations.HasAttribute(property.Attributes, "NotMapped") ||
                EfAnnotations.HasAttribute(property.Attributes, "Ignore"))
                continue;

            var isCollection = EfAnnotations.TryGetCollectionElementType(property.Type, out var elementType);
            CSharpScanner.ClassDecl? target;
            if (isCollection)
            {
                target = ResolveClassReference(elementType, declaring);
                if (target is null || !entityNames.Contains(target.FullName))
                    continue;
            }
            else
            {
                target = ResolveClassReference(property.Type, declaring);
                if (target is not null && !entityNames.Contains(target.FullName))
                    target = null;
            }

            mapped.Add(new EfMappedProperty(
                property.Name,
                property.Type,
                property.IsPublic,
                property.IsStatic,
                isCollection,
                property.IsNullable,
                target?.FullName,
                LocationOf(declaring, property.SourceIndex),
                property.Attributes));
        }

        return EfEntityFactory.Create(
            declaration.Name,
            declaration.Namespace,
            declaration.ContainingTypes,
            GetFile(declaration)?.ProjectName,
            LocationOf(declaration, declaration.DeclarationIndex),
            mapped,
            GetKeyNames(declaration));
    }

    private IReadOnlyList<string> GetKeyNames(CSharpScanner.ClassDecl declaration)
    {
        var candidates = MappedProperties(declaration)
            .Select(p => p.Property)
            .Where(p => p.IsPublic && !p.IsStatic)
            .Select(p => new EfKeyCandidate(
                p.Name,
                EfAnnotations.TryGetCollectionElementType(p.Type, out _),
                p.Attributes))
            .ToList();
        return _keys.Resolve(declaration.FullName, declaration.Name, candidates);
    }

    private IEnumerable<(CSharpScanner.PropertyDecl Property, CSharpScanner.ClassDecl Declaring)>
        MappedProperties(CSharpScanner.ClassDecl declaration)
    {
        var chain = new List<CSharpScanner.ClassDecl>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = declaration;
        while (current is not null && seen.Add(current.FullName))
        {
            chain.Insert(0, current);
            current = FirstBaseClass(current);
        }

        var properties = new List<(CSharpScanner.PropertyDecl, CSharpScanner.ClassDecl)>();
        foreach (var type in chain)
        {
            foreach (var property in type.Properties)
                properties.Add((property, type));
        }

        return properties;
    }

    private CSharpScanner.ClassDecl? FirstBaseClass(CSharpScanner.ClassDecl declaration)
    {
        foreach (var part in CSharpScanner.SplitTopLevel(declaration.BaseList))
        {
            var (name, args) = CSharpScanner.ParseTypeReference(part);
            if (name.Length == 0) continue;
            var resolved = _hierarchy.TryResolveClass(
                name,
                declaration.Namespace,
                declaration.Usings,
                declaration.ContainingTypes,
                args.Count,
                out _);
            if (resolved is not null)
                return resolved;
        }

        return null;
    }

    private CSharpScanner.ClassDecl? ResolveNavigationTarget(
        CSharpScanner.ClassDecl declaring,
        CSharpScanner.PropertyDecl property)
    {
        var type = property.Type;
        if (EfAnnotations.TryGetCollectionElementType(type, out var elementType))
            type = elementType;
        return ResolveClassReference(type, declaring);
    }

    private CSharpScanner.ClassDecl? ResolveClassReference(
        string writtenType,
        CSharpScanner.ClassDecl context)
        => TryResolveClassReference(writtenType, context, GetFile(context), out var resolved, out _)
            ? resolved
            : null;

    private bool TryResolveClassReference(
        string writtenType,
        CSharpScanner.ClassDecl context,
        SourceFile? contextFile,
        out CSharpScanner.ClassDecl resolved,
        out IReadOnlyList<string> ambiguous)
    {
        resolved = null!;
        ambiguous = Array.Empty<string>();
        var normalized = EfAnnotations.NormalizeType(writtenType);
        if (normalized.EndsWith('?')) normalized = normalized[..^1];
        var (name, args) = CSharpScanner.ParseTypeReference(normalized);
        if (name.Length == 0) return false;
        var candidate = _hierarchy.TryResolveClass(
            name,
            context.Namespace,
            context.Usings,
            context.ContainingTypes,
            args.Count,
            out ambiguous);
        if (candidate is null)
        {
            if (ambiguous.Count > 0)
            {
                _result.RecordDiagnostic(new AnalysisDiagnostic(
                    AnalysisResult.AmbiguousLinkCode,
                    AnalysisDiagnosticSeverity.Warning,
                    $"EF Core type `{name}` matches more than one source class: " +
                    string.Join(", ", ambiguous.OrderBy(a => a, StringComparer.Ordinal)) + ".",
                    contextFile is null
                        ? null
                        : LocationOf(context, context.DeclarationIndex),
                    contextFile?.ProjectName));
            }
            return false;
        }

        resolved = candidate;
        return true;
    }

    private SourceFile? GetFile(CSharpScanner.ClassDecl declaration)
        => _classes.TryGetValue(declaration.FullName, out var entry) ? entry.File : null;

    private SourceLocation? LocationOf(CSharpScanner.ClassDecl declaration, int index)
    {
        var file = GetFile(declaration);
        return file is null
            ? null
            : SourceLocation.Create(file.Path, _result.RootPath, file.Code, index);
    }
}
