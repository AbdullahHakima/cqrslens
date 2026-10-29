namespace CqrsLens;

/// <summary>
/// Composes CLR-style full names for a type declared inside a namespace and
/// zero or more containing types.
/// </summary>
internal static class TypeFullName
{
    public static string Compose(
        string namespaceName,
        IReadOnlyList<string> containingTypes,
        string name)
    {
        var typePath = containingTypes.Count == 0
            ? name
            : string.Join(".", containingTypes) + "." + name;

        return string.IsNullOrEmpty(namespaceName)
            ? typePath
            : $"{namespaceName}.{typePath}";
    }
}
