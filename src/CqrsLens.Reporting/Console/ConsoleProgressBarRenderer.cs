namespace CqrsLens;

/// <summary>Formats the single-line interactive console progress bar.</summary>
internal static class ConsoleProgressBarRenderer
{
    internal static string Format(ScanProgress progress, bool useColor, int barWidth)
    {
        barWidth = Math.Max(8, barWidth);
        var ratio = progress.Total <= 0
            ? 1.0
            : Math.Clamp((double)progress.Completed / progress.Total, 0.0, 1.0);
        var filled = (int)Math.Round(ratio * barWidth);
        var empty = Math.Max(0, barWidth - filled);
        var percent = (int)Math.Round(ratio * 100);

        var bar = new string('█', filled) + new string('░', empty);
        var detail = string.IsNullOrWhiteSpace(progress.Detail) ? "" : $" ({progress.Detail})";
        var plain = $"  [{bar}] {percent,3}% · {progress.Phase}{detail}";

        if (!useColor)
            return plain;

        var coloredBar = AnsiColor.Colorize(new string('█', filled), 56, 189, 248) +
                         AnsiColor.Colorize(new string('░', empty), 71, 85, 105);
        var coloredPct = AnsiColor.Colorize($"{percent,3}%", 129, 140, 248, bold: true);
        var coloredPhase = AnsiColor.Colorize(progress.Phase, 226, 232, 240) +
                           (detail.Length > 0 ? AnsiColor.Colorize(detail, 148, 163, 184) : "");
        return $"  [{coloredBar}] {coloredPct} · {coloredPhase}";
    }
}
