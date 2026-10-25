using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CqrsLens;

/// <summary>
/// Builds the EF Core entity model in Roslyn mode using declared symbols and
/// semantic type resolution, while sharing key/annotation/relationship rules
/// with text mode so both backends stay in lockstep.
/// </summary>
internal sealed class EfCoreRoslynModelBuilder
{
    private readonly Compilation _compilation;
    private readonly TypeHierarchy _hierarchy;
    private readonly AnalysisResult _result;
    private readonly Dictionary<string, RoslynEntityDecl> _classes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CSharpScanner.ClassDecl> _textClasses = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _configuredKeys;
    private readonly IReadOnlyList<EfRelationshipConfiguration> _configuredRelationships;
    private readonly Dictionary<string, List<string>> _keyCache = new(StringComparer.Ordinal);

    private sealed class RoslynEntityDecl
    {
        public required INamedTypeSymbol Symbol { get; init; }
        public required TypeDeclarationSyntax Syntax { get; init; }
        public required SemanticModel Model { get; init; }
        public required SourceFile File { get; init; }
        public required string FullName { get; init; }
        public List<RoslynPropertyDecl> Properties { get; } = new();
    }

    private sealed class RoslynPropertyDecl
    {
        public required string Name { get; init; }
        public required string Type { get; init; }
        public required bool IsNullable { get; init; }
        public required bool IsPublic { get; init; }
        public required bool IsStatic { get; init; }
        public ITypeSymbol? ResolvedType { get; init; }
        public TypeSyntax? TypeSyntax { get; init; }
        public SourceLocation? Location { get; init; }
        public List<string> Attributes { get; } = new();
    }

    private EfCoreRoslynModelBuilder(
        Compilation compilation,
        IReadOnlyList<(SourceFile File, SyntaxTree Tree)> parsed,
        TypeHierarchy hierarchy,
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan)> textScans,
        AnalysisResult result,
        EfFluentConfiguration fluent)
    {
        _compilation = compilation;
        _hierarchy = hierarchy;
        _result = result;

        foreach (var (_, scan) in textScans)
        {
            foreach (var decl in scan.Classes)
                _textClasses.TryAdd(decl.FullName, decl);
        }

        foreach (var (file, tree) in parsed)
        {
            var model = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            var declarations = tree.GetRoot()
                .DescendantNodes()
                .OfType<TypeDeclarationSyntax>()
                .Where(d => d is not InterfaceDeclarationSyntax);

            foreach (var syntax in declarations)
            {
                if (model.GetDeclaredSymbol(syntax) is not INamedTypeSymbol symbol)
                    continue;

                var fullName = RoslynArchitectureScanner.FullName(symbol);
                if (!_classes.TryGetValue(fullName, out var entry))
                {
                    entry = new RoslynEntityDecl
                    {
                        Symbol = symbol,
                        Syntax = syntax,
                        Model = model,
                        File = file,
                        FullName = fullName,
                    };
                    _classes[fullName] = entry;
                }

                ExtractProperties(entry, syntax, model, file, result.RootPath);
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
        var contextFullName = ComposeFullName(context.Namespace, context.ContainingTypes, context.Name);
        if (!_classes.TryGetValue(contextFullName, out var contextDecl))
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

            if (!TryResolveEntityDecl(
                    dbSet.Entity,
                    dbSetTargetSymbol,
                    dbSetTargetSyntax,
                    contextDecl,
                    recordAmbiguity: true,
                    out var entityDecl))
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

    private EntityInfo CreateEntity(
        RoslynEntityDecl declaration,
        IReadOnlySet<string> entityNames)
    {
        var entity = new EntityInfo
        {
            Name = declaration.Symbol.Name,
            Namespace = RoslynArchitectureScanner.NamespaceOf(declaration.Symbol),
            ProjectName = declaration.File.ProjectName,
            Location = RoslynArchitectureScanner.LocationOf(declaration.Symbol, _result.RootPath),
        };
        entity.ContainingTypes.AddRange(RoslynArchitectureScanner.ContainingTypeNames(declaration.Symbol));

        var keys = GetKeyNames(declaration).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (property, declaring) in MappedProperties(declaration))
        {
            if (!property.IsPublic || property.IsStatic) continue;
            if (EfAnnotations.HasAttribute(property.Attributes, "NotMapped") ||
                EfAnnotations.HasAttribute(property.Attributes, "Ignore"))
                continue;

            var isCollection = TryGetCollectionElement(property, out var elementWrittenType, out var elementSymbol);
            RoslynEntityDecl? target;
            if (isCollection)
            {
                TryResolveEntityDecl(
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
                TryResolveEntityDecl(
                    property.Type,
                    property.ResolvedType,
                    property.TypeSyntax,
                    declaring,
                    recordAmbiguity: false,
                    out target);
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
                Location = property.Location,
            };
            info.IsKey = keys.Contains(property.Name);
            info.Attributes.AddRange(property.Attributes);
            if (!isNavigation && EfAnnotations.HasAttribute(property.Attributes, "ForeignKey"))
                info.IsForeignKey = true;
            entity.Properties.Add(info);
        }

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

    private IReadOnlyList<string> GetKeyNames(RoslynEntityDecl declaration)
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
            !TryGetCollectionElement(p, out _, out _));
        if (byId is not null)
        {
            var result = new List<string> { byId.Name };
            _keyCache[declaration.FullName] = result;
            return result;
        }

        var conventionalName = declaration.Symbol.Name + "Id";
        var byName = properties.FirstOrDefault(p =>
            string.Equals(p.Name, conventionalName, StringComparison.OrdinalIgnoreCase) &&
            !TryGetCollectionElement(p, out _, out _));
        var names = byName is null ? new List<string>() : new List<string> { byName.Name };
        _keyCache[declaration.FullName] = names;
        return names;
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
            current = FirstBaseClass(current);
        }

        var properties = new List<(RoslynPropertyDecl, RoslynEntityDecl)>();
        foreach (var type in chain)
        {
            foreach (var property in type.Properties)
                properties.Add((property, type));
        }

        return properties;
    }

    private RoslynEntityDecl? FirstBaseClass(RoslynEntityDecl declaration)
    {
        if (declaration.Symbol.BaseType is { TypeKind: not TypeKind.Error } baseSymbol)
        {
            var baseFullName = RoslynArchitectureScanner.FullName(baseSymbol.OriginalDefinition);
            if (_classes.TryGetValue(baseFullName, out var direct))
                return direct;
        }

        if (_textClasses.TryGetValue(declaration.FullName, out var textDecl))
        {
            foreach (var part in CSharpScanner.SplitTopLevel(textDecl.BaseList))
            {
                var (name, args) = CSharpScanner.ParseTypeReference(part);
                if (name.Length == 0) continue;
                var resolved = _hierarchy.TryResolveClass(
                    name,
                    textDecl.Namespace,
                    textDecl.Usings,
                    textDecl.ContainingTypes,
                    args.Count,
                    out _);
                if (resolved is not null && _classes.TryGetValue(resolved.FullName, out var entry))
                    return entry;
            }
        }

        return null;
    }

    private RoslynEntityDecl? ResolveNavigationTarget(
        RoslynEntityDecl declaring,
        RoslynPropertyDecl property)
    {
        if (TryGetCollectionElement(property, out var elementWrittenType, out var elementSymbol))
        {
            return TryResolveEntityDecl(
                elementWrittenType, elementSymbol, null, declaring, recordAmbiguity: false, out var elementTarget)
                ? elementTarget
                : null;
        }

        return TryResolveEntityDecl(
            property.Type, property.ResolvedType, property.TypeSyntax, declaring, recordAmbiguity: false, out var target)
            ? target
            : null;
    }

    private static bool TryGetCollectionElement(
        RoslynPropertyDecl property,
        out string elementWrittenType,
        out ITypeSymbol? elementSymbol)
    {
        elementSymbol = null;
        if (EfAnnotations.TryGetCollectionElementType(property.Type, out elementWrittenType))
        {
            var unwrapped = UnwrapNullable(property.ResolvedType);
            if (unwrapped is IArrayTypeSymbol arrayType)
                elementSymbol = arrayType.ElementType;
            else if (unwrapped is INamedTypeSymbol { TypeArguments.Length: 1 } named)
                elementSymbol = named.TypeArguments[0];
            return true;
        }

        return false;
    }

    private bool TryResolveEntityDecl(
        string writtenType,
        ITypeSymbol? resolvedSymbol,
        TypeSyntax? typeSyntax,
        RoslynEntityDecl context,
        bool recordAmbiguity,
        out RoslynEntityDecl resolved)
    {
        resolved = null!;
        var unwrapped = UnwrapNullable(resolvedSymbol);
        if (unwrapped is INamedTypeSymbol { TypeKind: not TypeKind.Error } named)
        {
            var fullName = RoslynArchitectureScanner.FullName(named.OriginalDefinition);
            if (_classes.TryGetValue(fullName, out var direct))
            {
                resolved = direct;
                return true;
            }
        }

        var ambiguousCandidates = new List<string>();
        if (unwrapped is IErrorTypeSymbol { CandidateSymbols.Length: > 1 } errorType)
        {
            ambiguousCandidates.AddRange(errorType.CandidateSymbols
                .OfType<INamedTypeSymbol>()
                .Select(s => RoslynArchitectureScanner.FullName(s.OriginalDefinition))
                .Where(_classes.ContainsKey)
                .Distinct(StringComparer.Ordinal));
        }

        if (ambiguousCandidates.Count == 0 && typeSyntax is not null)
        {
            var info = context.Model.GetSymbolInfo(typeSyntax);
            if (info.CandidateSymbols.Length > 1)
            {
                ambiguousCandidates.AddRange(info.CandidateSymbols
                    .OfType<INamedTypeSymbol>()
                    .Select(s => RoslynArchitectureScanner.FullName(s.OriginalDefinition))
                    .Where(_classes.ContainsKey)
                    .Distinct(StringComparer.Ordinal));
            }
        }

        var normalized = EfAnnotations.NormalizeType(writtenType);
        if (normalized.EndsWith('?')) normalized = normalized[..^1];
        var (name, args) = CSharpScanner.ParseTypeReference(normalized);
        if (name.Length == 0) return false;

        if (_textClasses.TryGetValue(context.FullName, out var textContext))
        {
            var candidate = _hierarchy.TryResolveClass(
                name,
                textContext.Namespace,
                textContext.Usings,
                textContext.ContainingTypes,
                args.Count,
                out var hierarchyAmbiguous);
            if (candidate is not null && _classes.TryGetValue(candidate.FullName, out var fallback))
            {
                resolved = fallback;
                return true;
            }

            if (ambiguousCandidates.Count == 0 && hierarchyAmbiguous.Count > 0)
                ambiguousCandidates.AddRange(hierarchyAmbiguous);
        }

        if (recordAmbiguity && ambiguousCandidates.Count > 1)
        {
            _result.RecordDiagnostic(new AnalysisDiagnostic(
                AnalysisResult.AmbiguousLinkCode,
                AnalysisDiagnosticSeverity.Warning,
                $"EF Core type `{name}` matches more than one source class: " +
                string.Join(", ", ambiguousCandidates.OrderBy(a => a, StringComparer.Ordinal)) + ".",
                RoslynArchitectureScanner.LocationOf(context.Symbol, _result.RootPath),
                context.File.ProjectName));
        }

        return false;
    }

    private static ITypeSymbol? UnwrapNullable(ITypeSymbol? symbol)
    {
        if (symbol is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T,
                TypeArguments.Length: 1
            } nullable)
        {
            return nullable.TypeArguments[0];
        }

        return symbol;
    }

    private static void ExtractProperties(
        RoslynEntityDecl entry,
        TypeDeclarationSyntax syntax,
        SemanticModel model,
        SourceFile file,
        string rootPath)
    {
        foreach (var propSyntax in syntax.Members.OfType<PropertyDeclarationSyntax>())
        {
            if (propSyntax.AccessorList is null)
                continue;

            var hasSetter = propSyntax.AccessorList.Accessors.Any(a =>
                a.Keyword.IsKind(SyntaxKind.SetKeyword) ||
                a.Keyword.IsKind(SyntaxKind.InitKeyword));
            if (!hasSetter)
                continue;

            if (propSyntax.Modifiers.Any(m =>
                    m.IsKind(SyntaxKind.StaticKeyword) ||
                    m.IsKind(SyntaxKind.ConstKeyword)))
                continue;

            var isPublic = propSyntax.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword));
            var propSymbol = model.GetDeclaredSymbol(propSyntax);
            var rawType = EfAnnotations.NormalizeType(propSyntax.Type.ToString());
            if (model.GetAliasInfo(propSyntax.Type) is not null && propSymbol is not null)
                rawType = EfAnnotations.NormalizeType(RoslynArchitectureScanner.DisplayType(propSymbol.Type));

            var propDecl = new RoslynPropertyDecl
            {
                Name = propSyntax.Identifier.ValueText,
                Type = rawType,
                IsNullable = EfAnnotations.IsNullable(rawType),
                IsPublic = isPublic,
                IsStatic = false,
                ResolvedType = propSymbol?.Type,
                TypeSyntax = propSyntax.Type,
                Location = RoslynArchitectureScanner.LocationOf(propSyntax.GetLocation(), rootPath),
            };
            foreach (var attribute in propSyntax.AttributeLists.SelectMany(al => al.Attributes))
                propDecl.Attributes.Add("[" + attribute + "]");

            entry.Properties.Add(propDecl);
        }

        if (syntax.ParameterList is null)
            return;

        foreach (var param in syntax.ParameterList.Parameters)
        {
            if (param.Type is null) continue;
            var name = param.Identifier.ValueText;
            if (name.Length == 0 || entry.Properties.Any(p => p.Name == name))
                continue;

            var rawType = EfAnnotations.NormalizeType(param.Type.ToString());
            var resolvedType = model.GetTypeInfo(param.Type).Type;
            if (model.GetAliasInfo(param.Type) is not null && resolvedType is not null)
                rawType = EfAnnotations.NormalizeType(RoslynArchitectureScanner.DisplayType(resolvedType));

            var propDecl = new RoslynPropertyDecl
            {
                Name = name,
                Type = rawType,
                IsNullable = EfAnnotations.IsNullable(rawType),
                IsPublic = true,
                IsStatic = false,
                ResolvedType = resolvedType,
                TypeSyntax = param.Type,
                Location = RoslynArchitectureScanner.LocationOf(syntax.GetLocation(), rootPath),
            };
            foreach (var attribute in param.AttributeLists.SelectMany(al => al.Attributes))
                propDecl.Attributes.Add("[" + attribute + "]");

            entry.Properties.Add(propDecl);
        }
    }

    private static EntityInfo? CreateStub(string writtenType, DbContextInfo context)
    {
        var (name, args) = CSharpScanner.ParseTypeReference(EfAnnotations.NormalizeType(writtenType));
        if (args.Count > 0) return null;
        var simple = CSharpScanner.SimpleName(name.TrimEnd('?'));
        if (simple.Length == 0) return null;
        return new EntityInfo
        {
            Name = simple,
            Namespace = CSharpScanner.NamespaceOfType(name),
            ProjectName = context.ProjectName,
            Location = context.Location,
        };
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
