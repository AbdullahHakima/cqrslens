using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

public class BaselineTests : IDisposable
{
    private readonly string _root;

    public BaselineTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cqrslens-baseline-" + Guid.NewGuid().ToString("N"));
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

    private void WriteExistingViolation()
        => WriteFile("Features/PingCommand.cs", """
            namespace App.Features;
            public sealed record PingCommand : IRequest<int>;
            """);

    private static (int Code, string Stdout, string Stderr) Run(string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var code = CliRunner.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public void ExistingFindingsBecomeKnownAndDoNotFailStrict()
    {
        WriteExistingViolation();
        var baseline = Path.Combine(_root, "baseline.json");
        var report = Path.Combine(_root, "ARCHITECTURE.md");

        var write = Run(new[] { _root, "--write-baseline", baseline, "-o", report });
        Assert.Equal(ExitCodes.Success, write.Code);
        Assert.Contains("Baseline written", write.Stdout);
        Assert.True(File.Exists(baseline));

        var json = File.ReadAllText(baseline);
        Assert.Contains("\"schemaVersion\": 1", json);
        Assert.Contains("\"code\": \"MissingHandler\"", json);
        Assert.Contains("\"target\": \"App.Features.PingCommand\"", json);
        Assert.Contains("\"file\": \"Features/PingCommand.cs\"", json);

        var check = Run(new[] { _root, "--baseline", baseline, "--strict", "-o", report });
        Assert.Equal(ExitCodes.Success, check.Code);
        Assert.Contains("2 known finding(s), 0 new finding(s)", check.Stdout);

        var markdown = File.ReadAllText(report);
        Assert.Contains("0 new error(s), 0 new warning(s); 2 known finding(s)", markdown);
        Assert.Contains("Known (from baseline):", markdown);
    }

    [Fact]
    public void NewFindingStillFailsStrictWhenBaselineExists()
    {
        WriteExistingViolation();
        var baseline = Path.Combine(_root, "baseline.json");
        var report = Path.Combine(_root, "ARCHITECTURE.md");
        Assert.Equal(ExitCodes.Success, Run(new[] { _root, "--write-baseline", baseline, "-o", report }).Code);

        WriteFile("Features/NewCommand.cs", """
            namespace App.Features;
            public sealed record NewCommand : IRequest<int>;
            """);

        var result = Run(new[] { _root, "--baseline", baseline, "--strict", "-o", report });

        Assert.Equal(ExitCodes.StrictFailure, result.Code);
        Assert.Contains("new architecture check error(s)", result.Stderr);
        var markdown = File.ReadAllText(report);
        Assert.Contains("NewCommand", markdown);
        Assert.Contains("1 new error(s), 1 new warning(s)", markdown);
    }

    [Fact]
    public void MovingCodeToANewLineKeepsTheFindingKnown()
    {
        WriteExistingViolation();
        var baseline = Path.Combine(_root, "baseline.json");
        var report = Path.Combine(_root, "ARCHITECTURE.md");
        Assert.Equal(ExitCodes.Success, Run(new[] { _root, "--write-baseline", baseline, "-o", report }).Code);

        WriteFile("Features/PingCommand.cs", """


            namespace App.Features;
            public sealed record PingCommand : IRequest<int>;
            """);

        var result = Run(new[] { _root, "--baseline", baseline, "--strict", "-o", report });
        Assert.Equal(ExitCodes.Success, result.Code);
        Assert.Contains("0 new finding(s)", result.Stdout);
    }

    [Fact]
    public void MovingCodeToANewFileMakesTheFindingNew()
    {
        WriteExistingViolation();
        var baseline = Path.Combine(_root, "baseline.json");
        var report = Path.Combine(_root, "ARCHITECTURE.md");
        Assert.Equal(ExitCodes.Success, Run(new[] { _root, "--write-baseline", baseline, "-o", report }).Code);

        var oldPath = Path.Combine(_root, "Features", "PingCommand.cs");
        var newPath = Path.Combine(_root, "Features", "MovedCommand.cs");
        File.Move(oldPath, newPath);

        var result = Run(new[] { _root, "--baseline", baseline, "--strict", "-o", report });

        Assert.Equal(ExitCodes.StrictFailure, result.Code);
        Assert.Contains("2 new finding(s)", result.Stdout);
    }

    [Fact]
    public void BaselineWriteIsDeterministic()
    {
        WriteExistingViolation();
        var first = Path.Combine(_root, "first-baseline.json");
        var second = Path.Combine(_root, "second-baseline.json");
        var report = Path.Combine(_root, "ARCHITECTURE.md");

        Assert.Equal(ExitCodes.Success, Run(new[] { _root, "--write-baseline", first, "-o", report }).Code);
        Assert.Equal(ExitCodes.Success, Run(new[] { _root, "--write-baseline", second, "-o", report }).Code);

        Assert.Equal(File.ReadAllText(first), File.ReadAllText(second));
    }

    [Fact]
    public void MissingBaselineIsAUsageError()
    {
        WriteExistingViolation();
        var result = Run(new[]
        {
            _root,
            "--baseline",
            Path.Combine(_root, "nope.json"),
            "-o",
            Path.Combine(_root, "ARCHITECTURE.md"),
        });

        Assert.Equal(ExitCodes.Usage, result.Code);
        Assert.Contains("baseline file not found", result.Stderr);
    }

    [Theory]
    [InlineData("""{"findings":[]}""", "missing schemaVersion")]
    [InlineData("""{"schemaVersion":2,"findings":[]}""", "unsupported baseline schema version 2")]
    [InlineData("""{"schemaVersion":1,"findings":[{"code":""}]}""", "every finding needs a code")]
    [InlineData("""not json""", "invalid baseline")]
    public void InvalidBaselineIsAUsageError(string json, string message)
    {
        WriteExistingViolation();
        var baseline = Path.Combine(_root, "bad-baseline.json");
        File.WriteAllText(baseline, json);

        var result = Run(new[]
        {
            _root, "--baseline", baseline, "-o", Path.Combine(_root, "ARCHITECTURE.md"),
        });

        Assert.Equal(ExitCodes.Usage, result.Code);
        Assert.Contains(message, result.Stderr);
    }

    [Fact]
    public void BaselineFlagsAreMutuallyExclusive()
    {
        var command = CliParser.Parse(new[]
        {
            ".", "--baseline", "a.json", "--write-baseline", "b.json",
        });

        Assert.Equal(CliCommandKind.Error, command.Kind);
        Assert.Contains("either --baseline or --write-baseline", command.Error);
    }

    [Fact]
    public void BaselineKeyIgnoresLineAndMessage()
    {
        var first = new CheckFinding(
            CheckSeverity.Error,
            "MissingHandler",
            "old wording",
            "App.Ping",
            new SourceLocation("Features/Ping.cs", 3, 1));
        var second = first with { Message = "new wording", Location = new SourceLocation("Features/Ping.cs", 99, 7) };

        Assert.Equal(first.BaselineKey, second.BaselineKey);
    }
}
