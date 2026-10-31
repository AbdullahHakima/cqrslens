namespace CqrsLens;

/// <summary>Discovery and parsing of .csproj and Directory.Build.props/targets files.</summary>
public static class CsprojParser
{
    /// <summary>
    /// True when any path segment is a build/tooling folder. Callers scanning a
    /// specific root should use the root-relative overload so that a root located
    /// beneath a folder named bin/obj is still scanned.
    /// </summary>
    public static bool IsSkippedPath(string path)
        => ProjectFileDiscovery.IsSkippedPath(path);

    /// <summary>True when a path is skipped relative to <paramref name="root"/>.</summary>
    public static bool IsSkippedPath(string path, string root)
        => ProjectFileDiscovery.IsSkippedPath(path, root);

    public static List<string> FindProjectFiles(string root)
        => ProjectFileDiscovery.FindProjectFiles(root);

    public static List<string> FindSourceFiles(string root)
        => ProjectFileDiscovery.FindSourceFiles(root);

    /// <summary>Reads the raw ProjectReference Include/Update values from a .csproj file.</summary>
    public static List<string> GetProjectReferenceIncludes(string csprojPath)
        => ProjectReferenceResolver.ReadIncludes(csprojPath);

    /// <summary>
    /// Loads every project under <paramref name="root"/> and resolves ProjectReference,
    /// PackageReference, ImplicitUsings and Using items from ancestor
    /// Directory.Build.props/targets files and the .csproj itself.
    /// </summary>
    public static List<ProjectInfo> LoadProjects(string root)
        => MsBuildProjectLoader.Load(root);
}
