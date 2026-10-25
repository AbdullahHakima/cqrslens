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
    public void LoadProjects_ScansRootThatLivesBeneathBinFolder()
    {
        var scanRoot = Path.Combine(_root, "bin", "scan");
        WriteProject(Path.Combine("bin", "scan", "App"), "App");

        var projects = CsprojParser.LoadProjects(scanRoot);

        var project = Assert.Single(projects);
        Assert.Equal("App", project.Name);
    }

    [Fact]
    public void FindSourceFiles_UsesRootRelativeSkipFolders()
    {
        var scanRoot = Path.Combine(_root, "bin", "scan");
        WriteProject(Path.Combine("bin", "scan", "App"), "App");
        var app = Path.Combine(scanRoot, "App");
        File.WriteAllText(Path.Combine(app, "Real.cs"), "namespace App;");
        Directory.CreateDirectory(Path.Combine(app, "obj"));
        File.WriteAllText(Path.Combine(app, "obj", "Ghost.cs"), "namespace Ghost;");

        var files = CsprojParser.FindSourceFiles(scanRoot);

        var file = Assert.Single(files);
        Assert.Equal("Real.cs", Path.GetFileName(file));
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

    [Fact]
    public void LoadProjects_EvaluatesDirectoryBuildPropsTargetsAndCsprojItems()
    {
        File.WriteAllText(Path.Combine(_root, "Directory.Build.props"), """
            <Project>
              <PropertyGroup>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="MediatR" Version="12.0.0" />
                <PackageReference Include="Obsolete.Package" Version="1.0.0" />
                <Using Include="MediatR" />
                <Using Include="System.Math" Static="true" />
                <Using Include="Obsolete.Namespace" />
              </ItemGroup>
            </Project>
            """);

        var appDir = Path.Combine(_root, "src", "App");
        Directory.CreateDirectory(appDir);
        File.WriteAllText(Path.Combine(appDir, "App.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Remove="Obsolete.Package" />
                <PackageReference Include="FluentValidation" Version="11.0.0" />
                <Using Remove="Obsolete.Namespace" />
                <Using Include="App.Contracts" Alias="Contracts" />
              </ItemGroup>
            </Project>
            """);

        var project = Assert.Single(CsprojParser.LoadProjects(_root));
        Assert.True(project.ImplicitUsings);
        Assert.Equal(new[] { "FluentValidation", "MediatR" }, project.PackageReferences);
        Assert.Equal(3, project.ProjectUsings.Count);
        Assert.Contains(project.ProjectUsings, u => u is { Target: "MediatR", Alias: null, IsStatic: false, IsGlobal: true });
        Assert.Contains(project.ProjectUsings, u => u is { Target: "System.Math", Alias: null, IsStatic: true, IsGlobal: true });
        Assert.Contains(project.ProjectUsings, u => u is { Target: "App.Contracts", Alias: "Contracts", IsStatic: false, IsGlobal: true });
        Assert.DoesNotContain(project.ProjectUsings, u => u.Target == "Obsolete.Namespace");
    }
}
