namespace CqrsLens;

/// <summary>
/// The character set used to draw console chrome.
/// </summary>
/// <remarks>
/// <see cref="Unicode"/> is the default and is defined by the property
/// defaults. <see cref="Ascii"/> replaces every East-Asian-Ambiguous code point
/// with a single-width equivalent: terminals using a CJK font render those two
/// cells wide, which shears the summary card, the progress bar and the banner
/// out of alignment because the renderers count characters, not cells. Block art
/// has no ASCII equivalent, so ASCII mode also turns off
/// <see cref="SupportsBlockArt"/>.
/// </remarks>
internal sealed class ConsoleGlyphs
{
    /// <summary>Box-drawing and block characters; the default set.</summary>
    public static ConsoleGlyphs Unicode { get; } = new();

    /// <summary>Pure ASCII: every glyph occupies exactly one cell in any font.</summary>
    public static ConsoleGlyphs Ascii { get; } = new()
    {
        Pass = "+",
        Warn = "!",
        Fail = "x",
        Bullet = "*",
        Separator = "-",
        Ellipsis = "...",
        BarFilled = "#",
        BarEmpty = "-",
        Horizontal = "-",
        Vertical = "|",
        TopLeft = "+",
        TopRight = "+",
        BottomLeft = "+",
        BottomRight = "+",
        LeftTee = "+",
        RightTee = "+",
        DoubleHorizontal = "=",
        DoubleVertical = "|",
        DoubleTopLeft = "+",
        DoubleTopRight = "+",
        DoubleBottomLeft = "+",
        DoubleBottomRight = "+",
        DoubleLeftTee = "+",
        DoubleRightTee = "+",
        StampMark = "*",
        SupportsBlockArt = false,
    };

    /// <summary>Icon for a passing check.</summary>
    public string Pass { get; private init; } = "✔";

    /// <summary>Icon for a warning.</summary>
    public string Warn { get; private init; } = "▲";

    /// <summary>Icon for an error.</summary>
    public string Fail { get; private init; } = "✖";

    /// <summary>Origin mark that opens the text lockup and the tagline.</summary>
    public string Bullet { get; private init; } = "◆";

    /// <summary>Interpolates labels, versions and caption clauses.</summary>
    public string Separator { get; private init; } = "·";

    /// <summary>Marks a truncated string; may be wider than one cell.</summary>
    public string Ellipsis { get; private init; } = "…";

    /// <summary>Filled cell of the progress bar.</summary>
    public string BarFilled { get; private init; } = "█";

    /// <summary>Empty cell of the progress bar.</summary>
    public string BarEmpty { get; private init; } = "░";

    /// <summary>Horizontal rule of the summary card.</summary>
    public string Horizontal { get; private init; } = "─";

    /// <summary>Vertical rule of the summary card.</summary>
    public string Vertical { get; private init; } = "│";

    /// <summary>Top-left corner of the summary card.</summary>
    public string TopLeft { get; private init; } = "╭";

    /// <summary>Top-right corner of the summary card.</summary>
    public string TopRight { get; private init; } = "╮";

    /// <summary>Bottom-left corner of the summary card.</summary>
    public string BottomLeft { get; private init; } = "╰";

    /// <summary>Bottom-right corner of the summary card.</summary>
    public string BottomRight { get; private init; } = "╯";

    /// <summary>Section divider opening the summary card's status strip.</summary>
    public string LeftTee { get; private init; } = "├";

    /// <summary>Section divider closing the summary card's status strip.</summary>
    public string RightTee { get; private init; } = "┤";

    /// <summary>Heavy horizontal rule of the watermark stamp.</summary>
    public string DoubleHorizontal { get; private init; } = "═";

    /// <summary>Heavy vertical rule of the watermark stamp.</summary>
    public string DoubleVertical { get; private init; } = "║";

    /// <summary>Heavy top-left corner of the watermark stamp.</summary>
    public string DoubleTopLeft { get; private init; } = "╔";

    /// <summary>Heavy top-right corner of the watermark stamp.</summary>
    public string DoubleTopRight { get; private init; } = "╗";

    /// <summary>Heavy bottom-left corner of the watermark stamp.</summary>
    public string DoubleBottomLeft { get; private init; } = "╚";

    /// <summary>Heavy bottom-right corner of the watermark stamp.</summary>
    public string DoubleBottomRight { get; private init; } = "╝";

    /// <summary>Heavy divider opening the watermark stamp caption.</summary>
    public string DoubleLeftTee { get; private init; } = "╟";

    /// <summary>Heavy divider closing the watermark stamp caption.</summary>
    public string DoubleRightTee { get; private init; } = "╢";

    /// <summary>Decoration either side of the watermark caption.</summary>
    public string StampMark { get; private init; } = "◈";

    /// <summary>
    /// True when half-block characters are safe to draw. The pixel wordmark and
    /// the watermark block font depend on <c>▀ ▄ █</c>, which have no
    /// single-width equivalent, so ASCII mode falls back to the text lockup
    /// instead.
    /// </summary>
    public bool SupportsBlockArt { get; private init; } = true;

    /// <summary>
    /// Resolves the glyph set for the current run. <paramref name="ascii"/> is
    /// the <c>--ascii</c> / <c>--unicode</c> override; when it is absent the
    /// <c>CQRSLENS_ASCII</c> environment variable decides, and Unicode is the
    /// default.
    /// </summary>
    public static ConsoleGlyphs Resolve(bool? ascii = null)
        => (ascii ?? ReadAsciiEnvironmentVariable()) ? Ascii : Unicode;

    private static bool ReadAsciiEnvironmentVariable()
    {
        var value = Environment.GetEnvironmentVariable("CQRSLENS_ASCII");
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return value.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "on" => true,
            _ => false,
        };
    }
}
