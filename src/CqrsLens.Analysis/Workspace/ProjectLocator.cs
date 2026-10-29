namespace CqrsLens;

/// <summary>Finds the project that owns a source file, preferring the deepest directory.</summary>
internal static class ProjectLocator
{
    public static ProjectInfo? FindOwningProject(IReadOnlyList<ProjectInfo> projects, string file)
    {
        ProjectInfo? best = null;
        foreach (var project in projects)
        {
            var directory = project.DirectoryPath.TrimEnd(Path.DirectorySeparatorChar) +
                            Path.DirectorySeparatorChar;
            if (file.StartsWith(directory, StringComparison.OrdinalIgnoreCase) &&
                (best is null || directory.Length > best.DirectoryPath.Length))
            {
                best = project;
            }
        }

        return best;
    }
}
