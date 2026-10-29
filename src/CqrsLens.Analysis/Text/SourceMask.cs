namespace CqrsLens;

/// <summary>
/// A code/string/comment mask for a C# source file. Brace matching and shape
/// discovery must never treat braces or keywords inside literals/comments as code.
/// This is intentionally a small lexer, not a full C# parser.
/// </summary>
internal sealed class SourceMask
{
    private readonly string _code;
    private readonly bool[] _isCode;

    private SourceMask(string code, bool[] isCode)
    {
        _code = code;
        _isCode = isCode;
    }

    public bool IsCode(int index) => index >= 0 && index < _isCode.Length && _isCode[index];

    public int FindMatchingBrace(int openBraceIndex)
    {
        if (!IsCode(openBraceIndex) || _code[openBraceIndex] != '{') return -1;
        var depth = 0;
        for (var i = openBraceIndex; i < _code.Length; i++)
        {
            if (!_isCode[i]) continue;
            if (_code[i] == '{') depth++;
            else if (_code[i] == '}')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    public static SourceMask Build(string code)
    {
        var isCode = new bool[code.Length];
        Array.Fill(isCode, true);

        for (var i = 0; i < code.Length;)
        {
            if (StartsWith(code, i, "//"))
            {
                var end = code.IndexOf('\n', i);
                if (end < 0) end = code.Length;
                Mark(isCode, i, end);
                i = end;
                continue;
            }

            if (StartsWith(code, i, "/*"))
            {
                var end = code.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? code.Length : end + 2;
                Mark(isCode, i, end);
                i = end;
                continue;
            }

            if (TrySkipLiteral(code, i, out var literalEnd))
            {
                Mark(isCode, i, literalEnd);
                i = literalEnd;
                continue;
            }

            i++;
        }

        return new SourceMask(code, isCode);
    }

    private static void Mark(bool[] isCode, int start, int end)
    {
        var from = Math.Max(0, start);
        var to = Math.Min(isCode.Length, end);
        for (var i = from; i < to; i++) isCode[i] = false;
    }

    private static bool StartsWith(string text, int index, string value)
        => index >= 0 && index + value.Length <= text.Length &&
           string.CompareOrdinal(text, index, value, 0, value.Length) == 0;

    private static int CountQuotes(string code, int index)
    {
        var count = 0;
        while (index + count < code.Length && code[index + count] == '"') count++;
        return count;
    }

    private static bool TrySkipLiteral(string code, int index, out int end)
    {
        end = index;
        var c = code[index];

        // Raw strings: """...""" (optionally prefixed by $ / $$ / @).
        if (c == '"')
        {
            var quotes = CountQuotes(code, index);
            if (quotes >= 3)
            {
                end = SkipRawString(code, index, quotes);
                return true;
            }
            end = SkipQuotedString(code, index, verbatim: false, interpolated: false);
            return true;
        }

        if (c == '\'')
        {
            end = SkipCharLiteral(code, index);
            return true;
        }

        if (c == '@' && index + 1 < code.Length && code[index + 1] == '"')
        {
            end = SkipQuotedString(code, index + 1, verbatim: true, interpolated: false);
            return true;
        }

        if (c == '$' && index + 1 < code.Length && code[index + 1] == '"')
        {
            var quoteIndex = index + 1;
            var quotes = CountQuotes(code, quoteIndex);
            if (quotes >= 3)
            {
                end = SkipRawString(code, quoteIndex, quotes);
                return true;
            }
            end = SkipQuotedString(code, quoteIndex, verbatim: false, interpolated: true);
            return true;
        }

        if (c == '$' && index + 2 < code.Length && code[index + 1] == '@' && code[index + 2] == '"')
        {
            end = SkipQuotedString(code, index + 2, verbatim: true, interpolated: true);
            return true;
        }

        if (c == '@' && index + 2 < code.Length && code[index + 1] == '$' && code[index + 2] == '"')
        {
            end = SkipQuotedString(code, index + 2, verbatim: true, interpolated: true);
            return true;
        }

        return false;
    }

    private static int SkipRawString(string code, int quoteIndex, int quoteCount)
    {
        var i = quoteIndex + quoteCount;
        while (i < code.Length)
        {
            if (code[i] == '"' && CountQuotes(code, i) >= quoteCount)
                return i + quoteCount;
            i++;
        }
        return code.Length;
    }

    private static int SkipCharLiteral(string code, int quoteIndex)
    {
        var i = quoteIndex + 1;
        while (i < code.Length)
        {
            if (code[i] == '\\')
            {
                i += 2;
                continue;
            }
            if (code[i] == '\'' || code[i] == '\n' || code[i] == '\r')
                return i < code.Length && code[i] == '\'' ? i + 1 : i;
            i++;
        }
        return code.Length;
    }

    private static int SkipQuotedString(string code, int quoteIndex, bool verbatim, bool interpolated)
    {
        var i = quoteIndex + 1;
        while (i < code.Length)
        {
            var c = code[i];

            if (c == '"')
            {
                if (verbatim && i + 1 < code.Length && code[i + 1] == '"')
                {
                    i += 2;
                    continue;
                }
                return i + 1;
            }

            if (!verbatim && c == '\\')
            {
                i += 2;
                continue;
            }

            if (interpolated && c == '{')
            {
                if (i + 1 < code.Length && code[i + 1] == '{')
                {
                    i += 2;
                    continue;
                }
                i = SkipInterpolationExpression(code, i);
                continue;
            }

            i++;
        }
        return code.Length;
    }

    /// <summary>Skips a {...} interpolation expression, including nested strings/comments.</summary>
    private static int SkipInterpolationExpression(string code, int openBrace)
    {
        var depth = 0;
        for (var i = openBrace; i < code.Length;)
        {
            if (StartsWith(code, i, "//"))
            {
                var end = code.IndexOf('\n', i);
                i = end < 0 ? code.Length : end;
                continue;
            }
            if (StartsWith(code, i, "/*"))
            {
                var end = code.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? code.Length : end + 2;
                continue;
            }
            if (TrySkipLiteral(code, i, out var literalEnd))
            {
                i = literalEnd;
                continue;
            }

            if (code[i] == '{') depth++;
            else if (code[i] == '}')
            {
                depth--;
                if (depth == 0) return i + 1;
            }
            i++;
        }
        return code.Length;
    }
}
