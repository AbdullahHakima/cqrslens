using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CqrsLens;

/// <summary>
/// Read-only queries over Roslyn symbols: interface/base traversal, names, source locations and
/// display labels. Keeping these in one place prevents each shape reader from reimplementing
/// partial-interface or namespace rules differently.
/// </summary>
internal static class RoslynSymbolFacts
{
    internal static IEnumerable<INamedTypeSymbol> ImplementedInterfaces(
        INamedTypeSymbol symbol,
        TypeDeclarationSyntax? declaration = null,
        SemanticModel? model = null)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        IEnumerable<INamedTypeSymbol> candidates;

        if (declaration is not null &&
            model is not null &&
            symbol.DeclaringSyntaxReferences.Length > 1 &&
            !declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            candidates = DeclarationInterfaces(declaration, model);
        }
        else
        {
            candidates = symbol.Interfaces.Concat(symbol.AllInterfaces);
        }

        foreach (var type in candidates)
        {
            var key = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (seen.Add(key))
                yield return type;
        }
    }

    private static IEnumerable<INamedTypeSymbol> DeclarationInterfaces(
        TypeDeclarationSyntax declaration,
        SemanticModel model)
    {
        if (declaration.BaseList is null)
            yield break;

        foreach (var baseSyntax in declaration.BaseList.Types)
        {
            if (model.GetTypeInfo(baseSyntax.Type).Type is not INamedTypeSymbol baseSymbol)
                continue;

            if (baseSymbol.TypeKind is TypeKind.Interface or TypeKind.Error)
                yield return baseSymbol;

            foreach (var transitive in baseSymbol.Interfaces.Concat(baseSymbol.AllInterfaces))
                yield return transitive;
        }
    }

    internal static IEnumerable<INamedTypeSymbol> BaseTypes(INamedTypeSymbol symbol)
    {
        for (var current = symbol.BaseType; current is not null; current = current.BaseType)
            yield return current;
    }

    internal static IEnumerable<INamedTypeSymbol> ErrorBaseTypes(
        TypeDeclarationSyntax declaration,
        SemanticModel model)
    {
        if (declaration.BaseList is null)
            yield break;

        foreach (var baseType in declaration.BaseList.Types)
        {
            if (model.GetTypeInfo(baseType.Type).Type is INamedTypeSymbol { TypeKind: TypeKind.Error } errorType)
                yield return errorType;
        }
    }

    internal static bool HasErrorBaseType(
        INamedTypeSymbol symbol,
        TypeDeclarationSyntax declaration,
        SemanticModel model)
    {
        if (ErrorBaseTypes(declaration, model).Any())
            return true;

        for (var current = symbol.BaseType; current is not null; current = current.BaseType)
        {
            if (current.TypeKind == TypeKind.Error)
                return true;
        }

        return false;
    }

    internal static INamedTypeSymbol? FindBaseType(INamedTypeSymbol symbol, string name)
    {
        for (var current = symbol.BaseType; current is not null; current = current.BaseType)
            if (current.Name == name)
                return current;
        return null;
    }

    internal static string TypeName(ITypeSymbol symbol)
        => symbol is INamedTypeSymbol named
            ? named.Name
            : DisplayType(symbol);

    internal static string NamespaceOf(ITypeSymbol symbol)
        => symbol is INamedTypeSymbol named ? NamespaceOf(named) : "";

    internal static string NamespaceOf(INamedTypeSymbol symbol)
        => symbol.ContainingNamespace is { IsGlobalNamespace: false } ns
            ? ns.ToDisplayString()
            : "";

    internal static SourceLocation? LocationOf(ISymbol symbol, string rootPath)
    {
        var location = symbol.Locations.FirstOrDefault(l => l.IsInSource);
        return location is null ? null : LocationOf(location, rootPath);
    }

    internal static SourceLocation? LocationOf(Location location, string rootPath)
    {
        var lineSpan = location.GetMappedLineSpan();
        return SourceLocation.FromLine(
            location.SourceTree?.FilePath,
            rootPath,
            lineSpan.StartLinePosition.Line + 1,
            lineSpan.StartLinePosition.Character + 1);
    }

    internal static string ProjectLabel(string project)
        => string.IsNullOrEmpty(project) ? "(no project)" : project;

    internal static string FullName(INamedTypeSymbol symbol)
    {
        var typeNames = ContainingTypeNames(symbol).Concat(new[] { symbol.Name });
        var path = string.Join(".", typeNames);
        var ns = NamespaceOf(symbol);
        return string.IsNullOrEmpty(ns) ? path : $"{ns}.{path}";
    }

    internal static List<string> ContainingTypeNames(INamedTypeSymbol symbol)
    {
        var names = new List<string>();
        for (var current = symbol.ContainingType; current is not null; current = current.ContainingType)
            names.Insert(0, current.Name);
        return names;
    }

    internal static string DisplayType(ITypeSymbol symbol)
        => symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
}
