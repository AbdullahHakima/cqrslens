using System.Text;

namespace CqrsLens;

/// <summary>
/// Packs a pixel grid into terminal rows by pairing two pixel rows per cell and
/// drawing them with the upper/lower half-block glyphs. This preserves the square
/// proportions of the brand artwork inside a cell grid that is roughly 1:2.
/// </summary>
internal static class HalfBlockCanvas
{
    private const char Upper = '▀';
    private const char Lower = '▄';
    private const char Full = '█';

    /// <summary>
    /// Compresses <paramref name="rows"/> — where <c>#</c> marks a lit pixel — into
    /// half as many terminal lines.
    /// </summary>
    internal static IReadOnlyList<string> Compress(IReadOnlyList<string> rows)
    {
        var lines = new List<string>((rows.Count + 1) / 2);
        for (var y = 0; y < rows.Count; y += 2)
        {
            var top = rows[y];
            var bottom = y + 1 < rows.Count ? rows[y + 1] : string.Empty;
            var width = Math.Max(top.Length, bottom.Length);

            var sb = new StringBuilder(width);
            for (var x = 0; x < width; x++)
            {
                var upper = x < top.Length && top[x] == '#';
                var lower = x < bottom.Length && bottom[x] == '#';

                sb.Append(upper && lower ? Full
                    : upper ? Upper
                    : lower ? Lower
                    : ' ');
            }

            lines.Add(sb.ToString());
        }

        return lines;
    }
}
