namespace CqrsLens;

/// <summary>
/// Builds the declared-vs-actual configuration view: the settings the scan ran
/// under (from the resolved configuration carried on the result) beside what the
/// scan actually found under them. Presentation only; it reads the existing
/// confidence rule and findings, and reinterprets nothing.
/// </summary>
internal static class ConfigurationView
{
    internal sealed record Row(string Label, string Value);

    internal sealed record Summary(
        IReadOnlyList<Row> Declared,
        string LayerStatus,
        string LayerChecks,
        int RequestsDiscovered,
        string Conventions);

    private static readonly string[] LayerRuleCodes =
    {
        ArchitectureChecker.LayerViolationCode,
        ArchitectureChecker.ApplicationLayerViolationCode,
        ArchitectureChecker.AmbiguousProjectNameCode,
        ArchitectureChecker.CircularProjectDependencyCode,
    };

    internal static Summary Build(AnalysisResult result)
    {
        var provenance = result.Configuration ?? ConfigurationProvenance.Empty;

        var declared = new List<Row>
        {
            new("Config file", provenance.HasConfigFile
                ? provenance.ConfigFilePath!
                : "No config file found"),
            new("Request markers", List(provenance.DeclaredMarkers)),
            new("Handler markers", List(provenance.DeclaredHandlerMarkers)),
            new("Layer overrides", Map(provenance.LayerOverrides, " → ")),
            new("Include globs", List(provenance.IncludeGlobs)),
            new("Exclude globs", List(provenance.ExcludeGlobs)),
            new("Rule severity overrides", RuleOverrides(provenance.RuleOverrides)),
        };

        var layerStatus = result.HasConfiguredLayers
            ? "configured"
            : LayerModelConfidence.IsAsserted(result.Projects) ? "asserted" : "name-guessed";

        var layerChecks = layerStatus switch
        {
            "configured" => "Layer rules are configured and asserted as errors. " + LayerFindings(result),
            "asserted" => "Layer rules are asserted as errors. " + LayerFindings(result),
            _ => "The layer model is name-guessed, so layer rules are reported at warning severity instead of errors. "
                 + LayerFindings(result),
        };

        var conventions = result.Conventions.Count > 0
            ? string.Join(", ", result.Conventions.OrderBy(c => c, StringComparer.Ordinal))
            : "none detected";

        return new Summary(declared, layerStatus, layerChecks, result.Requests.Count, conventions);
    }

    private static string List(IReadOnlyList<string> values)
        => values.Count == 0
            ? "none declared"
            : string.Join(", ", values.OrderBy(v => v, StringComparer.Ordinal));

    private static string Map(IReadOnlyDictionary<string, string> values, string separator)
        => values.Count == 0
            ? "none declared"
            : string.Join("; ", values
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => $"{kv.Key}{separator}{kv.Value}"));

    private static string RuleOverrides(IReadOnlyDictionary<string, CheckSeverity?> overrides)
    {
        if (overrides.Count == 0)
            return "none declared";

        var groups = overrides.Values
            .GroupBy(severity => severity switch
            {
                null => "off",
                CheckSeverity.Warning => "warning",
                CheckSeverity.Error => "error",
                _ => "unknown",
            })
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"{g.Count()} {g.Key}");
        return $"{overrides.Count} declared ({string.Join(", ", groups)})";
    }

    private static string LayerFindings(AnalysisResult result)
    {
        var findings = result.Checks
            .Where(c => LayerRuleCodes.Contains(c.Code, StringComparer.Ordinal))
            .GroupBy(c => (c.Code, c.Severity))
            .OrderBy(g => g.Key.Severity == CheckSeverity.Error ? 0 : 1)
            .ThenByDescending(g => g.Count())
            .ThenBy(g => g.Key.Code, StringComparer.Ordinal)
            .Select(g => $"{g.Count()} × {g.Key.Code} ({g.Key.Severity.ToString().ToLowerInvariant()})")
            .ToList();
        return findings.Count == 0
            ? "No layer findings."
            : string.Join(", ", findings) + ".";
    }
}
