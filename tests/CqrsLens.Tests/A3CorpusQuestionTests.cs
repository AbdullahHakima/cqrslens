using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace CqrsLens.Tests;

/// <summary>
/// The Stage 4 A3 success-test harness: runs every frozen request-impact query, every frozen
/// grouped-check query, and the grouping reconciliation against the real scan of its corpus. A
/// corpus that is not present on disk is skipped rather than answered from a hand-built model.
/// </summary>
public sealed class A3CorpusQuestionTests
{
    // One cache shared across the cases of this class (xunit runs a class's cases sequentially).
    private static readonly McpScanCache Cache = new();

    private readonly ITestOutputHelper _output;

    public A3CorpusQuestionTests(ITestOutputHelper output) => _output = output;

    public static IEnumerable<object[]> RequestImpactCases =>
        FrozenQuestionSet.RequestImpactQueries.Select(q => new object[] { q.Id });

    public static IEnumerable<object[]> GroupingCases =>
        FrozenQuestionSet.GroupingQueries.Select(q => new object[] { q.Id });

    public static IEnumerable<object[]> ReconciliationCases =>
        FrozenQuestionSet.GroupingReconciliation.Select(r => new object[] { r.Corpus.ToString() });

    [Theory]
    [MemberData(nameof(RequestImpactCases))]
    public void FrozenRequestImpactQuery_MatchesTheRealScan(string id)
    {
        var query = FrozenQuestionSet.GetRequestImpactQuery(id);
        var root = FrozenQuestionSet.Root(query.Corpus);
        if (!Directory.Exists(root))
        {
            _output.WriteLine($"SKIP {id}: corpus root not found at {root}.");
            throw Xunit.Sdk.SkipException.ForSkip($"corpus not found: {root}");
        }

        _output.WriteLine($"derivation: {query.Derivation}");

        var result = Scan(root);
        var text = AgentReportGenerator.GenerateRequestImpact(result, query.Request, query.Contains, query.Limit);

        foreach (var marker in query.ExpectedMarkers)
            Assert.Contains(marker, text, StringComparison.Ordinal);
        foreach (var marker in query.AbsentMarkers)
            Assert.DoesNotContain(marker, text, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(GroupingCases))]
    public void FrozenGroupingQuery_MatchesTheRealScan(string id)
    {
        var query = FrozenQuestionSet.GetGroupingQuery(id);
        var root = FrozenQuestionSet.Root(query.Corpus);
        if (!Directory.Exists(root))
        {
            _output.WriteLine($"SKIP {id}: corpus root not found at {root}.");
            throw Xunit.Sdk.SkipException.ForSkip($"corpus not found: {root}");
        }

        _output.WriteLine($"derivation: {query.Derivation}");

        var result = Scan(root);
        var text = AgentChecksSummaryGenerator.GenerateGrouped(
            result, new CheckFilter(query.Rules, query.Severity, query.Target, query.Limit), "project");

        Assert.Contains($"- **Matching findings**: {query.ExpectedMatching}", text, StringComparison.Ordinal);
        Assert.Contains($"- **Projects**: {query.ExpectedGroups}", text, StringComparison.Ordinal);
        if (query.Limit > 0)
        {
            Assert.Contains($"- **Projects**: {query.ExpectedGroups} (showing {query.ExpectedShownGroups}",
                text, StringComparison.Ordinal);
        }
        foreach (var marker in query.ExpectedMarkers)
            Assert.Contains(marker, text, StringComparison.Ordinal);

        // When every group is shown, the per-project totals must sum back to the matching count.
        if (query.ExpectedShownGroups == query.ExpectedGroups)
            Assert.Equal(query.ExpectedMatching, SumProjectTotals(text));
    }

    [Theory]
    [MemberData(nameof(ReconciliationCases))]
    public void GroupingTotals_ReconcileExactlyWithTheUnfilteredCount(string corpusName)
    {
        var reconciliation = FrozenQuestionSet.GroupingReconciliation
            .First(r => r.Corpus.ToString() == corpusName);
        var root = FrozenQuestionSet.Root(reconciliation.Corpus);
        if (!Directory.Exists(root))
        {
            _output.WriteLine($"SKIP {corpusName}: corpus root not found at {root}.");
            throw Xunit.Sdk.SkipException.ForSkip($"corpus not found: {root}");
        }

        _output.WriteLine($"derivation: {reconciliation.Derivation}");

        var result = Scan(root);

        var activeChecks = result.BaselineApplied
            ? result.Checks.Where(c => !c.IsKnown).Count()
            : result.Checks.Count;
        Assert.Equal(reconciliation.ExpectedTotal, activeChecks);

        // The unfiltered answer and the grouped answer describe the same findings.
        var unfiltered = AgentChecksSummaryGenerator.Generate(result);
        var errors = result.Checks.Count(c => c.Severity == CheckSeverity.Error);
        var warnings = activeChecks - errors;
        Assert.Contains($"- **Errors**: {errors}", unfiltered, StringComparison.Ordinal);
        Assert.Contains($"- **Warnings**: {warnings}", unfiltered, StringComparison.Ordinal);

        var grouped = AgentChecksSummaryGenerator.GenerateGrouped(result, CheckFilter.None, "project");
        Assert.Contains($"- **Matching findings**: {reconciliation.ExpectedTotal}", grouped, StringComparison.Ordinal);
        Assert.Contains($"- **Errors**: {errors} · **Warnings**: {warnings}", grouped, StringComparison.Ordinal);
        Assert.Equal(reconciliation.ExpectedTotal, SumProjectTotals(grouped));
    }

    /// <summary>Sums the "**N** finding(s)" totals across the grouped project lines.</summary>
    internal static int SumProjectTotals(string groupedText)
        => Regex.Matches(groupedText, @"— \*\*(\d+)\*\* finding\(s\)")
            .Sum(m => int.Parse(m.Groups[1].Value));

    private static AnalysisResult Scan(string root)
        => McpScanProvider.GetOrRun(
            root, "text", new CliOptions { Path = root, Mcp = true }, Cache);
}
