using System.Text;

namespace CqrsLens;

/// <summary>Renders a framed ASCII art watermark stamp.</summary>
internal static class WatermarkStampRenderer
{
    private static readonly (byte R, byte G, byte B)[] WatermarkGradient =
    {
        (245, 158, 11),  // Amber gold (#f59e0b)
        (236, 72, 153),  // Pink (#ec4899)
        (139, 92, 246),  // Purple (#8b5cf6)
    };

    internal static string Render(string watermark, bool useColor)
    {
        var clean = ConsoleText.CollapseWhitespace(watermark);
        if (string.IsNullOrEmpty(clean))
            return string.Empty;

        var artLines = AsciiTextRenderer.RenderLines(clean, maxCharsPerLine: 12);
        var caption = $"◈ WATERMARK · {clean.ToUpperInvariant()} ◈";

        var innerWidth = caption.Length;
        foreach (var line in artLines)
        {
            if (line.Length > innerWidth)
                innerWidth = line.Length;
        }
        innerWidth += 4; // 2 spaces padding on each side

        var sb = new StringBuilder();
        var topBorder = "╔" + new string('═', innerWidth) + "╗";
        var divider = "╟" + new string('─', innerWidth) + "╢";
        var bottomBorder = "╚" + new string('═', innerWidth) + "╝";

        var framedLines = new List<string> { topBorder };
        foreach (var line in artLines)
        {
            framedLines.Add("║" + ConsoleText.CenterPad(line, innerWidth) + "║");
        }
        framedLines.Add(divider);
        framedLines.Add("║" + ConsoleText.CenterPad(caption, innerWidth) + "║");
        framedLines.Add(bottomBorder);

        for (var i = 0; i < framedLines.Count; i++)
        {
            if (useColor)
            {
                var bias = framedLines.Count <= 1 ? 0.0 : (double)i / (framedLines.Count - 1) * 0.25;
                AnsiColor.AppendGradientLine(sb, framedLines[i], WatermarkGradient, bias);
            }
            else
            {
                sb.AppendLine(framedLines[i]);
            }
        }

        return sb.ToString().TrimEnd('\r', '\n');
    }
}
