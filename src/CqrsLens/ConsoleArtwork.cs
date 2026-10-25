using System.Globalization;
using System.Text;

namespace CqrsLens;

/// <summary>Progress snapshot emitted during solution scanning.</summary>
public readonly record struct ScanProgress(
    string Phase,
    int Completed,
    int Total,
    string? Detail = null);

/// <summary>
/// Zero-dependency ASCII artwork, FIGlet-style block font renderer, ANSI 24-bit
/// TrueColor gradient engine, live progress bar, and watermark generator.
/// </summary>
public static class ConsoleArtwork
{
    private static readonly string[] LogoLines =
    {
        @"   ╭──────╮     ██████╗  ██████╗ ██████╗ ███████╗██╗     ███████╗███╗   ██╗███████╗",
        @"  ╱  ╭──╮  ╲   ██╔════╝ ██╔═══██╗██╔══██╗██╔════╝██║     ██╔════╝████╗  ██║██╔════╝",
        @" │  │ ◆ │  │   ██║      ██║   ██║██████╔╝███████╗██║     █████╗  ██╔██╗ ██║███████╗",
        @"  ╲  ╰──╯  ╱   ██║      ██║▄▄ ██║██╔══██╗╚════██║██║     ██╔══╝  ██║╚██╗██║╚════██║",
        @"   ╰──┬───╯    ╚██████╗ ╚██████╔╝██║  ██║███████║███████╗███████╗██║ ╚████║███████║",
        @"      ╰──►      ╚═════╝  ╚══▀▀═╝ ╚═╝  ╚═╝╚══════╝╚══════╝╚══════╝╚═╝  ╚═══╝╚══════╝",
    };

    private static readonly (byte R, byte G, byte B)[] BrandGradient =
    {
        (56, 189, 248),  // Sky cyan (#38bdf8)
        (99, 102, 241),  // Indigo (#6366f1)
        (192, 132, 252), // Violet (#c084fc)
    };

    private static readonly (byte R, byte G, byte B)[] WatermarkGradient =
    {
        (245, 158, 11),  // Amber gold (#f59e0b)
        (236, 72, 153),  // Pink (#ec4899)
        (139, 92, 246),  // Purple (#8b5cf6)
    };

    // 5-row block glyphs for custom ASCII watermarks and headers.
    private static readonly Dictionary<char, string[]> BlockFont = new()
    {
        ['A'] = new[] { " ███ ", "█   █", "█████", "█   █", "█   █" },
        ['B'] = new[] { "████ ", "█   █", "████ ", "█   █", "████ " },
        ['C'] = new[] { " ████", "█    ", "█    ", "█    ", " ████" },
        ['D'] = new[] { "████ ", "█   █", "█   █", "█   █", "████ " },
        ['E'] = new[] { "█████", "█    ", "████ ", "█    ", "█████" },
        ['F'] = new[] { "█████", "█    ", "████ ", "█    ", "█    " },
        ['G'] = new[] { " ████", "█    ", "█  ██", "█   █", " ████" },
        ['H'] = new[] { "█   █", "█   █", "█████", "█   █", "█   █" },
        ['I'] = new[] { " ███ ", "  █  ", "  █  ", "  █  ", " ███ " },
        ['J'] = new[] { "  ███", "    █", "    █", "█   █", " ███ " },
        ['K'] = new[] { "█   █", "█  █ ", "███  ", "█  █ ", "█   █" },
        ['L'] = new[] { "█    ", "█    ", "█    ", "█    ", "█████" },
        ['M'] = new[] { "█   █", "██ ██", "█ █ █", "█   █", "█   █" },
        ['N'] = new[] { "█   █", "██  █", "█ █ █", "█  ██", "█   █" },
        ['O'] = new[] { " ███ ", "█   █", "█   █", "█   █", " ███ " },
        ['P'] = new[] { "████ ", "█   █", "████ ", "█    ", "█    " },
        ['Q'] = new[] { " ███ ", "█   █", "█   █", "█  ██", " ████" },
        ['R'] = new[] { "████ ", "█   █", "████ ", "█  █ ", "█   █" },
        ['S'] = new[] { " ████", "█    ", " ███ ", "    █", "████ " },
        ['T'] = new[] { "█████", "  █  ", "  █  ", "  █  ", "  █  " },
        ['U'] = new[] { "█   █", "█   █", "█   █", "█   █", " ███ " },
        ['V'] = new[] { "█   █", "█   █", "█   █", " █ █ ", "  █  " },
        ['W'] = new[] { "█   █", "█   █", "█ █ █", "██ ██", "█   █" },
        ['X'] = new[] { "█   █", " █ █ ", "  █  ", " █ █ ", "█   █" },
        ['Y'] = new[] { "█   █", " █ █ ", "  █  ", "  █  ", "  █  " },
        ['Z'] = new[] { "█████", "   █ ", "  █  ", " █   ", "█████" },
        ['0'] = new[] { " ███ ", "█  ██", "█ █ █", "██  █", " ███ " },
        ['1'] = new[] { " ██  ", "  █  ", "  █  ", "  █  ", "████ " },
        ['2'] = new[] { "████ ", "    █", " ███ ", "█    ", "█████" },
        ['3'] = new[] { "████ ", "    █", " ███ ", "    █", "████ " },
        ['4'] = new[] { "█   █", "█   █", "█████", "    █", "    █" },
        ['5'] = new[] { "█████", "█    ", "████ ", "    █", "████ " },
        ['6'] = new[] { " ███ ", "█    ", "████ ", "█   █", " ███ " },
        ['7'] = new[] { "█████", "    █", "   █ ", "  █  ", "  █  " },
        ['8'] = new[] { " ███ ", "█   █", " ███ ", "█   █", " ███ " },
        ['9'] = new[] { " ███ ", "█   █", " ████", "    █", " ███ " },
        [' '] = new[] { "   ", "   ", "   ", "   ", "   " },
        ['-'] = new[] { "     ", "     ", "█████", "     ", "     " },
        ['_'] = new[] { "     ", "     ", "     ", "     ", "█████" },
        ['.'] = new[] { "  ", "  ", "  ", "  ", "██" },
        [':'] = new[] { "  ", "██", "  ", "██", "  " },
        ['/'] = new[] { "    █", "   █ ", "  █  ", " █   ", "█    " },
        ['!'] = new[] { "██", "██", "██", "  ", "██" },
        ['+'] = new[] { "     ", "  █  ", "█████", "  █  ", "     " },
        ['#'] = new[] { " █ █ ", "█████", " █ █ ", "█████", " █ █ " },
        ['('] = new[] { "  █", " █ ", " █ ", " █ ", "  █" },
        [')'] = new[] { "█  ", " █ ", " █ ", " █ ", "█  " },
        ['&'] = new[] { " ██  ", "█  █ ", " ██  ", "█  █ ", " ██ █" },
    };

    /// <summary>Determines whether ANSI colors should be emitted for the given writer and options.</summary>
    public static bool ShouldUseColor(TextWriter writer, bool? explicitColor)
    {
        if (explicitColor.HasValue)
            return explicitColor.Value;

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR")))
            return false;

        if (string.Equals(Environment.GetEnvironmentVariable("TERM"), "dumb", StringComparison.OrdinalIgnoreCase))
            return false;

        return ReferenceEquals(writer, Console.Out) && !Console.IsOutputRedirected;
    }

    /// <summary>Determines whether the ASCII banner and summary box should be rendered.</summary>
    public static bool ShouldShowBanner(TextWriter writer, bool? explicitBanner, bool gitHubAnnotations)
    {
        if (explicitBanner.HasValue)
            return explicitBanner.Value;

        if (gitHubAnnotations)
            return false;

        return ReferenceEquals(writer, Console.Out) && !Console.IsOutputRedirected;
    }

    /// <summary>Renders the signature CqrsLens ASCII art logo banner.</summary>
    public static string RenderLogoBanner(string version, bool useColor)
    {
        var sb = new StringBuilder();
        for (var row = 0; row < LogoLines.Length; row++)
        {
            var line = LogoLines[row];
            if (useColor)
            {
                var rowBias = LogoLines.Length <= 1 ? 0.0 : (double)row / (LogoLines.Length - 1) * 0.25;
                AppendGradientLine(sb, line, BrandGradient, rowBias);
            }
            else
            {
                sb.AppendLine(line);
            }
        }

        var tagline = $"      ── ◆ Static CQRS & Clean Architecture Lens · v{version} ◆ ──";
        if (useColor)
            sb.AppendLine(Colorize(tagline, 148, 163, 184, bold: true));
        else
            sb.AppendLine(tagline);

        return sb.ToString();
    }

    /// <summary>
    /// Converts arbitrary text into 5-row block ASCII art lines. Long inputs wrap
    /// cleanly across multiple block rows.
    /// </summary>
    public static IReadOnlyList<string> RenderAsciiTextLines(string text, int maxCharsPerLine = 12)
    {
        var normalized = NormalizeWatermarkText(text);
        if (normalized.Length == 0)
            return Array.Empty<string>();

        maxCharsPerLine = Math.Max(4, maxCharsPerLine);
        var chunks = ChunkText(normalized, maxCharsPerLine);
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

                var glyph = GetGlyph(chunk[i]);
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

    /// <summary>
    /// Renders a framed ASCII art watermark stamp suitable for console display or
    /// embedding inside Markdown/HTML reports.
    /// </summary>
    public static string RenderWatermarkStamp(string watermark, bool useColor = false)
    {
        var clean = CollapseWhitespace(watermark);
        if (string.IsNullOrEmpty(clean))
            return string.Empty;

        var artLines = RenderAsciiTextLines(clean, maxCharsPerLine: 12);
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
            framedLines.Add("║" + CenterPad(line, innerWidth) + "║");
        }
        framedLines.Add(divider);
        framedLines.Add("║" + CenterPad(caption, innerWidth) + "║");
        framedLines.Add(bottomBorder);

        for (var i = 0; i < framedLines.Count; i++)
        {
            if (useColor)
            {
                var bias = framedLines.Count <= 1 ? 0.0 : (double)i / (framedLines.Count - 1) * 0.25;
                AppendGradientLine(sb, framedLines[i], WatermarkGradient, bias);
            }
            else
            {
                sb.AppendLine(framedLines[i]);
            }
        }

        return sb.ToString().TrimEnd('\r', '\n');
    }

    /// <summary>
    /// Renders a stylized Unicode box-drawing summary card of the scan results.
    /// </summary>
    public static string RenderSummaryCard(AnalysisResult result, string outputPath, bool useColor)
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

        sb.AppendLine(useColor ? Colorize(top, 100, 116, 139) : top);
        var headerLeft = "CqrsLens Architecture Summary".PadRight(labelWidth);
        var headerRight = new DirectoryInfo(result.RootPath).Name;
        if (headerRight.Length > valueWidth)
            headerRight = headerRight[..(valueWidth - 1)] + "…";
        headerRight = headerRight.PadRight(valueWidth);

        if (useColor)
        {
            sb.Append(Colorize("│ ", 100, 116, 139));
            sb.Append(Colorize(headerLeft, 56, 189, 248, bold: true));
            sb.Append(Colorize(" │ ", 100, 116, 139));
            sb.Append(Colorize(headerRight, 226, 232, 240, bold: true));
            sb.AppendLine(Colorize(" │", 100, 116, 139));
            sb.AppendLine(Colorize(mid, 100, 116, 139));
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
                sb.Append(Colorize("│ ", 100, 116, 139));
                sb.Append(Colorize(paddedLabel, 203, 213, 225));
                sb.Append(Colorize(" │ ", 100, 116, 139));
                sb.Append(Colorize(paddedValue, color.R, color.G, color.B, bold: true));
                sb.AppendLine(Colorize(" │", 100, 116, 139));
            }
            else
            {
                sb.AppendLine($"│ {paddedLabel} │ {paddedValue} │");
            }
        }

        sb.AppendLine(useColor ? Colorize(bot, 100, 116, 139) : bot);
        return sb.ToString().TrimEnd('\r', '\n');
    }

    /// <summary>Formats a single-line progress bar string for interactive console updates.</summary>
    public static string FormatProgressBar(ScanProgress progress, bool useColor, int barWidth = 24)
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

        var coloredBar = Colorize(new string('█', filled), 56, 189, 248) +
                         Colorize(new string('░', empty), 71, 85, 105);
        var coloredPct = Colorize($"{percent,3}%", 129, 140, 248, bold: true);
        var coloredPhase = Colorize(progress.Phase, 226, 232, 240) +
                           (detail.Length > 0 ? Colorize(detail, 148, 163, 184) : "");
        return $"  [{coloredBar}] {coloredPct} · {coloredPhase}";
    }

    /// <summary>Wraps text in an ANSI 24-bit TrueColor foreground sequence.</summary>
    public static string Colorize(string text, byte r, byte g, byte b, bool bold = false)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return bold
            ? $"\x1b[1;38;2;{r};{g};{b}m{text}\x1b[0m"
            : $"\x1b[38;2;{r};{g};{b}m{text}\x1b[0m";
    }

    private static void AppendGradientLine(
        StringBuilder sb,
        string line,
        (byte R, byte G, byte B)[] stops,
        double offsetBias = 0.0)
    {
        if (string.IsNullOrEmpty(line))
        {
            sb.AppendLine();
            return;
        }

        var len = Math.Max(1, line.Length - 1);
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == ' ')
            {
                sb.Append(' ');
                continue;
            }

            var t = Math.Clamp((double)i / len * 0.75 + offsetBias, 0.0, 1.0);
            var (r, g, b) = InterpolateStops(stops, t);
            sb.Append($"\x1b[38;2;{r};{g};{b}m{ch}");
        }

        sb.AppendLine("\x1b[0m");
    }

    private static (byte R, byte G, byte B) InterpolateStops(
        (byte R, byte G, byte B)[] stops,
        double t)
    {
        if (stops.Length == 0) return (255, 255, 255);
        if (stops.Length == 1 || t <= 0.0) return stops[0];
        if (t >= 1.0) return stops[^1];

        var scaled = t * (stops.Length - 1);
        var idx = Math.Min((int)Math.Floor(scaled), stops.Length - 2);
        var localT = scaled - idx;

        var a = stops[idx];
        var b = stops[idx + 1];
        var r = (byte)Math.Round(a.R + (b.R - a.R) * localT);
        var g = (byte)Math.Round(a.G + (b.G - a.G) * localT);
        var bl = (byte)Math.Round(a.B + (b.B - a.B) * localT);
        return (r, g, bl);
    }

    private static string[] GetGlyph(char c)
    {
        var upper = char.ToUpperInvariant(c);
        if (BlockFont.TryGetValue(upper, out var glyph))
            return glyph;

        return new[]
        {
            " ███ ",
            "█   █",
            $"  {upper}  ",
            "█   █",
            " ███ ",
        };
    }

    private static string NormalizeWatermarkText(string text)
    {
        var clean = CollapseWhitespace(text).ToUpperInvariant();
        return clean;
    }

    private static string CollapseWhitespace(string? text)
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

    private static List<string> ChunkText(string text, int maxCharsPerLine)
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

    private static string CenterPad(string text, int width)
    {
        if (text.Length >= width)
            return text[..width];

        var totalPad = width - text.Length;
        var leftPad = totalPad / 2;
        var rightPad = totalPad - leftPad;
        return new string(' ', leftPad) + text + new string(' ', rightPad);
    }
}
