using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CqrsLens;

/// <summary>
/// Reads CQRS notification and domain event shapes from one semantic model.
/// </summary>
internal static class RoslynNotificationShapeReader
{
    internal static void AddNotification(
        INamedTypeSymbol symbol,
        TypeDeclarationSyntax declaration,
        SemanticModel model,
        CSharpScanner.ClassDecl? classDecl,
        string? projectName,
        TypeHierarchy hierarchy,
        AnalysisResult result)
    {
        if (symbol.IsAbstract) return;

        var interfaces = RoslynSymbolFacts.ImplementedInterfaces(symbol, declaration, model).ToList();
        var notificationInterface = interfaces.FirstOrDefault(i =>
            i.Name == "INotification" && i.TypeArguments.Length == 0);
        var domainEventInterface = interfaces.FirstOrDefault(i =>
            i.Name == "IDomainEvent" && i.TypeArguments.Length == 0);

        INamedTypeSymbol? externalNotification = null;
        if (notificationInterface is null && domainEventInterface is null)
        {
            foreach (var errorBase in RoslynSymbolFacts.ErrorBaseTypes(declaration, model))
            {
                if (errorBase.Name is not ("INotification" or "IDomainEvent"))
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

                externalNotification = errorBase;
                break;
            }
        }

        if (notificationInterface is null && domainEventInterface is null && externalNotification is null)
            return;

        var ns = RoslynSymbolFacts.NamespaceOf(symbol);
        var containing = RoslynSymbolFacts.ContainingTypeNames(symbol);
        if (result.Notifications.Any(n =>
                n.Name == symbol.Name &&
                n.ProjectName == projectName &&
                n.Namespace == ns &&
                n.ContainingTypes.SequenceEqual(containing)))
            return;

        string? viaMarker = null;
        if (domainEventInterface is not null)
        {
            viaMarker = interfaces
                .FirstOrDefault(i =>
                    i.Name != "INotification" &&
                    (i.Name == "IDomainEvent" ||
                     RoslynSymbolFacts.ImplementedInterfaces(i).Any(x => x.Name is "INotification" or "IDomainEvent")))
                ?.Name ?? "IDomainEvent";
        }
        else if (externalNotification is not null && externalNotification.Name != "INotification")
        {
            viaMarker = externalNotification.Name;
        }
        else if (notificationInterface is not null)
        {
            viaMarker = interfaces
                .FirstOrDefault(i =>
                    i.Name != "INotification" &&
                    RoslynSymbolFacts.ImplementedInterfaces(i).Any(x => x.Name == "INotification"))
                ?.Name
                ?? RoslynSymbolFacts.BaseTypes(symbol)
                    .FirstOrDefault(b =>
                        b.Name != "Object" &&
                        RoslynSymbolFacts.ImplementedInterfaces(b).Any(x => x.Name == "INotification"))
                    ?.Name;
        }

        var notification = new CqrsNotification
        {
            Name = symbol.Name,
            Namespace = ns,
            ProjectName = projectName,
            ViaMarker = viaMarker,
            Location = RoslynSymbolFacts.LocationOf(declaration.Identifier.GetLocation(), result.RootPath),
        };
        notification.ContainingTypes.AddRange(containing);
        result.Notifications.Add(notification);
    }
}
