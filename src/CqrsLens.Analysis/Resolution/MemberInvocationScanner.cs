namespace CqrsLens;

/// <summary>
/// Fast span-based member-access invocation scanner (<c>receiver.Method&lt;T&gt;(</c>) shared by
/// <see cref="ServiceCallEdgeResolver"/> and <see cref="ComponentCallAndDataResolver"/>.
/// Filters candidate identifiers by receiver length and leading-character mask before
/// allocating strings.
/// </summary>
internal static class MemberInvocationScanner
{
    public static void FindMatches<TTarget>(
        string code,
        SourceMask mask,
        int bodyStart,
        int bodyEnd,
        Dictionary<string, TTarget> targetByMember,
        List<MemberInvocationHit<TTarget>> results)
    {
        if (targetByMember.Count == 0 || bodyEnd <= bodyStart)
            return;

        var minLen = int.MaxValue;
        var maxLen = 0;
        var highMask = 0UL;
        var hasNonAscii = false;

        foreach (var key in targetByMember.Keys)
        {
            if (key.Length == 0)
                continue;
            if (key.Length < minLen) minLen = key.Length;
            if (key.Length > maxLen) maxLen = key.Length;

            var first = key[0];
            if (first >= 64 && first < 128)
                highMask |= 1UL << (first - 64);
            else
                hasNonAscii = true;
        }

        if (maxLen == 0)
            return;

        var span = code.AsSpan();
        var pos = bodyStart;
        while (pos < bodyEnd)
        {
            var rel = span[pos..bodyEnd].IndexOf('.');
            if (rel < 0)
                break;

            var dot = pos + rel;
            pos = dot + 1;
            if (!mask.IsCode(dot))
                continue;

            var r = dot - 1;
            while (r >= bodyStart && char.IsWhiteSpace(span[r]))
                r--;
            if (r >= bodyStart && (span[r] == '?' || span[r] == '!'))
            {
                r--;
                while (r >= bodyStart && char.IsWhiteSpace(span[r]))
                    r--;
            }

            if (r < bodyStart || !IsIdentPart(span[r]))
                continue;

            var receiverEnd = r + 1;
            while (r > bodyStart && IsIdentPart(span[r - 1]))
                r--;

            var receiverStart = r;
            if (!IsIdentStart(span[receiverStart]))
                continue;

            var matchStart = (receiverStart > 0 && span[receiverStart - 1] == '@')
                ? receiverStart - 1
                : receiverStart;

            if (matchStart > 0)
            {
                var before = span[matchStart - 1];
                if (IsIdentPart(before) || before == '.' || before == '@')
                    continue;
            }

            var receiverLen = receiverEnd - receiverStart;
            if (receiverLen < minLen || receiverLen > maxLen)
                continue;

            var firstCh = span[receiverStart];
            if (!hasNonAscii && (firstCh < 64 || firstCh >= 128 || (highMask & (1UL << (firstCh - 64))) == 0))
                continue;

            var m = dot + 1;
            while (m < bodyEnd && char.IsWhiteSpace(span[m]))
                m++;
            if (m >= bodyEnd || !IsIdentStart(span[m]))
                continue;

            var methodStart = m;
            m++;
            while (m < bodyEnd && IsIdentPart(span[m]))
                m++;
            var methodEnd = m;

            while (m < bodyEnd && char.IsWhiteSpace(span[m]))
                m++;

            string? typeArgs = null;
            if (m < bodyEnd && span[m] == '<')
            {
                var typeStart = m + 1;
                var angleDepth = 1;
                var k = typeStart;
                var valid = true;
                while (k < bodyEnd && angleDepth > 0)
                {
                    var c = span[k];
                    if (c == '<')
                    {
                        angleDepth++;
                    }
                    else if (c == '>')
                    {
                        angleDepth--;
                        if (angleDepth == 0)
                            break;
                    }
                    else if (!IsValidTypeArgChar(c))
                    {
                        valid = false;
                        break;
                    }
                    k++;
                }

                if (!valid || angleDepth != 0)
                    continue;

                var typeEnd = k;
                m = k + 1;
                while (m < bodyEnd && char.IsWhiteSpace(span[m]))
                    m++;
                if (m >= bodyEnd || span[m] != '(')
                    continue;

                typeArgs = typeEnd > typeStart ? code[typeStart..typeEnd] : null;
            }
            else if (m >= bodyEnd || span[m] != '(')
            {
                continue;
            }

            if (!mask.IsCode(matchStart))
                continue;

            var receiverName = code[receiverStart..receiverEnd];
            if (!targetByMember.TryGetValue(receiverName, out var target))
                continue;

            results.Add(new MemberInvocationHit<TTarget>(
                matchStart,
                target,
                code[methodStart..methodEnd],
                typeArgs,
                m));
        }
    }

    private static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_';

    private static bool IsIdentPart(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static bool IsValidTypeArgChar(char c) =>
        char.IsLetterOrDigit(c) ||
        c is '_' or '.' or ',' or '?' or '[' or ']' or ' ' or '\t' or '\r' or '\n';

    internal static string CollapseWhitespace(string type)
    {
        var wsCount = 0;
        foreach (var c in type)
        {
            if (char.IsWhiteSpace(c))
                wsCount++;
        }

        if (wsCount == 0)
            return type;
        if (wsCount == type.Length)
            return string.Empty;

        return string.Create(type.Length - wsCount, type, static (dst, src) =>
        {
            var idx = 0;
            foreach (var c in src)
            {
                if (!char.IsWhiteSpace(c))
                    dst[idx++] = c;
            }
        });
    }
}
