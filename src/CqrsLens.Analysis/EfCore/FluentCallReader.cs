using System.Text.RegularExpressions;

namespace CqrsLens;

/// <summary>
/// Tokenizes the Fluent API call chain of a statement and the member expressions passed to it.
/// </summary>
internal static class FluentCallReader
{
    private static readonly Regex CallRegex = new(
        @"\.\s*(?<name>[A-Za-z_]\w*)\s*(?:<(?<generic>[^<>]*)>)?\s*\((?<args>[^()]*(?:\([^()]*\)[^()]*)*)\)",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex EntityRegex = new(
        @"\bEntity\s*<\s*(?<entity>[^<>]+?)\s*>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex MemberAccessRegex = new(
        @"\.\s*(?<name>[A-Za-z_]\w*)",
        RegexOptions.Singleline | RegexOptions.Compiled);

    internal static IReadOnlyList<FluentCall> ParseCalls(string statement)
        => CallRegex.Matches(statement)
            .Cast<Match>()
            .Select(m => new FluentCall(
                m.Groups["name"].Value,
                m.Groups["generic"].Success ? m.Groups["generic"].Value : null,
                m.Groups["args"].Value))
            .ToList();

    /// <summary>Returns the entity written in an <c>Entity&lt;T&gt;()</c> call, if any.</summary>
    internal static string? ParseEntity(string statement)
    {
        var match = EntityRegex.Match(statement);
        return match.Success ? match.Groups["entity"].Value : null;
    }

    internal static string? ParseMember(string args)
    {
        var members = ParseMembers(args);
        return members.Count > 0 ? members[0] : null;
    }

    internal static IReadOnlyList<string> ParseMembers(string args)
    {
        var text = args.Trim();
        if (text.Length == 0) return Array.Empty<string>();

        var arrow = text.IndexOf("=>", StringComparison.Ordinal);
        if (arrow >= 0)
            text = text[(arrow + 2)..].Trim();

        foreach (Match quoted in Regex.Matches(text, @"""([^""]+)"""))
        {
            if (quoted.Groups[1].Value.Length > 0)
                return new[] { quoted.Groups[1].Value };
        }

        var members = MemberAccessRegex.Matches(text)
            .Cast<Match>()
            .Select(m => m.Groups["name"].Value)
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (members.Count > 0) return members;

        var bare = Regex.Match(text, @"^@?([A-Za-z_]\w*)$");
        return bare.Success ? new[] { bare.Groups[1].Value } : Array.Empty<string>();
    }
}
