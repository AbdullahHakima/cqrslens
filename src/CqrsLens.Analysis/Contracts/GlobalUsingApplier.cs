namespace CqrsLens;

/// <summary>
/// Applies project-level and file-level global usings to every file in the project
/// before contract resolution runs. Returns the directives injected per file path.
/// </summary>
internal static class GlobalUsingApplier
{
    public static Dictionary<string, List<UsingDirective>> Apply(
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan)> ordered,
        IReadOnlyList<ProjectInfo> projects)
    {
        var globalsByProject = new Dictionary<string, List<UsingDirective>>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in projects)
        {
            if (project.ProjectUsings.Count == 0) continue;
            globalsByProject[project.Name] = project.ProjectUsings.Distinct().ToList();
        }

        foreach (var (file, scan) in ordered)
        {
            var key = file.ProjectName ?? "";
            foreach (var directive in scan.Usings.Where(u => u.IsGlobal))
            {
                if (!globalsByProject.TryGetValue(key, out var list))
                    globalsByProject[key] = list = new List<UsingDirective>();
                if (!list.Contains(directive))
                    list.Add(directive);
            }
        }

        var injectedByPath = new Dictionary<string, List<UsingDirective>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (file, scan) in ordered)
        {
            if (!globalsByProject.TryGetValue(file.ProjectName ?? "", out var globals))
                continue;

            List<UsingDirective>? injected = null;
            foreach (var global in globals)
            {
                if (!scan.Usings.Contains(global))
                {
                    scan.Usings.Add(global);
                    injected ??= new List<UsingDirective>();
                    injected.Add(global);
                }
            }

            if (injected is { Count: > 0 })
                injectedByPath[file.Path] = injected;
        }

        return injectedByPath;
    }
}
