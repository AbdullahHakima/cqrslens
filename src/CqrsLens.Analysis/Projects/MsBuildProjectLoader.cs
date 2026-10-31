using System.Xml.Linq;

namespace CqrsLens;

/// <summary>
/// Loads every project under a root and evaluates the .csproj plus ancestor
/// Directory.Build.props/targets files into <see cref="ProjectInfo"/> values.
/// </summary>
internal static class MsBuildProjectLoader
{
    internal static List<ProjectInfo> Load(string root)
    {
        var files = ProjectFileDiscovery.FindProjectFiles(root);
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

        ProjectReferenceResolver.Resolve(byFullPath);

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
}
