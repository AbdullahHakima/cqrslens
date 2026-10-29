namespace CqrsLens;

/// <summary>
/// Follows a class's source-declared base classes and interfaces to find request
/// roots (IRequest and configured markers). Every root is kept so a request can be
/// classified through a meaningful base class such as QueryBase&lt;T&gt;.
/// </summary>
internal sealed class RequestShapeResolver
{
    private const int MaxDepth = 32;
    private readonly TypeNameResolver _names;

    public RequestShapeResolver(TypeNameResolver names) => _names = names;

    public RequestShapeResolution Resolve(
        CSharpScanner.ClassDecl declaration,
        IReadOnlySet<string> markerRoots)
    {
        var shape = new RequestShapeResolution();
        VisitClass(
            declaration,
            GenericBindings.Empty,
            markerRoots,
            new HashSet<string>(StringComparer.Ordinal),
            0,
            shape);
        return shape;
    }

    private void VisitClass(
        CSharpScanner.ClassDecl declaration,
        IReadOnlyDictionary<string, string> bindings,
        IReadOnlySet<string> markerRoots,
        HashSet<string> visited,
        int depth,
        RequestShapeResolution shape)
    {
        if (depth > MaxDepth ||
            !visited.Add(GenericBindings.VisitKey(
                declaration.FullName, declaration.TypeParameters.Count, bindings)))
            return;

        foreach (var part in CSharpScanner.SplitTopLevel(declaration.BaseList))
        {
            var (name, rawArgs) = CSharpScanner.ParseTypeReference(part);
            var args = rawArgs.Select(argument => GenericBindings.Substitute(argument, bindings)).ToList();
            VisitBase(
                name,
                args,
                declaration.Namespace,
                declaration.Usings,
                declaration.ContainingTypes,
                markerRoots,
                visited,
                depth + 1,
                shape);
        }
    }

    private void VisitInterface(
        CSharpScanner.InterfaceDecl declaration,
        IReadOnlyDictionary<string, string> bindings,
        IReadOnlySet<string> markerRoots,
        HashSet<string> visited,
        int depth,
        RequestShapeResolution shape)
    {
        if (depth > MaxDepth ||
            !visited.Add(GenericBindings.VisitKey(
                declaration.FullName, declaration.TypeParameters.Count, bindings)))
            return;

        foreach (var part in CSharpScanner.SplitTopLevel(declaration.BaseList))
        {
            var (name, rawArgs) = CSharpScanner.ParseTypeReference(part);
            var args = rawArgs.Select(argument => GenericBindings.Substitute(argument, bindings)).ToList();
            VisitBase(
                name,
                args,
                declaration.Namespace,
                declaration.Usings,
                declaration.ContainingTypes,
                markerRoots,
                visited,
                depth + 1,
                shape);
        }
    }

    private void VisitBase(
        string name,
        IReadOnlyList<string> args,
        string currentNamespace,
        IReadOnlyList<UsingDirective> usings,
        IReadOnlyList<string> containingTypes,
        IReadOnlySet<string> markerRoots,
        HashSet<string> visited,
        int depth,
        RequestShapeResolution shape)
    {
        var simple = CSharpScanner.SimpleName(name);
        var isRoot = simple is "IRequest" || markerRoots.Contains(simple);

        var interfaceDecl = _names.ResolveInterfaceDecl(
            name, currentNamespace, usings, containingTypes, args.Count,
            out var interfaceAmbiguous);
        AddAmbiguities(shape, interfaceAmbiguous);
        if (interfaceDecl is not null)
        {
            if (isRoot) AddRoot(shape, simple, args);
            var interfaceBindings = GenericBindings.Bind(
                interfaceDecl.TypeParameters, args, GenericBindings.Empty);
            VisitInterface(
                interfaceDecl, interfaceBindings, markerRoots, visited, depth + 1, shape);
            return;
        }

        var classDecl = _names.ResolveClassDecl(
            name, currentNamespace, usings, containingTypes, args.Count,
            out var classAmbiguous);
        AddAmbiguities(shape, classAmbiguous);
        if (classDecl is not null)
        {
            if (isRoot) AddRoot(shape, simple, args);
            var classBindings = GenericBindings.Bind(
                classDecl.TypeParameters, args, GenericBindings.Empty);
            VisitClass(
                classDecl, classBindings, markerRoots, visited, depth + 1, shape);
            return;
        }

        // An external marker/root is usable; an ambiguous source name is not.
        if (isRoot && interfaceAmbiguous.Count == 0 && classAmbiguous.Count == 0)
        {
            AddRoot(shape, simple, args);
            return;
        }

        if (!isRoot && interfaceAmbiguous.Count == 0 && classAmbiguous.Count == 0 &&
            !shape.UnresolvedBases.Contains(simple, StringComparer.Ordinal))
            shape.UnresolvedBases.Add(simple);
    }

    private static void AddRoot(
        RequestShapeResolution shape,
        string root,
        IReadOnlyList<string> args)
    {
        if (!shape.Roots.Contains(root, StringComparer.Ordinal))
            shape.Roots.Add(root);
        if (shape.ResponseType is null && args.Count > 0)
            shape.ResponseType = args[0];
    }

    private static void AddAmbiguities(
        RequestShapeResolution shape,
        IReadOnlyList<string> candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!shape.AmbiguousCandidates.Contains(candidate, StringComparer.Ordinal))
                shape.AmbiguousCandidates.Add(candidate);
        }
    }
}
