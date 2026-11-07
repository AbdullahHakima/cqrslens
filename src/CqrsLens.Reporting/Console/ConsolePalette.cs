namespace CqrsLens;

/// <summary>
/// Console palette derived from the lapis / papyrus / saffron brand tokens and
/// the print-calibrated diagnostic ink ramps. Two variants cover dark and light
/// terminals; the dark plate is the primary identity.
/// </summary>
internal sealed class ConsolePalette
{
    /// <summary>Cream papyrus ink on the deep lapis plate (default).</summary>
    /// <remarks>
    /// The 100-level diagnostic inks are calibrated for text sitting on a tinted
    /// badge; on a bare lapis field they collapse into the papyrus cream. The
    /// console therefore uses the mid-band of the same hues so status stays
    /// legible without leaving the brand ramp.
    /// </remarks>
    public static ConsolePalette Dark { get; } = new()
    {
        Ink = (245, 239, 221),       // papyrus-100
        InkDim = (216, 208, 185),    // papyrus-300
        InkMuted = (184, 171, 140),  // papyrus-500
        Accent = (237, 189, 106),    // saffron-400
        Rule = (43, 72, 113),        // lapis-600
        Command = (127, 172, 240),   // cobalt mid-band
        Query = (90, 198, 189),      // teal mid-band
        Warn = (240, 171, 85),       // amber mid-band
        Error = (227, 100, 96),      // crimson mid-band
        Pass = (101, 194, 129),      // forest mid-band
    };

    /// <summary>Lapis ink on a light terminal, using the 700 ink ramps.</summary>
    public static ConsolePalette Light { get; } = new()
    {
        Ink = (14, 44, 84),          // lapis-800
        InkDim = (26, 56, 97),       // lapis-700
        InkMuted = (43, 72, 113),    // lapis-600
        Accent = (23, 82, 164),      // cobalt-700
        Rule = (43, 72, 113),        // lapis-600
        Command = (23, 82, 164),     // cobalt-700
        Query = (0, 103, 96),        // teal-700
        Warn = (134, 76, 0),         // amber-700
        Error = (156, 37, 42),       // crimson-700
        Pass = (23, 103, 55),        // forest-700
    };

    /// <summary>Primary foreground ink.</summary>
    public (byte R, byte G, byte B) Ink { get; private init; }

    /// <summary>Secondary foreground ink.</summary>
    public (byte R, byte G, byte B) InkDim { get; private init; }

    /// <summary>Muted foreground ink for labels and hairlines.</summary>
    public (byte R, byte G, byte B) InkMuted { get; private init; }

    /// <summary>Brand accent used for the origin mark and version stamp.</summary>
    public (byte R, byte G, byte B) Accent { get; private init; }

    /// <summary>Structural rule colour for boxes and separators.</summary>
    public (byte R, byte G, byte B) Rule { get; private init; }

    /// <summary>Diagnostic ink for command surfaces.</summary>
    public (byte R, byte G, byte B) Command { get; private init; }

    /// <summary>Diagnostic ink for query surfaces.</summary>
    public (byte R, byte G, byte B) Query { get; private init; }

    /// <summary>Diagnostic ink for warnings.</summary>
    public (byte R, byte G, byte B) Warn { get; private init; }

    /// <summary>Diagnostic ink for errors.</summary>
    public (byte R, byte G, byte B) Error { get; private init; }

    /// <summary>Diagnostic ink for passing checks.</summary>
    public (byte R, byte G, byte B) Pass { get; private init; }

    /// <summary>
    /// Resolves the palette for the current terminal. <c>COLORFGBG</c> is the only
    /// portable background hint available to a console application; when it is
    /// absent the deep lapis plate is assumed.
    /// </summary>
    public static ConsolePalette Resolve()
        => IsLightBackground() ? Light : Dark;

    private static bool IsLightBackground()
    {
        var spec = Environment.GetEnvironmentVariable("COLORFGBG");
        if (string.IsNullOrWhiteSpace(spec))
            return false;

        // Format is "fg;bg" or "fg;default;bg"; the background is the last segment.
        var parts = spec.Split(';');
        if (!int.TryParse(parts[^1], out var background))
            return false;

        // 0-6 and 8 are dark backgrounds; 7 and 9-15 are light ones.
        return background == 7 || background >= 9;
    }
}
