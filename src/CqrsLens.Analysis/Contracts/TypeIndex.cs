namespace CqrsLens;

/// <summary>
/// Lookup tables for source-declared classes and interfaces. Full identities keep
/// namespaces and generic arity distinct; simple-name lists preserve every candidate
/// so callers can detect ambiguity instead of guessing.
/// </summary>
internal sealed class TypeIndex
{
    private readonly Dictionary<string, CSharpScanner.ClassDecl> _classesByFull;
    private readonly Dictionary<string, List<CSharpScanner.ClassDecl>> _classesBySimple;
    private readonly Dictionary<string, CSharpScanner.InterfaceDecl> _interfacesByFull;
    private readonly Dictionary<string, List<CSharpScanner.InterfaceDecl>> _interfacesBySimple;
    private readonly HashSet<string> _interfaceSimpleNames;

    public TypeIndex(
        IEnumerable<CSharpScanner.ClassDecl> classes,
        IEnumerable<CSharpScanner.InterfaceDecl> interfaces)
    {
        _classesByFull = new Dictionary<string, CSharpScanner.ClassDecl>(StringComparer.Ordinal);
        _classesBySimple = new Dictionary<string, List<CSharpScanner.ClassDecl>>(StringComparer.Ordinal);
        foreach (var declaration in classes)
        {
            var arity = declaration.TypeParameters.Count;
            _classesByFull.TryAdd(
                CSharpScanner.TypeIdentity(declaration.FullName, arity), declaration);
            AddBySimple(
                _classesBySimple,
                CSharpScanner.TypeIdentity(CSharpScanner.SimpleName(declaration.Name), arity),
                declaration);
        }

        _interfacesByFull = new Dictionary<string, CSharpScanner.InterfaceDecl>(StringComparer.Ordinal);
        _interfacesBySimple = new Dictionary<string, List<CSharpScanner.InterfaceDecl>>(StringComparer.Ordinal);
        _interfaceSimpleNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var declaration in interfaces)
        {
            var arity = declaration.TypeParameters.Count;
            _interfacesByFull.TryAdd(
                CSharpScanner.TypeIdentity(declaration.FullName, arity), declaration);
            AddBySimple(
                _interfacesBySimple,
                CSharpScanner.TypeIdentity(CSharpScanner.SimpleName(declaration.Name), arity),
                declaration);
            _interfaceSimpleNames.Add(CSharpScanner.SimpleName(declaration.Name));
        }
    }

    public bool HasInterfaceSimpleName(string name) => _interfaceSimpleNames.Contains(name);

    public bool TryGetClass(string identity, out CSharpScanner.ClassDecl declaration)
        => _classesByFull.TryGetValue(identity, out declaration!);

    public bool TryGetInterface(string identity, out CSharpScanner.InterfaceDecl declaration)
        => _interfacesByFull.TryGetValue(identity, out declaration!);

    public IReadOnlyList<CSharpScanner.ClassDecl> ClassesBySimple(string identity)
        => _classesBySimple.TryGetValue(identity, out var matches) ? matches : Array.Empty<CSharpScanner.ClassDecl>();

    public IReadOnlyList<CSharpScanner.InterfaceDecl> InterfacesBySimple(string identity)
        => _interfacesBySimple.TryGetValue(identity, out var matches) ? matches : Array.Empty<CSharpScanner.InterfaceDecl>();

    private static void AddBySimple<T>(Dictionary<string, List<T>> map, string key, T value)
    {
        if (!map.TryGetValue(key, out var list))
            map[key] = list = new List<T>();
        list.Add(value);
    }
}
