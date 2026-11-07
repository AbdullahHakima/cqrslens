using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// The <c>diff_architecture</c> MCP tool: counts-first shape delta, findings delta identified by
/// the existing baseline key, an explicitly empty delta for identical scans, clean failures for an
/// unreadable base, and agreement with the CLI's own <c>--diff</c> over the same pair.
/// </summary>
public sealed class DiffArchitectureToolTests : IDisposable
{
    private readonly string _temp;
    private readonly string _fixtureBase = Path.Combine(FindRepositoryRoot(), "tests", "fixtures", "DiffPair", "base");
    private readonly string _fixtureHead = Path.Combine(FindRepositoryRoot(), "tests", "fixtures", "DiffPair", "head");
    private readonly string _golden = Path.Combine(FindRepositoryRoot(), "tests", "fixtures", "GoldenCorpus");

    public DiffArchitectureToolTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), "cqrslens-diff-mcp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temp);
    }

    public void Dispose()
    {
        try { Directory.Delete(_temp, recursive: true); } catch { /* best effort */ }
    }

    // ------------------------------------------------------------------ empty delta

    [Fact]
    public void IdenticalScans_ProduceAnExplicitlyEmptyDelta()
    {
        var snapshot = Path.Combine(_temp, "golden.json");
        File.WriteAllText(snapshot, JsonReportGenerator.Generate(ScanGoldenCorpus()));

        var answer = Call(_golden, snapshot);

        Assert.False(answer.IsError);
        Assert.Contains("- **Changed**: no", answer.Text);
        Assert.Contains("- **New findings**: 0", answer.Text);
        Assert.Contains("- **Resolved findings**: 0", answer.Text);
        Assert.Contains("_No architecture delta: 0 added, 0 removed, 0 modified, 0 new, 0 resolved._", answer.Text);
        // The unchanged counts are informational, not zeros: identical scans still share every shape.
        Assert.Contains("unchanged", answer.Text);
    }

    // ------------------------------------------------ fixture pair: one handler removed

    [Fact]
    public void FixturePair_NamesExactlyTheOneHandlerRemoved_AndNothingElse()
    {
        var answer = Call(_fixtureHead, _fixtureBase);

        Assert.False(answer.IsError);
        Assert.Contains("- **Changed**: yes", answer.Text);

        // Exactly one handler removed; nothing added or removed anywhere else.
        Assert.Contains("- **Handlers**: +0 added · -1 removed · =0 unchanged", answer.Text);
        Assert.Contains("- **Requests**: +0 added · -0 removed · =1 unchanged · ~1 modified", answer.Text);
        Assert.Contains("- **Validators**: +0 added · -0 removed · =0 unchanged", answer.Text);
        Assert.Contains("- **Pipeline behaviors**: +0 added · -0 removed · =0 unchanged", answer.Text);
        Assert.Contains("- **DbContexts**: +0 added · -0 removed · =0 unchanged · ~0 modified", answer.Text);
        Assert.Contains("- **Endpoints**: +0 added · -0 removed · =0 unchanged", answer.Text);

        // The removed handler is named; its request records the same single change.
        Assert.Contains("- - `App.DoThingQueryHandler` (request `DoThingQuery`)", answer.Text);
        Assert.Contains("- ~ `DoThingQuery` · project `App` — -Handler: DoThingQueryHandler", answer.Text);

        // No added handler, validator, behavior, DbContext or endpoint surfaces in the item lists.
        Assert.DoesNotContain("- + `", answer.Text);
    }

    [Fact]
    public void FixturePair_FindingsDelta_NamesOneNewAndOneResolved_WithBaselineKeys()
    {
        var answer = Call(_fixtureHead, _fixtureBase);

        Assert.Contains("- **New findings**: 1", answer.Text);
        Assert.Contains("- **Resolved findings**: 1", answer.Text);

        var newKey = BaselineKeys.Create("MissingHandler", "App.DoThingQuery", "App/Features.cs");
        var resolvedKey = BaselineKeys.Create("UnreachableRequest", "App.DoThingQuery", "App/Features.cs");
        Assert.Contains("**[ERROR] `MissingHandler`** target=`App.DoThingQuery`", answer.Text);
        Assert.Contains(newKey, answer.Text);
        Assert.Contains("**[WARN] `UnreachableRequest`** target=`App.DoThingQuery`", answer.Text);
        Assert.Contains(resolvedKey, answer.Text);
    }

    // ------------------------------------------------------------ CLI / MCP agreement

    [Fact]
    public void CliAndMcp_AgreeOnTheFixturePair()
    {
        var reportOut = Path.Combine(_temp, "head.md");
        var diffJson = Path.Combine(_temp, "diff.json");
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var code = CliRunner.Run(
            new[] { _fixtureHead, "-o", reportOut, "--diff", _fixtureBase, "--diff-output", diffJson, "--no-banner", "--no-color" },
            stdout,
            stderr);
        Assert.Equal(ExitCodes.Success, code);

        using var cli = JsonDocument.Parse(File.ReadAllText(diffJson));
        var answer = Call(_fixtureHead, _fixtureBase);
        Assert.False(answer.IsError);

        // The endpoint delta the MCP answer adds is not part of the CLI's serialized diff surface.
        Assert.False(cli.RootElement.TryGetProperty("endpoints", out _));
        Assert.False(cli.RootElement.GetProperty("summary").TryGetProperty("endpoints", out _));

        // Shape counts agree.
        Assert.Equal(
            cli.RootElement.GetProperty("handlers").GetProperty("removed").GetArrayLength(),
            CountMarked(answer.Text, "Handlers"));
        Assert.Equal(
            cli.RootElement.GetProperty("requests").GetProperty("modified").GetArrayLength(),
            ModifiedRequests(answer.Text));

        // Finding sets agree, matched by the same baseline key the CLI diff uses.
        var cliNew = cli.RootElement.GetProperty("checks").GetProperty("newFindings");
        var cliResolved = cli.RootElement.GetProperty("checks").GetProperty("resolvedFindings");
        Assert.Equal(cliNew.GetArrayLength(), KeyCount(answer.Text, "New findings"));
        Assert.Equal(cliResolved.GetArrayLength(), KeyCount(answer.Text, "Resolved findings"));

        foreach (var finding in cliNew.EnumerateArray())
        {
            var key = BaselineKeys.Create(
                finding.GetProperty("code").GetString()!,
                finding.TryGetProperty("target", out var t) ? t.GetString() : null,
                finding.GetProperty("location").GetProperty("file").GetString());
            Assert.Contains(key, answer.Text);
        }

        foreach (var finding in cliResolved.EnumerateArray())
        {
            var key = BaselineKeys.Create(
                finding.GetProperty("code").GetString()!,
                finding.TryGetProperty("target", out var t) ? t.GetString() : null,
                finding.GetProperty("location").GetProperty("file").GetString());
            Assert.Contains(key, answer.Text);
        }
    }

    [Fact]
    public void ItemLists_ArePagedByLimit_ButCountsAreNotTruncated()
    {
        var answer = Call(_golden, _fixtureBase, limit: 1);

        Assert.False(answer.IsError);
        // Counts first, over the whole delta, independent of the page size.
        Assert.Contains("- **Requests**: +4 added · -1 removed · =0 unchanged · ~0 modified", answer.Text);
        Assert.Contains("- **New findings**: 1", answer.Text);
        // Each item list is capped at the page size; the remainder is counted, not printed.
        Assert.Contains("more requests item(s) — raise `limit` (max 200)", answer.Text);
        Assert.Contains("more handlers item(s)", answer.Text);
    }

    [Fact]
    public void SnapshotBaseAndFolderBase_ProduceTheSameDelta()
    {
        var snapshot = Path.Combine(_temp, "base.json");
        File.WriteAllText(snapshot, JsonReportGenerator.Generate(ScanFixture(_fixtureBase)));

        var fromFolder = Call(_fixtureHead, _fixtureBase).Text;
        var fromSnapshot = Call(_fixtureHead, snapshot).Text;

        // The only difference is the base descriptor line; the delta itself is identical.
        Assert.Contains("- **Handlers**: +0 added · -1 removed · =0 unchanged", fromFolder);
        Assert.Contains("- **Handlers**: +0 added · -1 removed · =0 unchanged", fromSnapshot);
        Assert.Contains("- **New findings**: 1", fromSnapshot);
        Assert.Contains("- **Resolved findings**: 1", fromSnapshot);
    }

    // ------------------------------------------------------------------- clean failure

    [Fact]
    public void MissingBase_IsAnArgumentError()
    {
        var answer = Call(_fixtureHead, basePath: null);
        Assert.True(answer.IsError);
        Assert.Contains("'base' argument is required", answer.Text);
    }

    [Fact]
    public void MissingBaseOperand_IsACleanError()
    {
        var answer = Call(_fixtureHead, Path.Combine(_temp, "does-not-exist.json"));
        Assert.True(answer.IsError);
        Assert.Contains("error:", answer.Text);
        Assert.DoesNotContain("## Shape delta", answer.Text);
    }

    [Fact]
    public void MalformedBaseJson_IsACleanError_NotAPartialDelta()
    {
        var malformed = Path.Combine(_temp, "malformed.json");
        File.WriteAllText(malformed, "{ this is not json }");

        var answer = Call(_fixtureHead, malformed);
        Assert.True(answer.IsError);
        Assert.Contains("error:", answer.Text);
        Assert.DoesNotContain("## Shape delta", answer.Text);
    }

    [Fact]
    public void BaseWithoutSchemaVersion_IsACleanError()
    {
        var noSchema = Path.Combine(_temp, "no-schema.json");
        File.WriteAllText(noSchema, "{}");

        var answer = Call(_fixtureHead, noSchema);
        Assert.True(answer.IsError);
        Assert.Contains("missing schemaVersion", answer.Text);
    }

    // ------------------------------------------------------------------------- helpers

    private sealed record Answer(bool IsError, string Text);

    private static Answer Call(string headPath, string? basePath, int limit = 0)
    {
        var arguments = new JsonObject { ["path"] = headPath };
        if (basePath is not null)
            arguments["base"] = basePath;
        if (limit > 0)
            arguments["limit"] = limit;

        var parameters = new JsonObject
        {
            ["name"] = "diff_architecture",
            ["arguments"] = arguments,
        };

        var json = McpToolInvoker.Call(
            JsonValue.Create(1),
            parameters,
            new CliOptions { Path = headPath, Mcp = true },
            new McpScanCache());

        var result = JsonNode.Parse(json)!.AsObject()["result"]!.AsObject();
        return new Answer(
            result["isError"]!.GetValue<bool>(),
            result["content"]![0]!["text"]!.GetValue<string>());
    }

    private AnalysisResult ScanGoldenCorpus()
        => McpScanProvider.GetOrRun(_golden, "text", new CliOptions { Path = _golden, Mcp = true }, new McpScanCache());

    private static AnalysisResult ScanFixture(string path)
        => McpScanProvider.GetOrRun(path, "text", new CliOptions { Path = path, Mcp = true }, new McpScanCache());

    private static IEnumerable<string> SplitLines(string text)
        => text.Split('\n').Select(l => l.TrimEnd('\r'));

    private static int CountMarked(string text, string label)
    {
        var line = SplitLines(text).FirstOrDefault(l => l.StartsWith($"- **{label}**:", StringComparison.Ordinal));
        if (line is null)
            return -1;

        var removed = Regex.Match(line, @"-(\d+) removed");
        return removed.Success ? int.Parse(removed.Groups[1].Value) : -1;
    }

    private static int ModifiedRequests(string text)
    {
        var line = SplitLines(text).First(l => l.StartsWith("- **Requests**:", StringComparison.Ordinal));
        var modified = Regex.Match(line, @"~(\d+) modified");
        return modified.Success ? int.Parse(modified.Groups[1].Value) : 0;
    }

    private static int KeyCount(string text, string heading)
    {
        var match = Regex.Match(text, $"### {Regex.Escape(heading)} \\((\\d+)\\)");
        return match.Success ? int.Parse(match.Groups[1].Value) : -1;
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
