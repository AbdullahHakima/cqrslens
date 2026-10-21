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
}
