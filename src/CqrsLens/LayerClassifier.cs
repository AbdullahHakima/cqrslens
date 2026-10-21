namespace CqrsLens;

/// <summary>
/// Groups projects into Clean Architecture layers by naming convention
/// (Domain / Application / Infrastructure / Api / Web / UI). When a project name
/// matches no convention, it falls back to its top-level folder under the scan root.
/// </summary>
public static class LayerClassifier
{
    private static readonly string[] ConventionLayers =
        { "Domain", "Application", "Infrastructure", "Api", "Web", "UI" };

    /// <summary>Canonical display order for known layers.</summary>
    public static readonly string[] LayerOrder =
        { "Api", "Web", "UI", "Application", "Infrastructure", "Domain" };

    public static string Classify(string projectName, string projectDirectory, string rootPath)
    {
        var segments = projectName.Split(new[] { '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var layer in ConventionLayers)
        {
            if (segments.Any(s => string.Equals(s, layer, StringComparison.OrdinalIgnoreCase)))
                return layer;
        }

        var relative = Path.GetRelativePath(rootPath, projectDirectory);
        var first = relative.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                            .FirstOrDefault();
        return string.IsNullOrWhiteSpace(first) || first == "." ? "Other" : first;
    }

    public static IEnumerable<string> OrderLayers(IEnumerable<string> layers)
    {
        var distinct = layers.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var known = LayerOrder.Where(l => distinct.Contains(l, StringComparer.OrdinalIgnoreCase));
        var rest = distinct.Where(l => !LayerOrder.Contains(l, StringComparer.OrdinalIgnoreCase))
                           .OrderBy(l => l, StringComparer.OrdinalIgnoreCase);
        return known.Concat(rest);
    }
}
