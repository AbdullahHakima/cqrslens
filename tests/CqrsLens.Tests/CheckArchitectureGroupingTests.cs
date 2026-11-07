using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// The <c>check_architecture</c> per-project grouping form: counts first, ordered by count
/// descending, paged, composing with the A2 filters, and leaving the ungrouped answer
/// byte-for-byte unchanged.
/// </summary>
public sealed class CheckArchitectureGroupingTests
{
    private static readonly string GoldenRoot = Path.Combine(
        FindRepositoryRoot(), "tests", "fixtures", "GoldenCorpus");

    // ------------------------------------------------------------- generator-level grouping

    [Fact]
    public void Grouped_CountsComeFirstAndProjectsAreOrderedByCountDescending()
    {
        var text = AgentChecksSummaryGenerator.GenerateGrouped(BuildResult(), CheckFilter.None, "project");

        Assert.Contains("- **Grouped by**: project", text);
        Assert.Contains("- **Matching findings**: 6", text);
        Assert.Contains("- **Projects**: 3", text);
        Assert.Contains("- **Errors**: 2 · **Warnings**: 4", text);

        var alpha = text.IndexOf("**`Alpha`**", StringComparison.Ordinal);
        var beta = text.IndexOf("**`Beta`**", StringComparison.Ordinal);
        var gamma = text.IndexOf("**`Gamma`**", StringComparison.Ordinal);
        Assert.True(alpha >= 0 && beta > alpha && gamma > beta,
            "projects must be ordered by total count descending (Alpha 3 > Beta 2 > Gamma 1)");
    }

    [Fact]
    public void Grouped_EachProjectCarriesTotalErrorsWarningsAndTheRuleBreakdown()
    {
        var text = AgentChecksSummaryGenerator.GenerateGrouped(BuildResult(), CheckFilter.None, "project");

        Assert.Contains("**`Alpha`** — **3** finding(s): 1 error(s), 2 warning(s)", text);
        Assert.Contains("  - `CommandWithoutValidator` × 1", text);
        Assert.Contains("  - `MissingHandler` × 1", text);
        Assert.Contains("  - `OrphanValidator` × 1", text);
        Assert.Contains("**`Gamma`** — **1** finding(s): 1 error(s), 0 warning(s)", text);
    }

    [Fact]
    public void Grouped_RuleBreakdownSumsToTheProjectTotal()
    {
        var text = AgentChecksSummaryGenerator.GenerateGrouped(BuildResult(), CheckFilter.None, "project");

        // Every project line's total equals the sum of its rule breakdown lines.
        var projectTotals = Regex.Matches(text, @"\*\*`([^`]+)`\*\* — \*\*(\d+)\*\* finding\(s\)")
            .ToDictionary(m => m.Groups[1].Value, m => int.Parse(m.Groups[2].Value));
        var ruleCounts = new Dictionary<string, int>();
        var current = "";
        foreach (var line in text.Split('\n'))
        {
            var project = Regex.Match(line, @"\*\*`([^`]+)`\*\* — \*\*\d+\*\* finding\(s\)");
            if (project.Success)
            {
                current = project.Groups[1].Value;
                ruleCounts.TryAdd(current, 0);
            }
            var rule = Regex.Match(line, @"^\s+- `([^`]+)` × (\d+)$");
            if (rule.Success)
                ruleCounts[current] += int.Parse(rule.Groups[2].Value);
        }

        Assert.Equal(projectTotals.OrderBy(p => p.Key), ruleCounts.OrderBy(p => p.Key));
    }

    [Fact]
    public void Grouped_UnderASeverityFilter_GroupsOnlyTheMatchingFindingsAndStatesTheFilter()
    {
        var text = AgentChecksSummaryGenerator.GenerateGrouped(
            BuildResult(), new CheckFilter(Array.Empty<string>(), "error", null, 0), "project");

        Assert.Contains("- **Filters**: severity=`error`", text);
        Assert.Contains("- **Matching findings**: 2", text);
        Assert.Contains("**`Alpha`** — **1** finding(s): 1 error(s), 0 warning(s)", text);
        Assert.Contains("**`Gamma`** — **1** finding(s): 1 error(s), 0 warning(s)", text);
        Assert.DoesNotContain("`CommandWithoutValidator`", text);
    }

    [Fact]
    public void Grouped_UnderARuleFilter_GroupsOnlyTheMatchingFindings()
    {
        var text = AgentChecksSummaryGenerator.GenerateGrouped(
            BuildResult(), new CheckFilter(new[] { "CommandWithoutValidator" }, null, null, 0), "project");

        Assert.Contains("- **Filters**: rules=`CommandWithoutValidator`", text);
        Assert.Contains("- **Matching findings**: 2", text);
        Assert.Contains("**`Alpha`** — **1**", text);
        Assert.Contains("**`Beta`** — **1**", text);
        Assert.DoesNotContain("**`Gamma`**", text);
    }

    [Fact]
    public void Grouped_PagingStatesThePageAndCountsTheRest()
    {
        var text = AgentChecksSummaryGenerator.GenerateGrouped(
            BuildResult(), new CheckFilter(Array.Empty<string>(), null, null, 1), "project");

        Assert.Contains("- **Projects**: 3 (showing 1, limit 1)", text);
        Assert.Contains("_+2 more project(s)", text);
        Assert.Contains("**`Alpha`**", text);
        Assert.DoesNotContain("**`Beta`**", text);
    }

    [Fact]
    public void Grouped_NoMatch_StatesNone()
    {
        var text = AgentChecksSummaryGenerator.GenerateGrouped(
            BuildResult(), new CheckFilter(new[] { "MissingHandler" }, "error", "Beta", 0), "project");

        Assert.Contains("- **Matching findings**: 0", text);
        Assert.Contains("_No findings match the filters._", text);
    }

    // -------------------------------------------------------------------- tool surface

    [Fact]
    public void Tool_GroupByProject_RoutesToTheGroupingForm()
    {
        var text = CallCheck(new JsonObject
        {
            ["path"] = GoldenRoot,
            ["groupBy"] = "project",
        });

        Assert.Contains("- **Grouped by**: project", text);
        Assert.Contains("- **Matching findings**: 1", text);
    }

    [Fact]
    public void Tool_InvalidGroupBy_IsAnError()
    {
        var result = CallCheckResult(new JsonObject { ["path"] = GoldenRoot, ["groupBy"] = "severity" });

        Assert.True(result["isError"]!.GetValue<bool>());
        Assert.Contains("'groupBy' must be 'project'", result["content"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public void Tool_NoGrouping_IsByteIdenticalToTheFilteredAnswer()
    {
        var result = McpScanProvider.GetOrRun(
            GoldenRoot, "text", new CliOptions { Path = GoldenRoot, Mcp = true }, new McpScanCache());

        var filter = new CheckFilter(new[] { "CommandWithoutValidator" }, null, null, 5);
        var expected = AgentChecksSummaryGenerator.Generate(result, filter);

        var actual = CallCheck(new JsonObject
        {
            ["path"] = GoldenRoot,
            ["rules"] = new JsonArray("CommandWithoutValidator"),
            ["limit"] = 5,
        });

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Tool_NoGrouping_NoFilter_IsTheLegacyAnswer()
    {
        var result = McpScanProvider.GetOrRun(
            GoldenRoot, "text", new CliOptions { Path = GoldenRoot, Mcp = true }, new McpScanCache());

        Assert.Equal(AgentChecksSummaryGenerator.Generate(result),
            CallCheck(new JsonObject { ["path"] = GoldenRoot }));
    }

    // ------------------------------------------------------------------------------ helpers

    private static AnalysisResult BuildResult()
    {
        var result = new AnalysisResult { RootPath = "/tmp/cqrslens-check-grouping" };
        result.Checks.Add(new CheckFinding(
            CheckSeverity.Error, "MissingHandler", "Command `Alpha.FooCommand` has no handler.",
            Target: "Alpha.FooCommand", Location: new SourceLocation("Alpha/FooCommand.cs", 3, 1),
            ProjectName: "Alpha"));
        result.Checks.Add(new CheckFinding(
            CheckSeverity.Warning, "CommandWithoutValidator", "Command `Alpha.BarCommand` has no validator.",
            Target: "Alpha.BarCommand", Location: new SourceLocation("Alpha/BarCommand.cs", 5, 1),
            ProjectName: "Alpha"));
        result.Checks.Add(new CheckFinding(
            CheckSeverity.Warning, "OrphanValidator", "Validator `Alpha.BarValidator` is orphaned.",
            Target: "Alpha.BarValidator", Location: new SourceLocation("Alpha/BarValidator.cs", 7, 1),
            ProjectName: "Alpha"));
        result.Checks.Add(new CheckFinding(
            CheckSeverity.Warning, "CommandWithoutValidator", "Command `Beta.BazCommand` has no validator.",
            Target: "Beta.BazCommand", Location: new SourceLocation("Beta/BazCommand.cs", 5, 1),
            ProjectName: "Beta"));
        result.Checks.Add(new CheckFinding(
            CheckSeverity.Warning, "UnpublishedNotification", "Notification `Beta.Ping` is never published.",
            Target: "Beta.Ping", Location: new SourceLocation("Beta/Ping.cs", 9, 1),
            ProjectName: "Beta"));
        result.Checks.Add(new CheckFinding(
            CheckSeverity.Error, "LayerViolation", "Domain project `Gamma` references Infrastructure.",
            Target: "Gamma -> Infra", Location: new SourceLocation("Gamma/Gamma.csproj", 1, 1),
            ProjectName: "Gamma"));
        return result;
    }

    private static string CallCheck(JsonObject arguments)
        => CallCheckResult(arguments)["content"]![0]!["text"]!.GetValue<string>();

    private static JsonObject CallCheckResult(JsonObject arguments)
    {
        var parameters = new JsonObject
        {
            ["name"] = "check_architecture",
            ["arguments"] = arguments,
        };

        var json = McpToolInvoker.Call(
            JsonValue.Create(1),
            parameters,
            new CliOptions { Path = GoldenRoot, Mcp = true },
            new McpScanCache());

        return JsonNode.Parse(json)!.AsObject()["result"]!.AsObject();
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
