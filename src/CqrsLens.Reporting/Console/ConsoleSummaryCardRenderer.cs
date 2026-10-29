using System.Globalization;
using System.Text;

namespace CqrsLens;

/// <summary>Renders the Unicode box-drawing summary card of a scan.</summary>
internal static class ConsoleSummaryCardRenderer
{
    internal static string Render(AnalysisResult result, bool useColor)
    {
        var errors = result.Checks.Count(c => c.Severity == CheckSeverity.Error);
        var warnings = result.Checks.Count - errors;
        var statusText = errors == 0 && warnings == 0
            ? "✔ Passed (0 issues)"
            : $"{errors} error(s), {warnings} warning(s)";

        var rows = new List<(string Label, string Value, (byte R, byte G, byte B) Color)>
        {
            ("Projects", result.Projects.Count.ToString(CultureInfo.InvariantCulture), (56, 189, 248)),
            ("Requests (Commands/Queries)", result.Requests.Count.ToString(CultureInfo.InvariantCulture), (99, 102, 241)),
            ("Handlers", result.Handlers.Count.ToString(CultureInfo.InvariantCulture), (129, 140, 248)),
            ("Validators", result.Validators.Count.ToString(CultureInfo.InvariantCulture), (168, 85, 247)),
            ("Pipeline Behaviors", result.Behaviors.Count.ToString(CultureInfo.InvariantCulture), (192, 132, 252)),
            ("EF Core DbContexts", result.DbContexts.Count.ToString(CultureInfo.InvariantCulture), (232, 121, 249)),
            ("Scanner / Timing", $"{result.ScannerName} · {result.ElapsedMilliseconds} ms", (148, 163, 184)),
            ("Architecture Checks", statusText, errors > 0 ? ((byte)251, (byte)113, (byte)133) : warnings > 0 ? ((byte)251, (byte)191, (byte)36) : ((byte)52, (byte)211, (byte)153)),
        };

        const int labelWidth = 29;
        var valueWidth = Math.Max(26, rows.Max(r => r.Value.Length) + 2);

        var sb = new StringBuilder();
        var top = $"╭─{new string('─', labelWidth)}─┬─{new string('─', valueWidth)}─╮";
        var mid = $"├─{new string('─', labelWidth)}─┼─{new string('─', valueWidth)}─┤";
        var bot = $"╰─{new string('─', labelWidth)}─┴─{new string('─', valueWidth)}─╯";

        sb.AppendLine(useColor ? AnsiColor.Colorize(top, 100, 116, 139) : top);
        var headerLeft = "CqrsLens Architecture Summary".PadRight(labelWidth);
        var headerRight = new DirectoryInfo(result.RootPath).Name;
        if (headerRight.Length > valueWidth)
            headerRight = headerRight[..(valueWidth - 1)] + "…";
        headerRight = headerRight.PadRight(valueWidth);

        if (useColor)
        {
            sb.Append(AnsiColor.Colorize("│ ", 100, 116, 139));
            sb.Append(AnsiColor.Colorize(headerLeft, 56, 189, 248, bold: true));
            sb.Append(AnsiColor.Colorize(" │ ", 100, 116, 139));
            sb.Append(AnsiColor.Colorize(headerRight, 226, 232, 240, bold: true));
            sb.AppendLine(AnsiColor.Colorize(" │", 100, 116, 139));
            sb.AppendLine(AnsiColor.Colorize(mid, 100, 116, 139));
        }
        else
        {
            sb.AppendLine($"│ {headerLeft} │ {headerRight} │");
            sb.AppendLine(mid);
        }

        foreach (var (label, value, color) in rows)
        {
            var paddedLabel = label.PadRight(labelWidth);
            var paddedValue = value.PadRight(valueWidth);
            if (useColor)
            {
                sb.Append(AnsiColor.Colorize("│ ", 100, 116, 139));
                sb.Append(AnsiColor.Colorize(paddedLabel, 203, 213, 225));
                sb.Append(AnsiColor.Colorize(" │ ", 100, 116, 139));
                sb.Append(AnsiColor.Colorize(paddedValue, color.R, color.G, color.B, bold: true));
                sb.AppendLine(AnsiColor.Colorize(" │", 100, 116, 139));
            }
            else
            {
                sb.AppendLine($"│ {paddedLabel} │ {paddedValue} │");
            }
        }

        sb.AppendLine(useColor ? AnsiColor.Colorize(bot, 100, 116, 139) : bot);
        return sb.ToString().TrimEnd('\r', '\n');
    }
}
