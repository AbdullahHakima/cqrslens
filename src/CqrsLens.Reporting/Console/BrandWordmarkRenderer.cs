using System.Text;

namespace CqrsLens;

/// <summary>Renders text in the stepped-pixel brand face as half-block artwork.</summary>
internal static class BrandWordmarkRenderer
{
    private const int GlyphWidth = 10;

    /// <summary>Width in terminal columns for <paramref name="text"/>.</summary>
    internal static int Measure(string text, int letterGap = 1)
        => string.IsNullOrEmpty(text)
            ? 0
            : text.Length * GlyphWidth + (text.Length - 1) * letterGap;

    internal static IReadOnlyList<string> Render(string text, int letterGap = 1)
    {
        if (string.IsNullOrEmpty(text))
            return Array.Empty<string>();

        var pixelRows = new string[BrandWordmarkGlyphs.Height];
        var sb = new StringBuilder();

        for (var row = 0; row < pixelRows.Length; row++)
        {
            sb.Clear();
            for (var i = 0; i < text.Length; i++)
            {
                if (i > 0)
                    sb.Append(' ', letterGap);

                sb.Append(BrandWordmarkGlyphs.Get(text[i])[row]);
            }

            pixelRows[row] = sb.ToString();
        }

        return HalfBlockCanvas.Compress(pixelRows);
    }
}
