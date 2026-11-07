namespace CqrsLens;

/// <summary>
/// The stepped-pixel display face from the brand sheet: ten rows per glyph with
/// two-unit stems and notched corner shoulders.
/// </summary>
/// <remarks>
/// Only the glyphs needed to spell the display wordmark are carried here. To add
/// one, port it verbatim from <c>PixelWordmark.tsx</c> in the landing page repo.
/// </remarks>
internal static class BrandWordmarkGlyphs
{
    internal const int Height = 10;

    private static readonly Dictionary<char, string[]> Glyphs = new()
    {
        ['A'] = new[]
        {
            "...####...",
            "..######..",
            ".##....##.",
            "##......##",
            "##......##",
            "##########",
            "##########",
            "##......##",
            "##......##",
            "##......##",
        },
        ['C'] = new[]
        {
            "..######..",
            ".########.",
            "###....###",
            "##........",
            "##........",
            "##........",
            "##........",
            "###....###",
            ".########.",
            "..######..",
        },
        ['N'] = new[]
        {
            "##......##",
            "###.....##",
            "####....##",
            "##.##...##",
            "##..##..##",
            "##...##.##",
            "##....####",
            "##.....###",
            "##......##",
            "##......##",
        },
        ['O'] = new[]
        {
            "..######..",
            ".########.",
            "###....###",
            "##......##",
            "##......##",
            "##......##",
            "##......##",
            "###....###",
            ".########.",
            "..######..",
        },
        ['T'] = new[]
        {
            ".########.",
            "##########",
            "##..##..##",
            "....##....",
            "....##....",
            "....##....",
            "....##....",
            "....##....",
            "....##....",
            "....##....",
        },
        ['U'] = new[]
        {
            "##......##",
            "##......##",
            "##......##",
            "##......##",
            "##......##",
            "##......##",
            "##......##",
            "###....###",
            ".########.",
            "..######..",
        },
        [' '] = new[]
        {
            "....",
            "....",
            "....",
            "....",
            "....",
            "....",
            "....",
            "....",
            "....",
            "....",
        },
    };

    /// <summary>Returns the ten rows for <paramref name="glyph"/>, or blank space.</summary>
    internal static string[] Get(char glyph)
        => Glyphs.TryGetValue(char.ToUpperInvariant(glyph), out var rows)
            ? rows
            : Glyphs[' '];
}
