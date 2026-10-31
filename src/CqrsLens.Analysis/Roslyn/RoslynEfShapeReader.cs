using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CqrsLens;

/// <summary>
/// Reads EF Core DbContext shapes from Roslyn symbols, including their DbSet properties.
/// </summary>
internal static class RoslynEfShapeReader
{
    internal static void AddDbContext(
        INamedTypeSymbol symbol,
        TypeDeclarationSyntax declaration,
        string? projectName,
        AnalysisResult result)
    {
        if (symbol.IsAbstract || !IsDbContext(symbol))
            return;

        var ns = RoslynSymbolFacts.NamespaceOf(symbol);
        var containingTypes = RoslynSymbolFacts.ContainingTypeNames(symbol);
        if (result.DbContexts.Any(c =>
                c.Name == symbol.Name &&
                c.Namespace == ns &&
                c.ContainingTypes.SequenceEqual(containingTypes)))
            return;

        var context = new DbContextInfo
        {
            Name = symbol.Name,
            Namespace = ns,
            ProjectName = projectName,
            Location = RoslynSymbolFacts.LocationOf(declaration.Identifier.GetLocation(), result.RootPath),
        };
        context.ContainingTypes.AddRange(containingTypes);

        foreach (var property in symbol.GetMembers().OfType<IPropertySymbol>())
        {
            if (property.Type is not INamedTypeSymbol propertyType ||
                propertyType.Name != "DbSet" ||
                propertyType.TypeArguments.Length != 1)
                continue;

            context.DbSets.Add(new DbSetInfo(
                RoslynSymbolFacts.DisplayType(propertyType.TypeArguments[0]),
                property.Name));
        }

        result.DbContexts.Add(context);
    }

    private static bool IsDbContext(INamedTypeSymbol symbol)
    {
        for (var current = symbol.BaseType; current is not null; current = current.BaseType)
        {
            if (current.Name == "DbContext") return true;
            if (current.Name.EndsWith("DbContext", StringComparison.Ordinal) &&
                !current.Name.StartsWith('I'))
                return true;
        }
        return false;
    }
}
