using System.Text.RegularExpressions;

namespace CqrsLens;

/// <summary>
/// A <c>using</c> directive captured from one source file. Text mode uses these
/// to choose between same-named contracts in different namespaces, which is
/// common in modular solutions.
/// </summary>
public sealed record UsingDirective(
    string Target,
    string? Alias = null,
    bool IsStatic = false,
    bool IsGlobal = false,
    string? ContainerNamespace = null)
{
    /// <summary>
    /// Namespace imports implied by this directive, relative namespace first
    /// (C# looks there first) and the written/global name second.
    /// </summary>
    public IEnumerable<string> NamespaceCandidates()
    {
        if (IsStatic || Alias is not null || Target.Length == 0 || Target.Contains('<'))
            yield break;

        if (!string.IsNullOrEmpty(ContainerNamespace))
            yield return $"{ContainerNamespace}.{Target}";
        yield return Target;
    }

    /// <summary>Renders this directive as a file-scoped C# <c>using</c> statement.</summary>
    public string ToUsingDeclaration()
    {
        if (IsStatic)
            return $"using static {Target};";
        if (!string.IsNullOrEmpty(Alias))
            return $"using {Alias} = {Target};";
        return $"using {Target};";
    }
}

/// <summary>
/// Captures using directives without treating <c>using</c> statements, strings
/// or comments as imports. Namespace qualification is deliberately best-effort:
/// a later resolver tries both the relative and absolute forms.
/// </summary>
internal static partial class UsingDirectiveParser
{
    [GeneratedRegex(
        @"(?m)^[ \t]*(?<global>global[ \t]+)?using[ \t]+(?<static>static[ \t]+)?(?:(?<alias>[A-Za-z_]\w*)[ \t]*=[ \t]*)?(?<target>(?:global::)?[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*(?:[ \t]*<[^;{}]*>)?)[ \t]*;")]
    private static partial Regex UsingRegex();

    public static List<UsingDirective> Extract(string code, SourceMask mask, NamespaceMap namespaces)
    {
        var result = new List<UsingDirective>();
        foreach (Match match in UsingRegex().Matches(code))
        {
            if (!mask.IsCode(match.Index)) continue;

            var target = match.Groups["target"].Value.Trim();
            if (target.StartsWith("global::", StringComparison.Ordinal))
                target = target[8..];

            var alias = match.Groups["alias"].Success
                ? match.Groups["alias"].Value
                : null;
            result.Add(new UsingDirective(
                target,
                alias,
                IsStatic: match.Groups["static"].Success,
                IsGlobal: match.Groups["global"].Success,
                ContainerNamespace: namespaces.NamespaceAt(match.Index)));
        }

        return result;
    }

    /// <summary>
    /// Replaces the <c>global</c> keyword in <c>global using</c> directives with spaces of
    /// identical length so the directive becomes file-scoped without shifting any line or
    /// column offsets in the syntax tree.
    /// </summary>
    public static string StripGlobalUsingKeywords(string code)
    {
        if (!code.Contains("global", StringComparison.Ordinal))
            return code;

        var mask = SourceMask.Build(code);
        char[]? chars = null;
        foreach (Match match in UsingRegex().Matches(code))
        {
            if (!mask.IsCode(match.Index)) continue;
            var globalGroup = match.Groups["global"];
            if (!globalGroup.Success) continue;

            chars ??= code.ToCharArray();
            Array.Fill(chars, ' ', globalGroup.Index, globalGroup.Length);
        }

        return chars is null ? code : new string(chars);
    }
}
