using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

public class CsprojParserTests : IDisposable
{
    private readonly string _root;

    public CsprojParserTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cqrslens-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private string WriteProject(string folder, string name, params string[] references)
    {
        var dir = Path.Combine(_root, folder);
        Directory.CreateDirectory(dir);
        var refs = string.Concat(references.Select(r =>
            $"    <ProjectReference Include=\"{r}\" />\n"));
        var itemGroup = references.Length == 0
            ? ""
            : $"  <ItemGroup>\n{refs}  </ItemGroup>\n";
        var path = Path.Combine(dir, name + ".csproj");
        File.WriteAllText(path,
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" + itemGroup + "</Project>\n");
        return path;
    }

    [Fact]
    public void GetProjectReferenceIncludes_ReadsRawIncludes()
    {
        var path = WriteProject("App", "App",
            @"..\Domain\Domain.csproj", @"..\Shared\Shared.csproj");

        var includes = CsprojParser.GetProjectReferenceIncludes(path);

        Assert.Equal(2, includes.Count);
        Assert.Contains(@"..\Domain\Domain.csproj", includes);
        Assert.Contains(@"..\Shared\Shared.csproj", includes);
    }

    [Fact]
    public void LoadProjects_ResolvesReferencesToProjectNames()
    {
        WriteProject("Domain", "Domain");
        WriteProject("App", "App", @"..\Domain\Domain.csproj");

        var projects = CsprojParser.LoadProjects(_root);

        Assert.Equal(2, projects.Count);
        var app = projects.Single(p => p.Name == "App");
        Assert.Equal(new[] { "Domain" }, app.ReferenceNames);
        var domain = projects.Single(p => p.Name == "Domain");
        Assert.Empty(domain.ReferenceNames);
    }

    [Fact]
    public void LoadProjects_SkipsBinAndObjFolders()
    {
        WriteProject("Real", "Real");
        WriteProject(Path.Combine("Real", "obj"), "Ghost");

        var projects = CsprojParser.LoadProjects(_root);

        Assert.Single(projects);
        Assert.Equal("Real", projects[0].Name);
    }

    [Fact]
    public void GetProjectReferenceIncludes_MalformedXml_ReturnsEmpty()
    {
        var dir = Path.Combine(_root, "Broken");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "Broken.csproj");
        File.WriteAllText(path, "<Project><unclosed>");

        Assert.Empty(CsprojParser.GetProjectReferenceIncludes(path));
    }
}
