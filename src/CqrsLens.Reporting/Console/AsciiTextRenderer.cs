using System.Text;

namespace CqrsLens;

/// <summary>Renders arbitrary text as five-row block ASCII art.</summary>
internal static class AsciiTextRenderer
{
    internal static IReadOnlyList<string> RenderLines(string text, int maxCharsPerLine)
    {
        var normalized = ConsoleText.CollapseWhitespace(text).ToUpperInvariant();
        if (normalized.Length == 0)
            return Array.Empty<string>();

        maxCharsPerLine = Math.Max(4, maxCharsPerLine);
        var chunks = ConsoleText.ChunkText(normalized, maxCharsPerLine);
        var output = new List<string>(chunks.Count * 6);

        for (var c = 0; c < chunks.Count; c++)
        {
            if (c > 0)
                output.Add(string.Empty);

            var chunk = chunks[c];
            var rows = new StringBuilder[5];
            for (var r = 0; r < 5; r++)
                rows[r] = new StringBuilder();

            for (var i = 0; i < chunk.Length; i++)
            {
                if (i > 0)
                {
                    for (var r = 0; r < 5; r++)
                        rows[r].Append(' ');
                }

                var glyph = BlockFont.GetGlyph(chunk[i]);
                for (var r = 0; r < 5; r++)
                    rows[r].Append(glyph[r]);
            }

            var trimmedRows = rows.Select(r => r.ToString().TrimEnd()).ToArray();
            var chunkWidth = trimmedRows.Max(r => r.Length);
            for (var r = 0; r < 5; r++)
                output.Add(trimmedRows[r].PadRight(chunkWidth));
        }

        return output;
    }
}
