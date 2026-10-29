using System.Text;

namespace CqrsLens;

/// <summary>Small text helpers shared by the console renderers.</summary>
internal static class ConsoleText
{
    internal static string CollapseWhitespace(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var sb = new StringBuilder(text.Length);
        var prevSpace = false;
        foreach (var ch in text.Trim())
        {
            if (char.IsWhiteSpace(ch) || char.IsControl(ch))
            {
                if (!prevSpace)
                {
                    sb.Append(' ');
                    prevSpace = true;
                }
            }
            else
            {
                sb.Append(ch);
                prevSpace = false;
            }
        }

        return sb.ToString();
    }

    internal static List<string> ChunkText(string text, int maxCharsPerLine)
    {
        var result = new List<string>();
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var current = new StringBuilder();

        foreach (var word in words)
        {
            if (word.Length > maxCharsPerLine)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }

                for (var i = 0; i < word.Length; i += maxCharsPerLine)
                {
                    var sliceLen = Math.Min(maxCharsPerLine, word.Length - i);
                    result.Add(word.Substring(i, sliceLen));
                }
                continue;
            }

            if (current.Length == 0)
            {
                current.Append(word);
            }
            else if (current.Length + 1 + word.Length <= maxCharsPerLine)
            {
                current.Append(' ').Append(word);
            }
            else
            {
                result.Add(current.ToString());
                current.Clear();
                current.Append(word);
            }
        }

        if (current.Length > 0)
            result.Add(current.ToString());

        return result;
    }

    internal static string CenterPad(string text, int width)
    {
        if (text.Length >= width)
            return text[..width];

        var totalPad = width - text.Length;
        var leftPad = totalPad / 2;
        var rightPad = totalPad - leftPad;
        return new string(' ', leftPad) + text + new string(' ', rightPad);
    }
}
