using System.Text.RegularExpressions;

namespace CqrsLens;

/// <summary>
/// Extracts closed-request target and generic constraint metadata for pipeline behaviors so
/// text and Roslyn scanners populate <see cref="BehaviorInfo"/> identically (F0 §4.3).
/// </summary>
internal static partial class BehaviorShapeHelper
{
    [GeneratedRegex(@"where\s+(?<param>[A-Za-z_]\w*)\s*:\s*(?<constraints>.+?)(?=\bwhere\b|\{|$)", RegexOptions.Singleline)]
    private static partial Regex WhereClauseRegex();

    internal static void PopulateFromHeader(
        BehaviorInfo behavior,
        string? headerText,
        string baseList,
        IReadOnlyList<string> typeParameters)
    {
        if (!TryExtractFirstPipelineArg(baseList, out var firstArg))
            return;

        var firstArgSimple = CSharpScanner.UnqualifiedTypeName(firstArg);
        if (!typeParameters.Contains(firstArgSimple, StringComparer.Ordinal))
        {
            behavior.ClosedRequestTarget = CSharpScanner.SimpleName(firstArg);
            var ns = CSharpScanner.NamespaceOfType(firstArg);
            behavior.ClosedRequestNamespace = ns.Length > 0 ? ns : null;
            return;
        }

        if (string.IsNullOrWhiteSpace(headerText))
            return;

        foreach (Match match in WhereClauseRegex().Matches(headerText))
        {
            if (!string.Equals(match.Groups["param"].Value, firstArgSimple, StringComparison.Ordinal))
                continue;

            var raw = match.Groups["constraints"].Value;
            foreach (var part in SplitTopLevel(raw))
            {
                var trimmed = part.Trim();
                if (trimmed.Length > 0 && !behavior.RequestTypeConstraints.Contains(trimmed, StringComparer.Ordinal))
                    behavior.RequestTypeConstraints.Add(trimmed);
            }
        }
    }

    private static bool TryExtractFirstPipelineArg(string baseList, out string firstArg)
    {
        firstArg = string.Empty;
        const string marker = "IPipelineBehavior<";
        var idx = baseList.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0)
            return false;

        var start = idx + marker.Length;
        var depth = 1;
        for (var i = start; i < baseList.Length; i++)
        {
            var ch = baseList[i];
            if (ch == '<')
                depth++;
            else if (ch == '>')
            {
                depth--;
                if (depth == 0)
                    break;
            }
            else if (ch == ',' && depth == 1)
            {
                firstArg = baseList[start..i].Trim();
                return firstArg.Length > 0;
            }
        }

        return false;
    }

    private static IEnumerable<string> SplitTopLevel(string text)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '<') depth++;
            else if (ch == '>' && depth > 0) depth--;
            else if (ch == ',' && depth == 0)
            {
                yield return text[start..i];
                start = i + 1;
            }
        }
        if (start < text.Length)
            yield return text[start..];
    }
}
