namespace CqrsLens;

/// <summary>Discovered test project metadata and its detected testing stack.</summary>
public sealed record TestProjectInfo(
    string Name,
    string FilePath,
    IReadOnlyList<string> Frameworks,
    IReadOnlyList<string> MockingLibraries,
    IReadOnlyList<string> AssertionLibraries,
    IReadOnlyList<string> ReferencedProductionProjects);
