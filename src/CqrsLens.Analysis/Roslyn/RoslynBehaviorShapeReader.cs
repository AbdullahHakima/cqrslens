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
        var behaviorInterface = RoslynSymbolFacts.ImplementedInterfaces(symbol, declaration, model)
            .FirstOrDefault(i => i.Name == "IPipelineBehavior" && i.TypeArguments.Length == 2);
        if (behaviorInterface is null)
            return;

        var location = RoslynSymbolFacts.LocationOf(declaration.Identifier.GetLocation(), result.RootPath);
        if (result.Behaviors.Any(b =>
                b.Name == symbol.Name &&
                b.ProjectName == projectName &&
                b.Location?.File == location?.File))
            return;

        var behavior = new BehaviorInfo
        {
            Name = symbol.Name,
            Namespace = RoslynSymbolFacts.NamespaceOf(symbol),
            GenericArity = symbol.TypeParameters.Length,
            ProjectName = projectName,
            Location = location,
        };
        behavior.ContainingTypes.AddRange(RoslynSymbolFacts.ContainingTypeNames(symbol));

        var firstArg = behaviorInterface.TypeArguments[0];
        if (firstArg.TypeKind == TypeKind.TypeParameter)
        {
            var typeParamName = firstArg.Name;
            foreach (var clause in declaration.ConstraintClauses)
            {
                if (!string.Equals(clause.Name.Identifier.Text, typeParamName, StringComparison.Ordinal))
                    continue;
                foreach (var constraint in clause.Constraints)
                {
                    var text = constraint.ToString().Trim();
                    if (text.Length > 0 && !behavior.RequestTypeConstraints.Contains(text, StringComparer.Ordinal))
                        behavior.RequestTypeConstraints.Add(text);
                }
            }
        }
        else
        {
            var targetName = RoslynSymbolFacts.TypeName(firstArg);
            var targetNs = RoslynSymbolFacts.NamespaceOf(firstArg);
            behavior.ClosedRequestTarget = targetName;
            behavior.ClosedRequestNamespace = string.IsNullOrEmpty(targetNs) ? null : targetNs;
        }

        result.Behaviors.Add(behavior);
    }
}
