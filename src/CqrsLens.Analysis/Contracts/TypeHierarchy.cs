namespace CqrsLens;

/// <summary>
/// Resolves shape contracts through source-declared generic interface/base-class
/// chains. Text mode has no semantic model, so this intentionally handles the
/// common single-inheritance substitution cases (including reordered and fixed
/// generic arguments) and stops on ambiguous names rather than guessing.
/// </summary>
internal sealed class TypeHierarchy
{
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
}
