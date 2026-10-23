using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

public class ConfigurationTests : IDisposable
{
    private readonly string _root;

    public ConfigurationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cqrslens-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            Path.Combine(_root, "App.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private void WriteFile(string relativePath, string contents)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }

    private void WriteConfig(string json)
        => WriteFile(ConfigurationLoader.FileName, json);

    private static (int Code, string Stdout, string Stderr) Run(string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var code = CliRunner.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public void DefaultConfig_AppliesExclusionsAndRuleSeverities()
    {
        WriteFile("Features/PingCommand.cs", """
            namespace App.Features;
            public sealed record PingCommand : IRequest<int>;
            """);
        WriteFile("Generated/GeneratedQuery.cs", """
            namespace App.Generated;
            public sealed record GeneratedQuery : IRequest<int>;
            """);
        WriteConfig("""
            {
              "exclude": ["**/Generated/**"],
              "rules": { "CommandWithoutValidator": "off" }
            }
            """);

        var output = Path.Combine(_root, "ARCHITECTURE.md");
        var result = Run(new[] { _root, "-o", output, "--generated-at", "2026-06-01T00:00:00Z" });

        Assert.Equal(ExitCodes.Success, result.Code);
        Assert.Contains("1 request(s)", result.Stdout);
        var report = File.ReadAllText(output);
        Assert.Contains("MissingHandler", report);
        Assert.DoesNotContain("CommandWithoutValidator", report);
        Assert.DoesNotContain("GeneratedQuery", report);
    }

    [Fact]
    public void ConfigMarkers_AreUsedWhenCliDoesNotOverrideThem()
    {
        WriteFile("Features/CustomRequest.cs", """
            namespace App.Features;
            public interface IAppMessage { }
            public sealed record CustomRequest : IAppMessage;
            """);
        WriteConfig("""
            {
              "markers": ["IAppMessage"]
            }
            """);

        var output = Path.Combine(_root, "markers.md");
        var withConfig = Run(new[] { _root, "-o", output });

        Assert.Equal(ExitCodes.Success, withConfig.Code);
        Assert.Contains("1 request(s)", withConfig.Stdout);

        var withoutConfig = Run(new[] { _root, "--config", Path.Combine(_root, "missing.json"), "-o", output });
        Assert.Equal(ExitCodes.Usage, withoutConfig.Code);
        Assert.Contains("config file not found", withoutConfig.Stderr);
    }

    [Fact]
    public void ConfigHandlerMarkers_LinkExternalWrappers()
    {
        WriteFile("Features/Contributors.cs", """
            using Ardalis.SharedKernel;

            namespace App.Features;

            public sealed record CreateContributorCommand : ICommand<Result<int>>;
            public sealed class CreateContributorHandler
                : ICommandHandler<CreateContributorCommand, Result<int>> { }
            """);
        WriteConfig("""
            {
              "markers": ["ICommand"],
              "handlerMarkers": ["ICommandHandler"]
            }
            """);

        var output = Path.Combine(_root, "handlers.md");
        var result = Run(new[] { _root, "-o", output });

        Assert.Equal(ExitCodes.Success, result.Code);
        Assert.Contains("1 request(s), 1 handler(s)", result.Stdout);
        Assert.DoesNotContain("MissingHandler", File.ReadAllText(output));
    }

    [Fact]
    public void CliHandlerMarkers_OverrideConfigHandlerMarkers()
    {
        WriteFile("Features/Contributors.cs", """
            using Ardalis.SharedKernel;

            namespace App.Features;

            public sealed record CreateContributorCommand : ICommand<Result<int>>;
            public sealed class CreateContributorHandler
                : ICommandHandler<CreateContributorCommand, Result<int>> { }
            """);
        WriteConfig("""
            {
              "markers": ["ICommand"],
              "handlerMarkers": ["IWrongHandler"]
            }
            """);

        var output = Path.Combine(_root, "override.md");
        var withoutOverride = Run(new[] { _root, "-o", output });
        Assert.Equal(ExitCodes.Success, withoutOverride.Code);
        Assert.Contains("1 request(s), 0 handler(s)", withoutOverride.Stdout);

        var withOverride = Run(new[] { _root, "-o", output, "--handler-markers", "ICommandHandler" });
        Assert.Equal(ExitCodes.Success, withOverride.Code);
        Assert.Contains("1 request(s), 1 handler(s)", withOverride.Stdout);
    }

    [Fact]
    public void CliAnalysis_OverridesConfigAnalysis()
    {
        WriteFile("Features/PingQuery.cs", """
            namespace App.Features;
            public sealed record PingQuery : IRequest<int>;
            public sealed class PingQueryHandler : IRequestHandler<PingQuery, int> { }
            """);
        WriteConfig("""
            {
              "analysis": "roslyn"
            }
            """);

        var output = Path.Combine(_root, "text.md");
        var result = Run(new[] { _root, "-o", output, "--analysis", "text" });

        Assert.Equal(ExitCodes.Success, result.Code);
        Assert.Contains("text scanner", File.ReadAllText(output));
    }

    [Fact]
    public void ConfigIncludeAndLayerOverride_AreApplied()
    {
        WriteFile("src/Keep.cs", """
            namespace App.Features;
            public sealed record KeepQuery : IRequest<int>;
            public sealed class KeepQueryHandler : IRequestHandler<KeepQuery, int> { }
            """);
        WriteFile("other/Drop.cs", """
            namespace App.Other;
            public sealed record DropQuery : IRequest<int>;
            public sealed class DropQueryHandler : IRequestHandler<DropQuery, int> { }
            """);
        WriteConfig("""
            {
              "include": ["src/**"],
              "layers": { "App": "Domain" }
            }
            """);

        var output = Path.Combine(_root, "filtered.md");
        var result = Run(new[] { _root, "-o", output });

        Assert.Equal(ExitCodes.Success, result.Code);
        Assert.Contains("1 request(s)", result.Stdout);
        var report = File.ReadAllText(output);
        Assert.Contains("KeepQuery", report);
        Assert.DoesNotContain("DropQuery", report);
        Assert.Contains("subgraph lyr_Domain[\"Domain\"]", report);
    }

    [Fact]
    public void RuleSeverityCanPromoteAWarningForStrictChecks()
    {
        WriteFile("Features/PingCommand.cs", """
            namespace App.Features;
            public sealed record PingCommand : IRequest<int>;
            public sealed class PingCommandHandler : IRequestHandler<PingCommand, int> { }
            """);
        WriteConfig("""
            {
              "rules": { "CommandWithoutValidator": "error" }
            }
            """);

        var result = Run(new[] { _root, "-o", Path.Combine(_root, "strict.md"), "--strict" });

        Assert.Equal(ExitCodes.StrictFailure, result.Code);
        Assert.Contains("--strict", result.Stderr);
    }

    [Theory]
    [InlineData("""{ "unknown": true }""")]
    [InlineData("""{ "rules": { "NoSuchRule": "error" } }""")]
    [InlineData("""{ "rules": { "MissingHandler": "fatal" } }""")]
    [InlineData("""{ "analysis": "magic" }""")]
    [InlineData("""{ "markers": [""] }""")]
    [InlineData("""{ "handlerMarkers": [""] }""")]
    public void InvalidConfig_IsAUsageError(string json)
    {
        WriteConfig(json);

        var result = Run(new[] { _root, "-o", Path.Combine(_root, "out.md") });

        Assert.Equal(ExitCodes.Usage, result.Code);
        Assert.Contains("invalid config", result.Stderr);
    }

    [Fact]
    public void ExplicitConfigPath_IsRelativeToTheCurrentDirectory()
    {
        WriteFile("Features/PingQuery.cs", """
            namespace App.Features;
            public sealed record PingQuery : IRequest<int>;
            public sealed class PingQueryHandler : IRequestHandler<PingQuery, int> { }
            """);
        WriteFile("custom/settings.json", """{ "analysis": "text" }""");

        var result = Run(new[]
        {
            _root,
            "--config", Path.Combine(_root, "custom", "settings.json"),
            "-o", Path.Combine(_root, "out.md"),
        });

        Assert.Equal(ExitCodes.Success, result.Code);
    }

    [Theory]
    [InlineData("**/Generated/**", "src/App/Generated/Thing.cs", true)]
    [InlineData("**/Generated/**", "src/App/Handlers/Thing.cs", false)]
    [InlineData("src/**/*.cs", "src/App/Handlers/Thing.cs", true)]
    [InlineData("**/*.g.cs", "src/App/Handlers/Thing.g.cs", true)]
    [InlineData("**/*.g.cs", "src/App/Handlers/Thing.cs", false)]
    [InlineData("**/migrations/**", "src/Migrations/M1.cs", true)]
    public void GlobPattern_MatchesRootRelativePaths(
        string pattern,
        string path,
        bool expected)
    {
        Assert.Equal(expected, new GlobPattern(pattern).IsMatch(path));
    }

    [Fact]
    public void IncludeAndExclude_CombineInOrder()
    {
        var filter = new SourcePathFilter(
            includes: new[] { "src/**" },
            excludes: new[] { "**/Generated/**" });

        Assert.True(filter.IsIncluded("src/App/Order.cs"));
        Assert.False(filter.IsIncluded("tests/App/Order.cs"));
        Assert.False(filter.IsIncluded("src/App/Generated/Order.cs"));
    }
}
