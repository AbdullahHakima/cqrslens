using Xunit;
using Xunit.Abstractions;

namespace CqrsLens.Tests;

/// <summary>
/// The success-test harness for Phase 6 Stage 4: runs every frozen claim in
/// <see cref="FrozenQuestionSet"/> against the real scan of its corpus and asserts both the
/// verdict and that the answer carries evidence (or, for not determinable, the reason).
/// </summary>
public sealed class VerifyClaimCorpusTests
{
    // One cache shared across the cases of this class (xunit runs a class's cases sequentially).
    private static readonly McpScanCache Cache = new();

    private readonly ITestOutputHelper _output;

    public VerifyClaimCorpusTests(ITestOutputHelper output) => _output = output;

    public static IEnumerable<object[]> Cases => FrozenQuestionSet.All.Select(c => new object[] { c.Id });

    [SkippableTheory]
    [MemberData(nameof(Cases))]
    public void FrozenClaim_VerdictMatchesTheRealScan(string id)
    {
        var claim = FrozenQuestionSet.Get(id);
        var root = FrozenQuestionSet.Root(claim.Corpus);
        Skip.If(!Directory.Exists(root), $"corpus not found: {root}");

        var result = McpScanProvider.GetOrRun(
            root, "text", new CliOptions { Path = root, Mcp = true }, Cache);

        var check = AgentClaimVerifier.Verify(result, claim.Subject, claim.Relation, claim.Object);

        Assert.Equal(claim.ExpectedVerdict, check.Verdict);

        if (check.Verdict == ClaimVerdict.NotDeterminable)
        {
            Assert.False(string.IsNullOrWhiteSpace(check.Reason), $"{id}: not-determinable must state a reason.");
            Assert.False(string.IsNullOrWhiteSpace(check.Detail), $"{id}: not-determinable must explain itself.");
            Assert.Contains(claim.ExpectedMarker, $"{check.Reason} {check.Detail}", StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.False(string.IsNullOrWhiteSpace(check.Basis), $"{id}: a decided verdict must state its basis.");
            Assert.NotEmpty(check.Evidence);
            // A claim about a shape the scan holds must cite its file:line provenance; a claim about
            // an absent shape instead says so in its basis.
            Assert.True(
                check.Evidence.Any(e => e.Contains(" (`", StringComparison.Ordinal)) ||
                check.Basis.Contains("absent", StringComparison.OrdinalIgnoreCase),
                $"{id}: evidence must carry file:line provenance (or state the absence).");
            var decidedText = $"{check.Basis}\n{string.Join("\n", check.Evidence)}";
            Assert.Contains(claim.ExpectedMarker, decidedText, StringComparison.OrdinalIgnoreCase);
        }

        // The MCP-facing text must carry the same verdict and the evidence/reason.
        var text = AgentReportGenerator.VerifyClaim(result, claim.Subject, claim.Relation, claim.Object);
        Assert.StartsWith($"verdict: {VerdictText(claim.ExpectedVerdict)}", text, StringComparison.Ordinal);
        Assert.Contains("evidence: ", text, StringComparison.Ordinal);
        if (check.Verdict == ClaimVerdict.NotDeterminable)
            Assert.Contains("reason: ", text, StringComparison.Ordinal);
    }

    private static string VerdictText(ClaimVerdict verdict) => verdict switch
    {
        ClaimVerdict.Supported => "supported",
        ClaimVerdict.NotSupported => "not supported",
        _ => "not determinable",
    };
}
