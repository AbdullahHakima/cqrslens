namespace CqrsLens;

/// <summary>
/// Resolves shape contracts through source-declared generic interface/base-class
/// chains. Text mode has no semantic model, so this intentionally handles the
/// common single-inheritance substitution cases (including reordered and fixed
/// generic arguments) and stops on ambiguous names rather than guessing.
/// </summary>
internal sealed class TypeHierarchy
{
    private const int MaxDepth = 32;
    private readonly TypeIndex _index;
    private readonly TypeNameResolver _names;
    private readonly RequestShapeResolver _requestShapes;
    private readonly ShapeTargetResolver _shapeTargets;

    public TypeHierarchy(
        IEnumerable<CSharpScanner.ClassDecl> classes,
        IEnumerable<CSharpScanner.InterfaceDecl> interfaces)
    {
        _index = new TypeIndex(classes, interfaces);
        _names = new TypeNameResolver(_index);
        _requestShapes = new RequestShapeResolver(_names);
        _shapeTargets = new ShapeTargetResolver(_names);
    }

    public bool HasInterface(string name)
        => _index.HasInterfaceSimpleName(CSharpScanner.SimpleNameFromIdentity(name));

    /// <summary>
    /// Follows a class's source-declared base classes and interfaces to find
    /// request roots (IRequest and configured markers).
    /// </summary>
    public RequestShapeResolution ResolveRequestShape(
        CSharpScanner.ClassDecl declaration,
        IReadOnlySet<string> markerRoots)
        => _requestShapes.Resolve(declaration, markerRoots);

    /// <summary>
    /// Resolves an interface reference to its internal source identity (full name
    /// plus generic arity). A null result with
    /// <paramref name="ambiguousCandidates"/> populated means the simple name has
    /// multiple candidates and no local/using match.
    /// </summary>
    public string? ResolveInterfaceReference(
        string name,
        string currentNamespace,
        IReadOnlyList<UsingDirective> usings,
        IReadOnlyList<string> containingTypes,
        int arity,
        out IReadOnlyList<string> ambiguousCandidates)
        => _names.ResolveInterfaceReference(
            name, currentNamespace, usings, containingTypes, arity, out ambiguousCandidates);

    /// <summary>
    /// Resolves the first generic argument of a shape root (e.g. IRequestHandler or
    /// AbstractValidator) reached from <paramref name="basePart"/>.
    /// </summary>
    public bool TryResolveShapeTarget(
        string currentNamespace,
        string basePart,
        IReadOnlySet<string> rootNames,
        IReadOnlyList<UsingDirective> usings,
        IReadOnlyList<string> containingTypes,
        out string target,
        out IReadOnlyList<string> ambiguousCandidates)
        => _shapeTargets.TryResolve(
            currentNamespace, basePart, rootNames, usings, containingTypes,
            out target, out ambiguousCandidates);

    /// <summary>
    /// Resolves a written class reference using the same namespace/using rules as
    /// contract linking. Entity discovery uses this instead of a simple-name guess.
    /// </summary>
    public CSharpScanner.ClassDecl? TryResolveClass(
        string name,
        string currentNamespace,
        IReadOnlyList<UsingDirective> usings,
        IReadOnlyList<string> containingTypes,
        int arity,
        out IReadOnlyList<string> ambiguousCandidates)
        => _names.TryResolveClass(
            name, currentNamespace, usings, containingTypes, arity, out ambiguousCandidates);

    /// <summary>
    /// Enumerates every interface a concrete class implements, following source-declared
    /// base classes and interface chains with the same generic substitution used for
    /// request-shape resolution. The result is the declaration half of the
    /// service-resolution index; it reuses this hierarchy instead of a parallel one.
    /// An interface reached with an unresolved class type parameter as an argument is
    /// marked open, so it cannot disambiguate a closed interface.
    /// </summary>
    public void CollectImplementedInterfaces(
        CSharpScanner.ClassDecl declaration,
        List<ImplementedInterface> results)
    {
        // No base list means no interface can be reached; skip the hash sets and key.
        if (string.IsNullOrWhiteSpace(declaration.BaseList)) return;

        var openParameters = declaration.TypeParameters.Count == 0
            ? EmptyOpenParameters
            : new HashSet<string>(declaration.TypeParameters, StringComparer.Ordinal);
        VisitClassInterfaces(
            declaration,
            GenericBindings.Empty,
            openParameters,
            new HashSet<string>(StringComparer.Ordinal),
            0,
            results);
    }

    private static readonly IReadOnlySet<string> EmptyOpenParameters =
        new HashSet<string>(StringComparer.Ordinal);

    private void VisitClassInterfaces(
        CSharpScanner.ClassDecl declaration,
        IReadOnlyDictionary<string, string> bindings,
        IReadOnlySet<string> openParameters,
        HashSet<string> visited,
        int depth,
        List<ImplementedInterface> results)
    {
        if (depth > MaxDepth ||
            !visited.Add(GenericBindings.VisitKey(
                declaration.FullName, declaration.TypeParameters.Count, bindings)))
            return;

        foreach (var part in CSharpScanner.SplitTopLevel(declaration.BaseList))
        {
            var (name, rawArgs) = CSharpScanner.ParseTypeReference(part);
            // With no outer bindings the arguments are used as written, so the mapped
            // copy is skipped entirely (the common case).
            var args = bindings.Count == 0
                ? rawArgs
                : rawArgs.Select(argument => GenericBindings.Substitute(argument, bindings)).ToList();

            var interfaceDecl = _names.ResolveInterfaceDecl(
                name, declaration.Namespace, declaration.Usings, declaration.ContainingTypes,
                args.Count, out _);
            if (interfaceDecl is not null)
            {
                RecordInterface(interfaceDecl, args, openParameters, results);
                VisitInterfaceInterfaces(
                    interfaceDecl,
                    GenericBindings.Bind(interfaceDecl.TypeParameters, args),
                    openParameters,
                    visited,
                    depth + 1,
                    results);
                continue;
            }

            var classDecl = _names.ResolveClassDecl(
                name, declaration.Namespace, declaration.Usings, declaration.ContainingTypes,
                args.Count, out _);
            if (classDecl is null) continue;
            VisitClassInterfaces(
                classDecl,
                GenericBindings.Bind(classDecl.TypeParameters, args),
                openParameters,
                visited,
                depth + 1,
                results);
        }
    }

    private void VisitInterfaceInterfaces(
        CSharpScanner.InterfaceDecl declaration,
        IReadOnlyDictionary<string, string> bindings,
        IReadOnlySet<string> openParameters,
        HashSet<string> visited,
        int depth,
        List<ImplementedInterface> results)
    {
        if (depth > MaxDepth ||
            !visited.Add(GenericBindings.VisitKey(
                declaration.FullName, declaration.TypeParameters.Count, bindings)))
            return;

        foreach (var part in CSharpScanner.SplitTopLevel(declaration.BaseList))
        {
            var (name, rawArgs) = CSharpScanner.ParseTypeReference(part);
            var args = bindings.Count == 0
                ? rawArgs
                : rawArgs.Select(argument => GenericBindings.Substitute(argument, bindings)).ToList();

            var interfaceDecl = _names.ResolveInterfaceDecl(
                name, declaration.Namespace, declaration.Usings, declaration.ContainingTypes,
                args.Count, out _);
            if (interfaceDecl is null) continue;

            RecordInterface(interfaceDecl, args, openParameters, results);
            VisitInterfaceInterfaces(
                interfaceDecl,
                GenericBindings.Bind(interfaceDecl.TypeParameters, args),
                openParameters,
                visited,
                depth + 1,
                results);
        }
    }

    private static void RecordInterface(
        CSharpScanner.InterfaceDecl declaration,
        IReadOnlyList<string> args,
        IReadOnlySet<string> openParameters,
        List<ImplementedInterface> results)
    {
        var arity = declaration.TypeParameters.Count;
        var isOpen = arity > 0 &&
            (args.Count != arity || args.Any(argument => ContainsOpenParameter(argument, openParameters)));
        results.Add(new ImplementedInterface(
            CSharpScanner.TypeIdentity(declaration.FullName, arity),
            arity,
            args,
            isOpen));
    }

    private static bool ContainsOpenParameter(string argument, IReadOnlySet<string> openParameters)
    {
        if (openParameters.Count == 0) return false;

        for (var i = 0; i < argument.Length;)
        {
            var c = argument[i];
            if (!char.IsLetter(c) && c != '_')
            {
                i++;
                continue;
            }

            var start = i++;
            while (i < argument.Length && (char.IsLetterOrDigit(argument[i]) || argument[i] == '_')) i++;
            if (openParameters.Contains(argument[start..i])) return true;
        }

        return false;
    }
}
