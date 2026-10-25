using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

public class CliTests : IDisposable
{
    private readonly string _root;

    public CliTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cqrslens-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private string WriteProject()
    {
        var path = Path.Combine(_root, "App.csproj");
        File.WriteAllText(path, "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        return path;
    }

    private void WriteFile(string relativePath, string contents)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }

    private void WriteValidFixture()
    {
        WriteProject();
        WriteFile("Ping.cs", """
            using MediatR;

            namespace App;

            public sealed record PingQuery : IRequest<int>;
            public sealed class PingQueryHandler : IRequestHandler<PingQuery, int>
            {
                public Task<int> Handle(PingQuery request, CancellationToken cancellationToken) => Task.FromResult(1);
            }
            """);
    }

    private static (int Code, string Stdout, string Stderr) Run(string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var code = CliRunner.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public void Version_PrintsToolVersionAndExitsZero()
    {
        var result = Run(new[] { "--version" });

        Assert.Equal(ExitCodes.Success, result.Code);
        Assert.Equal($"CqrsLens {ToolInfo.Version}", result.Stdout.Trim());
        Assert.Equal("", result.Stderr);
    }

    [Fact]
    public void Help_PrintsUsageAndExitsZero()
    {
        var result = Run(new[] { "--help" });

        Assert.Equal(ExitCodes.Success, result.Code);
        Assert.Contains("Usage: cqrslens", result.Stdout);
        Assert.Contains("--render", result.Stdout);
        Assert.Contains("--handler-markers", result.Stdout);
        Assert.Contains("--version", result.Stdout);
    }

    [Fact]
    public void GeneratedAtOption_WritesPinnedTimestamp()
    {
        WriteValidFixture();
        var output = Path.Combine(_root, "ARCHITECTURE.md");

        var result = Run(new[]
        {
            _root, "-o", output, "--generated-at", "2026-02-03T04:05:06Z",
        });

        Assert.Equal(ExitCodes.Success, result.Code);
        Assert.Contains("2026-02-03 04:05:06 UTC", File.ReadAllText(output));

        var secondOutput = Path.Combine(_root, "ARCHITECTURE-second.md");
        var second = Run(new[]
        {
            _root, "-o", secondOutput, "--generated-at", "2026-02-03T04:05:06Z",
        });
        Assert.Equal(ExitCodes.Success, second.Code);
        Assert.Equal(File.ReadAllText(output), File.ReadAllText(secondOutput));
    }

    [Fact]
    public void InvalidGeneratedAt_IsUsageError()
    {
        var command = CliParser.Parse(new[] { ".", "--generated-at", "not-a-date" });

        Assert.Equal(CliCommandKind.Error, command.Kind);
        Assert.Contains("invalid --generated-at", command.Error);
    }

    [Fact]
    public void MissingPath_IsUsageError()
    {
        var result = Run(Array.Empty<string>());

        Assert.Equal(ExitCodes.Usage, result.Code);
        Assert.Contains("pass a folder to scan", result.Stderr);
        Assert.Contains("Usage: cqrslens", result.Stderr);
    }

    [Fact]
    public void UnknownOption_IsUsageError()
    {
        var result = Run(new[] { "--nope" });

        Assert.Equal(ExitCodes.Usage, result.Code);
        Assert.Contains("unknown option '--nope'", result.Stderr);
        Assert.Contains("Usage: cqrslens", result.Stderr);
    }

    [Theory]
    [InlineData("-o")]
    [InlineData("--output")]
    [InlineData("--format")]
    [InlineData("--render")]
    [InlineData("--config")]
    [InlineData("--analysis")]
    [InlineData("--markers")]
    [InlineData("--handler-markers")]
    [InlineData("--baseline")]
    [InlineData("--write-baseline")]
    [InlineData("--sarif")]
    [InlineData("--summary")]
    [InlineData("--ci-root")]
    public void OptionWithoutValue_IsUsageError(string option)
    {
        var result = Run(new[] { option });

        Assert.Equal(ExitCodes.Usage, result.Code);
        Assert.Contains("needs a value", result.Stderr);
    }

    [Fact]
    public void MultiplePaths_AreRejected()
    {
        var result = Run(new[] { "one", "two" });

        Assert.Equal(ExitCodes.Usage, result.Code);
        Assert.Contains("exactly one path", result.Stderr);
    }

    [Fact]
    public void UnknownFormat_IsRejected()
    {
        var result = Run(new[] { _root, "--format", "pdf" });

        Assert.Equal(ExitCodes.Usage, result.Code);
        Assert.Contains("unknown format 'pdf'", result.Stderr);
    }

    [Fact]
    public void UnknownRenderMode_IsRejected()
    {
        var result = Run(new[] { _root, "--render", "canvas" });

        Assert.Equal(ExitCodes.Usage, result.Code);
        Assert.Contains("unknown render mode 'canvas'", result.Stderr);
    }

    [Fact]
    public void SvgRenderMode_ProducesZeroJsHtmlReport()
    {
        WriteValidFixture();
        var output = Path.Combine(_root, "report.html");

        var result = Run(new[] { _root, "-o", output, "--render", "svg" });

        Assert.Equal(ExitCodes.Success, result.Code);
        var html = File.ReadAllText(output);
        Assert.Contains("<div class=\"svg-diagram\"><svg ", html);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mermaid.min.js", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnknownAnalysisMode_IsRejected()
    {
        var result = Run(new[] { _root, "--analysis", "magic" });

        Assert.Equal(ExitCodes.Usage, result.Code);
        Assert.Contains("unknown analysis mode 'magic'", result.Stderr);
    }

    [Fact]
    public void RoslynMode_RunsThroughTheCli()
    {
        WriteValidFixture();
        var output = Path.Combine(_root, "roslyn.md");

        var result = Run(new[] { _root, "--analysis", "roslyn", "-o", output });

        Assert.Equal(ExitCodes.Success, result.Code);
        Assert.Contains("1 request(s), 1 handler(s)", result.Stdout);
        Assert.True(File.Exists(output));
    }

    [Fact]
    public void MissingFolder_ReturnsPathNotFound()
    {
        var result = Run(new[] { Path.Combine(_root, "missing") });

        Assert.Equal(ExitCodes.PathNotFound, result.Code);
        Assert.Contains("path not found", result.Stderr);
    }

    [Fact]
    public void FolderWithoutProjects_ReturnsNoProjects()
    {
        var result = Run(new[] { _root });

        Assert.Equal(ExitCodes.NoProjects, result.Code);
        Assert.Contains("no .csproj files found", result.Stderr);
    }

    [Fact]
    public void OutputDirectoriesAreCreated()
    {
        WriteValidFixture();
        var output = Path.Combine(_root, "reports", "nested", "ARCHITECTURE.md");

        var result = Run(new[] { _root, "-o", output });

        Assert.Equal(ExitCodes.Success, result.Code);
        Assert.True(File.Exists(output));
        Assert.Contains("Report written to", result.Stdout);
    }

    [Fact]
    public void OutputPathThatIsADirectory_IsRejectedClearly()
    {
        WriteValidFixture();
        var output = Path.Combine(_root, "reports");
        Directory.CreateDirectory(output);

        var result = Run(new[] { _root, "-o", output });

        Assert.Equal(ExitCodes.Usage, result.Code);
        Assert.Contains("output path is a directory", result.Stderr);
    }

    [Fact]
    public void StrictFailure_ReturnsStrictExitCode()
    {
        WriteProject();
        WriteFile("PingCommand.cs", """
            namespace App;
            public sealed record PingCommand : IRequest<int>;
            """);

        var output = Path.Combine(_root, "ARCHITECTURE.md");
        var result = Run(new[] { _root, "-o", output, "--strict" });

        Assert.Equal(ExitCodes.StrictFailure, result.Code);
        Assert.Contains("--strict", result.Stderr);
        Assert.True(File.Exists(output));
    }

    [Fact]
    public void Strict_WithAmbiguousScan_DoesNotFailOnLowConfidenceChecks()
    {
        WriteProject();
        WriteFile("Requests.cs", """
            namespace Alpha;
            public sealed record PingCommand : IRequest<int>;

            namespace Beta;
            public sealed record PingCommand : IRequest<int>;
            """);
        WriteFile("Handler.cs", """
            namespace Handlers;
            public sealed class PingCommandHandler : IRequestHandler<PingCommand, int>
            {
            }
            """);

        var output = Path.Combine(_root, "ARCHITECTURE.md");
        var result = Run(new[] { _root, "-o", output, "--strict" });

        Assert.Equal(ExitCodes.Success, result.Code);
        Assert.Contains("scan diagnostic", result.Stderr);
        Assert.Contains("strict skipped", result.Stderr);
        Assert.True(File.Exists(output));
    }

    [Fact]
    public void NonStrictRunWithCheckErrors_StillSucceeds()
    {
        WriteProject();
        WriteFile("PingCommand.cs", """
            namespace App;
            public sealed record PingCommand : IRequest<int>;
            """);

        var output = Path.Combine(_root, "ARCHITECTURE.md");
        var result = Run(new[] { _root, "-o", output });

        Assert.Equal(ExitCodes.Success, result.Code);
        Assert.Contains("Architecture checks:", result.Stdout);
    }

    [Fact]
    public void UnexpectedIoFailure_ReturnsStableExitCode()
    {
        WriteValidFixture();
        var blocker = Path.Combine(_root, "blocked");
        File.WriteAllText(blocker, "not a directory");

        var result = Run(new[]
        {
            _root,
            "-o",
            Path.Combine(blocker, "ARCHITECTURE.md"),
        });

        Assert.Equal(ExitCodes.Unexpected, result.Code);
        Assert.Contains("error:", result.Stderr);
        Assert.DoesNotContain("   at ", result.Stderr);
    }

    [Fact]
    public void Parser_ReadsFormatMarkersAndStrict()
    {
        var command = CliParser.Parse(new[]
        {
            "src", "-o", "out.html", "--format", "HTML", "--render", "SVG", "--analysis", "roslyn",
            "--markers", "ICommand, App.IQuery",
            "--handler-markers", "ICommandHandler, App.IQueryHandler", "--strict",
        });

        Assert.Equal(CliCommandKind.Run, command.Kind);
        var options = Assert.IsType<CliOptions>(command.Options);
        Assert.Equal("src", options.Path);
        Assert.Equal("out.html", options.Output);
        Assert.Equal("html", options.Format);
        Assert.Equal(DiagramRenderMode.Svg, options.RenderMode);
        Assert.True(options.RenderModeSpecified);
        Assert.Equal(AnalysisMode.Roslyn, options.AnalysisMode);
        Assert.True(options.Strict);
        Assert.Equal(new[] { "ICommand", "IQuery" }, options.Markers.OrderBy(m => m));
        Assert.Equal(
            new[] { "ICommandHandler", "IQueryHandler" },
            options.HandlerMarkers.OrderBy(m => m));
    }
}
