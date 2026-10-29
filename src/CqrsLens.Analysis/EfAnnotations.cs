using System.Text.RegularExpressions;

namespace CqrsLens;

/// <summary>
/// Small, shared helpers for reading EF Core data annotations and classifying
/// property types without a semantic model. Both text and Roslyn extraction use
/// the same rules so their ER models stay comparable.
/// </summary>
internal static class EfAnnotations
{
    private static readonly HashSet<string> CollectionTypeNames = new(StringComparer.Ordinal)
    {
        "List",
        "IList",
        "ICollection",
        "IEnumerable",
        "IReadOnlyList",
        "IReadOnlyCollection",
        "HashSet",
        "ISet",
        "Collection",
        "ObservableCollection",
    };

    private static readonly HashSet<string> ValueTypeNames = new(StringComparer.Ordinal)
    {
        "bool", "byte", "sbyte", "short", "ushort", "int", "uint", "long", "ulong",
        "float", "double", "decimal", "char", "Guid", "DateTime", "DateTimeOffset",
        "DateOnly", "TimeOnly", "TimeSpan", "byte[]",
    };

    public static bool HasAttribute(IEnumerable<string> attributes, string name)
        => attributes.Any(a => string.Equals(AttributeName(a), name, StringComparison.Ordinal) ||
                               string.Equals(AttributeName(a), name + "Attribute", StringComparison.Ordinal));

    public static string? AttributeArgument(IEnumerable<string> attributes, string name)
    {
        foreach (var attribute in attributes)
        {
            if (!string.Equals(AttributeName(attribute), name, StringComparison.Ordinal) &&
                !string.Equals(AttributeName(attribute), name + "Attribute", StringComparison.Ordinal))
                continue;

            var open = attribute.IndexOf('(');
            if (open < 0) return null;
            var close = attribute.LastIndexOf(')');
            var argument = close > open
                ? attribute[(open + 1)..close].Trim()
                : attribute[(open + 1)..].Trim();
            if (argument.Length == 0) return null;

            var comma = TopLevelComma(argument);
            if (comma >= 0) argument = argument[..comma].Trim();

            if (argument.StartsWith("nameof", StringComparison.Ordinal))
            {
                var nameOpen = argument.IndexOf('(');
                var nameClose = argument.LastIndexOf(')');
                if (nameOpen >= 0 && nameClose > nameOpen)
                    argument = argument[(nameOpen + 1)..nameClose].Trim();
            }

            argument = argument.Trim().Trim('"', '\'');
            var dot = argument.LastIndexOf('.');
            return dot >= 0 ? argument[(dot + 1)..] : argument;
        }

        return null;
    }

    public static string AttributeName(string attribute)
    {
        var text = attribute.Trim();
        if (text.StartsWith('[')) text = text[1..];
        if (text.EndsWith(']')) text = text[..^1];
        text = text.Trim();

        var colon = text.IndexOf(':');
        if (colon >= 0) text = text[(colon + 1)..].Trim();

        var open = text.IndexOf('(');
        if (open >= 0) text = text[..open].Trim();
        var dot = text.LastIndexOf('.');
        return dot >= 0 ? text[(dot + 1)..] : text;
    }

    /// <summary>Removes nullable annotations and whitespace from a written type.</summary>
    public static string NormalizeType(string? type)
        => Regex.Replace((type ?? "").Trim(), @"\s*([.<>\[\],?])\s*", "$1");

    public static bool IsNullable(string? type)
    {
        var value = NormalizeType(type);
        if (value.EndsWith('?')) return true;
        var (name, args) = CSharpScanner.ParseTypeReference(value);
        return args.Count == 1 &&
               string.Equals(CSharpScanner.SimpleName(name), "Nullable", StringComparison.Ordinal);
    }

    public static bool TryGetCollectionElementType(string? type, out string elementType)
    {
        elementType = "";
        var value = NormalizeType(type);
        if (value.EndsWith('?')) value = value[..^1];
        if (value.EndsWith("[]", StringComparison.Ordinal))
        {
            var candidate = value[..^2];
            if (string.Equals(CSharpScanner.SimpleName(candidate), "byte", StringComparison.OrdinalIgnoreCase))
                return false;
            elementType = candidate;
            return elementType.Length > 0;
        }

        var (name, args) = CSharpScanner.ParseTypeReference(value);
        if (args.Count != 1 || !CollectionTypeNames.Contains(CSharpScanner.SimpleName(name)))
            return false;

        elementType = args[0];
        return true;
    }

    public static bool IsLikelyValueType(string? type)
    {
        var value = CSharpScanner.UnqualifiedTypeName(type ?? "");
        return ValueTypeNames.Contains(value);
    }

    public static bool IsPublicScalarName(string? name)
        => !string.IsNullOrWhiteSpace(name);

    private static int TopLevelComma(string value)
    {
        var depth = 0;
        for (var i = 0; i < value.Length; i++)
        {
            switch (value[i])
            {
                case '(':
                case '<':
                case '[':
                    depth++;
                    break;
                case ')':
                case '>':
                case ']':
                    if (depth > 0) depth--;
                    break;
                case ',' when depth == 0:
                    return i;
            }
        }
        return -1;
    }
}
