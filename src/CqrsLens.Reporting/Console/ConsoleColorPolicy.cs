namespace CqrsLens;

/// <summary>Decides when ANSI color and the interactive banner are safe to emit.</summary>
internal static class ConsoleColorPolicy
{
    internal static bool ShouldUseColor(TextWriter writer, bool? explicitColor)
    {
        if (explicitColor.HasValue)
            return explicitColor.Value;

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR")))
            return false;

        if (string.Equals(Environment.GetEnvironmentVariable("TERM"), "dumb", StringComparison.OrdinalIgnoreCase))
            return false;

        return ReferenceEquals(writer, Console.Out) && !Console.IsOutputRedirected;
    }

    internal static bool ShouldShowBanner(TextWriter writer, bool? explicitBanner, bool gitHubAnnotations)
    {
        if (explicitBanner.HasValue)
            return explicitBanner.Value;

        if (gitHubAnnotations)
            return false;

        return ReferenceEquals(writer, Console.Out) && !Console.IsOutputRedirected;
    }
}
