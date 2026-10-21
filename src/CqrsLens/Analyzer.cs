namespace CqrsLens;

/// <summary>Runs the full static analysis over a directory tree.</summary>
public static class Analyzer
{
    public static AnalysisResult Analyze(string rootPath)
    {
        var root = Path.GetFullPath(rootPath);
        var result = new AnalysisResult { RootPath = root };

        result.Projects.AddRange(CsprojParser.LoadProjects(root));
        foreach (var project in result.Projects)
            project.Layer = LayerClassifier.Classify(project.Name, project.DirectoryPath, root);

        foreach (var file in CsprojParser.FindSourceFiles(root))
        {
            var project = FindOwningProject(result.Projects, file);
            try
            {
                CSharpScanner.ScanFile(file, project?.Name, result);
            }
            catch
            {
                // An unreadable file should not abort the whole scan.
            }
        }

        Link(result);
        return result;
    }

    private static ProjectInfo? FindOwningProject(List<ProjectInfo> projects, string file)
    {
        ProjectInfo? best = null;
        foreach (var p in projects)
        {
            var dir = p.DirectoryPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (file.StartsWith(dir, StringComparison.OrdinalIgnoreCase) &&
                (best is null || dir.Length > best.DirectoryPath.Length))
                best = p;
        }
        return best;
    }

    /// <summary>Connects handlers and validators to the requests they serve.</summary>
    private static void Link(AnalysisResult result)
    {
        foreach (var handler in result.Handlers)
        {
            var request = result.Requests.FirstOrDefault(
                r => string.Equals(r.Name, handler.RequestName, StringComparison.Ordinal));
            if (request is not null && !request.Handlers.Contains(handler.Name))
                request.Handlers.Add(handler.Name);
        }

        foreach (var validator in result.Validators)
        {
            var request = result.Requests.FirstOrDefault(
                r => string.Equals(r.Name, validator.TargetType, StringComparison.Ordinal));
            if (request is not null && !request.Validators.Contains(validator.Name))
                request.Validators.Add(validator.Name);
        }

        result.Requests.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
    }
}
