namespace CqrsLens;

/// <summary>Turns a deserialized .cqrslens.json into validated <see cref="CqrsLensSettings"/>.</summary>
internal static class ConfigurationValidator
{
    public static CqrsLensSettings Validate(string path, CqrsLensConfiguration config)
    {
        var analysis = ValidateAnalysisMode(path, config.Analysis);
        var render = ValidateRenderMode(path, config.Render);
        var cqrsMapSplit = ValidateCqrsMapSplit(path, config.CqrsMapSplit);
        var markers = ValidateMarkerNames(path, "markers", config.Markers);
        var watermark = ValidateWatermark(path, config.Watermark);
        var handlerMarkers = ValidateMarkerNames(path, "handlerMarkers", config.HandlerMarkers);
        var include = ValidatePatterns(path, "include", config.Include);
        var exclude = ValidatePatterns(path, "exclude", config.Exclude);
        var layers = ValidateLayers(path, config.Layers);
        var rules = ValidateRules(path, config.Rules);
        var intent = ValidateIntent(path, config.Intent);

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
            watermark,
            cqrsMapSplit,
            intent);
    }

    private static CqrsMapSplitMode? ValidateCqrsMapSplit(string path, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return CqrsMapSplitValues.TryParse(value, out var mode)
            ? mode
            : throw Invalid(path, $"unknown cqrsMapSplit '{value}' - use {CqrsMapSplitValues.Accepted}");
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

    private static IntentSpecification? ValidateIntent(string path, IntentConfiguration? intent)
    {
        if (intent is null)
            return null;

        var moduleMap = new Dictionary<string, IntentModuleDefinition>(StringComparer.Ordinal);
        foreach (var (rawName, mod) in intent.Modules ?? new Dictionary<string, IntentModuleConfiguration>())
        {
            if (string.IsNullOrWhiteSpace(rawName))
                throw Invalid(path, "intent.modules keys cannot be empty");

            var name = rawName.Trim();
            if (mod is null)
                throw Invalid(path, $"intent.modules['{name}'] cannot be null");

            var projects = ValidateIntentSelectors(path, $"intent.modules['{name}'].projects", mod.Projects);
            var components = ValidateIntentSelectors(path, $"intent.modules['{name}'].components", mod.Components);
            if (projects.Count == 0 && components.Count == 0)
                throw Invalid(path, $"intent.modules['{name}'] must declare at least one project or component selector");

            if (!moduleMap.TryAdd(name, new IntentModuleDefinition(name, projects, components)))
                throw Invalid(path, $"duplicate intent module '{name}'");
        }

        var orderedModules = moduleMap.Values
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .ToArray();

        var boundaries = new List<IntentBoundaryRule>();
        foreach (var entry in intent.Boundaries ?? new List<IntentBoundaryConfiguration>())
        {
            if (entry is null)
                throw Invalid(path, "intent.boundaries cannot contain null entries");

            if (string.IsNullOrWhiteSpace(entry.From) || string.IsNullOrWhiteSpace(entry.To))
                throw Invalid(path, "intent.boundaries require non-empty 'from' and 'to' module names");

            var from = entry.From.Trim();
            var to = entry.To.Trim();
            if (!moduleMap.ContainsKey(from))
                throw Invalid(path, $"intent boundary 'from' references unknown module '{from}'");
            if (!moduleMap.ContainsKey(to))
                throw Invalid(path, $"intent boundary 'to' references unknown module '{to}'");
            if (string.Equals(from, to, StringComparison.Ordinal))
                throw Invalid(path, $"intent boundary '{from}' -> '{to}' cannot target the same module");

            var severity = ValidateIntentSeverity(path, $"intent.boundaries['{from}' -> '{to}'].severity", entry.Severity);
            boundaries.Add(new IntentBoundaryRule(from, to, severity));
        }

        IntentPublicSurface? publicSurface = null;
        if (intent.PublicSurface is not null)
        {
            if (intent.PublicSurface.Endpoints is null && intent.PublicSurface.Components is null)
                throw Invalid(path, "intent.publicSurface requires at least one of 'endpoints' or 'components'");

            var endpoints = intent.PublicSurface.Endpoints is null
                ? null
                : ValidateIntentSelectors(path, "intent.publicSurface.endpoints", intent.PublicSurface.Endpoints);
            var components = intent.PublicSurface.Components is null
                ? null
                : ValidateIntentSelectors(path, "intent.publicSurface.components", intent.PublicSurface.Components);
            var severity = ValidateIntentSeverity(path, "intent.publicSurface.severity", intent.PublicSurface.Severity);
            publicSurface = new IntentPublicSurface(endpoints, components, severity);
        }

        var ownership = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (rawModule, rawOwner) in intent.Ownership ?? new Dictionary<string, string>())
        {
            if (string.IsNullOrWhiteSpace(rawModule))
                throw Invalid(path, "intent.ownership keys cannot be empty");

            var moduleName = rawModule.Trim();
            if (!moduleMap.ContainsKey(moduleName))
                throw Invalid(path, $"intent.ownership references unknown module '{moduleName}'");

            if (string.IsNullOrWhiteSpace(rawOwner))
                throw Invalid(path, $"intent.ownership['{moduleName}'] cannot be empty");

            if (!ownership.TryAdd(moduleName, rawOwner.Trim()))
                throw Invalid(path, $"duplicate intent.ownership entry for module '{moduleName}'");
        }

        return new IntentSpecification(orderedModules, boundaries, publicSurface, ownership);
    }

    private static IReadOnlyList<string> ValidateIntentSelectors(
        string path,
        string property,
        IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
            return Array.Empty<string>();

        var result = new List<string>(values.Count);
        foreach (var raw in values)
        {
            if (string.IsNullOrWhiteSpace(raw))
                throw Invalid(path, $"{property} cannot contain an empty selector");

            var trimmed = raw.Trim();
            if (trimmed.IndexOfAny(['*', '?', '[', ']']) >= 0)
            {
                throw Invalid(
                    path,
                    $"{property} selector '{trimmed}' contains forbidden wildcard/glob characters (*, ?, [, ])");
            }

            result.Add(trimmed);
        }

        return result;
    }

    private static CheckSeverity ValidateIntentSeverity(string path, string property, string? value)
    {
        if (value is null)
            return CheckSeverity.Warning;

        if (string.IsNullOrWhiteSpace(value))
            throw Invalid(path, $"{property} cannot be empty - use warning or error");

        return value.Trim().ToLowerInvariant() switch
        {
            "warning" => CheckSeverity.Warning,
            "error" => CheckSeverity.Error,
            _ => throw Invalid(path, $"invalid severity '{value}' for {property} - use warning or error"),
        };
    }

    private static ConfigurationException Invalid(string path, string message)
        => new($"invalid config '{path}': {message}");
}
