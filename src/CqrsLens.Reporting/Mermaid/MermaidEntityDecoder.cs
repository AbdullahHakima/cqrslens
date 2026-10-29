using System.Text;

namespace CqrsLens;

/// <summary>
/// Decodes CqrsLens Mermaid entity escapes (<c>#38;</c>, <c>#quot;</c>,
/// <c>#lt;</c>, <c>#gt;</c>, <c>#124;</c>, <c>#35;</c>) in a single pass.
/// </summary>
internal static class MermaidEntityDecoder
{
    internal static string Decode(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length;)
        {
            if (value[i] == '#')
            {
                if (MatchEntity(value, i, "#38;", out var next))
                {
                    sb.Append('&');
                    i = next;
                    continue;
                }
                if (MatchEntity(value, i, "#quot;", out next))
                {
                    sb.Append('"');
                    i = next;
                    continue;
                }
                if (MatchEntity(value, i, "#lt;", out next))
                {
                    sb.Append('<');
                    i = next;
                    continue;
                }
                if (MatchEntity(value, i, "#gt;", out next))
                {
                    sb.Append('>');
                    i = next;
                    continue;
                }
                if (MatchEntity(value, i, "#124;", out next))
                {
                    sb.Append('|');
                    i = next;
                    continue;
                }
                if (MatchEntity(value, i, "#35;", out next))
                {
                    sb.Append('#');
                    i = next;
                    continue;
                }
            }

            sb.Append(value[i]);
            i++;
        }

        return sb.ToString();
    }

    private static bool MatchEntity(string input, int index, string entity, out int nextIndex)
    {
        if (input.AsSpan(index).StartsWith(entity.AsSpan(), StringComparison.Ordinal))
        {
            nextIndex = index + entity.Length;
            return true;
        }

        nextIndex = index;
        return false;
    }
}
