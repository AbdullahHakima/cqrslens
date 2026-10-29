using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CqrsLens;

/// <summary>
/// Reads MediatR pipeline behavior shapes from one semantic model.
/// </summary>
internal static class RoslynBehaviorShapeReader
{
    internal static void AddBehavior(
        INamedTypeSymbol symbol,
        TypeDeclarationSyntax declaration,
        SemanticModel model,
        string? projectName,
        AnalysisResult result)
    {
        if (!RoslynSymbolFacts.ImplementedInterfaces(symbol, declaration, model).Any(i =>
                i.Name == "IPipelineBehavior" &&
                i.TypeArguments.Length == 2))
            return;

        var location = RoslynSymbolFacts.LocationOf(declaration.Identifier.GetLocation(), result.RootPath);
        if (result.Behaviors.Any(b =>
                b.Name == symbol.Name &&
                b.ProjectName == projectName &&
                b.Location?.File == location?.File))
            return;

        result.Behaviors.Add(new BehaviorInfo
        {
            Name = symbol.Name,
            ProjectName = projectName,
            Location = location,
        });
    }
}
