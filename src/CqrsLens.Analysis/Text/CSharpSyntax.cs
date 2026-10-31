using System.Text.RegularExpressions;

namespace CqrsLens;

/// <summary>
/// Pure parsing primitives for C# type references and declaration text. No scanner state or
/// analysis result is touched here, which keeps these rules reusable by the text scanner,
/// Roslyn fallback readers and EF model builders.
/// </summary>
public static class CSharpSyntax
{
    /// <summary>The final segment of a possibly dotted name.</summary>
    public static string SimpleName(string name)
    {
        var idx = name.LastIndexOf('.');
        return idx >= 0 ? name[(idx + 1)..] : name;
    }

    /// <summary>
    /// Internal identity for a source type. Overloaded generic and non-generic
    /// declarations share the same C# name (CommandBase / CommandBase&lt;T&gt;), so
    /// the arity has to be part of identity when resolving references.
    /// </summary>
    internal static string TypeIdentity(string fullName, int arity)
        => arity == 0 ? fullName : $"{fullName}`{arity}";

    /// <summary>Marker name from an internal type identity (drops the `N arity suffix).</summary>
    internal static string SimpleNameFromIdentity(string name)
    {
        var simple = SimpleName(name);
        var tick = simple.LastIndexOf('`');
        return tick > 0 ? simple[..tick] : simple;
    }

    /// <summary>
    /// The simple type name, removing namespace, generic arguments and nullable
    /// annotations: <c>App.Queries.GetThing&lt;int&gt;?</c> → <c>GetThing</c>.
    /// </summary>
    public static string UnqualifiedTypeName(string name)
    {
        var value = StripGenericArguments(name.Trim());
        value = value.TrimEnd('?');
        value = value.TrimEnd('[', ']');
        var idx = value.LastIndexOf('.');
        return idx >= 0 ? value[(idx + 1)..] : value;
    }

    /// <summary>The namespace part of a type reference, or "" when it is unqualified.</summary>
    public static string NamespaceOfType(string name)
    {
        var value = StripGenericArguments(name.Trim());
        value = value.TrimEnd('?');
        value = value.TrimEnd('[', ']');
        var idx = value.LastIndexOf('.');
        return idx >= 0 ? value[..idx] : "";
    }

    private static string StripGenericArguments(string value)
    {
        if (value.StartsWith("global::", StringComparison.Ordinal))
            value = value[8..];
        var angle = value.IndexOf('<');
        return angle >= 0 ? value[..angle].Trim() : value;
    }

    /// <summary>The namespace part of a dotted name ("" when the name is simple).</summary>
    public static string NamespaceOf(string name)
    {
        var idx = name.LastIndexOf('.');
        return idx >= 0 ? name[..idx] : "";
    }

    /// <summary>Parses declared generic parameters, discarding variance markers.</summary>
    public static List<string> ParseTypeParameters(string? parameters)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(parameters)) return result;
        foreach (var raw in SplitTopLevel(parameters))
        {
            var name = raw.Trim();
            if (name.StartsWith("in ", StringComparison.Ordinal) ||
                name.StartsWith("out ", StringComparison.Ordinal))
                name = name[3..].Trim();
            if (name.Length > 0) result.Add(name);
        }
        return result;
    }

    /// <summary>Base-type names from a base list, generic arguments stripped.</summary>
    public static List<string> ExtractBaseTypeNames(string baseList)
    {
        var names = new List<string>();
        foreach (var part in SplitTopLevel(baseList))
        {
            var (raw, _) = ParseTypeReference(part);
            if (raw.Length > 0)
                names.Add(raw);
        }
        return names;
    }

    public static List<string> ExtractBaseRawNames(string baseList)
        => ExtractBaseTypeNames(baseList).Select(SimpleName).ToList();

    /// <summary>
    /// Splits a type reference into its name and top-level generic arguments:
    /// <c>App.IHandler&lt;Foo, Bar&lt;int&gt;&gt;</c> → name + ["Foo", "Bar&lt;int&gt;"].
    /// A primary-constructor base specification carries a base-call argument list
    /// (<c>DbContext(options)</c>, <c>IdentityDbContext&lt;TUser&gt;(options)</c>);
    /// that suffix is not part of the type name and is stripped first.
    /// </summary>
    public static (string Name, List<string> Arguments) ParseTypeReference(string typeReference)
    {
        var text = typeReference.Trim();
        if (text.StartsWith("global::", StringComparison.Ordinal))
            text = text[8..];

        text = StripTrailingArgumentList(text);

        var angle = text.IndexOf('<');
        if (angle < 0)
            return (text.Trim(), new List<string>());

        var name = text[..angle].Trim();
        var depth = 0;
        var start = angle + 1;
        for (var i = angle; i < text.Length; i++)
        {
            if (text[i] == '<') depth++;
            else if (text[i] == '>')
            {
                depth--;
                if (depth == 0)
                    return (name, SplitTopLevel(text.Substring(start, i - start)));
            }
        }
        return (name, new List<string>());
    }

    /// <summary>
    /// Removes a trailing parenthesized argument list from a type reference, e.g.
    /// <c>DbContext(options)</c> → <c>DbContext</c> and
    /// <c>IdentityDbContext&lt;AppUser&gt;(options)</c> → <c>IdentityDbContext&lt;AppUser&gt;</c>.
    /// A reference that is itself a tuple type (<c>(int, string)</c>) is left alone:
    /// its opening parenthesis has no type name in front of it.
    /// </summary>
    private static string StripTrailingArgumentList(string text)
    {
        if (!text.EndsWith(')')) return text;

        var depth = 0;
        for (var i = text.Length - 1; i >= 0; i--)
        {
            if (text[i] == ')') depth++;
            else if (text[i] == '(')
            {
                depth--;
                if (depth != 0) continue;
                return i == 0 ? text : text[..i].Trim();
            }
        }
        return text;
    }

    /// <summary>
    /// True when the declaration immediately preceding <paramref name="declarationIndex"/>
    /// carries the <c>abstract</c> modifier. Best-effort for line-wrapped modifiers.
    /// </summary>
    public static bool IsAbstractDeclaration(string code, int declarationIndex)
    {
        var lineStart = code.LastIndexOf('\n', declarationIndex);
        var prefix = code[(lineStart + 1)..declarationIndex];
        if (prefix.Contains("abstract", StringComparison.Ordinal)) return true;

        var start = Math.Max(0, declarationIndex - 300);
        var segment = code[start..declarationIndex];
        var boundary = segment.LastIndexOfAny(new[] { ';', '}' });
        if (boundary >= 0) segment = segment[(boundary + 1)..];
        return Regex.IsMatch(segment, @"\babstract\b");
    }

    /// <summary>
    /// Recognizes DbContext base types by their final type name. Fully-qualified
    /// <c>Microsoft.EntityFrameworkCore.DbContext</c> and custom <c>*DbContext</c> bases
    /// count, including primary-constructor base calls such as <c>DbContext(options)</c>;
    /// interface-like <c>IDbContext</c>/<c>IShopDbContext</c> names do not.
    /// </summary>
    public static bool IsDbContextBase(string baseList)
    {
        foreach (var part in SplitTopLevel(baseList))
        {
            var (name, _) = ParseTypeReference(part);
            var simple = SimpleName(name);
            if (simple.Length == 0) continue;

            // IShopDbContext / IDbContext are interfaces, not concrete context bases.
            if (simple.Length > 1 && simple[0] == 'I' && char.IsUpper(simple[1]) &&
                simple.EndsWith("DbContext", StringComparison.Ordinal))
                continue;

            if (simple == "DbContext" || simple.EndsWith("DbContext", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Extracts the top-level generic arguments of the first
    /// <paramref name="interfaceName"/> occurrence in a base list, e.g.
    /// IRequestHandler&lt;Foo, Bar&gt; -> ["Foo", "Bar"]. Returns null when absent.
    /// </summary>
    public static List<string>? ExtractGenericArguments(string baseList, string interfaceName)
        => ExtractAllGenericArguments(baseList, interfaceName).FirstOrDefault();

    /// <summary>
    /// Extracts the generic arguments of every occurrence of <paramref name="interfaceName"/>
    /// in a base list, preserving declaration order.
    /// </summary>
    public static List<List<string>> ExtractAllGenericArguments(string baseList, string interfaceName)
    {
        var results = new List<List<string>>();
        foreach (var part in SplitTopLevel(baseList))
        {
            var (name, args) = ParseTypeReference(part);
            if (args.Count == 0) continue;
            if (string.Equals(SimpleName(name), interfaceName, StringComparison.Ordinal))
                results.Add(args);
        }
        return results;
    }

    public static List<string> SplitTopLevel(string inner)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < inner.Length; i++)
        {
            var c = inner[i];
            // Parentheses and brackets nest too: a primary-constructor base call
            // (DbContext(options, flag)) or a tuple/array type argument must not
            // be split at its inner commas.
            if (c is '<' or '(' or '[') depth++;
            else if (c is '>' or ')' or ']') depth--;
            else if (c == ',' && depth == 0)
            {
                AddPart(parts, inner.AsSpan(start, i - start));
                start = i + 1;
            }
        }
        AddPart(parts, inner.AsSpan(start));
        return parts;

        static void AddPart(List<string> parts, ReadOnlySpan<char> part)
        {
            var trimmed = part.Trim();
            if (trimmed.Length > 0)
                parts.Add(trimmed.ToString());
        }
    }
}
