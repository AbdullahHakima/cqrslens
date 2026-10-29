using System.Text;

namespace CqrsLens;

/// <summary>
/// Resolves shape contracts through source-declared generic interface/base-class
/// chains. Text mode has no semantic model, so this intentionally handles the
/// common single-inheritance substitution cases (including reordered and fixed
/// generic arguments) and stops on ambiguous names rather than guessing.
///
/// Resolution order mirrors C# closely enough for modular solutions: containing
/// types, the current/enclosing namespaces, using aliases and namespaces, then a
/// unique simple-name fallback. A simple name that still has multiple candidates
/// is reported as ambiguous instead of being guessed.
/// </summary>
internal sealed class TypeHierarchy
{
    private readonly Dictionary<string, CSharpScanner.ClassDecl> _classesByFull;
    private readonly Dictionary<string, List<CSharpScanner.ClassDecl>> _classesBySimple;
    private readonly Dictionary<string, CSharpScanner.InterfaceDecl> _interfacesByFull;
    private readonly Dictionary<string, List<CSharpScanner.InterfaceDecl>> _interfacesBySimple;
    private readonly HashSet<string> _interfaceSimpleNames;

    public TypeHierarchy(
        IEnumerable<CSharpScanner.ClassDecl> classes,
        IEnumerable<CSharpScanner.InterfaceDecl> interfaces)
    {
        _classesByFull = new Dictionary<string, CSharpScanner.ClassDecl>(StringComparer.Ordinal);
        _classesBySimple = new Dictionary<string, List<CSharpScanner.ClassDecl>>(StringComparer.Ordinal);
        foreach (var decl in classes)
        {
            var arity = decl.TypeParameters.Count;
            _classesByFull.TryAdd(CSharpScanner.TypeIdentity(decl.FullName, arity), decl);
            AddBySimple(
                _classesBySimple,
                CSharpScanner.TypeIdentity(CSharpScanner.SimpleName(decl.Name), arity),
                decl);
        }

        _interfacesByFull = new Dictionary<string, CSharpScanner.InterfaceDecl>(StringComparer.Ordinal);
        _interfacesBySimple = new Dictionary<string, List<CSharpScanner.InterfaceDecl>>(StringComparer.Ordinal);
        _interfaceSimpleNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var decl in interfaces)
        {
            var arity = decl.TypeParameters.Count;
            _interfacesByFull.TryAdd(CSharpScanner.TypeIdentity(decl.FullName, arity), decl);
            AddBySimple(
                _interfacesBySimple,
                CSharpScanner.TypeIdentity(CSharpScanner.SimpleName(decl.Name), arity),
                decl);
            _interfaceSimpleNames.Add(CSharpScanner.SimpleName(decl.Name));
        }
    }

    private static void AddBySimple<T>(Dictionary<string, List<T>> map, string key, T value)
    {
        if (!map.TryGetValue(key, out var list))
            map[key] = list = new List<T>();
        list.Add(value);
    }

    public bool HasInterface(string name)
        => _interfaceSimpleNames.Contains(CSharpScanner.SimpleNameFromIdentity(name));

    /// <summary>
    /// Follows a class's source-declared base classes and interfaces to find
    /// request roots (IRequest and configured markers). Unlike shape-target
    /// resolution this keeps every root it sees, so a request can be classified
    /// through a meaningful base class such as QueryBase&lt;T&gt;.
    /// </summary>
    public RequestShapeResolution ResolveRequestShape(
        CSharpScanner.ClassDecl decl,
        IReadOnlySet<string> markerRoots)
    {
        var shape = new RequestShapeResolution();
        VisitRequestClass(
            decl,
            EmptyBindings,
            markerRoots,
            new HashSet<string>(StringComparer.Ordinal),
            0,
            shape);
        return shape;
    }

    private void VisitRequestClass(
        CSharpScanner.ClassDecl decl,
        IReadOnlyDictionary<string, string> bindings,
        IReadOnlySet<string> markerRoots,
        HashSet<string> visited,
        int depth,
        RequestShapeResolution shape)
    {
        if (depth > 32 ||
            !visited.Add(VisitKey(decl.FullName, decl.TypeParameters.Count, bindings))) return;

        foreach (var part in SplitBaseList(decl.BaseList))
        {
            var (name, rawArgs) = CSharpScanner.ParseTypeReference(part);
            var args = rawArgs.Select(a => Substitute(a, bindings)).ToList();
            VisitRequestBase(
                name,
                args,
                decl.Namespace,
                decl.Usings,
                decl.ContainingTypes,
                markerRoots,
                visited,
                depth + 1,
                shape);
        }
    }

    private void VisitRequestInterface(
        CSharpScanner.InterfaceDecl decl,
        IReadOnlyDictionary<string, string> bindings,
        IReadOnlySet<string> markerRoots,
        HashSet<string> visited,
        int depth,
        RequestShapeResolution shape)
    {
        if (depth > 32 ||
            !visited.Add(VisitKey(decl.FullName, decl.TypeParameters.Count, bindings))) return;

        foreach (var part in SplitBaseList(decl.BaseList))
        {
            var (name, rawArgs) = CSharpScanner.ParseTypeReference(part);
            var args = rawArgs.Select(a => Substitute(a, bindings)).ToList();
            VisitRequestBase(
                name,
                args,
                decl.Namespace,
                decl.Usings,
                decl.ContainingTypes,
                markerRoots,
                visited,
                depth + 1,
                shape);
        }
    }

    private void VisitRequestBase(
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

        var interfaceDecl = ResolveInterface(
            name, currentNamespace, usings, containingTypes, args.Count,
            out var interfaceAmbiguous);
        AddRequestAmbiguities(shape, interfaceAmbiguous);
        if (interfaceDecl is not null)
        {
            if (isRoot) AddRequestRoot(shape, simple, args);
            var interfaceBindings = Bind(interfaceDecl.TypeParameters, args, EmptyBindings);
            VisitRequestInterface(
                interfaceDecl, interfaceBindings, markerRoots, visited, depth + 1, shape);
            return;
        }

        var classDecl = ResolveClass(
            name, currentNamespace, usings, containingTypes, args.Count,
            out var classAmbiguous);
        AddRequestAmbiguities(shape, classAmbiguous);
        if (classDecl is not null)
        {
            if (isRoot) AddRequestRoot(shape, simple, args);
            var classBindings = Bind(classDecl.TypeParameters, args, EmptyBindings);
            VisitRequestClass(
                classDecl, classBindings, markerRoots, visited, depth + 1, shape);
            return;
        }

        // An external marker/root is usable; an ambiguous source name is not.
        if (isRoot && interfaceAmbiguous.Count == 0 && classAmbiguous.Count == 0)
        {
            AddRequestRoot(shape, simple, args);
            return;
        }

        if (!isRoot && interfaceAmbiguous.Count == 0 && classAmbiguous.Count == 0 &&
            !shape.UnresolvedBases.Contains(simple, StringComparer.Ordinal))
            shape.UnresolvedBases.Add(simple);
    }

    private static void AddRequestRoot(
        RequestShapeResolution shape,
        string root,
        IReadOnlyList<string> args)
    {
        if (!shape.Roots.Contains(root, StringComparer.Ordinal))
            shape.Roots.Add(root);
        if (shape.ResponseType is null && args.Count > 0)
            shape.ResponseType = args[0];
    }

    private static void AddRequestAmbiguities(
        RequestShapeResolution shape,
        IReadOnlyList<string> candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!shape.AmbiguousCandidates.Contains(candidate, StringComparer.Ordinal))
                shape.AmbiguousCandidates.Add(candidate);
        }
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
        var decl = ResolveInterface(
            name, currentNamespace, usings, containingTypes, arity, out ambiguousCandidates);
        if (decl is not null)
            return CSharpScanner.TypeIdentity(decl.FullName, decl.TypeParameters.Count);

        return ambiguousCandidates.Count > 0
            ? null
            : CSharpScanner.SimpleName(name);
    }

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
    {
        target = "";
        ambiguousCandidates = Array.Empty<string>();

        var (name, args) = CSharpScanner.ParseTypeReference(basePart);
        if (string.IsNullOrWhiteSpace(name)) return false;

        var ambiguities = new List<string>();
        var interfaceDecl = ResolveInterface(
            name, currentNamespace, usings, containingTypes, args.Count,
            out var interfaceAmbiguous);
        AddAmbiguities(ambiguities, interfaceAmbiguous);
        if (interfaceDecl is not null)
        {
            var bindings = Bind(interfaceDecl.TypeParameters, args, EmptyBindings);
            if (ResolveInInterface(
                    interfaceDecl,
                    bindings,
                    rootNames,
                    new HashSet<string>(StringComparer.Ordinal),
                    0,
                    ambiguities,
                    out target))
                return true;
        }

        var classDecl = ResolveClass(
            name, currentNamespace, usings, containingTypes, args.Count,
            out var classAmbiguous);
        AddAmbiguities(ambiguities, classAmbiguous);
        if (classDecl is not null)
        {
            var bindings = Bind(classDecl.TypeParameters, args, EmptyBindings);
            if (ResolveInClass(
                    classDecl,
                    bindings,
                    rootNames,
                    new HashSet<string>(StringComparer.Ordinal),
                    0,
                    ambiguities,
                    out target))
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
        CSharpScanner.InterfaceDecl decl,
        IReadOnlyDictionary<string, string> bindings,
        IReadOnlySet<string> rootNames,
        HashSet<string> visited,
        int depth,
        List<string> ambiguities,
        out string target)
    {
        target = "";
        if (depth > 32 ||
            !visited.Add(VisitKey(decl.FullName, decl.TypeParameters.Count, bindings))) return false;

        foreach (var part in SplitBaseList(decl.BaseList))
        {
            var (name, rawArgs) = CSharpScanner.ParseTypeReference(part);
            var args = rawArgs.Select(a => Substitute(a, bindings)).ToList();
            var simple = CSharpScanner.SimpleName(name);

            if (rootNames.Contains(simple) && args.Count > 0)
            {
                target = args[0];
                return true;
            }

            var baseDecl = ResolveInterface(
                name, decl.Namespace, decl.Usings, decl.ContainingTypes, args.Count,
                out var baseAmbiguous);
            AddAmbiguities(ambiguities, baseAmbiguous);
            if (baseDecl is null) continue;
            var baseBindings = Bind(baseDecl.TypeParameters, args, EmptyBindings);
            if (ResolveInInterface(
                    baseDecl, baseBindings, rootNames, visited, depth + 1, ambiguities, out target))
                return true;
        }

        return false;
    }

    private bool ResolveInClass(
        CSharpScanner.ClassDecl decl,
        IReadOnlyDictionary<string, string> bindings,
        IReadOnlySet<string> rootNames,
        HashSet<string> visited,
        int depth,
        List<string> ambiguities,
        out string target)
    {
        target = "";
        if (depth > 32 ||
            !visited.Add(VisitKey(decl.FullName, decl.TypeParameters.Count, bindings))) return false;

        foreach (var part in SplitBaseList(decl.BaseList))
        {
            var (name, rawArgs) = CSharpScanner.ParseTypeReference(part);
            var args = rawArgs.Select(a => Substitute(a, bindings)).ToList();
            var simple = CSharpScanner.SimpleName(name);

            if (rootNames.Contains(simple) && args.Count > 0)
            {
                target = args[0];
                return true;
            }

            var baseInterface = ResolveInterface(
                name, decl.Namespace, decl.Usings, decl.ContainingTypes, args.Count,
                out var interfaceAmbiguous);
            AddAmbiguities(ambiguities, interfaceAmbiguous);
            if (baseInterface is not null)
            {
                var interfaceBindings = Bind(baseInterface.TypeParameters, args, EmptyBindings);
                if (ResolveInInterface(
                        baseInterface, interfaceBindings, rootNames, visited, depth + 1,
                        ambiguities, out target))
                    return true;
            }

            var baseClass = ResolveClass(
                name, decl.Namespace, decl.Usings, decl.ContainingTypes, args.Count,
                out var classAmbiguous);
            AddAmbiguities(ambiguities, classAmbiguous);
            if (baseClass is null) continue;
            var classBindings = Bind(baseClass.TypeParameters, args, EmptyBindings);
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

    private CSharpScanner.InterfaceDecl? ResolveInterface(
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
            _interfacesByFull,
            _interfacesBySimple,
            out ambiguousCandidates);

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
        => ResolveClass(
            name,
            currentNamespace,
            usings,
            containingTypes,
            arity,
            out ambiguousCandidates);

    private CSharpScanner.ClassDecl? ResolveClass(
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
            _classesByFull,
            _classesBySimple,
            out ambiguousCandidates);

    private static T? Resolve<T>(
        string name,
        string currentNamespace,
        IReadOnlyList<UsingDirective> usings,
        IReadOnlyList<string> containingTypes,
        int arity,
        IReadOnlyDictionary<string, T> byFull,
        IReadOnlyDictionary<string, List<T>> bySimple,
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
        {
            return byFull.TryGetValue(
                CSharpScanner.TypeIdentity(name, arity), out var globalHit)
                ? globalHit
                : null;
        }

        AddScopeCandidates(name, currentNamespace, containingTypes, candidates);

        foreach (var candidate in candidates.Distinct(StringComparer.Ordinal))
        {
            if (byFull.TryGetValue(CSharpScanner.TypeIdentity(candidate, arity), out var hit))
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
                if (byFull.ContainsKey(CSharpScanner.TypeIdentity(candidate, arity)) &&
                    !usingMatches.Contains(candidate, StringComparer.Ordinal))
                    usingMatches.Add(candidate);
            }
        }

        if (usingMatches.Count == 1)
            return byFull[CSharpScanner.TypeIdentity(usingMatches[0], arity)];
        if (usingMatches.Count > 1)
        {
            ambiguousCandidates = usingMatches.OrderBy(c => c, StringComparer.Ordinal).ToList();
            return null;
        }

        var simple = CSharpScanner.SimpleName(name);
        var simpleKey = CSharpScanner.TypeIdentity(simple, arity);
        if (bySimple.TryGetValue(simpleKey, out var simpleCandidates) && simpleCandidates.Count > 0)
        {
            if (simpleCandidates.Count == 1)
                return simpleCandidates[0];

            ambiguousCandidates = simpleCandidates
                .Select(c => FullNameOf(c))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(c => c, StringComparer.Ordinal)
                .ToList();
        }

        return null;
    }

    private static string FullNameOf<T>(T value)
        => value switch
        {
            CSharpScanner.ClassDecl classDecl => classDecl.FullName,
            CSharpScanner.InterfaceDecl interfaceDecl => interfaceDecl.FullName,
            _ => value?.ToString() ?? "",
        };

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

    private static Dictionary<string, string> Bind(
        IReadOnlyList<string> parameters,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> outer)
    {
        if (parameters.Count == 0 || parameters.Count != arguments.Count)
            return EmptyBindings;

        var bindings = new Dictionary<string, string>(parameters.Count, StringComparer.Ordinal);
        for (var i = 0; i < parameters.Count; i++)
            bindings[parameters[i]] = Substitute(arguments[i], outer);
        return bindings;
    }

    private static string Substitute(string value, IReadOnlyDictionary<string, string> bindings)
    {
        if (bindings.Count == 0) return value;

        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length;)
        {
            var c = value[i];
            if (!char.IsLetter(c) && c != '_')
            {
                sb.Append(c);
                i++;
                continue;
            }

            var start = i++;
            while (i < value.Length && (char.IsLetterOrDigit(value[i]) || value[i] == '_')) i++;
            var token = value[start..i];
            sb.Append(bindings.TryGetValue(token, out var replacement) ? replacement : token);
        }
        return sb.ToString();
    }

    private static string VisitKey(
        string fullName,
        int arity,
        IReadOnlyDictionary<string, string> bindings)
    {
        var identity = CSharpScanner.TypeIdentity(fullName, arity);
        return bindings.Count == 0
            ? identity
            : identity + "|" + string.Join(",", bindings
                .OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => $"{p.Key}={p.Value}"));
    }

    private static IEnumerable<string> SplitBaseList(string baseList)
        => CSharpScanner.SplitTopLevel(baseList);

    private static readonly Dictionary<string, string> EmptyBindings =
        new(StringComparer.Ordinal);
}

/// <summary>Result of following a class's base classes/interfaces for request roots.</summary>
internal sealed class RequestShapeResolution
{
    public List<string> Roots { get; } = new();
    public string? ResponseType { get; set; }
    public List<string> AmbiguousCandidates { get; } = new();
    public List<string> UnresolvedBases { get; } = new();
}
