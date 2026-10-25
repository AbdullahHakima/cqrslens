using System.Security;
using System.Xml.Linq;

namespace CqrsLens;

/// <summary>Discovery and parsing of .csproj and Directory.Build.props/targets files.</summary>
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
    /// Loads every project under <paramref name="root"/> and resolves ProjectReference,
    /// PackageReference, ImplicitUsings and Using items from ancestor
    /// Directory.Build.props/targets files and the .csproj itself.
    /// </summary>
    public static List<ProjectInfo> LoadProjects(string root)
    {
        var files = FindProjectFiles(root);
        var fullRoot = Directory.Exists(root) ? Path.GetFullPath(root) : root;
        var byFullPath = new Dictionary<string, ProjectInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var projectDir = Path.GetDirectoryName(file) ?? fullRoot;
            var info = new ProjectInfo
            {
                Name = Path.GetFileNameWithoutExtension(file),
                FilePath = file,
                DirectoryPath = projectDir,
            };

            var hierarchyDirs = GetHierarchyDirectories(fullRoot, projectDir);
            foreach (var dir in hierarchyDirs)
            {
                if (FindBuildFile(dir, "Directory.Build.props") is { } propsPath)
                    EvaluateMsBuildFile(propsPath, info, isCsproj: false);
            }

            EvaluateMsBuildFile(file, info, isCsproj: true);

            foreach (var dir in hierarchyDirs)
            {
                if (FindBuildFile(dir, "Directory.Build.targets") is { } targetsPath)
                    EvaluateMsBuildFile(targetsPath, info, isCsproj: false);
            }

            info.PackageReferences.Sort(StringComparer.OrdinalIgnoreCase);
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

    private static List<string> GetHierarchyDirectories(string fullRoot, string projectDir)
    {
        var dirs = new List<string>();
        try
        {
            var rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(fullRoot));
            var current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectDir));

            while (!string.IsNullOrEmpty(current))
            {
                dirs.Add(current);
                if (string.Equals(current, rootPath, StringComparison.OrdinalIgnoreCase))
                    break;

                var parent = Path.GetDirectoryName(current);
                if (string.IsNullOrEmpty(parent) ||
                    string.Equals(parent, current, StringComparison.OrdinalIgnoreCase) ||
                    !parent.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
                    break;

                current = parent;
            }
        }
        catch
        {
            if (dirs.Count == 0)
                dirs.Add(projectDir);
        }

        dirs.Reverse();
        return dirs;
    }

    private static string? FindBuildFile(string directory, string fileName)
    {
        try
        {
            var candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate))
                return candidate;

            return Directory.EnumerateFiles(directory)
                .FirstOrDefault(f => string.Equals(
                    Path.GetFileName(f), fileName, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    private static void EvaluateMsBuildFile(string xmlPath, ProjectInfo info, bool isCsproj)
    {
        try
        {
            var doc = XDocument.Load(xmlPath);
            var xmlDir = Path.GetDirectoryName(xmlPath) ?? info.DirectoryPath;

            foreach (var el in doc.Descendants())
            {
                switch (el.Name.LocalName)
                {
                    case "ImplicitUsings":
                    {
                        var val = el.Value.Trim();
                        if (string.Equals(val, "enable", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(val, "true", StringComparison.OrdinalIgnoreCase))
                        {
                            info.ImplicitUsings = true;
                        }
                        else if (string.Equals(val, "disable", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(val, "false", StringComparison.OrdinalIgnoreCase))
                        {
                            info.ImplicitUsings = false;
                        }
                        break;
                    }

                    case "ProjectReference":
                    {
                        var raw = (el.Attribute("Include")?.Value ?? el.Attribute("Update")?.Value)?.Trim();
                        if (string.IsNullOrWhiteSpace(raw))
                            break;

                        if (isCsproj)
                        {
                            info.ReferenceIncludes.Add(raw);
                        }
                        else
                        {
                            try
                            {
                                var normalized = raw.Replace('\\', Path.DirectorySeparatorChar)
                                                    .Replace('/', Path.DirectorySeparatorChar);
                                var fullTarget = Path.GetFullPath(Path.Combine(xmlDir, normalized));
                                var relativeToProject = Path.GetRelativePath(info.DirectoryPath, fullTarget);
                                info.ReferenceIncludes.Add(relativeToProject);
                            }
                            catch
                            {
                                info.ReferenceIncludes.Add(raw);
                            }
                        }
                        break;
                    }

                    case "PackageReference":
                    {
                        var removed = el.Attribute("Remove")?.Value?.Trim();
                        if (!string.IsNullOrWhiteSpace(removed))
                        {
                            info.PackageReferences.RemoveAll(p =>
                                string.Equals(p, removed, StringComparison.OrdinalIgnoreCase));
                        }

                        var pkg = (el.Attribute("Include")?.Value ?? el.Attribute("Update")?.Value)?.Trim();
                        if (!string.IsNullOrWhiteSpace(pkg) &&
                            !info.PackageReferences.Contains(pkg, StringComparer.OrdinalIgnoreCase))
                        {
                            info.PackageReferences.Add(pkg);
                        }
                        break;
                    }

                    case "Using":
                    {
                        var removed = el.Attribute("Remove")?.Value?.Trim();
                        if (!string.IsNullOrWhiteSpace(removed))
                        {
                            var normalizedRemove = StripGlobalPrefix(removed);
                            info.ProjectUsings.RemoveAll(u =>
                                string.Equals(u.Target, normalizedRemove, StringComparison.Ordinal));
                        }

                        var rawTarget = (el.Attribute("Include")?.Value ?? el.Attribute("Update")?.Value)?.Trim();
                        if (string.IsNullOrWhiteSpace(rawTarget))
                            break;

                        var target = StripGlobalPrefix(rawTarget);
                        if (target.Length == 0)
                            break;

                        var rawAlias = (el.Attribute("Alias")?.Value
                            ?? el.Elements().FirstOrDefault(e => e.Name.LocalName == "Alias")?.Value)
                            ?.Trim();
                        var alias = string.IsNullOrEmpty(rawAlias) ? null : rawAlias;

                        var rawStatic = (el.Attribute("Static")?.Value
                            ?? el.Elements().FirstOrDefault(e => e.Name.LocalName == "Static")?.Value)
                            ?.Trim();
                        var isStatic = string.Equals(rawStatic, "true", StringComparison.OrdinalIgnoreCase);

                        var directive = new UsingDirective(
                            target,
                            alias,
                            IsStatic: isStatic,
                            IsGlobal: true);

                        if (alias is not null)
                        {
                            info.ProjectUsings.RemoveAll(u =>
                                string.Equals(u.Alias, alias, StringComparison.Ordinal));
                        }

                        if (!info.ProjectUsings.Contains(directive))
                            info.ProjectUsings.Add(directive);
                        break;
                    }
                }
            }
        }
        catch
        {
            // A malformed MSBuild file should not abort the scan.
        }
    }

    private static string StripGlobalPrefix(string value)
        => value.StartsWith("global::", StringComparison.Ordinal)
            ? value[8..].Trim()
            : value;

    private static IEnumerable<string> SplitSegments(string path)
        => path.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
}
