namespace CqrsLens;

/// <summary>Renders the owl binocular monogram as half-block terminal artwork.</summary>
internal static class BrandMarkRenderer
{
    /// <summary>Width of the rendered mark in terminal columns.</summary>
    internal const int Width = 11;

    /// <summary>Height of the rendered mark in terminal rows.</summary>
    internal const int Height = 6;

    internal static IReadOnlyList<string> Render()
        => HalfBlockCanvas.Compress(BrandMarkGlyphs.Rows);
}
