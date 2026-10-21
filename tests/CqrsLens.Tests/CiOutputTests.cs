using System.Text.Json;
using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

public class CiOutputTests : IDisposable
{
    private readonly string _root;
    private readonly string _scanRoot;

    public CiOutputTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cqrslens-ci-" + Guid.NewGuid().ToString("N"));
        _scanRoot = Path.Combine(_root, "src");
        Directory.CreateDirectory(_scanRoot);
        File.WriteAllText(
            Path.Combine(_scanRoot, "App.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        WriteFile("src/Features/PingCommand.cs", """
            namespace App.Features;
            public sealed record PingCommand : IRequest<int>;
            """);
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

    private static (int Code, string Stdout, string Stderr) Run(string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var code = CliRunner.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private (string Report, string Sarif, string Summary) Outputs()
        => (Path.Combine(_root, "out", "ARCHITECTURE.md"),
            Path.Combine(_root, "out", "cqrslens.sarif"),
            Path.Combine(_root, "out", "summary.md"));

    [Fact]
    public void SarifSummaryAndAnnotationsAreGeneratedWithCiRootRebasing()
    {
        var outputs = Outputs();
        var result = Run(new[]
        {
            _scanRoot,
            "-o", outputs.Report,
            "--sarif", outputs.Sarif,
            "--summary", outputs.Summary,
            "--github-annotations",
            "--ci-root", _root,
            "--generated-at", "2026-06-01T00:00:00Z",
        });

        Assert.Equal(ExitCodes.Success, result.Code);
        Assert.True(File.Exists(outputs.Sarif));
        Assert.True(File.Exists(outputs.Summary));
        Assert.Contains("SARIF written to", result.Stdout);
        Assert.Contains("Summary written to", result.Stdout);
        Assert.Contains("::error file=src/Features/PingCommand.cs,line=", result.Stdout);
        Assert.Contains("::warning file=src/Features/PingCommand.cs,line=", result.Stdout);
        Assert.Contains("title=MissingHandler", result.Stdout);

        using var document = JsonDocument.Parse(File.ReadAllText(outputs.Sarif));
        var root = document.RootElement;
        Assert.Equal("2.1.0", root.GetProperty("version").GetString());
        var run = root.GetProperty("runs")[0];
        Assert.Equal("CqrsLens", run.GetProperty("tool").GetProperty("driver").GetProperty("name").GetString());
        Assert.Equal("utf16CodeUnits", run.GetProperty("columnKind").GetString());
        Assert.True(run.GetProperty("invocations")[0].GetProperty("executionSuccessful").GetBoolean());

        var results = run.GetProperty("results").EnumerateArray().ToList();
        Assert.Equal(2, results.Count);
        var missing = results.Single(r => r.GetProperty("ruleId").GetString() == "MissingHandler");
        Assert.Equal("error", missing.GetProperty("level").GetString());
        var physical = missing.GetProperty("locations")[0].GetProperty("physicalLocation");
        Assert.Equal("src/Features/PingCommand.cs", physical.GetProperty("artifactLocation").GetProperty("uri").GetString());
        Assert.True(physical.GetProperty("region").GetProperty("startLine").GetInt32() > 0);
        Assert.False(missing.TryGetProperty("baselineState", out _));
        Assert.False(string.IsNullOrWhiteSpace(
            missing.GetProperty("partialFingerprints").GetProperty("cqrslens/v1").GetString()));

        var summary = File.ReadAllText(outputs.Summary);
        Assert.Contains("## CqrsLens architecture summary", summary);
        Assert.Contains("**1** error(s), **1** warning(s).", summary);
        Assert.Contains("src/Features/PingCommand.cs", summary);
        Assert.Contains("MissingHandler", summary);
    }

    [Fact]
    public void BaselineSurvivesIntoSummarySarifAndAnnotations()
    {
        var outputs = Outputs();
        var baseline = Path.Combine(_root, "baseline.json");
        Assert.Equal(
            ExitCodes.Success,
            Run(new[] { _scanRoot, "--write-baseline", baseline, "-o", outputs.Report }).Code);

        WriteFile("src/Features/NewCommand.cs", """
            namespace App.Features;
            public sealed record NewCommand : IRequest<int>;
            """);

        var result = Run(new[]
        {
            _scanRoot,
            "--baseline", baseline,
            "--strict",
            "-o", outputs.Report,
            "--sarif", outputs.Sarif,
            "--summary", outputs.Summary,
            "--github-annotations",
            "--ci-root", _root,
        });

        Assert.Equal(ExitCodes.StrictFailure, result.Code);
        Assert.DoesNotContain("::error file=src/Features/PingCommand.cs", result.Stdout);
        Assert.Contains("::error file=src/Features/NewCommand.cs", result.Stdout);

        using var document = JsonDocument.Parse(File.ReadAllText(outputs.Sarif));
        var results = document.RootElement.GetProperty("runs")[0]
            .GetProperty("results").EnumerateArray().ToList();
        Assert.Equal(4, results.Count);

        var oldFinding = results.Single(r =>
            r.GetProperty("properties").GetProperty("target").GetString() == "App.Features.PingCommand" &&
            r.GetProperty("ruleId").GetString() == "MissingHandler");
        Assert.Equal("unchanged", oldFinding.GetProperty("baselineState").GetString());

        var newFinding = results.Single(r =>
            r.GetProperty("properties").GetProperty("target").GetString() == "App.Features.NewCommand" &&
            r.GetProperty("ruleId").GetString() == "MissingHandler");
        Assert.Equal("new", newFinding.GetProperty("baselineState").GetString());

        var summary = File.ReadAllText(outputs.Summary);
        Assert.Contains("**1** new error(s), **1** new warning(s); 2 known finding(s)", summary);
        Assert.Contains("#### Known findings", summary);
        Assert.Contains("#### New findings", summary);
    }

    [Fact]
    public void MissingCiRootIsAUsageError()
    {
        var result = Run(new[]
        {
            _scanRoot,
            "-o", Path.Combine(_root, "ARCHITECTURE.md"),
            "--ci-root", Path.Combine(_root, "missing"),
        });

        Assert.Equal(ExitCodes.Usage, result.Code);
        Assert.Contains("--ci-root directory not found", result.Stderr);
    }

    [Fact]
    public void CollidingOutputPathsAreRejected()
    {
        var output = Path.Combine(_root, "same.md");
        var result = Run(new[]
        {
            _scanRoot,
            "-o", output,
            "--summary", output,
        });

        Assert.Equal(ExitCodes.Usage, result.Code);
        Assert.Contains("must be different files", result.Stderr);
    }

    [Fact]
    public void AnnotationPropertiesAndMessagesAreEscaped()
    {
        var analysis = new AnalysisResult { RootPath = _root };
        analysis.Checks.Add(new CheckFinding(
            CheckSeverity.Warning,
            "Rule:Name",
            "100%\nnext",
            "App.Target",
            new SourceLocation("src/A,B.cs", 1, 2)));
        using var writer = new StringWriter();

        GitHubAnnotations.Write(analysis, writer, _root);

        Assert.Equal(
            "::warning file=src/A%2CB.cs,line=1,col=2,title=Rule%3AName::100%25%0Anext\n",
            writer.ToString().Replace("\r\n", "\n", StringComparison.Ordinal));
    }
}
