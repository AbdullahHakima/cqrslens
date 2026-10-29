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
        { "Api", "Web", "UI", "Application", "Infrastructure", "Domain", "Tests" };

    public static string Classify(string projectName, string projectDirectory, string rootPath)
    {
        if (LooksLikeTestProject(projectName, projectDirectory, rootPath))
            return "Tests";

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

    /// <summary>
    /// Test projects are not production layers: they may legitimately reference any
    /// layer, so flagging them as e.g. Domain produces false LayerViolation errors.
    /// </summary>
    private static bool LooksLikeTestProject(string projectName, string projectDirectory, string rootPath)
    {
        var relative = Path.GetRelativePath(rootPath, projectDirectory);
        if (relative.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
            .Any(s => s.Equals("test", StringComparison.OrdinalIgnoreCase) ||
                      s.Equals("tests", StringComparison.OrdinalIgnoreCase)))
            return true;

        foreach (var segment in projectName.Split(new[] { '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment.Equals("Test", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("Tests", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("Specs", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("Specifications", StringComparison.OrdinalIgnoreCase))
                return true;
            if (segment.EndsWith("Tests", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
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
