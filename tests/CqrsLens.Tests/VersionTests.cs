using System.Reflection;
using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

public class VersionTests
{
    [Fact]
    public void ToolInfo_VersionComesFromAssemblyMetadata()
    {
        var informational = typeof(ToolInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        Assert.False(string.IsNullOrWhiteSpace(informational));
        var expected = informational!.Split('+')[0];
        Assert.Equal(expected, ToolInfo.Version);
    }

    [Fact]
    public void ToolInfo_VersionIsASemVerWithoutBuildMetadata()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+$", ToolInfo.Version);
    }

    [Fact]
    public void ActionAndReadme_PinTheCurrentVersionAndExposeAllInputs()
    {
        var root = FindRepositoryRoot();
        var action = File.ReadAllText(Path.Combine(root, "action.yml"));
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));

        Assert.Contains($"default: '{ToolInfo.Version}'", action);
        Assert.Contains("analysis:", action);
        Assert.Contains("--analysis \"$CQRSLENS_ANALYSIS\"", action);
        Assert.Contains("markers:", action);
        Assert.Contains("if: always()", action);
        Assert.Contains($"AbdullahHakima/cqrslens@v{ToolInfo.Version}", readme);
        Assert.Contains($"version: '{ToolInfo.Version}'", readme);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CqrsLens.sln")))
                return directory.FullName;
        }

        throw new InvalidOperationException("Could not find CqrsLens.sln above the test output directory.");
    }
}
