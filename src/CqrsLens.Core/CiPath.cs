namespace CqrsLens;

/// <summary>Path presentation shared by SARIF and GitHub annotations.</summary>
public static class CiPath
{
    /// <summary>
    /// Converts a scan-root-relative source file to a path relative to the CI
    /// workspace. When <paramref name="ciRoot"/> is null (or is not an ancestor
    /// of the file), the scan-relative path is returned unchanged.
    /// </summary>
    public static string Relative(AnalysisResult result, string file, string? ciRoot)
    {
        var normalized = file.Replace('\\', '/');
        if (string.IsNullOrEmpty(ciRoot))
            return normalized;

        var absolute = Path.GetFullPath(Path.Combine(result.RootPath, file));
        var root = Path.GetFullPath(ciRoot);
        var candidate = Path.GetRelativePath(root, absolute);
        if (candidate == ".." ||
            candidate.StartsWith("../", StringComparison.Ordinal) ||
            candidate.StartsWith("..\\", StringComparison.Ordinal))
        {
            return normalized;
        }

        return candidate.Replace('\\', '/');
    }

    public static string Uri(AnalysisResult result, SourceLocation location, string? ciRoot)
        => string.Join(
            "/",
            Relative(result, location.File, ciRoot)
                .Split('/')
                .Select(System.Uri.EscapeDataString));
}
