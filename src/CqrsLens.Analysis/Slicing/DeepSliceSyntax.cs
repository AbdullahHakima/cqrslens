using System.Text.RegularExpressions;

namespace CqrsLens;

/// <summary>
/// Small, behavior-preserving source-navigation helpers used by the deep-slice readers.
/// These are intentionally stateless: they answer one syntax question at a time and never
/// mutate the analysis result.
/// </summary>
internal static class DeepSliceSyntax
{
    internal static string? ExtractParameterType(string rawParameter)
    {
        var parameter = rawParameter.Trim();
        if (parameter.Length == 0) return null;

        while (parameter.StartsWith('['))
        {
            var close = parameter.IndexOf(']');
            if (close < 0) break;
            parameter = parameter[(close + 1)..].TrimStart();
        }

        var equals = parameter.IndexOf('=');
        if (equals >= 0)
            parameter = parameter[..equals].TrimEnd();

        parameter = Regex.Replace(parameter, @"^(?:(?:params|in|ref|out|this)\s+)+", "").Trim();
        var match = Regex.Match(parameter, @"^(?<type>.+?)\s+@?[A-Za-z_]\w*$");
        if (!match.Success) return null;

        var type = Regex.Replace(match.Groups["type"].Value.Trim(), @"\s+", " ");
        return type.Length > 0 ? type : null;
    }

    internal static string? ExtractParameterName(string rawParameter)
    {
        var parameter = rawParameter.Trim();
        var equals = parameter.IndexOf('=');
        if (equals >= 0)
            parameter = parameter[..equals].TrimEnd();

        var match = Regex.Match(parameter, @"\s@?(?<name>[A-Za-z_]\w*)$");
        return match.Success ? match.Groups["name"].Value : null;
    }

    /// <summary>End of a method body starting just after its parameter list.</summary>
    internal static int FindMethodEnd(string code, SourceMask mask, int afterParamsIndex, int maxEnd)
    {
        for (var i = afterParamsIndex; i < maxEnd; i++)
        {
            if (!mask.IsCode(i)) continue;
            if (code[i] == '{')
            {
                var close = mask.FindMatchingBrace(i);
                return close > i ? Math.Min(maxEnd, close + 1) : maxEnd;
            }
            if (code[i] == ';')
                return i + 1;
        }
        return maxEnd;
    }

    internal static int FindMatchingParen(string code, SourceMask mask, int openParenIndex)
    {
        var depth = 0;
        for (var i = openParenIndex; i < code.Length; i++)
        {
            if (!mask.IsCode(i)) continue;
            if (code[i] == '(') depth++;
            else if (code[i] == ')')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// The class whose body encloses <paramref name="index"/>, choosing the innermost one
    /// when classes are nested.
    /// </summary>
    internal static CSharpScanner.ClassDecl? InnermostClass(
        IReadOnlyList<CSharpScanner.ClassDecl> classes,
        int index)
    {
        CSharpScanner.ClassDecl? owner = null;
        foreach (var candidate in classes)
        {
            if (index < candidate.BodySpan.Start || index >= candidate.BodySpan.End)
                continue;
            if (owner is null || candidate.BodySpan.Start > owner.BodySpan.Start)
                owner = candidate;
        }
        return owner;
    }
}
