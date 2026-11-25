using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CqrsLens;

/// <summary>
/// Reads CQRS request handler shapes, including handlers discovered through marker interfaces.
/// </summary>
internal static class RoslynHandlerShapeReader
{
    internal static void AddHandlers(
        INamedTypeSymbol symbol,
        TypeDeclarationSyntax declaration,
        SemanticModel model,
        IReadOnlyList<UsingDirective>? usings,
        string? projectName,
        IReadOnlySet<string> handlerMarkers,
        TypeHierarchy hierarchy,
        AnalysisResult result)
    {
        if (symbol.IsAbstract) return;

        var candidateInterfaces = RoslynSymbolFacts.ImplementedInterfaces(symbol, declaration, model)
            .Where(i => (i.Name is "IRequestHandler" or "IAsyncRequestHandler" || handlerMarkers.Contains(i.Name)) &&
                        i.TypeArguments.Length >= 1)
            .ToList();

        candidateInterfaces.AddRange(
            RoslynSymbolFacts.BaseTypes(symbol)
                .Where(b => b.Name is "RequestHandler" or "AsyncRequestHandler" &&
                            b.TypeArguments.Length >= 1));

        foreach (var errorBase in RoslynSymbolFacts.ErrorBaseTypes(declaration, model))
        {
            if (handlerMarkers.Contains(errorBase.Name) &&
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
            var targetFullNameHint = target is INamedTypeSymbol
                {
                    TypeKind: not TypeKind.Error,
                    ContainingType: not null,
                } namedTarget
                ? RoslynSymbolFacts.FullName(namedTarget)
                : null;
            var fullName = RoslynSymbolFacts.FullName(symbol);

            if (result.Handlers.Any(h =>
                    h.FullName == fullName &&
                    h.ProjectName == projectName &&
                    h.RequestName == targetName &&
                    h.RequestNamespace == normalizedNamespace &&
                    h.RequestFullNameHint == targetFullNameHint))
                continue;

            var handler = new HandlerInfo
            {
                Name = symbol.Name,
                Namespace = RoslynSymbolFacts.NamespaceOf(symbol),
                RequestName = targetName,
                RequestNamespace = normalizedNamespace,
                RequestFullNameHint = targetFullNameHint,
                ProjectName = projectName,
                Location = RoslynSymbolFacts.LocationOf(declaration.Identifier.GetLocation(), result.RootPath),
            };
            handler.ContainingTypes.AddRange(RoslynSymbolFacts.ContainingTypeNames(symbol));
            if (usings is not null)
                CSharpScanner.AddImportedNamespaces(usings, handler.ImportedNamespaces);
            result.Handlers.Add(handler);
        }
    }
}
