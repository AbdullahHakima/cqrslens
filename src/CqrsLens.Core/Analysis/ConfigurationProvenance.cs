namespace CqrsLens;

/// <summary>
/// The resolved configuration a scan ran under — recorded so reports can present
/// declared-vs-actual facts. This is a plain record of what the CLI already
/// resolved; it reinterprets nothing and takes part in no detection. It is not part
/// of the JSON contract (the JSON factory does not read it).
/// </summary>
public sealed class ConfigurationProvenance
{
    /// <summary>Absolute path of the config file used, or null when none was found.</summary>
    public string? ConfigFilePath { get; init; }

    public IReadOnlyList<string> DeclaredMarkers { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> DeclaredHandlerMarkers { get; init; } = Array.Empty<string>();

    public IReadOnlyDictionary<string, string> LayerOverrides { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> IncludeGlobs { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ExcludeGlobs { get; init; } = Array.Empty<string>();

    public IReadOnlyDictionary<string, CheckSeverity?> RuleOverrides { get; init; } =
        new Dictionary<string, CheckSeverity?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The default: no config file and nothing declared.</summary>
    public static readonly ConfigurationProvenance Empty = new();

    public bool HasConfigFile => !string.IsNullOrEmpty(ConfigFilePath);
}
