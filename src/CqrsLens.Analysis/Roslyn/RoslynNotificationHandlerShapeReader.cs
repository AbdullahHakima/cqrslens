using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CqrsLens;

/// <summary>
/// Reads notification handler shapes, including domain event handler variants.
/// </summary>
internal static class RoslynNotificationHandlerShapeReader
{
    internal static void AddNotificationHandlers(
        INamedTypeSymbol symbol,
        TypeDeclarationSyntax declaration,
        SemanticModel model,
        IReadOnlyList<UsingDirective>? usings,
        string? projectName,
        TypeHierarchy hierarchy,
        AnalysisResult result)
    {
        if (symbol.IsAbstract) return;

        var candidateInterfaces = RoslynSymbolFacts.ImplementedInterfaces(symbol, declaration, model)
            .Where(i => i.Name is "INotificationHandler" or "IDomainEventHandler" &&
                        i.TypeArguments.Length >= 1)
            .ToList();

        foreach (var errorBase in RoslynSymbolFacts.ErrorBaseTypes(declaration, model))
        {
            if (errorBase.Name is "INotificationHandler" or "IDomainEventHandler" &&
                errorBase.TypeArguments.Length >= 1 &&
                !hierarchy.HasInterface(errorBase.Name))
            {
                candidateInterfaces.Add(errorBase);
            }
        }

        foreach (var handlerInterface in candidateInterfaces)
        {
            var target = handlerInterface.TypeArguments[0];
            if (target.TypeKind == TypeKind.TypeParameter)
                continue;

            var targetName = RoslynSymbolFacts.TypeName(target);
            var targetNamespace = RoslynSymbolFacts.NamespaceOf(target);
            var normalizedNamespace = string.IsNullOrEmpty(targetNamespace) ? null : targetNamespace;
            var fullName = RoslynSymbolFacts.FullName(symbol);

            if (result.NotificationHandlers.Any(h =>
                    h.FullName == fullName &&
                    h.ProjectName == projectName &&
                    h.NotificationName == targetName &&
                    h.NotificationNamespace == normalizedNamespace))
                continue;

            var handler = new NotificationHandlerInfo
            {
                Name = symbol.Name,
                Namespace = RoslynSymbolFacts.NamespaceOf(symbol),
                NotificationName = targetName,
                NotificationNamespace = normalizedNamespace,
                ProjectName = projectName,
                Location = RoslynSymbolFacts.LocationOf(declaration.Identifier.GetLocation(), result.RootPath),
            };
            handler.ContainingTypes.AddRange(RoslynSymbolFacts.ContainingTypeNames(symbol));
            if (usings is not null)
                CSharpScanner.AddImportedNamespaces(usings, handler.ImportedNamespaces);
            result.NotificationHandlers.Add(handler);
        }
    }
}
