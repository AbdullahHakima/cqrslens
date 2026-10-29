namespace CqrsLens;

/// <summary>
/// Resolves entity, configuration target, navigation, and property names used by fluent
/// configuration back to analyzed declarations.
/// </summary>
internal static class FluentTypeResolver
{
    internal static string? ResolveConfigurationTarget(
        CSharpScanner.ClassDecl declaration,
        TypeHierarchy hierarchy)
    {
        if (!declaration.BaseList.Contains("IEntityTypeConfiguration", StringComparison.Ordinal))
            return null;

        foreach (var arguments in CSharpScanner.ExtractAllGenericArguments(
                     declaration.BaseList, "IEntityTypeConfiguration"))
        {
            if (arguments.Count == 0) continue;
            return ResolveType(arguments[0], declaration, hierarchy);
        }

        return null;
    }

    internal static string? ResolveType(
        string writtenType,
        CSharpScanner.ClassDecl context,
        TypeHierarchy hierarchy)
    {
        var normalized = EfAnnotations.NormalizeType(writtenType);
        if (normalized.EndsWith('?')) normalized = normalized[..^1];
        var (name, args) = CSharpScanner.ParseTypeReference(normalized);
        if (name.Length == 0) return null;
        var resolved = hierarchy.TryResolveClass(
            name,
            context.Namespace,
            context.Usings,
            context.ContainingTypes,
            args.Count,
            out _);
        return resolved?.FullName;
    }

    internal static CSharpScanner.ClassDecl? FindClass(
        TypeHierarchy hierarchy,
        string fullName,
        CSharpScanner.ClassDecl context,
        IReadOnlyDictionary<string, CSharpScanner.ClassDecl> classesByFull)
    {
        if (classesByFull.TryGetValue(fullName, out var direct))
            return direct;

        var simple = CSharpScanner.SimpleName(fullName);
        return hierarchy.TryResolveClass(
            simple,
            context.Namespace,
            context.Usings,
            context.ContainingTypes,
            0,
            out _);
    }

    internal static string? ResolveNavigationTarget(
        CSharpScanner.ClassDecl? owner,
        string navigation,
        TypeHierarchy hierarchy)
    {
        if (owner is null) return null;
        var property = FindProperty(owner, navigation, hierarchy);
        if (property is null) return null;
        var type = property.Value.Property.Type;
        if (EfAnnotations.TryGetCollectionElementType(type, out var element))
            type = element;
        return ResolveType(type, property.Value.Declaring, hierarchy);
    }

    internal static (CSharpScanner.PropertyDecl Property, CSharpScanner.ClassDecl Declaring)? FindProperty(
        CSharpScanner.ClassDecl? owner,
        string name,
        TypeHierarchy? hierarchy = null)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = owner;
        while (current is not null && seen.Add(current.FullName))
        {
            var match = current.Properties.FirstOrDefault(p =>
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return (match, current);

            if (hierarchy is null) break;
            current = FirstBaseClass(current, hierarchy);
        }

        return null;
    }

    private static CSharpScanner.ClassDecl? FirstBaseClass(
        CSharpScanner.ClassDecl declaration,
        TypeHierarchy hierarchy)
    {
        foreach (var part in CSharpScanner.SplitTopLevel(declaration.BaseList))
        {
            var (name, args) = CSharpScanner.ParseTypeReference(part);
            if (name.Length == 0) continue;
            var resolved = hierarchy.TryResolveClass(
                name,
                declaration.Namespace,
                declaration.Usings,
                declaration.ContainingTypes,
                args.Count,
                out _);
            if (resolved is not null)
                return resolved;
        }

        return null;
    }
}
