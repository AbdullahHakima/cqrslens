using System.Text;

namespace CqrsLens;

/// <summary>Generic parameter substitution and visited-state keys.</summary>
internal static class GenericBindings
{
    public static readonly IReadOnlyDictionary<string, string> Empty =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public static Dictionary<string, string> Bind(
        IReadOnlyList<string> parameters,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> outer)
    {
        if (parameters.Count == 0 || parameters.Count != arguments.Count)
            return new Dictionary<string, string>(Empty, StringComparer.Ordinal);

        var bindings = new Dictionary<string, string>(parameters.Count, StringComparer.Ordinal);
        for (var i = 0; i < parameters.Count; i++)
            bindings[parameters[i]] = Substitute(arguments[i], outer);
        return bindings;
    }

    public static string Substitute(string value, IReadOnlyDictionary<string, string> bindings)
    {
        if (bindings.Count == 0) return value;

        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length;)
        {
            var c = value[i];
            if (!char.IsLetter(c) && c != '_')
            {
                builder.Append(c);
                i++;
                continue;
            }

            var start = i++;
            while (i < value.Length && (char.IsLetterOrDigit(value[i]) || value[i] == '_')) i++;
            var token = value[start..i];
            builder.Append(bindings.TryGetValue(token, out var replacement) ? replacement : token);
        }
        return builder.ToString();
    }

    public static string VisitKey(
        string fullName,
        int arity,
        IReadOnlyDictionary<string, string> bindings)
    {
        var identity = CSharpScanner.TypeIdentity(fullName, arity);
        return bindings.Count == 0
            ? identity
            : identity + "|" + string.Join(",", bindings
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}={pair.Value}"));
    }
}
