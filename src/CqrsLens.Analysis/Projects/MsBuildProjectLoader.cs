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

            if (FindBuildFile(projectDir, "packages.config") is { } packagesConfigPath)
                EvaluatePackagesConfig(packagesConfigPath, info);

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
            var doc = SafeXmlLoader.LoadDocument(xmlPath);
            var xmlDir = Path.GetDirectoryName(xmlPath) ?? info.DirectoryPath;

            if (isCsproj && doc.Root is { } rootEl)
                AppendSdk(info, rootEl.Attribute("Sdk")?.Value?.Trim());

            foreach (var el in doc.Descendants())
            {
                switch (el.Name.LocalName)
                {
                    case "Sdk":
                    {
                        if (isCsproj)
                            AppendSdk(info, el.Attribute("Name")?.Value?.Trim());
                        break;
                    }

                    case "Import":
                    {
                        if (isCsproj)
                            AppendSdk(info, el.Attribute("Sdk")?.Value?.Trim());
                        break;
                    }

                    case "OutputType":
                    {
                        var val = el.Value.Trim();
                        if (!string.IsNullOrWhiteSpace(val) && !val.Contains('$', StringComparison.Ordinal))
                            info.OutputType = val;
                        break;
                    }

                    case "TargetFramework":
                    case "TargetFrameworks":
                    {
                        foreach (var tfm in el.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        {
                            if (!string.IsNullOrWhiteSpace(tfm) &&
                                !info.TargetFrameworks.Contains(tfm, StringComparer.OrdinalIgnoreCase))
                            {
                                info.TargetFrameworks.Add(tfm);
                            }
                        }
                        break;
                    }

                    case "TargetFrameworkVersion":
                    {
                        if (NormalizeTargetFrameworkVersion(el.Value) is { } tfm &&
                            !info.TargetFrameworks.Contains(tfm, StringComparer.OrdinalIgnoreCase))
                        {
                            info.TargetFrameworks.Add(tfm);
                        }
                        break;
                    }

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

                        var effectiveInclude = isCsproj
                            ? raw
                            : ResolveRelativeInclude(xmlDir, raw, info.DirectoryPath);

                        if (CombineConditions(el) is { } condition)
                        {
                            var conditional = new ConditionalProjectReference(effectiveInclude, condition);
                            if (!info.ConditionalReferences.Contains(conditional))
                                info.ConditionalReferences.Add(conditional);
                        }
                        else
                        {
                            info.ReferenceIncludes.Add(effectiveInclude);
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

    private static void EvaluatePackagesConfig(string xmlPath, ProjectInfo info)
    {
        try
        {
            var doc = SafeXmlLoader.LoadDocument(xmlPath);
            foreach (var el in doc.Descendants())
            {
                if (!string.Equals(el.Name.LocalName, "package", StringComparison.Ordinal))
                    continue;

                var pkg = el.Attribute("id")?.Value?.Trim();
                if (!string.IsNullOrWhiteSpace(pkg) &&
                    !info.PackageReferences.Contains(pkg, StringComparer.OrdinalIgnoreCase))
                {
                    info.PackageReferences.Add(pkg);
                }
            }
        }
        catch
        {
            // A malformed packages.config should not abort the scan.
        }
    }

    private static string? NormalizeTargetFrameworkVersion(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.Length == 0 || trimmed.Contains('$', StringComparison.Ordinal))
            return null;

        if (trimmed.Length >= 2 &&
            (trimmed[0] is 'v' or 'V') &&
            char.IsDigit(trimmed[1]))
        {
            var numeric = trimmed[1..];
            var valid = true;
            foreach (var ch in numeric)
            {
                if (!char.IsDigit(ch) && ch != '.')
                {
                    valid = false;
                    break;
                }
            }

            if (valid)
                return "net" + numeric.Replace(".", string.Empty, StringComparison.Ordinal);
        }

        return trimmed;
    }

    private static string StripGlobalPrefix(string value)
        => value.StartsWith("global::", StringComparison.Ordinal)
            ? value[8..].Trim()
            : value;

    private static string ResolveRelativeInclude(string xmlDir, string rawInclude, string projectDir)
    {
        try
        {
            var normalized = rawInclude.Replace('\\', Path.DirectorySeparatorChar)
                                       .Replace('/', Path.DirectorySeparatorChar);
            var fullTarget = Path.GetFullPath(Path.Combine(xmlDir, normalized));
            return Path.GetRelativePath(projectDir, fullTarget);
        }
        catch
        {
            return rawInclude;
        }
    }

    private static string? CombineConditions(XElement el)
    {
        var parts = new List<string>();
        foreach (var ancestor in el.Ancestors()
            .Where(a => a.Name.LocalName is "ItemGroup" or "When")
            .Reverse())
        {
            var condition = ancestor.Attribute("Condition")?.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(condition))
                parts.Add(condition);
        }

        var selfCondition = el.Attribute("Condition")?.Value?.Trim();
        if (!string.IsNullOrWhiteSpace(selfCondition))
            parts.Add(selfCondition);

        return parts.Count == 0 ? null : string.Join(" && ", parts);
    }

    private static void AppendSdk(ProjectInfo info, string? rawSdk)
    {
        if (string.IsNullOrWhiteSpace(rawSdk))
            return;

        foreach (var part in rawSdk.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Contains('$', StringComparison.Ordinal))
                continue;

            if (string.IsNullOrEmpty(info.Sdk))
            {
                info.Sdk = part;
                continue;
            }

            var existing = info.Sdk.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (!existing.Contains(part, StringComparer.OrdinalIgnoreCase))
                info.Sdk = $"{info.Sdk};{part}";
        }
    }
}
