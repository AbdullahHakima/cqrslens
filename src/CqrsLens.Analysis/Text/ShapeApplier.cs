using System.Text.RegularExpressions;

namespace CqrsLens;

/// <summary>
/// Turns one scanned file fragment into analysis entries: requests, notifications, handlers,
/// notification handlers, validators, behaviors, DbContexts and their DbSets. Identity
/// deduplication is supplied by <see cref="ScanIdentityIndex"/>.
/// </summary>
internal static partial class ShapeApplier
{
    [GeneratedRegex(@"\bDbSet\s*<\s*([^<>]*(?:<[^<>]*>[^<>]*)*)\s*>\s+(\w+)", RegexOptions.Singleline)]
    private static partial Regex DbSetRegex();

    /// <summary>
    /// Records namespaces imported by non-alias, non-static usings. The linker
    /// uses them after written/fallback namespaces and before a unique-simple-name
    /// guess, so a handler can point at the contract its file actually imports.
    /// </summary>
    internal static void AddImportedNamespaces(
        IEnumerable<UsingDirective> usings,
        List<string> target)
    {
        foreach (var directive in usings)
        {
            foreach (var ns in directive.NamespaceCandidates())
            {
                if (!target.Contains(ns, StringComparer.Ordinal))
                    target.Add(ns);
            }
        }
    }

    internal static void Apply(
        CSharpScanner.FileScan scan,
        string code,
        string? projectName,
        AnalysisResult result,
        string? filePath = null,
        SourceMask? mask = null,
        ScanIdentityIndex? identities = null)
    {
        var contextClasses = new List<(CSharpScanner.ClassDecl Decl, DbContextInfo Info)>();
        identities ??= new ScanIdentityIndex(result);

        foreach (var decl in scan.Classes)
        {
            SourceLocation? location = null;

            // Open generic / abstract base declarations are not concrete shapes.
            bool IsConcreteTarget(string? target) =>
                !decl.IsAbstract && target is not null &&
                !decl.TypeParameters.Contains(target, StringComparer.Ordinal);

            // IRequest<...> / IRequest  -> a CQRS request
            if (!decl.IsAbstract && decl.BaseRawNames.Contains("IRequest"))
            {
                var response = CSharpSyntax.ExtractGenericArguments(decl.BaseList, "IRequest")?.FirstOrDefault();
                if (identities.TryAddRequest(decl, projectName))
                {
                    var request = new CqrsRequest
                    {
                        Name = decl.Name,
                        Namespace = decl.Namespace,
                        ResponseType = response,
                        ProjectName = projectName,
                        Location = location ??= SourceLocation.Create(
                            filePath, result.RootPath, code, decl.NameIndex),
                    };
                    request.ContainingTypes.AddRange(decl.ContainingTypes);
                    result.Requests.Add(request);
                }
            }

            // INotification / IDomainEvent -> a CQRS notification / domain event
            if (!decl.IsAbstract &&
                (decl.BaseRawNames.Contains("INotification") || decl.BaseRawNames.Contains("IDomainEvent")))
            {
                if (identities.TryAddNotification(decl, projectName))
                {
                    var notification = new CqrsNotification
                    {
                        Name = decl.Name,
                        Namespace = decl.Namespace,
                        ProjectName = projectName,
                        ViaMarker = decl.BaseRawNames.Contains("INotification") ? null : "IDomainEvent",
                        Location = location ??= SourceLocation.Create(
                            filePath, result.RootPath, code, decl.NameIndex),
                    };
                    notification.ContainingTypes.AddRange(decl.ContainingTypes);
                    result.Notifications.Add(notification);
                }
            }

            // IRequestHandler<TRequest, TResponse> -> a handler for every interface.
            if (decl.BaseRawNames.Contains("IRequestHandler"))
            {
                foreach (var handlerArgs in CSharpSyntax.ExtractAllGenericArguments(decl.BaseList, "IRequestHandler"))
                {
                    if (handlerArgs.Count < 1) continue;
                    var requestType = handlerArgs[0];
                    if (!IsConcreteTarget(CSharpSyntax.UnqualifiedTypeName(requestType))) continue;
                    if (!identities.TryAddHandler(decl, projectName, CSharpSyntax.UnqualifiedTypeName(requestType)))
                        continue;

                    var handler = new HandlerInfo
                    {
                        Name = decl.Name,
                        Namespace = decl.Namespace,
                        RequestName = CSharpSyntax.UnqualifiedTypeName(requestType),
                        RequestNamespace = CSharpSyntax.NamespaceOfType(requestType) is { Length: > 0 } ns ? ns : null,
                        ProjectName = projectName,
                        Location = location ??= SourceLocation.Create(
                            filePath, result.RootPath, code, decl.NameIndex),
                    };
                    handler.ContainingTypes.AddRange(decl.ContainingTypes);
                    AddImportedNamespaces(decl.Usings, handler.ImportedNamespaces);
                    result.Handlers.Add(handler);
                }
            }

            // INotificationHandler<TNotification> / IDomainEventHandler<TNotification> -> a notification handler
            if (decl.BaseRawNames.Contains("INotificationHandler") ||
                decl.BaseRawNames.Contains("IDomainEventHandler"))
            {
                var notificationTargets = new List<string>();
                foreach (var root in new[] { "INotificationHandler", "IDomainEventHandler" })
                    notificationTargets.AddRange(CSharpSyntax.ExtractAllGenericArguments(decl.BaseList, root)
                        .Where(a => a.Count >= 1)
                        .Select(a => a[0]));
                foreach (var notificationType in notificationTargets.Distinct(StringComparer.Ordinal))
                {
                    if (!IsConcreteTarget(CSharpSyntax.UnqualifiedTypeName(notificationType))) continue;
                    if (!identities.TryAddNotificationHandler(decl, projectName, CSharpSyntax.UnqualifiedTypeName(notificationType)))
                        continue;

                    var notificationHandler = new NotificationHandlerInfo
                    {
                        Name = decl.Name,
                        Namespace = decl.Namespace,
                        NotificationName = CSharpSyntax.UnqualifiedTypeName(notificationType),
                        NotificationNamespace = CSharpSyntax.NamespaceOfType(notificationType) is { Length: > 0 } ns ? ns : null,
                        ProjectName = projectName,
                        Location = location ??= SourceLocation.Create(
                            filePath, result.RootPath, code, decl.NameIndex),
                    };
                    notificationHandler.ContainingTypes.AddRange(decl.ContainingTypes);
                    AddImportedNamespaces(decl.Usings, notificationHandler.ImportedNamespaces);
                    result.NotificationHandlers.Add(notificationHandler);
                }
            }

            // AbstractValidator<T> / IValidator<T> -> a validator for every target.
            if (decl.BaseRawNames.Contains("AbstractValidator") ||
                decl.BaseRawNames.Contains("IValidator"))
            {
                var validatorTargets = new List<string>();
                foreach (var root in new[] { "AbstractValidator", "IValidator" })
                    validatorTargets.AddRange(CSharpSyntax.ExtractAllGenericArguments(decl.BaseList, root)
                        .Where(a => a.Count >= 1)
                        .Select(a => a[0]));
                foreach (var validatorTarget in validatorTargets.Distinct(StringComparer.Ordinal))
                {
                    if (!IsConcreteTarget(CSharpSyntax.UnqualifiedTypeName(validatorTarget))) continue;
                    if (!identities.TryAddValidator(decl, projectName, CSharpSyntax.UnqualifiedTypeName(validatorTarget)))
                        continue;

                    var validator = new ValidatorInfo
                    {
                        Name = decl.Name,
                        Namespace = decl.Namespace,
                        TargetType = CSharpSyntax.UnqualifiedTypeName(validatorTarget),
                        TargetNamespace = CSharpSyntax.NamespaceOfType(validatorTarget) is { Length: > 0 } ns ? ns : null,
                        ProjectName = projectName,
                        Location = location ??= SourceLocation.Create(
                            filePath, result.RootPath, code, decl.NameIndex),
                    };
                    validator.ContainingTypes.AddRange(decl.ContainingTypes);
                    AddImportedNamespaces(decl.Usings, validator.ImportedNamespaces);
                    result.Validators.Add(validator);
                }
            }

            // IPipelineBehavior<...> -> a pipeline behavior
            if (decl.BaseList.Contains("IPipelineBehavior<", StringComparison.Ordinal))
            {
                result.Behaviors.Add(new BehaviorInfo
                {
                    Name = decl.Name,
                    ProjectName = projectName,
                    Location = location ??= SourceLocation.Create(
                        filePath, result.RootPath, code, decl.NameIndex),
                });
            }

            // : DbContext (or *DbContext, e.g. IdentityDbContext) -> a DbContext
            if (!decl.IsAbstract && CSharpSyntax.IsDbContextBase(decl.BaseList))
            {
                var ctx = new DbContextInfo
                {
                    Name = decl.Name,
                    Namespace = decl.Namespace,
                    ProjectName = projectName,
                    Location = location ??= SourceLocation.Create(
                        filePath, result.RootPath, code, decl.NameIndex),
                };
                ctx.ContainingTypes.AddRange(decl.ContainingTypes);
                contextClasses.Add((decl, ctx));
                result.DbContexts.Add(ctx);
            }
        }

        if (contextClasses.Count > 0)
        {
            mask ??= SourceMask.Build(code);
            foreach (Match m in DbSetRegex().Matches(code))
            {
                if (!mask.IsCode(m.Index)) continue;
                var owner = contextClasses
                    .Where(c => m.Index >= c.Decl.BodySpan.Start && m.Index < c.Decl.BodySpan.End)
                    .OrderByDescending(c => c.Decl.BodySpan.Start)
                    .FirstOrDefault();
                if (owner.Info is null) continue;
                var set = new DbSetInfo(m.Groups[1].Value.Trim(), m.Groups[2].Value);
                if (!owner.Info.DbSets.Contains(set))
                    owner.Info.DbSets.Add(set);
            }
        }
    }
}
