namespace CqrsLens;

public sealed class ProjectInfo
{
    public required string Name { get; init; }
    public required string FilePath { get; init; }
    public required string DirectoryPath { get; init; }
    public string Layer { get; set; } = "Other";

    // Raw Include values as written in the .csproj.
    public List<string> ReferenceIncludes { get; } = new();

    // ReferenceIncludes resolved to project names found in the same scan.
    public List<string> ReferenceNames { get; } = new();

    // PackageReference Include values evaluated from Directory.Build.props/targets and .csproj.
    public List<string> PackageReferences { get; } = new();

    // Global <Using> items evaluated from Directory.Build.props/targets and .csproj.
    public List<UsingDirective> ProjectUsings { get; } = new();

    // True when <ImplicitUsings> is enable/true in Directory.Build.props/targets or .csproj.
    public bool ImplicitUsings { get; set; }
}
