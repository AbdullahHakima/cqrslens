using System.Text.RegularExpressions;

namespace CqrsLens;

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
