using System.Text.Json;
using System.Text.Json.Serialization;

namespace CqrsLens;

/// <summary>A config error that should be reported as a CLI usage failure.</summary>
public sealed class ConfigurationException : Exception
{
    public ConfigurationException(string message) : base(message)
    {
    }
}

/// <summary>Validated .cqrslens.json values.</summary>
public sealed record CqrsLensSettings(
    string? ConfigPath,
    AnalysisMode? AnalysisMode,
    IReadOnlySet<string> Markers,
    IReadOnlySet<string> HandlerMarkers,
    IReadOnlyList<string> Include,
    IReadOnlyList<string> Exclude,
    IReadOnlyDictionary<string, string> LayerOverrides,
    IReadOnlyDictionary<string, CheckSeverity?> RuleSeverities,
    DiagramRenderMode? RenderMode = null,
    string? Watermark = null)
{
    public static readonly CqrsLensSettings Empty = new(
        null,
        null,
        new HashSet<string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal),
        Array.Empty<string>(),
        Array.Empty<string>(),
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, CheckSeverity?>(StringComparer.OrdinalIgnoreCase),
        null);
}

/// <summary>
/// Loads and validates .cqrslens.json. By default the file is looked up only at
/// the scan root so a parent repository's config cannot silently affect a
/// nested scan. --config can point at an explicit file.
/// </summary>
public static class ConfigurationLoader
{
    public const string FileName = ".cqrslens.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private static readonly string[] KnownRules =
    {
        ArchitectureChecker.MissingHandlerCode,
        ArchitectureChecker.MultipleHandlersCode,
        ArchitectureChecker.LayerViolationCode,
        ArchitectureChecker.ApplicationLayerViolationCode,
        ArchitectureChecker.CircularProjectDependencyCode,
        ArchitectureChecker.CommandWithoutValidatorCode,
        ArchitectureChecker.OrphanValidatorCode,
        ArchitectureChecker.OrphanNotificationCode,
        ArchitectureChecker.AmbiguousProjectNameCode,
    };

    public static CqrsLensSettings Load(string scanRoot, string? explicitPath = null)
    {
        string path;
        if (explicitPath is null)
        {
            path = Path.Combine(scanRoot, FileName);
            if (!File.Exists(path))
                return CqrsLensSettings.Empty;
        }
        else
        {
            try
            {
                path = Path.GetFullPath(explicitPath);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                throw new ConfigurationException($"invalid config path '{explicitPath}': {ex.Message}");
            }

            if (!File.Exists(path))
                throw new ConfigurationException($"config file not found: {explicitPath}");
        }

        CqrsLensConfiguration? config;
        try
        {
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
                throw new ConfigurationException($"config file is empty: {path}");

            config = JsonSerializer.Deserialize<CqrsLensConfiguration>(json, JsonOptions);
        }
        catch (ConfigurationException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            throw new ConfigurationException($"invalid config '{path}': {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ConfigurationException($"could not read config '{path}': {ex.Message}");
        }

        if (config is null)
            throw new ConfigurationException($"config file is empty: {path}");

        return Validate(path, config);
    }

    private static CqrsLensSettings Validate(string path, CqrsLensConfiguration config)
    {
        AnalysisMode? analysis = null;
        if (!string.IsNullOrWhiteSpace(config.Analysis))
        {
            analysis = config.Analysis.Trim().ToLowerInvariant() switch
            {
                "text" => AnalysisMode.Text,
                "roslyn" => AnalysisMode.Roslyn,
                _ => throw Invalid(path, $"unknown analysis mode '{config.Analysis}' - use text or roslyn"),
            };
        }

        DiagramRenderMode? render = null;
        if (!string.IsNullOrWhiteSpace(config.Render))
        {
            render = config.Render.Trim().ToLowerInvariant() switch
            {
                "mermaid" => DiagramRenderMode.Mermaid,
                "svg" => DiagramRenderMode.Svg,
                _ => throw Invalid(path, $"unknown render mode '{config.Render}' - use mermaid or svg"),
            };
        }

        var markers = ValidateMarkerNames(path, "markers", config.Markers);
        string? watermark = null;
        if (config.Watermark is not null)
        {
            if (string.IsNullOrWhiteSpace(config.Watermark))
                throw Invalid(path, "watermark cannot be an empty value");
            watermark = config.Watermark.Trim();
        }
        var handlerMarkers = ValidateMarkerNames(path, "handlerMarkers", config.HandlerMarkers);

        var include = ValidatePatterns(path, "include", config.Include);
        var exclude = ValidatePatterns(path, "exclude", config.Exclude);

        var layers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (project, layer) in config.Layers ?? new Dictionary<string, string>())
        {
            if (string.IsNullOrWhiteSpace(project) || string.IsNullOrWhiteSpace(layer))
                throw Invalid(path, "layers require non-empty project names and layer names");
            layers[project.Trim()] = layer.Trim();
        }

        var rules = new Dictionary<string, CheckSeverity?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (code, severity) in config.Rules ?? new Dictionary<string, string>())
        {
            var known = KnownRules.FirstOrDefault(r =>
                string.Equals(r, code, StringComparison.OrdinalIgnoreCase));
            if (known is null)
            {
                throw Invalid(
                    path,
                    $"unknown rule '{code}' - known rules: {string.Join(", ", KnownRules)}");
            }

            rules[known] = severity.Trim().ToLowerInvariant() switch
            {
                "off" => null,
                "warning" => CheckSeverity.Warning,
                "error" => CheckSeverity.Error,
                _ => throw Invalid(
                    path,
                    $"invalid severity '{severity}' for rule '{code}' - use off, warning or error"),
            };
        }

        return new CqrsLensSettings(
            path,
            analysis,
            markers,
            handlerMarkers,
            include,
            exclude,
            layers,
            rules,
            render,
            watermark);
    }

    private static IReadOnlySet<string> ValidateMarkerNames(
        string path,
        string property,
        IReadOnlyList<string>? values)
    {
        var markers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var marker in values ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(marker))
                throw Invalid(path, $"{property} cannot contain an empty value");
            markers.Add(CSharpScanner.SimpleName(marker.Trim()));
        }

        return markers;
    }

    private static IReadOnlyList<string> ValidatePatterns(
        string path,
        string property,
        IReadOnlyList<string>? patterns)
    {
        var values = (patterns ?? Array.Empty<string>())
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToArray();

        try
        {
            _ = new SourcePathFilter(
                property == "include" ? values : null,
                property == "exclude" ? values : null);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            throw Invalid(path, $"invalid {property} glob: {ex.Message}");
        }

        return values;
    }

    private static ConfigurationException Invalid(string path, string message)
        => new($"invalid config '{path}': {message}");

    private sealed class CqrsLensConfiguration
    {
        [JsonPropertyName("$schema")]
        public string? Schema { get; set; }

        public string? Analysis { get; set; }
        public string? Render { get; set; }
        public string? Watermark { get; set; }
        public List<string>? Markers { get; set; }
        public List<string>? HandlerMarkers { get; set; }
        public List<string>? Include { get; set; }
        public List<string>? Exclude { get; set; }
        public Dictionary<string, string>? Layers { get; set; }
        public Dictionary<string, string>? Rules { get; set; }
    }
}
