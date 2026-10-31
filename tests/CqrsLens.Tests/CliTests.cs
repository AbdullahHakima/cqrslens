using System.Text.Json;
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
        Assert.Contains("--format md|html|json", result.Stdout);
        Assert.Contains("--render", result.Stdout);
        Assert.Contains("--handler-markers", result.Stdout);
        Assert.Contains("--version", result.Stdout);
    }

    [Fact]
    public void Profile_PrintsScanAndDeepSliceBreakdownToStderr()
    {
        WriteValidFixture();
        var output = Path.Combine(_root, "ARCHITECTURE.md");

        var result = Run(new[]
        {
            _root, "-o", output, "--profile", "--generated-at", "2026-01-01T00:00:00Z",
        });

        Assert.Equal(ExitCodes.Success, result.Code);
        Assert.Contains("Profile (text):", result.Stderr);
        Assert.Contains("Parse sources", result.Stderr);
        Assert.Contains("Scan shapes", result.Stderr);
        Assert.Contains("Scan: ApplyScan", result.Stderr);
        Assert.Contains("Deep: handler slices", result.Stderr);
        Assert.DoesNotContain("Profile (text):", result.Stdout);
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
    [InlineData("--watermark")]
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
    public void JsonFormat_AndJsonExtensionInference_WriteStructuredJsonReport()
    {
        WriteValidFixture();
        var inferredOutput = Path.Combine(_root, "report.json");
        var explicitOutput = Path.Combine(_root, "explicit.out");

        var inferredResult = Run(new[] { _root, "-o", inferredOutput, "--generated-at", "2026-02-03T04:05:06Z" });
        var explicitResult = Run(new[] { _root, "-o", explicitOutput, "--format", "json", "--generated-at", "2026-02-03T04:05:06Z" });

        Assert.Equal(ExitCodes.Success, inferredResult.Code);
        Assert.Equal(ExitCodes.Success, explicitResult.Code);

        var inferredJson = File.ReadAllText(inferredOutput);
        var explicitJson = File.ReadAllText(explicitOutput);
        Assert.Equal(inferredJson, explicitJson);

        using var doc = JsonDocument.Parse(inferredJson);
        Assert.Equal(1, doc.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("summary").GetProperty("requests").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("summary").GetProperty("handlers").GetInt32());
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
            namespace Alpha
            {
                public sealed record PingCommand : IRequest<int>;
            }

            namespace Beta
            {
                public sealed record PingCommand : IRequest<int>;
            }
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

    [Fact]
    public void BannerAndTrueColor_RenderAsciiLogoAndSummaryCard()
    {
        WriteValidFixture();
        var output = Path.Combine(_root, "ARCHITECTURE.md");

        var colored = Run(new[] { _root, "-o", output, "--banner", "--color" });
        Assert.Equal(ExitCodes.Success, colored.Code);
        Assert.Contains("Static CQRS & Clean Architecture Lens", colored.Stdout);
        Assert.Contains("CqrsLens Architecture Summary", colored.Stdout);
        Assert.Contains("\x1b[38;2;", colored.Stdout);

        var noColor = Run(new[] { _root, "-o", output, "--banner", "--no-color" });
        Assert.Equal(ExitCodes.Success, noColor.Code);
        Assert.Contains("██████╗", noColor.Stdout);
        Assert.Contains("╭─", noColor.Stdout);
        Assert.DoesNotContain("\x1b[38;2;", noColor.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void WatermarkOption_RendersAsciiStampInConsoleAndReports()
    {
        WriteValidFixture();
        var mdOut = Path.Combine(_root, "ARCHITECTURE.md");
        var htmlOut = Path.Combine(_root, "ARCHITECTURE.html");
        var jsonOut = Path.Combine(_root, "ARCHITECTURE.json");

        var mdRun = Run(new[] { _root, "-o", mdOut, "--watermark", "ACME CORP", "--no-color" });
        Assert.Equal(ExitCodes.Success, mdRun.Code);
        Assert.Contains("◈ WATERMARK · ACME CORP ◈", mdRun.Stdout);
        var md = File.ReadAllText(mdOut);
        Assert.Contains("◈ WATERMARK · ACME CORP ◈", md);
        Assert.Contains("╔═", md);

        var htmlRun = Run(new[] { _root, "-o", htmlOut, "--watermark", "ACME CORP" });
        Assert.Equal(ExitCodes.Success, htmlRun.Code);
        var html = File.ReadAllText(htmlOut);
        Assert.Contains("<div class=\"watermark-overlay\" aria-hidden=\"true\">ACME CORP</div>", html);
        Assert.Contains("<pre class=\"ascii-watermark\" aria-label=\"Watermark\">", html);
        Assert.Contains("◈ WATERMARK &#183; ACME CORP ◈", html);

        var jsonRun = Run(new[] { _root, "-o", jsonOut, "--watermark", "ACME CORP" });
        Assert.Equal(ExitCodes.Success, jsonRun.Code);
        using var doc = JsonDocument.Parse(File.ReadAllText(jsonOut));
        Assert.Equal("ACME CORP", doc.RootElement.GetProperty("watermark").GetString());
    }

    [Fact]
    public void EmptyWatermark_IsUsageError()
    {
        var result = Run(new[] { _root, "--watermark", "   " });

        Assert.Equal(ExitCodes.Usage, result.Code);
        Assert.Contains("--watermark cannot be empty", result.Stderr);
    }

    [Fact]
    public void ProgressBar_FormatsDeterministicAsciiAndColoredBars()
    {
        var progress = new ScanProgress("Analyzing C# shapes", 2, 4, "text");
        var plain = ConsoleArtwork.FormatProgressBar(progress, useColor: false, barWidth: 20);
        Assert.Contains("[██████████░░░░░░░░░░]  50% · Analyzing C# shapes (text)", plain);
        Assert.DoesNotContain("\x1b[", plain, StringComparison.Ordinal);

        var colored = ConsoleArtwork.FormatProgressBar(progress, useColor: true, barWidth: 20);
        Assert.Contains("\x1b[38;2;", colored);
        Assert.Contains("50%", colored);
    }
}
