using System.Reflection;

namespace CqrsLens;

/// <summary>Tool identity shared by the CLI, reports and packaging.</summary>
public static class ToolInfo
{
    /// <summary>
    /// Package version, read from the assembly metadata generated from
    /// <c>Directory.Build.props</c>. Keeping one source of truth prevents the CLI,
    /// NuGet package, Action and README from drifting apart.
    /// </summary>
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var assembly = typeof(ToolInfo).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            // The SDK may append "+<commit>" (SourceLink); the package version does not.
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
