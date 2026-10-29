namespace CqrsLens;

/// <summary>
/// Finds handlers and validators declared through application-specific interfaces or
/// base classes. Direct roots are already handled by <see cref="CSharpScanner.ApplyScan"/>.
/// </summary>
internal static class IndirectShapeDiscoverer
{
    public static void Discover(
        CSharpScanner.FileScan scan,
        SourceFile file,
        TypeHierarchy hierarchy,
        IReadOnlySet<string> handlerRoots,
        AnalysisResult result)
    {
        foreach (var declaration in scan.Classes)
        {
            if (declaration.IsAbstract) continue;

            var baseParts = CSharpScanner.SplitTopLevel(declaration.BaseList);
            if (baseParts.Count == 0) continue;

            // Direct roots are already recorded by ApplyScan. Only resolve base parts
            // that could be a user-defined wrapper (e.g. ICommandHandler<T> or a
            // CommandBase<T>); this avoids re-walking the hierarchy for every record
            // and entity in large modular solutions.
            var hasIndirectShape = false;
            foreach (var part in baseParts)
            {
                var (name, _) = CSharpScanner.ParseTypeReference(part);
                if (!ContractRoots.IsDirectShapeRoot(CSharpScanner.SimpleName(name)))
                {
                    hasIndirectShape = true;
                    break;
                }
            }
            if (!hasIndirectShape) continue;

            var typeParameters = new HashSet<string>(declaration.TypeParameters, StringComparer.Ordinal);
            var location = SourceLocation.Create(
                file.Path, result.RootPath, file.Code, declaration.NameIndex);

            foreach (var part in baseParts)
            {
                var (name, _) = CSharpScanner.ParseTypeReference(part);
                var simple = CSharpScanner.SimpleName(name);
                if (simple is "DbContext" or "IdentityDbContext") continue;

                TryAddHandler(
                    declaration, part, simple, file, location, typeParameters,
                    hierarchy, handlerRoots, result);
                TryAddValidator(
                    declaration, part, simple, file, location, typeParameters,
                    hierarchy, result);
                TryAddNotificationHandler(
                    declaration, part, simple, file, location, typeParameters,
                    hierarchy, result);
            }
        }
    }

    private static void TryAddHandler(
        CSharpScanner.ClassDecl declaration,
        string part,
        string simple,
        SourceFile file,
        SourceLocation? location,
        IReadOnlySet<string> typeParameters,
        TypeHierarchy hierarchy,
        IReadOnlySet<string> handlerRoots,
        AnalysisResult result)
    {
        if (simple == "IRequestHandler") return;

        var resolved = hierarchy.TryResolveShapeTarget(
            declaration.Namespace,
            part,
            handlerRoots,
            declaration.Usings,
            declaration.ContainingTypes,
            out var requestType,
            out var ambiguity);
        if (resolved)
        {
            var requestName = CSharpScanner.UnqualifiedTypeName(requestType);
            if (requestName.Length > 0 && !typeParameters.Contains(requestName) &&
                !result.Handlers.Any(h =>
                    h.Namespace == declaration.Namespace &&
                    h.Name == declaration.Name &&
                    h.ProjectName == file.ProjectName &&
                    h.ContainingTypes.SequenceEqual(declaration.ContainingTypes) &&
                    h.RequestName == requestName))
            {
                var handler = new HandlerInfo
                {
                    Name = declaration.Name,
                    Namespace = declaration.Namespace,
                    RequestName = requestName,
                    RequestNamespace = CSharpScanner.NamespaceOfType(requestType) is { Length: > 0 } ns
                        ? ns
                        : null,
                    ProjectName = file.ProjectName,
                    Location = location,
                };
                handler.ContainingTypes.AddRange(declaration.ContainingTypes);
                CSharpScanner.AddImportedNamespaces(declaration.Usings, handler.ImportedNamespaces);
                result.Handlers.Add(handler);
            }
        }
        else if (handlerRoots.Contains(simple) || ContractRoots.LooksLikeHandlerContract(simple))
        {
            ContractDiagnostics.RecordShapeResolutionGap(
                result, file, declaration.NameIndex, declaration.Name, simple,
                hierarchy, ambiguity, "handler");
        }
    }

    private static void TryAddValidator(
        CSharpScanner.ClassDecl declaration,
        string part,
        string simple,
        SourceFile file,
        SourceLocation? location,
        IReadOnlySet<string> typeParameters,
        TypeHierarchy hierarchy,
        AnalysisResult result)
    {
        if (simple is "AbstractValidator" or "IValidator") return;

        var resolved = hierarchy.TryResolveShapeTarget(
            declaration.Namespace,
            part,
            ContractRoots.ValidatorRoots,
            declaration.Usings,
            declaration.ContainingTypes,
            out var target,
            out var ambiguity);
        if (resolved)
        {
            var targetName = CSharpScanner.UnqualifiedTypeName(target);
            if (targetName.Length > 0 && !typeParameters.Contains(targetName) &&
                !result.Validators.Any(v =>
                    v.Namespace == declaration.Namespace &&
                    v.Name == declaration.Name &&
                    v.ProjectName == file.ProjectName &&
                    v.ContainingTypes.SequenceEqual(declaration.ContainingTypes) &&
                    v.TargetType == targetName))
            {
                var validator = new ValidatorInfo
                {
                    Name = declaration.Name,
                    Namespace = declaration.Namespace,
                    TargetType = targetName,
                    TargetNamespace = CSharpScanner.NamespaceOfType(target) is { Length: > 0 } ns
                        ? ns
                        : null,
                    ProjectName = file.ProjectName,
                    Location = location,
                };
                validator.ContainingTypes.AddRange(declaration.ContainingTypes);
                CSharpScanner.AddImportedNamespaces(declaration.Usings, validator.ImportedNamespaces);
                result.Validators.Add(validator);
            }
        }
        else if (ContractRoots.LooksLikeValidatorContract(simple))
        {
            ContractDiagnostics.RecordShapeResolutionGap(
                result, file, declaration.NameIndex, declaration.Name, simple,
                hierarchy, ambiguity, "validator");
        }
    }

    private static void TryAddNotificationHandler(
        CSharpScanner.ClassDecl declaration,
        string part,
        string simple,
        SourceFile file,
        SourceLocation? location,
        IReadOnlySet<string> typeParameters,
        TypeHierarchy hierarchy,
        AnalysisResult result)
    {
        if (simple is "INotificationHandler" or "IDomainEventHandler") return;

        var resolved = hierarchy.TryResolveShapeTarget(
            declaration.Namespace,
            part,
            ContractRoots.NotificationHandlerRoots,
            declaration.Usings,
            declaration.ContainingTypes,
            out var target,
            out _);
        if (!resolved) return;

        var targetName = CSharpScanner.UnqualifiedTypeName(target);
        if (targetName.Length == 0 || typeParameters.Contains(targetName)) return;
        if (result.NotificationHandlers.Any(h =>
                h.Namespace == declaration.Namespace &&
                h.Name == declaration.Name &&
                h.ProjectName == file.ProjectName &&
                h.ContainingTypes.SequenceEqual(declaration.ContainingTypes) &&
                h.NotificationName == targetName))
            return;

        var handler = new NotificationHandlerInfo
        {
            Name = declaration.Name,
            Namespace = declaration.Namespace,
            NotificationName = targetName,
            NotificationNamespace = CSharpScanner.NamespaceOfType(target) is { Length: > 0 } ns
                ? ns
                : null,
            ProjectName = file.ProjectName,
            Location = location,
        };
        handler.ContainingTypes.AddRange(declaration.ContainingTypes);
        CSharpScanner.AddImportedNamespaces(declaration.Usings, handler.ImportedNamespaces);
        result.NotificationHandlers.Add(handler);
    }

}
