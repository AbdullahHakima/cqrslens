namespace CqrsLens;

/// <summary>
/// Resolves a project's raw ProjectReference includes to the names of other loaded projects.
/// </summary>
internal static class ProjectReferenceResolver
{
    internal static void Resolve(IReadOnlyDictionary<string, ProjectInfo> projectsByFullPath)
    {
        foreach (var info in projectsByFullPath.Values)
        {
            foreach (var include in info.ReferenceIncludes)
            {
                if (ResolvePath(info.DirectoryPath, include) is { } targetPath &&
                    projectsByFullPath.TryGetValue(targetPath, out var target))
                {
                    if (!info.ReferenceNames.Contains(target.Name))
                        info.ReferenceNames.Add(target.Name);
                }
            }
            info.ReferenceNames.Sort(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < info.ConditionalReferences.Count; i++)
            {
                var conditional = info.ConditionalReferences[i];
                if (ResolvePath(info.DirectoryPath, conditional.Include) is { } targetPath &&
                    projectsByFullPath.TryGetValue(targetPath, out var target))
                {
                    info.ConditionalReferences[i] = conditional with { ResolvedProjectName = target.Name };
                }
            }
        }
    }

    internal static string? ResolvePath(string projectDirectory, string include)
    {
        try
        {
            var normalized = include.Replace('\\', Path.DirectorySeparatorChar)
                                    .Replace('/', Path.DirectorySeparatorChar);
            return Path.GetFullPath(Path.Combine(projectDirectory, normalized));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>Reads the raw ProjectReference Include/Update values from a .csproj file.</summary>
    internal static List<string> ReadIncludes(string csprojPath)
    {
        var includes = new List<string>();
        try
        {
            var doc = SafeXmlLoader.LoadDocument(csprojPath);
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
}
