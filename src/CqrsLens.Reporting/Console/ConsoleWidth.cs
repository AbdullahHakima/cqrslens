namespace CqrsLens;

/// <summary>
/// Resolves the usable console width so artwork can adapt instead of wrapping
/// mid-glyph. Falls back to a conservative default when no console is attached.
/// </summary>
internal static class ConsoleWidth
{
    /// <summary>Width assumed when the output is redirected or unavailable.</summary>
    internal const int Fallback = 100;

    /// <summary>Narrowest width the renderers will design against.</summary>
    internal const int Minimum = 40;

    internal static int Resolve(int? explicitWidth = null)
    {
        if (explicitWidth.HasValue)
            return Math.Max(Minimum, explicitWidth.Value);

        // COLUMNS is the conventional override and is what CI harnesses set.
        var columns = Environment.GetEnvironmentVariable("COLUMNS");
        if (int.TryParse(columns, out var parsed) && parsed > 0)
            return Math.Max(Minimum, parsed);

        try
        {
            if (!Console.IsOutputRedirected)
                return Math.Max(Minimum, Console.WindowWidth);
        }
        catch (IOException)
        {
            // No console handle; fall through to the default.
        }
        catch (PlatformNotSupportedException)
        {
            // Platform without a console window; fall through to the default.
        }

        return Fallback;
    }
}
