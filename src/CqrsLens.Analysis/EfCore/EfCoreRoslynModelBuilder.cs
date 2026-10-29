using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CqrsLens;

/// <summary>
/// Builds the EF Core entity model in Roslyn mode using declared symbols and
/// semantic type resolution, while sharing key/annotation/relationship rules
/// with text mode so both backends stay in lockstep.
/// </summary>
internal sealed class EfCoreRoslynModelBuilder
{
    private readonly RoslynEntityIndex _index;
    private readonly RoslynEntityResolver _resolver;
    private readonly AnalysisResult _result;
    private readonly EfKeyIndex _keys;
    private readonly IReadOnlyList<EfRelationshipConfiguration> _configuredRelationships;

    private EfCoreRoslynModelBuilder(
        Compilation compilation,
        IReadOnlyList<(SourceFile File, SyntaxTree Tree)> parsed,
        TypeHierarchy hierarchy,
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan)> textScans,
        AnalysisResult result,
        EfFluentConfiguration fluent)
    {
        _result = result;
        _index = new RoslynEntityIndex(compilation, parsed, textScans, result.RootPath);
        _resolver = new RoslynEntityResolver(_index, hierarchy, result);
        _keys = new EfKeyIndex(fluent);
        _configuredRelationships = fluent.Relationships;
    }

    public static void Build(
        Compilation compilation,
        IReadOnlyList<(SourceFile File, SyntaxTree Tree)> parsed,
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan)> ordered,
        TypeHierarchy hierarchy,
        AnalysisResult result)
    {
        if (parsed.Count == 0 || result.DbContexts.Count == 0)
            return;

        var fluent = EfFluentConfigurationParser.ParseText(ordered, hierarchy, result);

        var builder = new EfCoreRoslynModelBuilder(
            compilation, parsed, hierarchy, ordered, result, fluent);
        foreach (var context in result.DbContexts)
            builder.BuildContext(context);
    }

    private void BuildContext(DbContextInfo context)
    {
        var contextFullName = TypeFullName.Compose(context.Namespace, context.ContainingTypes, context.Name);
        if (!_index.TryGetClass(contextFullName, out var contextDecl))
            return;

        var roots = new HashSet<string>(StringComparer.Ordinal);
        var orderedRoots = new List<RoslynEntityDecl>();

        var dbSetProperties = contextDecl.Symbol
            .GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => p.Type is INamedTypeSymbol { Name: "DbSet", TypeArguments.Length: 1 })
            .ToDictionary(p => p.Name, p => p, StringComparer.Ordinal);

        foreach (var dbSet in context.DbSets)
        {
            ITypeSymbol? dbSetTargetSymbol = null;
            TypeSyntax? dbSetTargetSyntax = null;
            if (dbSetProperties.TryGetValue(dbSet.Property, out var propSymbol) &&
                propSymbol.Type is INamedTypeSymbol { TypeArguments.Length: 1 } namedDbSet)
            {
                dbSetTargetSymbol = namedDbSet.TypeArguments[0];
                var syntaxRef = propSymbol.DeclaringSyntaxReferences.FirstOrDefault();
                if (syntaxRef?.GetSyntax() is PropertyDeclarationSyntax propSyntax &&
                    propSyntax.Type is GenericNameSyntax { TypeArgumentList.Arguments.Count: 1 } genericSyntax)
                {
                    dbSetTargetSyntax = genericSyntax.TypeArgumentList.Arguments[0];
                }
            }

            if (!_resolver.TryResolveEntityDecl(
                    dbSet.Entity,
                    dbSetTargetSymbol,
                    dbSetTargetSyntax,
                    contextDecl,
                    recordAmbiguity: true,
                    out var entityDecl))
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

        var entityDecls = new Dictionary<string, RoslynEntityDecl>(StringComparer.Ordinal);
        var queue = new Queue<RoslynEntityDecl>();
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

                var target = _resolver.ResolveNavigationTarget(declaring, property);
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

    private EntityInfo CreateEntity(
        RoslynEntityDecl declaration,
        IReadOnlySet<string> entityNames)
    {
        var mapped = new List<EfMappedProperty>();
        foreach (var (property, declaring) in MappedProperties(declaration))
        {
            if (!property.IsPublic || property.IsStatic) continue;
            if (EfAnnotations.HasAttribute(property.Attributes, "NotMapped") ||
                EfAnnotations.HasAttribute(property.Attributes, "Ignore"))
                continue;

            var isCollection = RoslynEntityResolver.TryGetCollectionElement(
                property, out var elementWrittenType, out var elementSymbol);
            RoslynEntityDecl? target;
            if (isCollection)
            {
                _resolver.TryResolveEntityDecl(
                    elementWrittenType,
                    elementSymbol,
                    null,
                    declaring,
                    recordAmbiguity: false,
                    out target);
                if (target is null || !entityNames.Contains(target.FullName))
                    continue;
            }
            else
            {
                _resolver.TryResolveEntityDecl(
                    property.Type,
                    property.ResolvedType,
                    property.TypeSyntax,
                    declaring,
                    recordAmbiguity: false,
                    out target);
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
                property.Location,
                property.Attributes));
        }

        return EfEntityFactory.Create(
            declaration.Symbol.Name,
            RoslynSymbolFacts.NamespaceOf(declaration.Symbol),
            RoslynSymbolFacts.ContainingTypeNames(declaration.Symbol),
            declaration.File.ProjectName,
            RoslynSymbolFacts.LocationOf(declaration.Symbol, _result.RootPath),
            mapped,
            GetKeyNames(declaration));
    }

    private IReadOnlyList<string> GetKeyNames(RoslynEntityDecl declaration)
    {
        var candidates = MappedProperties(declaration)
            .Select(p => p.Property)
            .Where(p => p.IsPublic && !p.IsStatic)
            .Select(p => new EfKeyCandidate(
                p.Name,
                RoslynEntityResolver.TryGetCollectionElement(p, out _, out _),
                p.Attributes))
            .ToList();
        return _keys.Resolve(declaration.FullName, declaration.Symbol.Name, candidates);
    }

    private IEnumerable<(RoslynPropertyDecl Property, RoslynEntityDecl Declaring)>
        MappedProperties(RoslynEntityDecl declaration)
    {
        var chain = new List<RoslynEntityDecl>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = declaration;
        while (current is not null && seen.Add(current.FullName))
        {
            chain.Insert(0, current);
            current = _resolver.FirstBaseClass(current);
        }

        var properties = new List<(RoslynPropertyDecl, RoslynEntityDecl)>();
        foreach (var type in chain)
        {
            foreach (var property in type.Properties)
                properties.Add((property, type));
        }

        return properties;
    }
}
