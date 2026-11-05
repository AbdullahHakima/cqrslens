using System.Text;

namespace CqrsLens;

/// <summary>
/// Composes the CqrsLens lockup: the owl origin mark, the stepped-pixel wordmark
/// and the tagline. The lockup degrades through four sizes so it never wraps
/// mid-glyph on a narrow terminal, and collapses to a two-line text masthead
/// when block art is unavailable.
/// </summary>
internal static class ConsoleLogoRenderer
{
    private const string Wordmark = "NOCTUA";
    private const string Tagline = "A lens for the CQRS codebase you already have.";
    private const int MarkGap = 4;
    private const int CompactGap = 3;

    internal static string Render(
        string version,
        bool useColor,
        ConsolePalette palette,
        ConsoleGlyphs glyphs,
        int width)
    {
        var sb = new StringBuilder();
        foreach (var line in Compose(version, useColor, palette, glyphs, width))
            sb.AppendLine(line);

        return sb.ToString();
    }

    private static IReadOnlyList<string> Compose(
        string version,
        bool useColor,
        ConsolePalette palette,
        ConsoleGlyphs glyphs,
        int width)
    {
        // Half-block glyphs are East-Asian-Ambiguous and have no single-width
        // equivalent, so a CJK terminal gets a text masthead instead of art.
        if (!glyphs.SupportsBlockArt)
            return TextMasthead(version, useColor, palette, glyphs, width);

        var mark = BrandMarkRenderer.Render();
        var wordmark = BrandWordmarkRenderer.Render(Wordmark);
        var wordmarkWidth = BrandWordmarkRenderer.Measure(Wordmark);
        var compactWidth = BrandMarkRenderer.Width + CompactGap + Tagline.Length;

        var lines = new List<string>();
        var taglineInLockup = false;
        var versionInLockup = false;

        if (width >= BrandMarkRenderer.Width + MarkGap + wordmarkWidth)
        {
            lines.AddRange(SideBySide(mark, wordmark, MarkGap, palette.Accent, palette.Ink, useColor));
        }
        else if (width >= wordmarkWidth)
        {
            lines.AddRange(Paint(wordmark, palette.Ink, useColor));
        }
        else if (width >= compactWidth)
        {
            lines.AddRange(CompactLockup(mark, useColor, palette));
            taglineInLockup = true;
        }
        else
        {
            lines.Add(BuildTextLockup(version, useColor, palette, glyphs, width));
            versionInLockup = true;
        }

        if (taglineInLockup)
            return lines;

        var lockupWidth = lines.Max(line => AnsiColor.VisibleLength(line));

        if (lines.Count > 1)
            lines.Add(string.Empty);

        lines.Add(Center(
            BuildTagline(version, width, useColor, palette, glyphs, includeVersion: !versionInLockup),
            lockupWidth));

        return lines;
    }

    /// <summary>
    /// Two-line masthead for terminals where block art would double in width:
    /// the origin mark and wordmark, then the tagline and version.
    /// </summary>
    private static IReadOnlyList<string> TextMasthead(
        string version,
        bool useColor,
        ConsolePalette palette,
        ConsoleGlyphs glyphs,
        int width)
    {
        var head = $"{glyphs.Bullet} {Wordmark}";
        var tagline = $"{Tagline} {glyphs.Separator} v{version}";

        var fittedHead = head.Length <= width ? head : ConsoleText.Truncate(head, width, glyphs.Ellipsis);
        var fittedTagline = tagline.Length <= width
            ? tagline
            : ConsoleText.Truncate(tagline, width, glyphs.Ellipsis);

        return new[]
        {
            useColor ? AnsiColor.Colorize(fittedHead, palette.Accent, bold: true) : fittedHead,
            useColor ? AnsiColor.Colorize(fittedTagline, palette.InkMuted) : fittedTagline,
        };
    }

    /// <summary>Owl mark beside a two-line text lockup for mid-width terminals.</summary>
    private static IReadOnlyList<string> CompactLockup(
        IReadOnlyList<string> mark,
        bool useColor,
        ConsolePalette palette)
    {
        var right = new[]
        {
            (Text: Wordmark, Color: palette.Ink, Bold: true),
            (Text: Tagline, Color: palette.InkMuted, Bold: false),
        };

        var markWidth = mark.Max(line => line.Length);
        var height = Math.Max(mark.Count, right.Length);
        var markOffset = (height - mark.Count) / 2;
        var rightOffset = (height - right.Length) / 2;

        var lines = new List<string>(height);
        for (var row = 0; row < height; row++)
        {
            var markIndex = row - markOffset;
            var markLine = markIndex >= 0 && markIndex < mark.Count
                ? mark[markIndex]
                : string.Empty;

            var rightIndex = row - rightOffset;
            var rightText = rightIndex >= 0 && rightIndex < right.Length
                ? right[rightIndex].Text
                : string.Empty;
            var rightColor = rightIndex >= 0 && rightIndex < right.Length
                ? right[rightIndex].Color
                : palette.Ink;
            var bold = rightIndex >= 0 && rightIndex < right.Length && right[rightIndex].Bold;

            var plain = (markLine.PadRight(markWidth) +
                         new string(' ', CompactGap) +
                         rightText).TrimEnd();

            lines.Add(useColor
                ? PaintTwoTone(plain, markWidth + CompactGap, palette.Accent, rightColor, bold)
                : plain);
        }

        return lines;
    }

    private static string BuildTextLockup(
        string version,
        bool useColor,
        ConsolePalette palette,
        ConsoleGlyphs glyphs,
        int width)
    {
        var head = $"{glyphs.Bullet} {Wordmark}";
        var tail = $" {glyphs.Separator} v{version}";
        var text = head + tail;

        if (text.Length > width)
        {
            text = head.Length <= width ? head : ConsoleText.Truncate(head, width, glyphs.Ellipsis);
            return useColor ? AnsiColor.Colorize(text, palette.Accent, bold: true) : text;
        }

        if (!useColor)
            return text;

        return AnsiColor.Colorize(head, palette.Accent, bold: true) +
               AnsiColor.Colorize(tail, palette.InkMuted);
    }

    private static string BuildTagline(
        string version,
        int width,
        bool useColor,
        ConsolePalette palette,
        ConsoleGlyphs glyphs,
        bool includeVersion)
    {
        var rule = glyphs.Horizontal + glyphs.Horizontal;
        var suffix = includeVersion ? $" {glyphs.Separator} v{version}" : string.Empty;
        var decorated = $"{rule} {glyphs.Bullet} {Tagline}{suffix}";

        // Below the decorated width the tagline collapses to bare text, then truncates.
        if (decorated.Length > width)
        {
            var plain = ConsoleText.Truncate(Tagline, width, glyphs.Ellipsis);
            return useColor ? AnsiColor.Colorize(plain, palette.InkMuted) : plain;
        }

        if (!useColor)
            return decorated;

        return AnsiColor.Colorize(rule + " ", palette.Rule) +
               AnsiColor.Colorize(glyphs.Bullet + " ", palette.Accent) +
               AnsiColor.Colorize(Tagline, palette.InkMuted) +
               (includeVersion
                   ? AnsiColor.Colorize($" {glyphs.Separator} ", palette.Rule) +
                     AnsiColor.Colorize($"v{version}", palette.Accent)
                   : string.Empty);
    }

    private static IReadOnlyList<string> SideBySide(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right,
        int gap,
        (byte R, byte G, byte B) leftColor,
        (byte R, byte G, byte B) rightColor,
        bool useColor)
    {
        var leftWidth = left.Max(line => line.Length);
        var rightWidth = right.Max(line => line.Length);
        var height = Math.Max(left.Count, right.Count);
        var leftOffset = (height - left.Count) / 2;
        var rightOffset = (height - right.Count) / 2;

        var lines = new List<string>(height);
        for (var row = 0; row < height; row++)
        {
            var leftLine = row >= leftOffset && row - leftOffset < left.Count
                ? left[row - leftOffset]
                : string.Empty;
            var rightLine = row >= rightOffset && row - rightOffset < right.Count
                ? right[row - rightOffset]
                : string.Empty;

            var text = leftLine.PadRight(leftWidth) +
                       new string(' ', gap) +
                       rightLine.PadRight(rightWidth);

            lines.Add(useColor
                ? PaintTwoTone(text, leftWidth + gap, leftColor, rightColor, bold: false)
                : text.TrimEnd());
        }

        return lines;
    }

    private static string PaintTwoTone(
        string text,
        int split,
        (byte R, byte G, byte B) headColor,
        (byte R, byte G, byte B) tailColor,
        bool bold)
    {
        var trimmed = text.TrimEnd();
        var cut = Math.Min(split, trimmed.Length);

        var head = trimmed[..cut];
        var tail = trimmed[cut..];

        return AnsiColor.Colorize(head, headColor) +
               AnsiColor.Colorize(tail, tailColor, bold);
    }

    private static IReadOnlyList<string> Paint(
        IReadOnlyList<string> lines,
        (byte R, byte G, byte B) color,
        bool useColor)
        => lines
            .Select(line => line.TrimEnd())
            .Select(line => useColor ? AnsiColor.Colorize(line, color) : line)
            .ToList();

    private static string Center(string text, int width)
    {
        var visible = AnsiColor.VisibleLength(text);
        if (visible >= width)
            return text;

        return new string(' ', (width - visible) / 2) + text;
    }
}
