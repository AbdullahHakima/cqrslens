using System.Text.RegularExpressions;

namespace CqrsLens;

/// <summary>
/// Locates configuration method bodies and splits them into statements while honoring
/// comments, strings, and other non-code regions.
/// </summary>
internal static class FluentSourceReader
{
    internal static SourceMask ResolveMask(
        SourceFile file,
        IReadOnlyDictionary<string, SourceMask>? masks)
        => masks is not null && masks.TryGetValue(file.Path, out var shared)
            ? shared
            : SourceMask.Build(file.Code);

    internal static (int Start, int End)? FindMethodBody(
        string code,
        SourceMask mask,
        (int Start, int End) classSpan,
        string methodName)
    {
        var regex = new Regex(@"\b" + Regex.Escape(methodName) + @"\s*\(", RegexOptions.Compiled);
        foreach (Match match in regex.Matches(code, classSpan.Start))
        {
            if (match.Index >= classSpan.End) break;
            if (!mask.IsCode(match.Index)) continue;

            var openParen = code.IndexOf('(', match.Index);
            if (openParen < 0) continue;
            var closeParen = FindMatching(code, mask, openParen, '(', ')');
            if (closeParen < 0) continue;
            var openBrace = code.IndexOf('{', closeParen + 1);
            if (openBrace < 0 || openBrace >= classSpan.End) continue;
            var closeBrace = mask.FindMatchingBrace(openBrace);
            if (closeBrace < 0) continue;
            return (openBrace + 1, closeBrace);
        }

        return null;
    }

    internal static IEnumerable<(string Text, int Index)> SplitStatements(
        string code,
        SourceMask mask,
        int start,
        int end)
    {
        var parens = 0;
        var braces = 0;
        var statementStart = start;
        for (var i = start; i < end && i < code.Length; i++)
        {
            if (!mask.IsCode(i)) continue;
            switch (code[i])
            {
                case '(':
                    parens++;
                    break;
                case ')':
                    if (parens > 0) parens--;
                    break;
                case '{':
                    braces++;
                    break;
                case '}':
                    if (braces > 0) braces--;
                    break;
                case ';' when parens == 0 && braces == 0:
                    yield return (code[statementStart..i], statementStart);
                    statementStart = i + 1;
                    break;
            }
        }

        if (statementStart < end)
            yield return (code[statementStart..end], statementStart);
    }

    private static int FindMatching(
        string code,
        SourceMask mask,
        int start,
        char open,
        char close)
    {
        var depth = 0;
        for (var i = start; i < code.Length; i++)
        {
            if (!mask.IsCode(i)) continue;
            if (code[i] == open) depth++;
            else if (code[i] == close)
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }
}
