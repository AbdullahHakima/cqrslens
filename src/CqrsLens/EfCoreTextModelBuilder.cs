namespace CqrsLens;

/// <summary>
/// Builds the EF Core entity model in text mode. DbSet types are roots; source
/// classes reached through navigation properties join the model only when a key
/// can be established (convention, [Key] or explicit Fluent HasKey). This keeps
/// value objects and unrelated domain classes out of the ER diagram.
/// </summary>
internal sealed class EfCoreTextModelBuilder
{
    private readonly IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan)> _files;
    private readonly TypeHierarchy _hierarchy;
    private readonly AnalysisResult _result;
    private readonly Dictionary<string, (CSharpScanner.ClassDecl Decl, SourceFile File)> _classes;
    private readonly Dictionary<string, List<string>> _configuredKeys;
    private readonly IReadOnlyList<EfRelationshipConfiguration> _configuredRelationships;
    private readonly Dictionary<string, List<string>> _keyCache = new(StringComparer.Ordinal);

    private EfCoreTextModelBuilder(
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan)> files,
        TypeHierarchy hierarchy,
        AnalysisResult result,
        EfFluentConfiguration fluent)
    {
        _files = files;
        _hierarchy = hierarchy;
        _result = result;

        _classes = new Dictionary<string, (CSharpScanner.ClassDecl, SourceFile)>(StringComparer.Ordinal);
        foreach (var (file, scan) in files)
        {
            foreach (var decl in scan.Classes)
            {
                if (!_classes.ContainsKey(decl.FullName))
                    _classes[decl.FullName] = (decl, file);
            }
        }

        _configuredKeys = fluent.Keys
            .GroupBy(k => k.EntityFullName, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.SelectMany(k => k.Properties)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                StringComparer.Ordinal);
        _configuredRelationships = fluent.Relationships;
    }

    public static void Build(
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan)> files,
        TypeHierarchy hierarchy,
        AnalysisResult result)
    {
        if (files.Count == 0 || result.DbContexts.Count == 0) return;

        var fluent = EfFluentConfigurationParser.ParseText(files, hierarchy, result);
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
                var stub = CreateStub(dbSet.Entity, context);
                if (stub is not null)
                {
                    context.Entities.Add(stub);
                    context.Entities.Sort(EntityComparer);
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
        context.Entities.Sort(EntityComparer);

        var relationshipConfigs = _configuredRelationships
            .Where(r => contextEntityNames.Contains(r.PrincipalEntity) &&
                        contextEntityNames.Contains(r.DependentEntity))
            .ToList();
        EfRelationshipBuilder.Build(context, relationshipConfigs, _result);
    }

    private CSharpScanner.ClassDecl? FindContextDeclaration(DbContextInfo context)
    {
        var fullName = ComposeFullName(context.Namespace, context.ContainingTypes, context.Name);
        return _classes.TryGetValue(fullName, out var entry) ? entry.Decl : null;
    }

    private EntityInfo CreateEntity(
        CSharpScanner.ClassDecl declaration,
        IReadOnlySet<string> entityNames)
    {
        var entity = new EntityInfo
        {
            Name = declaration.Name,
            Namespace = declaration.Namespace,
            ProjectName = GetFile(declaration)?.ProjectName,
            Location = LocationOf(declaration, declaration.DeclarationIndex),
        };
        entity.ContainingTypes.AddRange(declaration.ContainingTypes);

        var keys = GetKeyNames(declaration).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (property, declaring) in MappedProperties(declaration))
        {
            if (!property.IsPublic || property.IsStatic) continue;
            if (EfAnnotations.HasAttribute(property.Attributes, "NotMapped") ||
                EfAnnotations.HasAttribute(property.Attributes, "Ignore"))
                continue;

            var isCollection = EfAnnotations.TryGetCollectionElementType(property.Type, out var elementType);
            CSharpScanner.ClassDecl? target = null;
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

            var isNavigation = target is not null;
            var isRequired = EfAnnotations.HasAttribute(property.Attributes, "Required") ||
                             (!isNavigation && !property.IsNullable &&
                              EfAnnotations.IsLikelyValueType(property.Type));
            var info = new EntityPropertyInfo
            {
                Name = property.Name,
                Type = property.Type,
                IsNavigation = isNavigation,
                IsCollection = isNavigation && isCollection,
                TargetEntity = target?.FullName,
                IsRequired = isRequired && !property.IsNullable,
                Location = LocationOf(declaring, property.SourceIndex),
            };
            info.IsKey = keys.Contains(property.Name);
            info.Attributes.AddRange(property.Attributes);
            if (!isNavigation && EfAnnotations.HasAttribute(property.Attributes, "ForeignKey"))
                info.IsForeignKey = true;
            entity.Properties.Add(info);
        }

        // Mapped-property lineage is base-first, but a derived override wins.
        var deduped = entity.Properties
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .OrderBy(p => p.IsKey ? 0 : 1)
            .ThenBy(p => p.Name, StringComparer.Ordinal)
            .ToList();
        entity.Properties.Clear();
        entity.Properties.AddRange(deduped);
        return entity;
    }

    private static EntityInfo? CreateStub(string writtenType, DbContextInfo context)
    {
        var (name, args) = CSharpScanner.ParseTypeReference(EfAnnotations.NormalizeType(writtenType));
        if (args.Count > 0) return null;
        var simple = CSharpScanner.SimpleName(name.TrimEnd('?'));
        if (simple.Length == 0) return null;
        var stub = new EntityInfo
        {
            Name = simple,
            Namespace = CSharpScanner.NamespaceOfType(name),
            ProjectName = context.ProjectName,
            Location = context.Location,
        };
        return stub;
    }

    private IReadOnlyList<string> GetKeyNames(CSharpScanner.ClassDecl declaration)
    {
        if (_keyCache.TryGetValue(declaration.FullName, out var cached))
            return cached;

        if (_configuredKeys.TryGetValue(declaration.FullName, out var configured) &&
            configured.Count > 0)
        {
            _keyCache[declaration.FullName] = configured;
            return configured;
        }

        var properties = MappedProperties(declaration)
            .Select(p => p.Property)
            .Where(p => p.IsPublic && !p.IsStatic)
            .ToList();
        var annotated = properties
            .Where(p => EfAnnotations.HasAttribute(p.Attributes, "Key"))
            .Select(p => p.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (annotated.Count > 0)
        {
            _keyCache[declaration.FullName] = annotated;
            return annotated;
        }

        var byId = properties.FirstOrDefault(p =>
            string.Equals(p.Name, "Id", StringComparison.OrdinalIgnoreCase) &&
            !EfAnnotations.TryGetCollectionElementType(p.Type, out _));
        if (byId is not null)
        {
            var result = new List<string> { byId.Name };
            _keyCache[declaration.FullName] = result;
            return result;
        }

        var conventionalName = declaration.Name + "Id";
        var byName = properties.FirstOrDefault(p =>
            string.Equals(p.Name, conventionalName, StringComparison.OrdinalIgnoreCase) &&
            !EfAnnotations.TryGetCollectionElementType(p.Type, out _));
        var names = byName is null ? new List<string>() : new List<string> { byName.Name };
        _keyCache[declaration.FullName] = names;
        return names;
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

    private static string ComposeFullName(
        string ns,
        IReadOnlyList<string> containingTypes,
        string name)
    {
        var typePath = containingTypes.Count == 0
            ? name
            : string.Join(".", containingTypes) + "." + name;
        return string.IsNullOrEmpty(ns) ? typePath : $"{ns}.{typePath}";
    }

    private static int EntityComparer(EntityInfo left, EntityInfo right)
        => string.Compare(left.FullName, right.FullName, StringComparison.Ordinal);
}
