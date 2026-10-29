using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CqrsLens;

/// <summary>
/// Reads CQRS request shapes, including marker-driven requests, from one semantic model.
/// </summary>
internal static class RoslynRequestShapeReader
{
    internal static void AddRequest(
        INamedTypeSymbol symbol,
        TypeDeclarationSyntax declaration,
        SemanticModel model,
        CSharpScanner.ClassDecl? classDecl,
        string? projectName,
        IReadOnlySet<string> markers,
        TypeHierarchy hierarchy,
        AnalysisResult result)
    {
        if (symbol.IsAbstract) return;

        var interfaces = RoslynSymbolFacts.ImplementedInterfaces(symbol, declaration, model).ToList();
        var requestInterface = interfaces.FirstOrDefault(i =>
            i.Name == "IRequest" && i.TypeArguments.Length > 0);
        requestInterface ??= interfaces.FirstOrDefault(i => i.Name == "IRequest");
        var markerInterface = interfaces.FirstOrDefault(i => markers.Contains(i.Name));

        INamedTypeSymbol? externalMarker = null;
        if (requestInterface is null && markerInterface is null)
        {
            foreach (var errorBase in RoslynSymbolFacts.ErrorBaseTypes(declaration, model))
            {
                if (!markers.Contains(errorBase.Name))
                    continue;
                if (hierarchy.HasInterface(errorBase.Name))
                    continue;

                if (classDecl is not null)
                {
                    hierarchy.ResolveInterfaceReference(
                        errorBase.Name,
                        classDecl.Namespace,
                        classDecl.Usings,
                        classDecl.ContainingTypes,
                        errorBase.TypeArguments.Length,
                        out var ambiguous);
                    if (ambiguous.Count >= 2)
                        continue;
                }

                externalMarker = errorBase;
                break;
            }
        }

        if (requestInterface is null && markerInterface is null && externalMarker is null)
            return;

        var ns = RoslynSymbolFacts.NamespaceOf(symbol);
        if (result.Requests.Any(r =>
                r.Name == symbol.Name &&
                r.ProjectName == projectName &&
                r.Namespace == ns &&
                r.ContainingTypes.SequenceEqual(RoslynSymbolFacts.ContainingTypeNames(symbol))))
            return;

        var effectiveMarker = markerInterface ?? externalMarker;
        var viaMarker = effectiveMarker?.Name;
        if (viaMarker is null && requestInterface is not null)
        {
            viaMarker = interfaces
                .FirstOrDefault(i =>
                    i.Name != "IRequest" &&
                    RoslynSymbolFacts.ImplementedInterfaces(i).Any(x => x.Name == "IRequest"))
                ?.Name
                ?? RoslynSymbolFacts.BaseTypes(symbol)
                    .FirstOrDefault(b =>
                        b.Name != "Object" &&
                        RoslynSymbolFacts.ImplementedInterfaces(b).Any(x => x.Name == "IRequest"))
                    ?.Name;
        }

        var response = requestInterface?.TypeArguments.FirstOrDefault();
        if (response is null && effectiveMarker is { TypeArguments.Length: > 0 })
        {
            response = effectiveMarker.TypeArguments[0];
        }
        if (response is null)
        {
            response = interfaces
                .FirstOrDefault(i =>
                    i.TypeArguments.Length > 0 &&
                    (markers.Contains(i.Name) ||
                     RoslynSymbolFacts.ImplementedInterfaces(i).Any(x => markers.Contains(x.Name))))
                ?.TypeArguments[0];
        }

        var request = new CqrsRequest
        {
            Name = symbol.Name,
            Namespace = ns,
            ResponseType = response is null ? null : RoslynSymbolFacts.DisplayType(response),
            ProjectName = projectName,
            ViaMarker = viaMarker,
            Location = RoslynSymbolFacts.LocationOf(declaration.Identifier.GetLocation(), result.RootPath),
        };
        request.ContainingTypes.AddRange(RoslynSymbolFacts.ContainingTypeNames(symbol));

        if (viaMarker is not null &&
            !request.Name.EndsWith("Command", StringComparison.Ordinal) &&
            !request.Name.EndsWith("Query", StringComparison.Ordinal))
        {
            request.Kind = MarkerResolver.ClassifyMarkerRequest(
                request.Name,
                interfaces.Select(i => i.Name)
                    .Concat(RoslynSymbolFacts.BaseTypes(symbol).Select(b => b.Name))
                    .Concat(effectiveMarker is null ? [] : new[] { effectiveMarker.Name }));
        }

        result.Requests.Add(request);
    }
}
