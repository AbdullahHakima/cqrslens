using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CqrsLens;

/// <summary>
/// Reads CQRS request, handler, notification, notification-handler, validator and pipeline
/// behavior shapes from one semantic model. EF Core contexts are handled separately by
/// <see cref="RoslynEfShapeReader"/>.
/// </summary>
internal static class RoslynCqrsShapeReader
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
            .Where(i => (i.Name == "IRequestHandler" || handlerMarkers.Contains(i.Name)) &&
                        i.TypeArguments.Length >= 1)
            .ToList();

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
            var fullName = RoslynSymbolFacts.FullName(symbol);

            if (result.Handlers.Any(h =>
                    h.FullName == fullName &&
                    h.ProjectName == projectName &&
                    h.RequestName == targetName &&
                    h.RequestNamespace == normalizedNamespace))
                continue;

            var handler = new HandlerInfo
            {
                Name = symbol.Name,
                Namespace = RoslynSymbolFacts.NamespaceOf(symbol),
                RequestName = targetName,
                RequestNamespace = normalizedNamespace,
                ProjectName = projectName,
                Location = RoslynSymbolFacts.LocationOf(declaration.Identifier.GetLocation(), result.RootPath),
            };
            handler.ContainingTypes.AddRange(RoslynSymbolFacts.ContainingTypeNames(symbol));
            if (usings is not null)
                CSharpScanner.AddImportedNamespaces(usings, handler.ImportedNamespaces);
            result.Handlers.Add(handler);
        }
    }

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

    internal static void AddValidators(
        INamedTypeSymbol symbol,
        TypeDeclarationSyntax declaration,
        SemanticModel model,
        IReadOnlyList<UsingDirective>? usings,
        string? projectName,
        AnalysisResult result)
    {
        if (symbol.IsAbstract) return;

        var targets = new List<ITypeSymbol>();

        var validatorBase = RoslynSymbolFacts.FindBaseType(symbol, "AbstractValidator");
        if (validatorBase is { TypeArguments.Length: >= 1 })
            targets.Add(validatorBase.TypeArguments[0]);

        targets.AddRange(RoslynSymbolFacts.ImplementedInterfaces(symbol, declaration, model)
            .Where(i => i.Name == "IValidator" && i.TypeArguments.Length >= 1)
            .Select(i => i.TypeArguments[0]));

        foreach (var target in targets
                     .Where(t => t.TypeKind != TypeKind.TypeParameter)
                     .GroupBy(t => t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                     .Select(g => g.First()))
        {
            var targetName = RoslynSymbolFacts.TypeName(target);
            var targetNamespace = RoslynSymbolFacts.NamespaceOf(target);
            var normalizedNamespace = string.IsNullOrEmpty(targetNamespace) ? null : targetNamespace;
            var fullName = RoslynSymbolFacts.FullName(symbol);

            if (result.Validators.Any(v =>
                    v.FullName == fullName &&
                    v.ProjectName == projectName &&
                    v.TargetType == targetName &&
                    v.TargetNamespace == normalizedNamespace))
                continue;

            var validator = new ValidatorInfo
            {
                Name = symbol.Name,
                Namespace = RoslynSymbolFacts.NamespaceOf(symbol),
                TargetType = targetName,
                TargetNamespace = normalizedNamespace,
                ProjectName = projectName,
                Location = RoslynSymbolFacts.LocationOf(declaration.Identifier.GetLocation(), result.RootPath),
            };
            validator.ContainingTypes.AddRange(RoslynSymbolFacts.ContainingTypeNames(symbol));
            if (usings is not null)
                CSharpScanner.AddImportedNamespaces(usings, validator.ImportedNamespaces);
            result.Validators.Add(validator);
        }
    }

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
