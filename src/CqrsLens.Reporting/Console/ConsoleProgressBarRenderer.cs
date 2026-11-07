namespace CqrsLens;

/// <summary>Formats the single-line interactive console progress bar.</summary>
internal static class ConsoleProgressBarRenderer
{
    internal static string Format(
        ScanProgress progress,
        bool useColor,
        ConsolePalette palette,
        ConsoleGlyphs glyphs,
        int barWidth)
    {
        barWidth = Math.Max(8, barWidth);
        var ratio = progress.Total <= 0
            ? 1.0
            : Math.Clamp((double)progress.Completed / progress.Total, 0.0, 1.0);
        var filled = (int)Math.Round(ratio * barWidth);
        var empty = Math.Max(0, barWidth - filled);
        var percent = (int)Math.Round(ratio * 100);

        var filledBar = Repeat(glyphs.BarFilled, filled);
        var emptyBar = Repeat(glyphs.BarEmpty, empty);
        var detailText = string.IsNullOrWhiteSpace(progress.Detail)
            ? ""
            : progress.Detail.Replace("·", glyphs.Separator, StringComparison.Ordinal);
        var detail = detailText.Length == 0 ? "" : $" ({detailText})";
        var plain = $"  [{filledBar}{emptyBar}] {percent,3}% {glyphs.Separator} {progress.Phase}{detail}";

        if (!useColor)
            return plain;

        var coloredBar = AnsiColor.Colorize(filledBar, palette.Accent) +
                         AnsiColor.Colorize(emptyBar, palette.Rule);
        var coloredPct = AnsiColor.Colorize($"{percent,3}%", palette.Ink, bold: true);
        var coloredPhase = AnsiColor.Colorize(progress.Phase, palette.InkDim) +
                           (detail.Length > 0 ? AnsiColor.Colorize(detail, palette.InkMuted) : "");
        return $"  [{coloredBar}] {coloredPct} {glyphs.Separator} {coloredPhase}";
    }

    private static string Repeat(string glyph, int count)
        => count <= 0 ? string.Empty : string.Concat(Enumerable.Repeat(glyph, count));
}
