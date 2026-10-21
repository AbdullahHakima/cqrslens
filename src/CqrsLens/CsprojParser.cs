using System.Xml.Linq;

namespace CqrsLens;

/// <summary>Discovery and parsing of .csproj files (project names + ProjectReference edges).</summary>
public static class CsprojParser
{
    private static readonly string[] SkippedFolders = { "bin", "obj", ".git" };

    public static bool IsSkippedPath(string path)
    {
        var segments = path.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(s => SkippedFolders.Contains(s, StringComparer.OrdinalIgnoreCase));
    }

    public static List<string> FindProjectFiles(string root)
    {
        return Directory
            .EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !IsSkippedPath(p))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static List<string> FindSourceFiles(string root)
    {
        return Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(p => !IsSkippedPath(p))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
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
                var normalized = include.Replace('\\', Path.DirectorySeparatorChar)
                                         .Replace('/', Path.DirectorySeparatorChar);
                var targetPath = Path.GetFullPath(Path.Combine(info.DirectoryPath, normalized));
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
}
