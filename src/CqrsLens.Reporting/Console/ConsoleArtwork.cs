namespace CqrsLens;

/// <summary>
/// Zero-dependency ASCII artwork, stepped-pixel brand wordmark, ANSI 24-bit
/// TrueColor palette, live progress bar, and watermark generator.
/// </summary>
public static class ConsoleArtwork
{
    /// <summary>Determines whether ANSI colors should be emitted for the given writer and options.</summary>
    public static bool ShouldUseColor(TextWriter writer, bool? explicitColor)
        => ConsoleColorPolicy.ShouldUseColor(writer, explicitColor);

    /// <summary>Determines whether the ASCII banner and summary box should be rendered.</summary>
    public static bool ShouldShowBanner(TextWriter writer, bool? explicitBanner, bool gitHubAnnotations)
        => ConsoleColorPolicy.ShouldShowBanner(writer, explicitBanner, gitHubAnnotations);

    /// <summary>Renders the signature CqrsLens lockup banner.</summary>
    public static string RenderLogoBanner(string version, bool useColor, bool? ascii = null)
        => ConsoleLogoRenderer.Render(
            version,
            useColor,
            ConsolePalette.Resolve(),
            ConsoleGlyphs.Resolve(ascii),
            ConsoleWidth.Resolve());

    /// <summary>Renders the banner for a known terminal width.</summary>
    public static string RenderLogoBanner(string version, bool useColor, int width, bool? ascii = null)
        => ConsoleLogoRenderer.Render(
            version,
            useColor,
            ConsolePalette.Resolve(),
            ConsoleGlyphs.Resolve(ascii),
            width);

    /// <summary>
    /// Converts arbitrary text into 5-row block ASCII art lines. Long inputs wrap
    /// cleanly across multiple block rows.
    /// </summary>
    public static IReadOnlyList<string> RenderAsciiTextLines(string text, int maxCharsPerLine = 12)
        => AsciiTextRenderer.RenderLines(text, maxCharsPerLine);

    /// <summary>
    /// Renders a framed ASCII art watermark stamp for embedding inside
    /// Markdown/HTML reports. Reports always use the Unicode frame because they
    /// are written to a file rather than a terminal; the console path goes
    /// through the glyph-aware overload.
    /// </summary>
    public static string RenderWatermarkStamp(string watermark, bool useColor = false)
        => WatermarkStampRenderer.Render(watermark, useColor, ConsolePalette.Resolve(), ConsoleGlyphs.Unicode);

    /// <summary>Renders the watermark stamp for the terminal's glyph set.</summary>
    public static string RenderWatermarkStamp(string watermark, bool useColor, bool? ascii)
        => WatermarkStampRenderer.Render(
            watermark,
            useColor,
            ConsolePalette.Resolve(),
            ConsoleGlyphs.Resolve(ascii));

    /// <summary>
    /// Renders a stylized Unicode box-drawing summary card of the scan results.
    /// </summary>
    public static string RenderSummaryCard(
        AnalysisResult result,
        string outputPath,
        bool useColor,
        bool? ascii = null)
        => ConsoleSummaryCardRenderer.Render(
            result,
            useColor,
            ConsolePalette.Resolve(),
            ConsoleGlyphs.Resolve(ascii),
            ConsoleWidth.Resolve());

    /// <summary>Renders the summary card for a known terminal width.</summary>
    public static string RenderSummaryCard(
        AnalysisResult result,
        string outputPath,
        bool useColor,
        int width,
        bool? ascii = null)
        => ConsoleSummaryCardRenderer.Render(
            result,
            useColor,
            ConsolePalette.Resolve(),
            ConsoleGlyphs.Resolve(ascii),
            width);

    /// <summary>Formats a single-line progress bar string for interactive console updates.</summary>
    public static string FormatProgressBar(
        ScanProgress progress,
        bool useColor,
        int barWidth = 24,
        bool? ascii = null)
        => ConsoleProgressBarRenderer.Format(
            progress,
            useColor,
            ConsolePalette.Resolve(),
            ConsoleGlyphs.Resolve(ascii),
            barWidth);

    /// <summary>Wraps text in an ANSI 24-bit TrueColor foreground sequence.</summary>
    public static string Colorize(string text, byte r, byte g, byte b, bool bold = false)
        => AnsiColor.Colorize(text, r, g, b, bold);
}
