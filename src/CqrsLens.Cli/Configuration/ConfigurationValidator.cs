namespace CqrsLens;

/// <summary>Turns a deserialized .cqrslens.json into validated <see cref="CqrsLensSettings"/>.</summary>
internal static class ConfigurationValidator
{
    public static CqrsLensSettings Validate(string path, CqrsLensConfiguration config)
    {
        var analysis = ValidateAnalysisMode(path, config.Analysis);
        var render = ValidateRenderMode(path, config.Render);
        var markers = ValidateMarkerNames(path, "markers", config.Markers);
        var watermark = ValidateWatermark(path, config.Watermark);
        var handlerMarkers = ValidateMarkerNames(path, "handlerMarkers", config.HandlerMarkers);
        var include = ValidatePatterns(path, "include", config.Include);
        var exclude = ValidatePatterns(path, "exclude", config.Exclude);
        var layers = ValidateLayers(path, config.Layers);
        var rules = ValidateRules(path, config.Rules);

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

    private static AnalysisMode? ValidateAnalysisMode(string path, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim().ToLowerInvariant() switch
        {
            "text" => AnalysisMode.Text,
            "roslyn" => AnalysisMode.Roslyn,
            _ => throw Invalid(path, $"unknown analysis mode '{value}' - use text or roslyn"),
        };
    }

    private static DiagramRenderMode? ValidateRenderMode(string path, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim().ToLowerInvariant() switch
        {
            "mermaid" => DiagramRenderMode.Mermaid,
            "svg" => DiagramRenderMode.Svg,
            _ => throw Invalid(path, $"unknown render mode '{value}' - use mermaid or svg"),
        };
    }

    private static string? ValidateWatermark(string path, string? value)
    {
        if (value is null)
            return null;

        if (string.IsNullOrWhiteSpace(value))
            throw Invalid(path, "watermark cannot be an empty value");

        return value.Trim();
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

    private static IReadOnlyDictionary<string, string> ValidateLayers(
        string path,
        IReadOnlyDictionary<string, string>? layers)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (project, layer) in layers ?? new Dictionary<string, string>())
        {
            if (string.IsNullOrWhiteSpace(project) || string.IsNullOrWhiteSpace(layer))
                throw Invalid(path, "layers require non-empty project names and layer names");

            result[project.Trim()] = layer.Trim();
        }

        return result;
    }

    private static IReadOnlyDictionary<string, CheckSeverity?> ValidateRules(
        string path,
        IReadOnlyDictionary<string, string>? rules)
    {
        var result = new Dictionary<string, CheckSeverity?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (code, severity) in rules ?? new Dictionary<string, string>())
        {
            var known = KnownRules.All.FirstOrDefault(rule =>
                string.Equals(rule, code, StringComparison.OrdinalIgnoreCase));
            if (known is null)
            {
                throw Invalid(
                    path,
                    $"unknown rule '{code}' - known rules: {string.Join(", ", KnownRules.All)}");
            }

            result[known] = severity.Trim().ToLowerInvariant() switch
            {
                "off" => null,
                "warning" => CheckSeverity.Warning,
                "error" => CheckSeverity.Error,
                _ => throw Invalid(
                    path,
                    $"invalid severity '{severity}' for rule '{code}' - use off, warning or error"),
            };
        }

        return result;
    }

    private static ConfigurationException Invalid(string path, string message)
        => new($"invalid config '{path}': {message}");
}
