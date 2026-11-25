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
        Assert.Contains("--format FORMAT", result.Stdout);
        Assert.Contains("md (default), html, json, or agent", result.Stdout);
        Assert.Contains("--render", result.Stdout);
        Assert.Contains("--view MODE", result.Stdout);
        Assert.Contains("--focus NAME", result.Stdout);
        Assert.Contains("--exclude-tests", result.Stdout);
        Assert.Contains("--handler-markers", result.Stdout);
        Assert.Contains("--proposal FILE", result.Stdout);
        Assert.Contains("--draft-slice REQUEST", result.Stdout);
        Assert.Contains("--impact TARGET", result.Stdout);
        Assert.Contains("--tdd-check FILE", result.Stdout);
        Assert.Contains("--allow-root DIR", result.Stdout);
        Assert.Contains("--allow-external-paths", result.Stdout);
        Assert.Contains("--version", result.Stdout);
    }

    [Fact]
    public void Proposal_EvaluatesExistingReferences_WithoutWritingAReportOrProject()
    {
        WriteFile("Shop.Application/Shop.Application.csproj", "<Project />");
        const string infrastructureProject = """
            <Project><ItemGroup>
              <ProjectReference Include="../Shop.Application/Shop.Application.csproj" />
            </ItemGroup></Project>
            """;
        WriteFile("Shop.Infrastructure/Shop.Infrastructure.csproj", infrastructureProject);
        WriteFile("proposal.json", """
            {"references":[{"from":"Shop.Application","to":"Shop.Infrastructure"}]}
            """);
        var before = Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();

        var run = Run([_root, "--proposal", Path.Combine(_root, "proposal.json")]);

        Assert.Equal(ExitCodes.Success, run.Code);
        Assert.Empty(run.Stderr);
        using var response = JsonDocument.Parse(run.Stdout);
        Assert.Equal("conflicts", response.RootElement.GetProperty("status").GetString());
        Assert.Contains(response.RootElement.GetProperty("findings").EnumerateArray(), finding =>
            finding.GetProperty("code").GetString() == "CircularProjectDependency");
        Assert.Equal(before, Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
        Assert.Equal(infrastructureProject, File.ReadAllText(Path.Combine(_root, "Shop.Infrastructure", "Shop.Infrastructure.csproj")));
    }

    [Fact]
    public void Proposal_SupportsGreenfieldModelInAnExistingEmptyDirectory()
    {
        WriteFile("proposal.json", """
            {
              "projects":[{"name":"Shop.Domain","layer":"Domain"},{"name":"Shop.Application","layer":"Application"}],
              "references":[{"from":"Shop.Application","to":"Shop.Domain"}]
            }
            """);

        var run = Run([_root, "--proposal", Path.Combine(_root, "proposal.json")]);

        Assert.Equal(ExitCodes.Success, run.Code);
        Assert.Empty(run.Stderr);
        using var response = JsonDocument.Parse(run.Stdout);
        Assert.Equal("checks_passed", response.RootElement.GetProperty("status").GetString());
        Assert.Equal(0, response.RootElement.GetProperty("existingProjectCount").GetInt32());
    }

    [Theory]
    [InlineData("{", "invalid proposal")]
    [InlineData("[]", "Expected a JSON object")]
    [InlineData("{}", "at least one proposed project or reference")]
    [InlineData("""{"referecnes":[]}""", "Unexpected field 'referecnes'")]
    [InlineData("""{"references":[{"from":"App","to":"Domain","op":"remove"}]}""", "Unexpected field 'op'")]
    [InlineData("""{"projects":[{"name":"App","layer":null}]}""", "'projects[0].layer'")]
    public void Proposal_RejectsMalformedInputBeforeScanning(string json, string message)
    {
        WriteFile("proposal.json", json);

        var run = Run([_root, "--proposal", Path.Combine(_root, "proposal.json")]);

        Assert.Equal(ExitCodes.Usage, run.Code);
        Assert.Empty(run.Stdout);
        Assert.Contains(message, run.Stderr);
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
    [InlineData("--view")]
    [InlineData("--focus")]
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
    [InlineData("--draft-slice")]
    [InlineData("--impact")]
    [InlineData("--impact-kind")]
    [InlineData("--tdd-check")]
    [InlineData("--test-results")]
    [InlineData("--allow-root")]
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
        Assert.Contains("A lens for the CQRS codebase you already have.", colored.Stdout);
        Assert.Contains("NOCTUA · ARCHITECTURE SUMMARY", colored.Stdout);
        Assert.Contains("\x1b[38;2;", colored.Stdout);

        var noColor = Run(new[] { _root, "-o", output, "--banner", "--no-color" });
        Assert.Equal(ExitCodes.Success, noColor.Code);
        Assert.Contains("██████", noColor.Stdout);
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
    public void AsciiFlag_RendersSingleWidthChrome()
    {
        WriteValidFixture();

        var result = Run(new[] { _root, "--banner", "--ascii", "--no-color" });

        Assert.Equal(ExitCodes.Success, result.Code);
        Assert.Contains("* NOCTUA", result.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain('█', result.Stdout);
        Assert.DoesNotContain('╭', result.Stdout);
        Assert.DoesNotContain('·', result.Stdout);
    }

    [Fact]
    public void Help_HonoursDisplayFlagsThatFollowIt()
    {
        var result = Run(new[] { "--help", "--ascii", "--banner", "--no-color" });

        Assert.Equal(ExitCodes.Success, result.Code);
        Assert.Contains("* NOCTUA", result.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain('█', result.Stdout);
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

    [Fact]
    public void DraftSlice_OutputsSchemaValidDesignThatRoundTripsWithSliceDesign()
    {
        WriteValidFixture();

        var run = Run([_root, "--draft-slice", "PingQuery"]);

        Assert.Equal(ExitCodes.Success, run.Code);
        Assert.Empty(run.Stderr);
        using var draft = JsonDocument.Parse(run.Stdout);
        Assert.Equal("drafted", draft.RootElement.GetProperty("status").GetString());
        Assert.Equal("App.PingQuery", draft.RootElement.GetProperty("request").GetString());

        var designJson = draft.RootElement.GetProperty("design").GetRawText();
        WriteFile("ping.slice.json", designJson);

        var verify = Run([_root, "--slice-design", Path.Combine(_root, "ping.slice.json"), "--strict"]);
        Assert.Equal(ExitCodes.Success, verify.Code);
        using var eval = JsonDocument.Parse(verify.Stdout);
        Assert.Equal("matched", eval.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public void Impact_ReportsBidirectionalImpactAndHotspots()
    {
        WriteValidFixture();

        var run = Run([_root, "--impact", "PingQuery", "--impact-kind", "request"]);

        Assert.Equal(ExitCodes.Success, run.Code);
        Assert.Empty(run.Stderr);
        using var report = JsonDocument.Parse(run.Stdout);
        Assert.Equal("analyzed", report.RootElement.GetProperty("status").GetString());
        Assert.Equal("PingQuery", report.RootElement.GetProperty("target").GetProperty("name").GetString());
        Assert.Contains(report.RootElement.GetProperty("impactedItems").EnumerateArray(), item =>
            item.GetProperty("kind").GetString() == "handler" &&
            item.GetProperty("name").GetString() == "PingQueryHandler");
        Assert.Contains(report.RootElement.GetProperty("uncoveredHotspots").EnumerateArray(), spot =>
            spot.GetString()!.Contains("App.PingQuery", StringComparison.Ordinal));
    }

    [Fact]
    public void TddCheck_EvaluatesPhasesWithOptionalTrxAndStrictGate()
    {
        WriteFile("App/App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        WriteFile("App/Ping.cs", """
            using MediatR;

            namespace App;

            public sealed record PingQuery : IRequest<int>;
            public sealed class PingQueryHandler : IRequestHandler<PingQuery, int>
            {
                public Task<int> Handle(PingQuery request, CancellationToken cancellationToken) => Task.FromResult(1);
            }

            public static class PingEndpoints
            {
                public static void Map(object app)
                {
                    app.MapGet("/ping", (IMediator mediator) => mediator.Send(new PingQuery()));
                }
            }
            """);
        WriteFile("App.Tests/App.Tests.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="xunit" Version="2.9.0" />
                <ProjectReference Include="..\App\App.csproj" />
              </ItemGroup>
            </Project>
            """);
        WriteFile("App.Tests/PingQueryTests.cs", """
            using App;
            using Xunit;

            namespace App.Tests;

            public sealed class PingQueryTests
            {
                [Fact]
                public void PingQuery_ReturnsOne()
                {
                    var q = new PingQuery();
                    _ = q;
                }
            }
            """);
        WriteFile("ping.slice.json", """
            {
              "schemaVersion": 1,
              "project": "App",
              "request": "App.PingQuery",
              "kind": "Query",
              "responseType": "int",
              "handler": "App.PingQueryHandler",
              "scenarios": [
                { "name": "returns one", "given": "a ping", "when": "handled", "then": "returns 1" }
              ]
            }
            """);

        var slicePath = Path.Combine(_root, "ping.slice.json");

        // Without TRX -> green_incomplete, exit 0 non-strict, exit 4 strict.
        var withoutTrx = Run([_root, "--tdd-check", slicePath]);
        Assert.Equal(ExitCodes.Success, withoutTrx.Code);
        using (var doc = JsonDocument.Parse(withoutTrx.Stdout))
        {
            Assert.Equal("green_incomplete", doc.RootElement.GetProperty("phase").GetString());
            Assert.True(doc.RootElement.GetProperty("hasDiscoveredTests").GetBoolean());
            Assert.False(doc.RootElement.GetProperty("hasRuntimeResults").GetBoolean());
        }
        Assert.Equal(ExitCodes.StrictFailure, Run([_root, "--tdd-check", slicePath, "--strict"]).Code);

        // Failing TRX -> red, exit 4 under --strict.
        WriteFile("red.trx", """
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results>
                <UnitTestResult testName="App.Tests.PingQueryTests.PingQuery_ReturnsOne" outcome="Failed" />
              </Results>
            </TestRun>
            """);
        var redRun = Run([_root, "--tdd-check", slicePath, "--test-results", Path.Combine(_root, "red.trx"), "--strict"]);
        Assert.Equal(ExitCodes.StrictFailure, redRun.Code);
        using (var redDoc = JsonDocument.Parse(redRun.Stdout))
            Assert.Equal("red", redDoc.RootElement.GetProperty("phase").GetString());

        // Passing TRX -> verified, exit 0 under --strict.
        WriteFile("green.trx", """
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results>
                <UnitTestResult testName="App.Tests.PingQueryTests.PingQuery_ReturnsOne" outcome="Passed" />
              </Results>
            </TestRun>
            """);
        var greenRun = Run([_root, "--tdd-check", slicePath, "--test-results", Path.Combine(_root, "green.trx"), "--strict"]);
        Assert.Equal(ExitCodes.Success, greenRun.Code);
        using (var greenDoc = JsonDocument.Parse(greenRun.Stdout))
            Assert.Equal("verified", greenDoc.RootElement.GetProperty("phase").GetString());

        // XXE TRX -> usage error before scanning.
        WriteFile("xxe.trx", """
            <?xml version="1.0"?>
            <!DOCTYPE foo [ <!ENTITY xxe SYSTEM "file:///etc/passwd"> ]>
            <TestRun><Results><UnitTestResult testName="&xxe;" outcome="Passed" /></Results></TestRun>
            """);
        var xxeRun = Run([_root, "--tdd-check", slicePath, "--test-results", Path.Combine(_root, "xxe.trx")]);
        Assert.Equal(ExitCodes.Usage, xxeRun.Code);
        Assert.Contains("invalid test results", xxeRun.Stderr);
    }

    [Fact]
    public void Parser_ReadsAllowRootAndRejectsOptionConflicts()
    {
        var mcp = CliParser.Parse([_root, "--mcp", "--allow-root", "/extra/one", "--allow-root", " /extra/two ", "--allow-external-paths"]);
        Assert.Equal(CliCommandKind.Run, mcp.Kind);
        Assert.Equal(["/extra/one", "/extra/two"], mcp.Options!.AllowedRoots);
        Assert.True(mcp.Options.AllowExternalPaths);

        Assert.Equal(CliCommandKind.Error, CliParser.Parse([_root, "--impact-kind", "entity"]).Kind);
        Assert.Equal(CliCommandKind.Error, CliParser.Parse([_root, "--impact", "Order", "--impact-kind", "bogus"]).Kind);
        Assert.Equal(CliCommandKind.Error, CliParser.Parse([_root, "--test-results", "a.trx"]).Kind);
        Assert.Equal(CliCommandKind.Error, CliParser.Parse([_root, "--draft-slice", "PingQuery", "--strict"]).Kind);
        Assert.Equal(CliCommandKind.Error, CliParser.Parse([_root, "--impact", "Order", "--strict"]).Kind);
        Assert.Equal(CliCommandKind.Error, CliParser.Parse([_root, "--draft-slice", "PingQuery", "--impact", "Order"]).Kind);
        Assert.Equal(CliCommandKind.Error, CliParser.Parse([_root, "--tdd-check", "a.json", "--sarif", "out.sarif"]).Kind);
        Assert.Equal(CliCommandKind.Error, CliParser.Parse([_root, "--tdd-check", "a.json", "--format", "agent"]).Kind);
        Assert.Equal(CliCommandKind.Error, CliParser.Parse([_root, "--slice-design", "a.json", "--view", "overview"]).Kind);
        Assert.Equal(CliCommandKind.Error, CliParser.Parse([_root, "--draft-slice", "PingQuery", "--focus", "App"]).Kind);
        Assert.Equal(CliCommandKind.Error, CliParser.Parse([_root, "--impact", "Order", "--exclude-tests"]).Kind);
    }

    [Fact]
    public void Parser_ReadsViewFocusAndExcludeTests_AndRejectsInvalidValues()
    {
        var overview = CliParser.Parse(["src", "--view", "overview", "--focus", " Orders.Api ", "--exclude-tests"]);
        Assert.Equal(CliCommandKind.Run, overview.Kind);
        Assert.Equal(ReportViewMode.Overview, overview.Options!.ViewMode);
        Assert.True(overview.Options.ViewModeSpecified);
        Assert.Equal("Orders.Api", overview.Options.FocusService);
        Assert.True(overview.Options.ExcludeTests);

        var full = CliParser.Parse(["src", "--view", "FULL"]);
        Assert.Equal(CliCommandKind.Run, full.Kind);
        Assert.Equal(ReportViewMode.Full, full.Options!.ViewMode);
        Assert.True(full.Options.ViewModeSpecified);

        var badView = Run([_root, "--view", "compact"]);
        Assert.Equal(ExitCodes.Usage, badView.Code);
        Assert.Contains("unknown view mode 'compact'", badView.Stderr);

        var emptyFocus = Run([_root, "--focus", "   "]);
        Assert.Equal(ExitCodes.Usage, emptyFocus.Code);
        Assert.Contains("--focus cannot be empty", emptyFocus.Stderr);
    }
}
