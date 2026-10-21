using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CqrsLens;

// First Roslyn slice for #1. It only emits CQRS request/handler/validator
// shapes for now and is intentionally not wired as the default scanner yet.
internal sealed class RoslynArchitectureScanner : IArchitectureScanner
{
    private static readonly Lazy<IReadOnlyList<MetadataReference>> RuntimeReferences =
        new(CreateRuntimeReferences);

    public void Scan(
        IReadOnlyList<SourceFile> files,
        IReadOnlySet<string> markers,
        AnalysisResult result)
    {
        foreach (var projectFiles in files
                     .GroupBy(f => f.ProjectName ?? "", StringComparer.OrdinalIgnoreCase))
        {
            ScanProject(projectFiles.ToList(), markers, result);
        }
    }

    private static void ScanProject(
        IReadOnlyList<SourceFile> files,
        IReadOnlySet<string> markers,
        AnalysisResult result)
    {
        var parsed = files
            .Select(file => (
                File: file,
                Tree: CSharpSyntaxTree.ParseText(
                    file.Code,
                    new CSharpParseOptions(LanguageVersion.Latest),
                    file.Path)))
            .ToList();

        var compilation = CSharpCompilation.Create(
            "CqrsLens.RoslynScan",
            parsed.Select(p => p.Tree),
            RuntimeReferences.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        foreach (var (file, tree) in parsed)
        {
            var model = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            var declarations = tree.GetRoot()
                .DescendantNodes()
                .OfType<TypeDeclarationSyntax>()
                .Where(d => d is not InterfaceDeclarationSyntax);

            foreach (var declaration in declarations)
            {
                if (model.GetDeclaredSymbol(declaration) is not INamedTypeSymbol symbol)
                    continue;

                AddRequest(symbol, file.ProjectName, markers, result);
                AddHandlers(symbol, file.ProjectName, result);
                AddValidators(symbol, file.ProjectName, result);
                AddBehavior(symbol, file.ProjectName, result);
                AddDbContext(symbol, file.ProjectName, result);
            }
        }
    }

    private static void AddRequest(
        INamedTypeSymbol symbol,
        string? projectName,
        IReadOnlySet<string> markers,
        AnalysisResult result)
    {
        var interfaces = ImplementedInterfaces(symbol).ToList();
        var requestInterface = interfaces.FirstOrDefault(i => i.Name == "IRequest");
        var markerInterface = interfaces.FirstOrDefault(i => markers.Contains(i.Name));
        if (requestInterface is null && markerInterface is null)
            return;

        var ns = NamespaceOf(symbol);
        if (result.Requests.Any(r => r.Name == symbol.Name && r.Namespace == ns))
            return;

        var viaMarker = markerInterface?.Name;
        if (viaMarker is null && requestInterface is not null)
        {
            viaMarker = symbol.Interfaces
                .FirstOrDefault(i =>
                    i.Name != "IRequest" &&
                    ImplementedInterfaces(i).Any(x => x.Name == "IRequest"))
                ?.Name;
        }

        var response = requestInterface?.TypeArguments.FirstOrDefault();
        if (response is null && markerInterface is not null)
        {
            response = symbol.Interfaces
                .FirstOrDefault(i =>
                    i.TypeArguments.Length > 0 &&
                    (markers.Contains(i.Name) ||
                     ImplementedInterfaces(i).Any(x => markers.Contains(x.Name))))
                ?.TypeArguments[0];
        }

        var request = new CqrsRequest
        {
            Name = symbol.Name,
            Namespace = ns,
            ResponseType = response is null ? null : DisplayType(response),
            ProjectName = projectName,
            ViaMarker = viaMarker,
        };

        if (!request.Name.EndsWith("Command", StringComparison.Ordinal) &&
            !request.Name.EndsWith("Query", StringComparison.Ordinal))
        {
            request.Kind = MarkerResolver.ClassifyMarkerRequest(
                request.Name,
                symbol.Interfaces.Select(i => i.Name)
                    .Concat(markerInterface is null ? [] : new[] { markerInterface.Name }));
        }

        result.Requests.Add(request);
    }

    private static void AddHandlers(
        INamedTypeSymbol symbol,
        string? projectName,
        AnalysisResult result)
    {
        foreach (var handlerInterface in ImplementedInterfaces(symbol)
                     .Where(i => i.Name == "IRequestHandler" && i.TypeArguments.Length >= 1))
        {
            var target = handlerInterface.TypeArguments[0];
            var targetName = TypeName(target);
            var targetNamespace = NamespaceOf(target);

            if (result.Handlers.Any(h =>
                    h.FullName == FullName(symbol) &&
                    h.RequestName == targetName &&
                    h.RequestNamespace == targetNamespace))
                continue;

            result.Handlers.Add(new HandlerInfo
            {
                Name = symbol.Name,
                Namespace = NamespaceOf(symbol),
                RequestName = targetName,
                RequestNamespace = string.IsNullOrEmpty(targetNamespace) ? null : targetNamespace,
                ProjectName = projectName,
            });
        }
    }

    private static void AddValidators(
        INamedTypeSymbol symbol,
        string? projectName,
        AnalysisResult result)
    {
        var targets = new List<ITypeSymbol>();

        var validatorBase = FindBaseType(symbol, "AbstractValidator");
        if (validatorBase is { TypeArguments.Length: >= 1 })
            targets.Add(validatorBase.TypeArguments[0]);

        targets.AddRange(ImplementedInterfaces(symbol)
            .Where(i => i.Name == "IValidator" && i.TypeArguments.Length >= 1)
            .Select(i => i.TypeArguments[0]));

        foreach (var target in targets
                     .GroupBy(t => t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                     .Select(g => g.First()))
        {
            var targetName = TypeName(target);
            var targetNamespace = NamespaceOf(target);

            if (result.Validators.Any(v =>
                    v.FullName == FullName(symbol) &&
                    v.TargetType == targetName &&
                    v.TargetNamespace == targetNamespace))
                continue;

            result.Validators.Add(new ValidatorInfo
            {
                Name = symbol.Name,
                Namespace = NamespaceOf(symbol),
                TargetType = targetName,
                TargetNamespace = string.IsNullOrEmpty(targetNamespace) ? null : targetNamespace,
                ProjectName = projectName,
            });
        }
    }


    private static void AddBehavior(
        INamedTypeSymbol symbol,
        string? projectName,
        AnalysisResult result)
    {
        if (!ImplementedInterfaces(symbol).Any(i =>
                i.Name == "IPipelineBehavior" &&
                i.TypeArguments.Length == 2))
            return;

        if (result.Behaviors.Any(b => b.Name == symbol.Name && b.ProjectName == projectName))
            return;

        result.Behaviors.Add(new BehaviorInfo
        {
            Name = symbol.Name,
            ProjectName = projectName,
        });
    }

    private static void AddDbContext(
        INamedTypeSymbol symbol,
        string? projectName,
        AnalysisResult result)
    {
        if (FindBaseType(symbol, "DbContext") is null)
            return;

        var ns = NamespaceOf(symbol);
        if (result.DbContexts.Any(c => c.Name == symbol.Name && c.Namespace == ns))
            return;

        var context = new DbContextInfo
        {
            Name = symbol.Name,
            Namespace = ns,
            ProjectName = projectName,
        };

        foreach (var property in symbol.GetMembers().OfType<IPropertySymbol>())
        {
            if (property.Type is not INamedTypeSymbol propertyType ||
                propertyType.Name != "DbSet" ||
                propertyType.TypeArguments.Length != 1)
                continue;

            context.DbSets.Add(new DbSetInfo(
                DisplayType(propertyType.TypeArguments[0]),
                property.Name));
        }

        result.DbContexts.Add(context);
    }

    private static IEnumerable<INamedTypeSymbol> ImplementedInterfaces(INamedTypeSymbol symbol)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in symbol.Interfaces.Concat(symbol.AllInterfaces))
        {
            var key = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (seen.Add(key))
                yield return type;
        }
    }

    private static INamedTypeSymbol? FindBaseType(INamedTypeSymbol symbol, string name)
    {
        for (var current = symbol.BaseType; current is not null; current = current.BaseType)
            if (current.Name == name)
                return current;
        return null;
    }

    private static string TypeName(ITypeSymbol symbol)
        => symbol is INamedTypeSymbol named
            ? named.Name
            : DisplayType(symbol);

    private static string NamespaceOf(ITypeSymbol symbol)
        => symbol is INamedTypeSymbol named ? NamespaceOf(named) : "";

    private static string NamespaceOf(INamedTypeSymbol symbol)
        => symbol.ContainingNamespace is { IsGlobalNamespace: false } ns
            ? ns.ToDisplayString()
            : "";

    private static string FullName(INamedTypeSymbol symbol)
    {
        var ns = NamespaceOf(symbol);
        return string.IsNullOrEmpty(ns) ? symbol.Name : $"{ns}.{symbol.Name}";
    }

    private static string DisplayType(ITypeSymbol symbol)
        => symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

    private static IReadOnlyList<MetadataReference> CreateRuntimeReferences()
    {
        var paths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            ?.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            ?? [];

        return paths
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToArray();
    }
}
