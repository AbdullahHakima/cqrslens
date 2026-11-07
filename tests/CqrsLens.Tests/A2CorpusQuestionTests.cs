using System.Text.Json.Nodes;
using Xunit;
using Xunit.Abstractions;

namespace CqrsLens.Tests;

/// <summary>
/// The Stage 4 A2 success-test harness: runs every frozen filtered-check query and every frozen
/// structured-slice query against the real scan of its corpus and asserts the answer the scan holds.
/// A corpus that is not present on disk is skipped rather than answered from a hand-built model.
/// </summary>
public sealed class A2CorpusQuestionTests
{
    // One cache shared across the cases of this class (xunit runs a class's cases sequentially).
    private static readonly McpScanCache Cache = new();

    private readonly ITestOutputHelper _output;

    public A2CorpusQuestionTests(ITestOutputHelper output) => _output = output;

    public static IEnumerable<object[]> CheckCases =>
        FrozenQuestionSet.CheckQueries.Select(q => new object[] { q.Id });

    public static IEnumerable<object[]> SliceCases =>
        FrozenQuestionSet.SliceQueries.Select(q => new object[] { q.Id });

    [Theory]
    [MemberData(nameof(CheckCases))]
    public void FrozenCheckQuery_MatchesTheRealScan(string id)
    {
        var query = FrozenQuestionSet.GetCheckQuery(id);
        var root = FrozenQuestionSet.Root(query.Corpus);
        if (!Directory.Exists(root))
        {
            _output.WriteLine($"SKIP {id}: corpus root not found at {root}.");
            throw Xunit.Sdk.SkipException.ForSkip($"corpus not found: {root}");
        }

        _output.WriteLine($"derivation: {query.Derivation}");

        var result = McpScanProvider.GetOrRun(
            root, "text", new CliOptions { Path = root, Mcp = true }, Cache);

        var text = AgentChecksSummaryGenerator.Generate(
            result,
            new CheckFilter(query.Rules, query.Severity, query.Target, query.Limit));

        Assert.Contains($"**Matching findings**: {query.ExpectedTotal}", text);
        Assert.Contains($"**Showing**: {query.ExpectedShown}", text);
        foreach (var marker in query.ExpectedMarkers)
            Assert.Contains(marker, text, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(SliceCases))]
    public void FrozenSliceQuery_MatchesTheRealScan(string id)
    {
        var query = FrozenQuestionSet.GetSliceQuery(id);
        var root = FrozenQuestionSet.Root(query.Corpus);
        if (!Directory.Exists(root))
        {
            _output.WriteLine($"SKIP {id}: corpus root not found at {root}.");
            throw Xunit.Sdk.SkipException.ForSkip($"corpus not found: {root}");
        }

        _output.WriteLine($"derivation: {query.Derivation}");

        var result = McpScanProvider.GetOrRun(
            root, "text", new CliOptions { Path = root, Mcp = true }, Cache);

        var json = AgentReportGenerator.GenerateSliceData(result, query.Query, query.Limit);
        var parsed = JsonNode.Parse(json)!.AsObject();

        Assert.Equal(query.ExpectedTotalMatched, parsed["totalMatched"]!.GetValue<int>());
        foreach (var marker in query.ExpectedMarkers)
            Assert.Contains(marker, json, StringComparison.Ordinal);
        foreach (var marker in query.AbsentMarkers)
            Assert.DoesNotContain(marker, json, StringComparison.Ordinal);
    }
}
