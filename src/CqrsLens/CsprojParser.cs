using System.Security;
using System.Xml.Linq;

namespace CqrsLens;

/// <summary>Discovery and parsing of .csproj files (project names + ProjectReference edges).</summary>
public static class CsprojParser
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
    public static bool IsSkippedPath(string path)
        => SplitSegments(path).Any(s => SkippedFolders.Contains(s, StringComparer.OrdinalIgnoreCase));

    /// <summary>True when a path is skipped relative to <paramref name="root"/>.</summary>
    public static bool IsSkippedPath(string path, string root)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return IsSkippedPath(relative);
    }

    public static List<string> FindProjectFiles(string root)
        => FindFiles(root, "*.csproj");

    public static List<string> FindSourceFiles(string root)
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

    /// <summary>Reads the raw ProjectReference Include/Update values from a .csproj file.</summary>
    public static List<string> GetProjectReferenceIncludes(string csprojPath)
    {
        var includes = new List<string>();
        try
        {
            var doc = XDocument.Load(csprojPath);
            foreach (var el in doc.Descendants().Where(e => e.Name.LocalName == "ProjectReference"))
            {
                var value = el.Attribute("Include")?.Value ?? el.Attribute("Update")?.Value;
                if (!string.IsNullOrWhiteSpace(value))
                    includes.Add(value.Trim());
            }
        }
        catch
        {
            // A malformed .csproj should not kill the scan; it just yields no references.
        }
        return includes;
    }

    /// <summary>
    /// Loads every project under <paramref name="root"/> and resolves ProjectReference
    /// paths to the names of projects that were found in the same scan.
    /// </summary>
    public static List<ProjectInfo> LoadProjects(string root)
    {
        var files = FindProjectFiles(root);
        var byFullPath = new Dictionary<string, ProjectInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var info = new ProjectInfo
            {
                Name = Path.GetFileNameWithoutExtension(file),
                FilePath = file,
                DirectoryPath = Path.GetDirectoryName(file) ?? root,
            };
            info.ReferenceIncludes.AddRange(GetProjectReferenceIncludes(file));
            byFullPath[Path.GetFullPath(file)] = info;
        }

        foreach (var info in byFullPath.Values)
        {
            foreach (var include in info.ReferenceIncludes)
            {
                string targetPath;
                try
                {
                    var normalized = include.Replace('\\', Path.DirectorySeparatorChar)
                                             .Replace('/', Path.DirectorySeparatorChar);
                    targetPath = Path.GetFullPath(Path.Combine(info.DirectoryPath, normalized));
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    continue;
                }

                if (byFullPath.TryGetValue(targetPath, out var target))
                {
                    if (!info.ReferenceNames.Contains(target.Name))
                        info.ReferenceNames.Add(target.Name);
                }
            }
            info.ReferenceNames.Sort(StringComparer.OrdinalIgnoreCase);
        }

        return byFullPath.Values
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> SplitSegments(string path)
        => path.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
}
