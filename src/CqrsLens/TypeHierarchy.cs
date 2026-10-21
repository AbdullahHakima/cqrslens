using System.Text;

namespace CqrsLens;

/// <summary>
/// Resolves shape contracts through source-declared generic interface/base-class
/// chains. Text mode has no semantic model, so this intentionally handles the
/// common single-inheritance substitution cases (including reordered and fixed
/// generic arguments) and stops on ambiguous names rather than guessing.
/// </summary>
internal sealed class TypeHierarchy
{
    private readonly Dictionary<string, CSharpScanner.ClassDecl> _classesByFull;
    private readonly Dictionary<string, List<CSharpScanner.ClassDecl>> _classesBySimple;
    private readonly Dictionary<string, CSharpScanner.InterfaceDecl> _interfacesByFull;
    private readonly Dictionary<string, List<CSharpScanner.InterfaceDecl>> _interfacesBySimple;

    public TypeHierarchy(
        IEnumerable<CSharpScanner.ClassDecl> classes,
        IEnumerable<CSharpScanner.InterfaceDecl> interfaces)
    {
        _classesByFull = new Dictionary<string, CSharpScanner.ClassDecl>(StringComparer.Ordinal);
        _classesBySimple = new Dictionary<string, List<CSharpScanner.ClassDecl>>(StringComparer.Ordinal);
        foreach (var decl in classes)
        {
            _classesByFull.TryAdd(decl.FullName, decl);
            AddBySimple(_classesBySimple, CSharpScanner.SimpleName(decl.Name), decl);
        }

        _interfacesByFull = new Dictionary<string, CSharpScanner.InterfaceDecl>(StringComparer.Ordinal);
        _interfacesBySimple = new Dictionary<string, List<CSharpScanner.InterfaceDecl>>(StringComparer.Ordinal);
        foreach (var decl in interfaces)
        {
            _interfacesByFull.TryAdd(decl.FullName, decl);
            AddBySimple(_interfacesBySimple, CSharpScanner.SimpleName(decl.Name), decl);
        }
    }

    private static void AddBySimple<T>(Dictionary<string, List<T>> map, string key, T value)
    {
        if (!map.TryGetValue(key, out var list))
            map[key] = list = new List<T>();
        list.Add(value);
    }

    /// <summary>
    /// Resolves the first generic argument of a shape root (e.g. IRequestHandler or
    /// AbstractValidator) reached from <paramref name="basePart"/>.
    /// </summary>
    public bool TryResolveShapeTarget(
        string currentNamespace,
        string basePart,
        IReadOnlySet<string> rootNames,
        out string target)
    {
        target = "";
        var (name, args) = CSharpScanner.ParseTypeReference(basePart);
        if (string.IsNullOrWhiteSpace(name)) return false;

        var interfaceDecl = ResolveInterface(name, currentNamespace);
        if (interfaceDecl is not null)
        {
            var bindings = Bind(interfaceDecl.TypeParameters, args, EmptyBindings);
            if (ResolveInInterface(interfaceDecl, bindings, rootNames, new HashSet<string>(StringComparer.Ordinal), 0, out target))
                return true;
        }

        var classDecl = ResolveClass(name, currentNamespace);
        if (classDecl is not null)
        {
            var bindings = Bind(classDecl.TypeParameters, args, EmptyBindings);
            if (ResolveInClass(classDecl, bindings, rootNames, new HashSet<string>(StringComparer.Ordinal), 0, out target))
                return true;
        }

        // External/unresolved contract: a direct root can still yield its first argument.
        if (rootNames.Contains(CSharpScanner.SimpleName(name)) && args.Count > 0)
        {
            target = args[0];
            return true;
        }

        return false;
    }

    private bool ResolveInInterface(
        CSharpScanner.InterfaceDecl decl,
        IReadOnlyDictionary<string, string> bindings,
        IReadOnlySet<string> rootNames,
        HashSet<string> visited,
        int depth,
        out string target)
    {
        target = "";
        if (depth > 32 || !visited.Add(VisitKey(decl.FullName, bindings))) return false;

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

            var baseDecl = ResolveInterface(name, decl.Namespace);
            if (baseDecl is null) continue;
            var baseBindings = Bind(baseDecl.TypeParameters, args, EmptyBindings);
            if (ResolveInInterface(baseDecl, baseBindings, rootNames, visited, depth + 1, out target))
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
        out string target)
    {
        target = "";
        if (depth > 32 || !visited.Add(VisitKey(decl.FullName, bindings))) return false;

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

            var baseInterface = ResolveInterface(name, decl.Namespace);
            if (baseInterface is not null)
            {
                var interfaceBindings = Bind(baseInterface.TypeParameters, args, EmptyBindings);
                if (ResolveInInterface(baseInterface, interfaceBindings, rootNames, visited, depth + 1, out target))
                    return true;
            }

            var baseClass = ResolveClass(name, decl.Namespace);
            if (baseClass is null) continue;
            var classBindings = Bind(baseClass.TypeParameters, args, EmptyBindings);
            if (ResolveInClass(baseClass, classBindings, rootNames, visited, depth + 1, out target))
                return true;
        }

        return false;
    }

    private CSharpScanner.InterfaceDecl? ResolveInterface(string name, string currentNamespace)
    {
        if (name.Contains('.'))
        {
            if (_interfacesByFull.TryGetValue(name, out var exact)) return exact;
            var local = string.IsNullOrEmpty(currentNamespace) ? name : $"{currentNamespace}.{name}";
            if (_interfacesByFull.TryGetValue(local, out var localHit)) return localHit;
        }
        else
        {
            var local = string.IsNullOrEmpty(currentNamespace) ? name : $"{currentNamespace}.{name}";
            if (_interfacesByFull.TryGetValue(local, out var localHit)) return localHit;
        }

        return _interfacesBySimple.TryGetValue(CSharpScanner.SimpleName(name), out var candidates) &&
               candidates.Count == 1
            ? candidates[0]
            : null;
    }

    private CSharpScanner.ClassDecl? ResolveClass(string name, string currentNamespace)
    {
        if (name.Contains('.'))
        {
            if (_classesByFull.TryGetValue(name, out var exact)) return exact;
            var local = string.IsNullOrEmpty(currentNamespace) ? name : $"{currentNamespace}.{name}";
            if (_classesByFull.TryGetValue(local, out var localHit)) return localHit;
        }
        else
        {
            var local = string.IsNullOrEmpty(currentNamespace) ? name : $"{currentNamespace}.{name}";
            if (_classesByFull.TryGetValue(local, out var localHit)) return localHit;
        }

        return _classesBySimple.TryGetValue(CSharpScanner.SimpleName(name), out var candidates) &&
               candidates.Count == 1
            ? candidates[0]
            : null;
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

    private static string VisitKey(string fullName, IReadOnlyDictionary<string, string> bindings)
        => bindings.Count == 0
            ? fullName
            : fullName + "|" + string.Join(",", bindings.OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => $"{p.Key}={p.Value}"));

    private static IEnumerable<string> SplitBaseList(string baseList)
        => CSharpScanner.SplitTopLevel(baseList);

    private static readonly Dictionary<string, string> EmptyBindings =
        new(StringComparer.Ordinal);
}
