using System.Text.RegularExpressions;

namespace CqrsLens;

/// <summary>
/// Finds the member (method, constructor, property) that encloses a source position inside a
/// type body, so an expression can be attributed to the member that contains it.
/// </summary>
internal static class EnclosingMemberLocator
{
    internal static string? Find(
        string code,
        SourceMask mask,
        CSharpScanner.ClassDecl decl,
        int targetIndex)
    {
        // BodySpan starts at the declaration keyword, not at the body brace. Member headers must be
        // read from inside the body; otherwise the class brace itself is taken as the first "member"
        // and every event in the class is attributed to `ClassName` (i.e. to the constructor).
        var end = Math.Min(code.Length, decl.BodySpan.End);
        var open = FindBodyOpenBrace(code, mask, decl.BodySpan.Start, end);
        if (open < 0 || targetIndex <= open)
            return null;

        var start = open + 1;
        var headerStart = start;

        for (var i = start; i < end; i++)
        {
            if (!mask.IsCode(i)) continue;
            var ch = code[i];

            if (ch == '(')
            {
                var closeParen = DeepSliceSyntax.FindMatchingParen(code, mask, i);
                if (closeParen > i)
                    i = closeParen;
                continue;
            }

            if (ch == '{')
            {
                var closeBrace = mask.FindMatchingBrace(i);
                if (closeBrace <= i) break;

                if (targetIndex >= headerStart && targetIndex <= closeBrace)
                    return ExtractMemberNameFromHeader(code[headerStart..i]);

                i = closeBrace;
                headerStart = closeBrace + 1;
            }
            else if (ch == ';')
            {
                if (targetIndex >= headerStart && targetIndex <= i)
                    return ExtractMemberNameFromHeader(code[headerStart..i]);

                headerStart = i + 1;
            }
        }

        return null;
    }

    /// <summary>Index of a type's body <c>{</c>, skipping a primary-constructor parameter list; -1 when absent.</summary>
    private static int FindBodyOpenBrace(string code, SourceMask mask, int declarationStart, int end)
    {
        for (var i = declarationStart; i < end; i++)
        {
            if (!mask.IsCode(i)) continue;
            if (code[i] == '(')
            {
                var close = DeepSliceSyntax.FindMatchingParen(code, mask, i);
                if (close < i) return -1;
                i = close;
            }
            else if (code[i] == '{')
            {
                return i;
            }
            else if (code[i] == ';')
            {
                return -1; // body-less declaration: `record Foo(int X);`
            }
        }
        return -1;
    }

    private static string? ExtractMemberNameFromHeader(string rawHeader)
    {
        var header = rawHeader.Trim();
        if (header.Length == 0) return null;

        // Strip leading attributes [...]
        while (header.StartsWith('['))
        {
            var close = header.IndexOf(']');
            if (close < 0) break;
            header = header[(close + 1)..].TrimStart();
        }

        // If there is `=>` (expression-bodied member), keep only the signature before `=>`
        var arrow = header.IndexOf("=>", StringComparison.Ordinal);
        if (arrow > 0)
            header = header[..arrow].TrimEnd();

        // Method or constructor: take identifier before the first `(`
        var openParen = header.IndexOf('(');
        if (openParen > 0)
        {
            var beforeParen = header[..openParen].TrimEnd();
            var match = Regex.Match(beforeParen, @"(?<name>[A-Za-z_]\w*)\s*$");
            return match.Success ? match.Groups["name"].Value : null;
        }

        // Property: take the last identifier in the header
        var propMatch = Regex.Match(header, @"(?<name>[A-Za-z_]\w*)\s*$");
        return propMatch.Success ? propMatch.Groups["name"].Value : null;
    }
}
