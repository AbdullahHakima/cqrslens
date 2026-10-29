namespace CqrsLens;

/// <summary>
/// Resolves the first generic argument of a shape root (e.g. IRequestHandler or
/// AbstractValidator) reached from a written base part.
/// </summary>
internal sealed class ShapeTargetResolver
{
    private const int MaxDepth = 32;
    private readonly TypeNameResolver _names;

    public ShapeTargetResolver(TypeNameResolver names) => _names = names;

    public bool TryResolve(
        string currentNamespace,
        string basePart,
        IReadOnlySet<string> rootNames,
        IReadOnlyList<UsingDirective> usings,
        IReadOnlyList<string> containingTypes,
        out string target,
        out IReadOnlyList<string> ambiguousCandidates)
    {
        target = "";
        ambiguousCandidates = Array.Empty<string>();

        var (name, args) = CSharpScanner.ParseTypeReference(basePart);
        if (string.IsNullOrWhiteSpace(name)) return false;

        var ambiguities = new List<string>();
        var interfaceDecl = _names.ResolveInterfaceDecl(
            name, currentNamespace, usings, containingTypes, args.Count,
            out var interfaceAmbiguous);
        AddAmbiguities(ambiguities, interfaceAmbiguous);
        if (interfaceDecl is not null)
        {
            var bindings = GenericBindings.Bind(
                interfaceDecl.TypeParameters, args, GenericBindings.Empty);
            if (ResolveInInterface(
                    interfaceDecl, bindings, rootNames,
                    new HashSet<string>(StringComparer.Ordinal), 0, ambiguities, out target))
                return true;
        }

        var classDecl = _names.ResolveClassDecl(
            name, currentNamespace, usings, containingTypes, args.Count,
            out var classAmbiguous);
        AddAmbiguities(ambiguities, classAmbiguous);
        if (classDecl is not null)
        {
            var bindings = GenericBindings.Bind(
                classDecl.TypeParameters, args, GenericBindings.Empty);
            if (ResolveInClass(
                    classDecl, bindings, rootNames,
                    new HashSet<string>(StringComparer.Ordinal), 0, ambiguities, out target))
                return true;
        }

        // External/unresolved contract: a direct root can still yield its first argument.
        if (rootNames.Contains(CSharpScanner.SimpleName(name)) && args.Count > 0)
        {
            target = args[0];
            return true;
        }

        ambiguousCandidates = ambiguities
            .Distinct(StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();
        return false;
    }

    private bool ResolveInInterface(
        CSharpScanner.InterfaceDecl declaration,
        IReadOnlyDictionary<string, string> bindings,
        IReadOnlySet<string> rootNames,
        HashSet<string> visited,
        int depth,
        List<string> ambiguities,
        out string target)
    {
        target = "";
        if (depth > MaxDepth ||
            !visited.Add(GenericBindings.VisitKey(
                declaration.FullName, declaration.TypeParameters.Count, bindings)))
            return false;

        foreach (var part in CSharpScanner.SplitTopLevel(declaration.BaseList))
        {
            var (name, rawArgs) = CSharpScanner.ParseTypeReference(part);
            var args = rawArgs.Select(argument => GenericBindings.Substitute(argument, bindings)).ToList();
            var simple = CSharpScanner.SimpleName(name);

            if (rootNames.Contains(simple) && args.Count > 0)
            {
                target = args[0];
                return true;
            }

            var baseDecl = _names.ResolveInterfaceDecl(
                name, declaration.Namespace, declaration.Usings, declaration.ContainingTypes,
                args.Count, out var baseAmbiguous);
            AddAmbiguities(ambiguities, baseAmbiguous);
            if (baseDecl is null) continue;
            var baseBindings = GenericBindings.Bind(
                baseDecl.TypeParameters, args, GenericBindings.Empty);
            if (ResolveInInterface(
                    baseDecl, baseBindings, rootNames, visited, depth + 1, ambiguities, out target))
                return true;
        }

        return false;
    }

    private bool ResolveInClass(
        CSharpScanner.ClassDecl declaration,
        IReadOnlyDictionary<string, string> bindings,
        IReadOnlySet<string> rootNames,
        HashSet<string> visited,
        int depth,
        List<string> ambiguities,
        out string target)
    {
        target = "";
        if (depth > MaxDepth ||
            !visited.Add(GenericBindings.VisitKey(
                declaration.FullName, declaration.TypeParameters.Count, bindings)))
            return false;

        foreach (var part in CSharpScanner.SplitTopLevel(declaration.BaseList))
        {
            var (name, rawArgs) = CSharpScanner.ParseTypeReference(part);
            var args = rawArgs.Select(argument => GenericBindings.Substitute(argument, bindings)).ToList();
            var simple = CSharpScanner.SimpleName(name);

            if (rootNames.Contains(simple) && args.Count > 0)
            {
                target = args[0];
                return true;
            }

            var baseInterface = _names.ResolveInterfaceDecl(
                name, declaration.Namespace, declaration.Usings, declaration.ContainingTypes,
                args.Count, out var interfaceAmbiguous);
            AddAmbiguities(ambiguities, interfaceAmbiguous);
            if (baseInterface is not null)
            {
                var interfaceBindings = GenericBindings.Bind(
                    baseInterface.TypeParameters, args, GenericBindings.Empty);
                if (ResolveInInterface(
                        baseInterface, interfaceBindings, rootNames, visited, depth + 1,
                        ambiguities, out target))
                    return true;
            }

            var baseClass = _names.ResolveClassDecl(
                name, declaration.Namespace, declaration.Usings, declaration.ContainingTypes,
                args.Count, out var classAmbiguous);
            AddAmbiguities(ambiguities, classAmbiguous);
            if (baseClass is null) continue;
            var classBindings = GenericBindings.Bind(
                baseClass.TypeParameters, args, GenericBindings.Empty);
            if (ResolveInClass(
                    baseClass, classBindings, rootNames, visited, depth + 1,
                    ambiguities, out target))
                return true;
        }

        return false;
    }

    private static void AddAmbiguities(List<string> target, IReadOnlyList<string> candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!target.Contains(candidate, StringComparer.Ordinal))
                target.Add(candidate);
        }
    }
}
