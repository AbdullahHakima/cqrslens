namespace CqrsLens;

/// <summary>
/// Zero-dependency ASCII artwork, FIGlet-style block font renderer, ANSI 24-bit
/// TrueColor gradient engine, live progress bar, and watermark generator.
/// </summary>
public static class ConsoleArtwork
{
    /// <summary>Determines whether ANSI colors should be emitted for the given writer and options.</summary>
    public static bool ShouldUseColor(TextWriter writer, bool? explicitColor)
        => ConsoleColorPolicy.ShouldUseColor(writer, explicitColor);

    /// <summary>Determines whether the ASCII banner and summary box should be rendered.</summary>
    public static bool ShouldShowBanner(TextWriter writer, bool? explicitBanner, bool gitHubAnnotations)
        => ConsoleColorPolicy.ShouldShowBanner(writer, explicitBanner, gitHubAnnotations);

    /// <summary>Renders the signature CqrsLens ASCII art logo banner.</summary>
    public static string RenderLogoBanner(string version, bool useColor)
        => ConsoleLogoRenderer.Render(version, useColor);

    /// <summary>
    /// Converts arbitrary text into 5-row block ASCII art lines. Long inputs wrap
    /// cleanly across multiple block rows.
    /// </summary>
    public static IReadOnlyList<string> RenderAsciiTextLines(string text, int maxCharsPerLine = 12)
        => AsciiTextRenderer.RenderLines(text, maxCharsPerLine);

    /// <summary>
    /// Renders a framed ASCII art watermark stamp suitable for console display or
    /// embedding inside Markdown/HTML reports.
    /// </summary>
    public static string RenderWatermarkStamp(string watermark, bool useColor = false)
        => WatermarkStampRenderer.Render(watermark, useColor);

    /// <summary>
    /// Renders a stylized Unicode box-drawing summary card of the scan results.
    /// </summary>
    public static string RenderSummaryCard(AnalysisResult result, string outputPath, bool useColor)
        => ConsoleSummaryCardRenderer.Render(result, useColor);

    /// <summary>Formats a single-line progress bar string for interactive console updates.</summary>
    public static string FormatProgressBar(ScanProgress progress, bool useColor, int barWidth = 24)
        => ConsoleProgressBarRenderer.Format(progress, useColor, barWidth);

    /// <summary>Wraps text in an ANSI 24-bit TrueColor foreground sequence.</summary>
    public static string Colorize(string text, byte r, byte g, byte b, bool bold = false)
        => AnsiColor.Colorize(text, r, g, b, bold);
}
