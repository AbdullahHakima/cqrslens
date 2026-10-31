using System.Security;

namespace CqrsLens;

/// <summary>
/// Filesystem discovery of project and source files, skipping build/tooling folders and
/// reparse points.
/// </summary>
internal static class ProjectFileDiscovery
{
    private static readonly string[] SkippedFolders = { "bin", "obj", ".git" };

    private static readonly EnumerationOptions DiscoveryOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        // Do not follow symlinks/junctions; this also prevents reparse-point loops.
        AttributesToSkip = FileAttributes.ReparsePoint,
        ReturnSpecialDirectories = false,
    };

    /// <summary>
    /// True when any path segment is a build/tooling folder. Callers scanning a
    /// specific root should use the root-relative overload so that a root located
    /// beneath a folder named bin/obj is still scanned.
    /// </summary>
    internal static bool IsSkippedPath(string path)
        => SplitSegments(path).Any(s => SkippedFolders.Contains(s, StringComparer.OrdinalIgnoreCase));

    /// <summary>True when a path is skipped relative to <paramref name="root"/>.</summary>
    internal static bool IsSkippedPath(string path, string root)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return IsSkippedPath(relative);
    }

    internal static List<string> FindProjectFiles(string root)
        => FindFiles(root, "*.csproj");

    internal static List<string> FindSourceFiles(string root)
        => FindFiles(root, "*.cs");

    private static List<string> FindFiles(string root, string pattern)
    {
        if (!Directory.Exists(root)) return new List<string>();
        var fullRoot = Path.GetFullPath(root);

        try
        {
            return Directory
                .EnumerateFiles(fullRoot, pattern, DiscoveryOptions)
                .Where(path => !IsSkippedPath(Path.GetRelativePath(fullRoot, path)))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            // A partially inaccessible tree should yield what can be discovered, not
            // abort the whole scan. EnumerationOptions.IgnoreInaccessible handles the
            // common case; this is the final fallback for the root itself.
            return new List<string>();
        }
    }

    private static IEnumerable<string> SplitSegments(string path)
        => path.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
}
