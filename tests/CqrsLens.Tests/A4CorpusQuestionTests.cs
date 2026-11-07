using System.Text.Json.Nodes;
using Xunit;
using Xunit.Abstractions;

namespace CqrsLens.Tests;

/// <summary>
/// The Stage 4 A4 success-test harness: runs every frozen <c>diff_architecture</c> change question
/// against the real diff (the in-repo fixture pair, a real corpus pair, and a corpus against its
/// own snapshot). A corpus that is not present on disk is skipped rather than answered from a
/// hand-built substitute.
/// </summary>
public sealed class A4CorpusQuestionTests : IDisposable
{
    // One cache shared across the cases of this class (xunit runs a class's cases sequentially).
    private static readonly McpScanCache Cache = new();

    private readonly ITestOutputHelper _output;
    private readonly string _temp;

    public A4CorpusQuestionTests(ITestOutputHelper output)
    {
        _output = output;
        _temp = Path.Combine(Path.GetTempPath(), "cqrslens-a4-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temp);
    }

    public void Dispose()
    {
        try { Directory.Delete(_temp, recursive: true); } catch { /* best effort */ }
    }

    public static IEnumerable<object[]> DiffCases =>
        FrozenQuestionSet.DiffQueries.Select(q => new object[] { q.Id });

    [SkippableTheory]
    [MemberData(nameof(DiffCases))]
    public void FrozenDiffQuery_MatchesTheRealDiff(string id)
    {
        var query = FrozenQuestionSet.GetDiffQuery(id);

        string headRoot;
        string basePath;
        if (query.FixturePair)
        {
            var repo = FindRepositoryRoot();
            headRoot = Path.Combine(repo, "tests", "fixtures", "DiffPair", "head");
            basePath = Path.Combine(repo, "tests", "fixtures", "DiffPair", "base");
        }
        else
        {
            headRoot = FrozenQuestionSet.Root(query.HeadCorpus!.Value);
            basePath = FrozenQuestionSet.Root(query.BaseCorpus!.Value);
            Skip.If(!Directory.Exists(headRoot) || !Directory.Exists(basePath), $"corpus not found: {headRoot}");
        }

        _output.WriteLine($"derivation: {query.Derivation}");

        if (query.BaseFromSnapshot)
        {
            var snapshot = Path.Combine(_temp, id + ".json");
            var baseResult = McpScanProvider.GetOrRun(
                basePath, "text", new CliOptions { Path = basePath, Mcp = true }, Cache);
            File.WriteAllText(snapshot, JsonReportGenerator.Generate(baseResult));
            basePath = snapshot;
        }

        var text = CallDiff(headRoot, basePath);

        foreach (var marker in query.ExpectedMarkers)
            Assert.Contains(marker, text, StringComparison.Ordinal);
        foreach (var marker in query.AbsentMarkers)
            Assert.DoesNotContain(marker, text, StringComparison.Ordinal);
    }

    private static string CallDiff(string headPath, string basePath)
    {
        var parameters = new JsonObject
        {
            ["name"] = "diff_architecture",
            ["arguments"] = new JsonObject
            {
                ["path"] = headPath,
                ["base"] = basePath,
            },
        };

        var json = McpToolInvoker.Call(
            JsonValue.Create(1),
            parameters,
            new CliOptions { Path = headPath, Mcp = true },
            Cache);

        return JsonNode.Parse(json)!.AsObject()["result"]!.AsObject()["content"]![0]!["text"]!.GetValue<string>();
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
