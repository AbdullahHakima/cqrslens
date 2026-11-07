using System.Text;

namespace CqrsLens;

/// <summary>Renders a framed ASCII art watermark stamp.</summary>
internal static class WatermarkStampRenderer
{
    internal static string Render(
        string watermark,
        bool useColor,
        ConsolePalette palette,
        ConsoleGlyphs glyphs)
    {
        // Saffron origin mark fading into papyrus ink: the same two-tone used by
        // the banner lockup, so the stamp reads as part of the same plate.
        var gradient = new[] { palette.Accent, palette.Ink };

        var clean = ConsoleText.CollapseWhitespace(watermark);
        if (string.IsNullOrEmpty(clean))
            return string.Empty;

        // The block font is drawn with full blocks, which are East-Asian-Ambiguous
        // too, so ASCII mode keeps the frame and drops the glyph art inside it.
        var artLines = glyphs.SupportsBlockArt
            ? AsciiTextRenderer.RenderLines(clean, maxCharsPerLine: 12)
            : new List<string>();

        var caption = $"{glyphs.StampMark} WATERMARK {glyphs.Separator} {clean.ToUpperInvariant()} {glyphs.StampMark}";

        var innerWidth = caption.Length;
        foreach (var line in artLines)
        {
            if (line.Length > innerWidth)
                innerWidth = line.Length;
        }
        innerWidth += 4; // 2 spaces padding on each side

        var sb = new StringBuilder();
        var topBorder = glyphs.DoubleTopLeft + Repeat(glyphs.DoubleHorizontal, innerWidth) + glyphs.DoubleTopRight;
        var divider = glyphs.DoubleLeftTee + Repeat(glyphs.Horizontal, innerWidth) + glyphs.DoubleRightTee;
        var bottomBorder = glyphs.DoubleBottomLeft + Repeat(glyphs.DoubleHorizontal, innerWidth) + glyphs.DoubleBottomRight;

        var framedLines = new List<string> { topBorder };
        foreach (var line in artLines)
        {
            framedLines.Add(glyphs.DoubleVertical + ConsoleText.CenterPad(line, innerWidth) + glyphs.DoubleVertical);
        }

        if (artLines.Count > 0)
            framedLines.Add(divider);

        framedLines.Add(glyphs.DoubleVertical + ConsoleText.CenterPad(caption, innerWidth) + glyphs.DoubleVertical);
        framedLines.Add(bottomBorder);

        for (var i = 0; i < framedLines.Count; i++)
        {
            if (useColor)
            {
                var bias = framedLines.Count <= 1 ? 0.0 : (double)i / (framedLines.Count - 1) * 0.25;
                AnsiColor.AppendGradientLine(sb, framedLines[i], gradient, bias);
            }
            else
            {
                sb.AppendLine(framedLines[i]);
            }
        }

        return sb.ToString().TrimEnd('\r', '\n');
    }

    private static string Repeat(string glyph, int count)
        => count <= 0 ? string.Empty : string.Concat(Enumerable.Repeat(glyph, count));
}
