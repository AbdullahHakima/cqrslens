using System.Text.RegularExpressions;

namespace CqrsLens;

/// <summary>
/// Detects which built-in mediator conventions apply to the scanned source by looking at
/// package references, project usings, and using directives.
/// </summary>
internal static partial class MediatorLibraryDetector
{
    internal static IReadOnlyList<string> Detect(
        IReadOnlyList<ProjectInfo> projects,
        IReadOnlyList<SourceFile> files)
    {
        var detected = new List<string>();
        if (UsesMediator(projects, files)) detected.Add(MediatorConventions.MediatorName);
        if (UsesWolverine(projects, files)) detected.Add(MediatorConventions.WolverineName);
        return detected;
    }

    [GeneratedRegex(@"^\s*(?:global\s+)?using\s+Mediator\s*;", RegexOptions.Multiline)]
    private static partial Regex MediatorUsingRegex();

    [GeneratedRegex(@"^\s*(?:global\s+)?using\s+Wolverine(?:\.[\w.]+)?\s*;", RegexOptions.Multiline)]
    private static partial Regex WolverineUsingRegex();

    private static bool UsesMediator(IReadOnlyList<ProjectInfo> projects, IReadOnlyList<SourceFile> files)
        => projects.Any(p =>
               p.PackageReferences.Any(r =>
                   r.Equals("Mediator.Abstractions", StringComparison.OrdinalIgnoreCase) ||
                   r.Equals("Mediator.SourceGenerator", StringComparison.OrdinalIgnoreCase)) ||
               p.ProjectUsings.Any(u => u.Target == "Mediator")) ||
           files.Any(f => MediatorUsingRegex().IsMatch(f.Code));

    private static bool UsesWolverine(IReadOnlyList<ProjectInfo> projects, IReadOnlyList<SourceFile> files)
        => projects.Any(p =>
               p.PackageReferences.Any(r => r.StartsWith("WolverineFx", StringComparison.OrdinalIgnoreCase)) ||
               p.ProjectUsings.Any(u => u.Target.StartsWith("Wolverine", StringComparison.Ordinal))) ||
           files.Any(f => WolverineUsingRegex().IsMatch(f.Code));
}
