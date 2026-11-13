using System.Runtime.CompilerServices;

namespace CqrsLens;

/// <summary>
/// Resolves written type references using the rules the text scanner needs:
/// containing types, the current/enclosing namespaces, using aliases and
/// namespaces, then a unique simple-name fallback. A simple name with multiple
/// candidates is reported as ambiguous instead of being guessed.
///
/// The resolution is a pure function of the written reference and the (fixed)
/// <see cref="TypeIndex"/>, so results are memoised. The scanner resolves the same
/// base/interface references over and over — across declarations that share a
/// namespace and using set — and re-running the candidate cascade for every one of
/// them is the dominant cost of the service-resolution passes. Caching only changes
/// how long resolution takes, never what it returns.
/// </summary>
internal sealed class TypeNameResolver
{
    private readonly TypeIndex _index;
    private readonly Dictionary<ResolveKey, Lookup<CSharpScanner.InterfaceDecl>> _interfaceCache = new();
    private readonly Dictionary<ResolveKey, Lookup<CSharpScanner.ClassDecl>> _classCache = new();
    private readonly ConditionalWeakTable<object, string[]> _usingNamespaces = new();

    // Hash of the current scope's using list / containing-type chain, remembered for the last
    // scope seen. Declarations resolve many references from the same scope in a row, so this
    // turns the per-lookup key hash from O(usings) into a reference check.
    private IReadOnlyList<UsingDirective>? _lastUsings;
    private int _lastUsingsHash;
    private IReadOnlyList<string>? _lastContaining;
    private int _lastContainingHash;

    public TypeNameResolver(TypeIndex index) => _index = index;

    public CSharpScanner.InterfaceDecl? ResolveInterfaceDecl(
        string name,
        string currentNamespace,
        IReadOnlyList<UsingDirective> usings,
        IReadOnlyList<string> containingTypes,
        int arity,
        out IReadOnlyList<string> ambiguousCandidates)
    {
        var key = CreateKey(name, currentNamespace, usings, containingTypes, arity);
        if (_interfaceCache.TryGetValue(key, out var cached))
        {
            ambiguousCandidates = cached.Ambiguous;
            return cached.Value;
        }

        var value = Resolve(
            name,
            currentNamespace,
            usings,
            containingTypes,
            arity,
            identity => _index.TryGetInterface(identity, out var declaration) ? declaration : null,
            _index.InterfacesBySimple,
            declaration => declaration.FullName,
            out var ambiguous);
        _interfaceCache[key] = new Lookup<CSharpScanner.InterfaceDecl>(value, ambiguous);
        ambiguousCandidates = ambiguous;
        return value;
    }

    public CSharpScanner.ClassDecl? ResolveClassDecl(
        string name,
        string currentNamespace,
        IReadOnlyList<UsingDirective> usings,
        IReadOnlyList<string> containingTypes,
        int arity,
        out IReadOnlyList<string> ambiguousCandidates)
    {
        var key = CreateKey(name, currentNamespace, usings, containingTypes, arity);
        if (_classCache.TryGetValue(key, out var cached))
        {
            ambiguousCandidates = cached.Ambiguous;
            return cached.Value;
        }

        var value = Resolve(
            name,
            currentNamespace,
            usings,
            containingTypes,
            arity,
            identity => _index.TryGetClass(identity, out var declaration) ? declaration : null,
            _index.ClassesBySimple,
            declaration => declaration.FullName,
            out var ambiguous);
        _classCache[key] = new Lookup<CSharpScanner.ClassDecl>(value, ambiguous);
        ambiguousCandidates = ambiguous;
        return value;
    }

    /// <summary>
    /// Resolves an interface reference to its internal source identity (full name
    /// plus generic arity). A null result with
    /// <paramref name="ambiguousCandidates"/> populated means the simple name has
    /// multiple candidates and no local/using match. A non-null simple name means
    /// the interface is external/unresolved (e.g. MediatR.IRequest).
    /// </summary>
    public string? ResolveInterfaceReference(
        string name,
        string currentNamespace,
        IReadOnlyList<UsingDirective> usings,
        IReadOnlyList<string> containingTypes,
        int arity,
        out IReadOnlyList<string> ambiguousCandidates)
    {
        var declaration = ResolveInterfaceDecl(
            name, currentNamespace, usings, containingTypes, arity, out ambiguousCandidates);
        if (declaration is not null)
            return CSharpScanner.TypeIdentity(declaration.FullName, declaration.TypeParameters.Count);

        return ambiguousCandidates.Count > 0
            ? null
            : CSharpScanner.SimpleName(name);
    }

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
        => ResolveClassDecl(
            name, currentNamespace, usings, containingTypes, arity, out ambiguousCandidates);

    private T? Resolve<T>(
        string name,
        string currentNamespace,
        IReadOnlyList<UsingDirective> usings,
        IReadOnlyList<string> containingTypes,
        int arity,
        Func<string, T?> findFull,
        Func<string, IReadOnlyList<T>> findSimple,
        Func<T, string> fullNameOf,
        out IReadOnlyList<string> ambiguousCandidates)
        where T : class
    {
        ambiguousCandidates = Array.Empty<string>();

        var explicitGlobal = name.StartsWith("global::", StringComparison.Ordinal);
        if (explicitGlobal) name = name[8..];
        name = name.Trim();
        if (name.Length == 0) return null;

        var candidates = new List<string>();
        AddAliasCandidates(name, usings, candidates);

        if (explicitGlobal)
            return findFull(CSharpScanner.TypeIdentity(name, arity));

        AddScopeCandidates(name, currentNamespace, containingTypes, candidates);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            if (!seen.Add(candidate)) continue;
            var hit = findFull(CSharpScanner.TypeIdentity(candidate, arity));
            if (hit is not null)
                return hit;
        }

        // Using namespaces are considered together. Two matches is a genuine
        // ambiguity; picking the first would silently link the wrong contract.
        var usingMatches = new List<string>();
        foreach (var ns in GetUsingNamespaces(usings))
        {
            var candidate = $"{ns}.{name}";
            if (findFull(CSharpScanner.TypeIdentity(candidate, arity)) is not null &&
                !usingMatches.Contains(candidate, StringComparer.Ordinal))
                usingMatches.Add(candidate);
        }

        if (usingMatches.Count == 1)
            return findFull(CSharpScanner.TypeIdentity(usingMatches[0], arity));
        if (usingMatches.Count > 1)
        {
            ambiguousCandidates = usingMatches.OrderBy(c => c, StringComparer.Ordinal).ToList();
            return null;
        }

        var simple = CSharpScanner.SimpleName(name);
        var simpleKey = CSharpScanner.TypeIdentity(simple, arity);
        var simpleCandidates = findSimple(simpleKey);
        if (simpleCandidates.Count == 0) return null;
        if (simpleCandidates.Count == 1)
            return simpleCandidates[0];

        ambiguousCandidates = simpleCandidates
            .Select(fullNameOf)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();
        return null;
    }

    /// <summary>
    /// The namespace imports implied by a using list, flattened once per list instance.
    /// Each file shares one using list across its declarations, so this removes the
    /// per-resolution iterator and interpolated-container allocation.
    /// </summary>
    private string[] GetUsingNamespaces(IReadOnlyList<UsingDirective> usings)
    {
        if (_usingNamespaces.TryGetValue(usings, out var cached))
            return cached;

        var namespaces = new List<string>();
        foreach (var directive in usings)
        {
            foreach (var ns in directive.NamespaceCandidates())
                namespaces.Add(ns);
        }

        var result = namespaces.ToArray();
        _usingNamespaces.AddOrUpdate(usings, result);
        return result;
    }

    private static void AddAliasCandidates(
        string name,
        IReadOnlyList<UsingDirective> usings,
        List<string> candidates)
    {
        foreach (var directive in usings)
        {
            if (directive.Alias is null) continue;

            var target = CSharpScanner.ParseTypeReference(directive.Target).Name;
            if (string.Equals(name, directive.Alias, StringComparison.Ordinal))
            {
                candidates.Add(target);
            }
            else if (name.StartsWith(directive.Alias + ".", StringComparison.Ordinal))
            {
                candidates.Add(target + name[directive.Alias.Length..]);
            }
        }
    }

    private static void AddScopeCandidates(
        string name,
        string currentNamespace,
        IReadOnlyList<string> containingTypes,
        List<string> candidates)
    {
        // Nested type lookup checks the containing type chain before namespaces. The
        // prefixes are sliced from the joined path instead of rebuilt with LINQ Take,
        // which allocated an iterator and two strings per prefix.
        if (containingTypes.Count > 0)
        {
            var joinedTypes = string.Join(".", containingTypes);
            var path = currentNamespace.Length == 0
                ? joinedTypes
                : string.Concat(currentNamespace, ".", joinedTypes);
            var end = path.Length;
            for (var count = containingTypes.Count; count >= 1; count--)
            {
                candidates.Add(string.Concat(path.AsSpan(0, end), ".", name));
                var dot = path.LastIndexOf('.', end - 1);
                if (dot < 0) break;
                end = dot;
            }
        }

        var namespaceEnd = currentNamespace.Length;
        while (namespaceEnd > 0)
        {
            candidates.Add(string.Concat(currentNamespace.AsSpan(0, namespaceEnd), ".", name));
            var dot = currentNamespace.LastIndexOf('.', namespaceEnd - 1);
            if (dot < 0) break;
            namespaceEnd = dot;
        }

        candidates.Add(name);
    }

    /// <summary>
    /// A cached resolution outcome. The ambiguous list is empty on a hit; sharing the
    /// same list across cache hits is safe because callers only read it.
    /// </summary>
    private readonly struct Lookup<T>
        where T : class
    {
        public Lookup(T? value, IReadOnlyList<string> ambiguous)
        {
            Value = value;
            Ambiguous = ambiguous;
        }

        public T? Value { get; }

        public IReadOnlyList<string> Ambiguous { get; }
    }

    private ResolveKey CreateKey(
        string name,
        string currentNamespace,
        IReadOnlyList<UsingDirective> usings,
        IReadOnlyList<string> containingTypes,
        int arity)
        => new(
            name, currentNamespace, usings, containingTypes, arity,
            HashUsings(usings), HashContainingTypes(containingTypes));

    private int HashUsings(IReadOnlyList<UsingDirective> usings)
    {
        if (ReferenceEquals(_lastUsings, usings)) return _lastUsingsHash;

        var hash = new HashCode();
        foreach (var directive in usings)
            hash.Add(directive);
        var value = hash.ToHashCode();
        _lastUsings = usings;
        _lastUsingsHash = value;
        return value;
    }

    private int HashContainingTypes(IReadOnlyList<string> containingTypes)
    {
        if (ReferenceEquals(_lastContaining, containingTypes)) return _lastContainingHash;

        var hash = new HashCode();
        foreach (var containing in containingTypes)
            hash.Add(containing, StringComparer.Ordinal);
        var value = hash.ToHashCode();
        _lastContaining = containingTypes;
        _lastContainingHash = value;
        return value;
    }

    /// <summary>
    /// Content-equality key for a resolution: the written reference plus the scope that
    /// affects it. Using directives compare by value (they are records) and containing
    /// types ordinally, so declarations in the same namespace/using set share entries.
    /// A shared list instance (one per file) short-circuits the content comparison; the
    /// hash is precomputed by the resolver.
    /// </summary>
    private readonly struct ResolveKey : IEquatable<ResolveKey>
    {
        private readonly string _name;
        private readonly string _currentNamespace;
        private readonly IReadOnlyList<UsingDirective> _usings;
        private readonly IReadOnlyList<string> _containingTypes;
        private readonly int _arity;
        private readonly int _usingsHash;
        private readonly int _containingHash;

        public ResolveKey(
            string name,
            string currentNamespace,
            IReadOnlyList<UsingDirective> usings,
            IReadOnlyList<string> containingTypes,
            int arity,
            int usingsHash,
            int containingHash)
        {
            _name = name;
            _currentNamespace = currentNamespace;
            _usings = usings;
            _containingTypes = containingTypes;
            _arity = arity;
            _usingsHash = usingsHash;
            _containingHash = containingHash;
        }

        public bool Equals(ResolveKey other)
        {
            if (_arity != other._arity ||
                !string.Equals(_name, other._name, StringComparison.Ordinal) ||
                !string.Equals(_currentNamespace, other._currentNamespace, StringComparison.Ordinal) ||
                _usingsHash != other._usingsHash ||
                _containingHash != other._containingHash)
                return false;

            // Same list instance (the common case: one using list per file) needs no content scan.
            if (!ReferenceEquals(_usings, other._usings))
            {
                if (_usings.Count != other._usings.Count) return false;
                for (var i = 0; i < _usings.Count; i++)
                {
                    if (!_usings[i].Equals(other._usings[i]))
                        return false;
                }
            }

            if (!ReferenceEquals(_containingTypes, other._containingTypes))
            {
                if (_containingTypes.Count != other._containingTypes.Count) return false;
                for (var i = 0; i < _containingTypes.Count; i++)
                {
                    if (!string.Equals(_containingTypes[i], other._containingTypes[i], StringComparison.Ordinal))
                        return false;
                }
            }

            return true;
        }

        public override bool Equals(object? obj) => obj is ResolveKey other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(_name, StringComparer.Ordinal);
            hash.Add(_currentNamespace, StringComparer.Ordinal);
            hash.Add(_arity);
            hash.Add(_usingsHash);
            hash.Add(_containingHash);
            return hash.ToHashCode();
        }
    }
}
