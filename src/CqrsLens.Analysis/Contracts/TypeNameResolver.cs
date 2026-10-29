namespace CqrsLens;

/// <summary>
/// Resolves written type references using the rules the text scanner needs:
/// containing types, the current/enclosing namespaces, using aliases and
/// namespaces, then a unique simple-name fallback. A simple name with multiple
/// candidates is reported as ambiguous instead of being guessed.
/// </summary>
internal sealed class TypeNameResolver
{
    private readonly TypeIndex _index;

    public TypeNameResolver(TypeIndex index) => _index = index;

    public CSharpScanner.InterfaceDecl? ResolveInterfaceDecl(
        string name,
        string currentNamespace,
        IReadOnlyList<UsingDirective> usings,
        IReadOnlyList<string> containingTypes,
        int arity,
        out IReadOnlyList<string> ambiguousCandidates)
        => Resolve(
            name,
            currentNamespace,
            usings,
            containingTypes,
            arity,
            identity => _index.TryGetInterface(identity, out var declaration) ? declaration : null,
            _index.InterfacesBySimple,
            declaration => declaration.FullName,
            out ambiguousCandidates);

    public CSharpScanner.ClassDecl? ResolveClassDecl(
        string name,
        string currentNamespace,
        IReadOnlyList<UsingDirective> usings,
        IReadOnlyList<string> containingTypes,
        int arity,
        out IReadOnlyList<string> ambiguousCandidates)
        => Resolve(
            name,
            currentNamespace,
            usings,
            containingTypes,
            arity,
            identity => _index.TryGetClass(identity, out var declaration) ? declaration : null,
            _index.ClassesBySimple,
            declaration => declaration.FullName,
            out ambiguousCandidates);

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

    private static T? Resolve<T>(
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

        foreach (var candidate in candidates.Distinct(StringComparer.Ordinal))
        {
            var hit = findFull(CSharpScanner.TypeIdentity(candidate, arity));
            if (hit is not null)
                return hit;
        }

        // Using namespaces are considered together. Two matches is a genuine
        // ambiguity; picking the first would silently link the wrong contract.
        var usingMatches = new List<string>();
        foreach (var directive in usings)
        {
            if (directive.IsStatic || directive.Alias is not null) continue;
            foreach (var ns in directive.NamespaceCandidates())
            {
                var candidate = $"{ns}.{name}";
                if (findFull(CSharpScanner.TypeIdentity(candidate, arity)) is not null &&
                    !usingMatches.Contains(candidate, StringComparer.Ordinal))
                    usingMatches.Add(candidate);
            }
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
        // Nested type lookup checks the containing type chain before namespaces.
        for (var count = containingTypes.Count; count >= 1; count--)
        {
            var typePath = string.Join(".", containingTypes.Take(count));
            candidates.Add(string.IsNullOrEmpty(currentNamespace)
                ? $"{typePath}.{name}"
                : $"{currentNamespace}.{typePath}.{name}");
        }

        if (!string.IsNullOrEmpty(currentNamespace))
        {
            var parts = currentNamespace.Split('.');
            for (var count = parts.Length; count >= 1; count--)
                candidates.Add($"{string.Join(".", parts.Take(count))}.{name}");
        }

        candidates.Add(name);
    }
}
