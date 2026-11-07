using System.Globalization;
using System.Text;

namespace CqrsLens;

/// <summary>
/// Renders the scan summary as a lapis plate card: brand rule, papyrus labels,
/// cream values and a diagnostic status strip. The card shrinks to fit the
/// terminal instead of overflowing.
/// </summary>
internal static class ConsoleSummaryCardRenderer
{
    private const int MaxCardWidth = 78;
    private const int MinValueWidth = 6;
    private const int MinLabelWidth = 6;

    internal static string Render(
        AnalysisResult result,
        bool useColor,
        ConsolePalette palette,
        ConsoleGlyphs glyphs,
        int width)
    {
        var errors = result.Checks.Count(check => check.Severity == CheckSeverity.Error);
        var warnings = result.Checks.Count - errors;
        var dot = glyphs.Separator;

        var rows = new (string Label, string Value)[]
        {
            ("Projects", Count(result.Projects.Count)),
            ("Requests (Commands / Queries)", Count(result.Requests.Count)),
            ("Handlers", Count(result.Handlers.Count)),
            ("Validators", Count(result.Validators.Count)),
            ("Pipeline Behaviors", Count(result.Behaviors.Count)),
            ("EF Core DbContexts", Count(result.DbContexts.Count)),
            ($"Scanner {dot} Timing", $"{result.ScannerName} {dot} {result.ElapsedMilliseconds} ms"),
        };

        var (statusIcon, statusText, statusColor) = Status(errors, warnings, dot, palette, glyphs);

        var labelWidth = rows.Max(row => row.Label.Length);
        var valueWidth = rows.Max(row => row.Value.Length);
        var inner = FitInner(labelWidth, valueWidth, width, out labelWidth, out valueWidth);

        var title = $"NOCTUA {dot} ARCHITECTURE SUMMARY";
        var subtitle = new DirectoryInfo(result.RootPath).Name;

        var sb = new StringBuilder();

        AppendRule(sb, glyphs.TopLeft, glyphs.TopRight, inner, useColor, palette, glyphs);
        AppendTextRow(sb, title, inner, useColor, palette.Ink, bold: true, palette, glyphs);
        AppendTextRow(sb, subtitle, inner, useColor, palette.InkMuted, bold: false, palette, glyphs);
        AppendRule(sb, glyphs.LeftTee, glyphs.RightTee, inner, useColor, palette, glyphs);

        foreach (var (label, value) in rows)
            AppendValueRow(sb, label, value, labelWidth, valueWidth, useColor, palette, glyphs);

        AppendRule(sb, glyphs.LeftTee, glyphs.RightTee, inner, useColor, palette, glyphs);
        AppendStatusRow(sb, statusIcon, statusText, statusColor, inner, useColor, palette, glyphs);
        AppendRule(sb, glyphs.BottomLeft, glyphs.BottomRight, inner, useColor, palette, glyphs);

        return sb.ToString().TrimEnd('\r', '\n');
    }

    private static (string Icon, string Text, (byte R, byte G, byte B) Color) Status(
        int errors,
        int warnings,
        string dot,
        ConsolePalette palette,
        ConsoleGlyphs glyphs)
    {
        if (errors > 0)
        {
            return (glyphs.Fail,
                $"{Count(errors)} error(s) {dot} {Count(warnings)} warning(s)",
                palette.Error);
        }

        if (warnings > 0)
            return (glyphs.Warn, $"{Count(warnings)} warning(s) {dot} no errors", palette.Warn);

        return (glyphs.Pass, $"Passed {dot} no findings", palette.Pass);
    }

    private static int FitInner(
        int labelWidth,
        int valueWidth,
        int width,
        out int fittedLabelWidth,
        out int fittedValueWidth)
    {
        var maxInner = Math.Max(24, Math.Min(width - 2, MaxCardWidth) - 2);
        var inner = Math.Min(labelWidth + valueWidth + 6, maxInner);
        var budget = Math.Max(MinLabelWidth + MinValueWidth, inner - 6);

        if (labelWidth + valueWidth > budget)
        {
            var overflow = labelWidth + valueWidth - budget;

            var reducedValue = Math.Max(MinValueWidth, valueWidth - overflow);
            overflow -= valueWidth - reducedValue;
            valueWidth = reducedValue;

            labelWidth = Math.Max(MinLabelWidth, labelWidth - overflow);
        }

        fittedLabelWidth = labelWidth;
        fittedValueWidth = valueWidth;

        return labelWidth + valueWidth + 6;
    }

    private static void AppendRule(
        StringBuilder sb,
        string left,
        string right,
        int inner,
        bool useColor,
        ConsolePalette palette,
        ConsoleGlyphs glyphs)
    {
        var line = left + Repeat(glyphs.Horizontal, inner) + right;
        sb.AppendLine(useColor ? AnsiColor.Colorize(line, palette.Rule) : line);
    }

    private static void AppendTextRow(
        StringBuilder sb,
        string text,
        int inner,
        bool useColor,
        (byte R, byte G, byte B) color,
        bool bold,
        ConsolePalette palette,
        ConsoleGlyphs glyphs)
    {
        var content = "  " + ConsoleText.Truncate(text, inner - 2, glyphs.Ellipsis).PadRight(inner - 2);

        sb.Append(useColor ? AnsiColor.Colorize(glyphs.Vertical, palette.Rule) : glyphs.Vertical);
        sb.Append(useColor ? AnsiColor.Colorize(content, color, bold) : content);
        sb.AppendLine(useColor ? AnsiColor.Colorize(glyphs.Vertical, palette.Rule) : glyphs.Vertical);
    }

    private static void AppendValueRow(
        StringBuilder sb,
        string label,
        string value,
        int labelWidth,
        int valueWidth,
        bool useColor,
        ConsolePalette palette,
        ConsoleGlyphs glyphs)
    {
        var labelText = ConsoleText.Truncate(label, labelWidth, glyphs.Ellipsis).PadRight(labelWidth);
        var valueText = ConsoleText.Truncate(value, valueWidth, glyphs.Ellipsis).PadLeft(valueWidth);

        sb.Append(useColor ? AnsiColor.Colorize(glyphs.Vertical, palette.Rule) : glyphs.Vertical);
        sb.Append("  ");
        sb.Append(useColor ? AnsiColor.Colorize(labelText, palette.InkMuted) : labelText);
        sb.Append("  ");
        sb.Append(useColor ? AnsiColor.Colorize(valueText, palette.Ink, bold: true) : valueText);
        sb.Append("  ");
        sb.AppendLine(useColor ? AnsiColor.Colorize(glyphs.Vertical, palette.Rule) : glyphs.Vertical);
    }

    private static void AppendStatusRow(
        StringBuilder sb,
        string icon,
        string text,
        (byte R, byte G, byte B) color,
        int inner,
        bool useColor,
        ConsolePalette palette,
        ConsoleGlyphs glyphs)
    {
        var padded = ConsoleText.Truncate($"  {icon} {text}", inner, glyphs.Ellipsis).PadRight(inner);

        sb.Append(useColor ? AnsiColor.Colorize(glyphs.Vertical, palette.Rule) : glyphs.Vertical);
        sb.Append(useColor ? AnsiColor.Colorize(padded, color, bold: true) : padded);
        sb.AppendLine(useColor ? AnsiColor.Colorize(glyphs.Vertical, palette.Rule) : glyphs.Vertical);
    }

    private static string Repeat(string glyph, int count)
        => string.Concat(Enumerable.Repeat(glyph, count));

    private static string Count(int value)
        => value.ToString(CultureInfo.InvariantCulture);
}
