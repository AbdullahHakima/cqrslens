using System.Text.Json.Nodes;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// The <c>check_architecture</c> filter and paging contract: each filter dimension alone, the
/// dimensions composed (AND), paging, the no-filter byte-identity, and argument validation.
/// </summary>
public sealed class CheckArchitectureFilterTests
{
    private static readonly string GoldenRoot = Path.Combine(
        FindRepositoryRoot(), "tests", "fixtures", "GoldenCorpus");

    // ---------------------------------------------------------------- generator-level filters

    [Fact]
    public void RuleFilterAlone_SelectsOnlyThatRule()
    {
        var text = AgentChecksSummaryGenerator.Generate(BuildResult(), Filter(rules: ["MissingHandler"]));

        Assert.Contains("**Matching findings**: 1", text);
        Assert.Contains("`MissingHandler`", text);
        Assert.DoesNotContain("`CommandWithoutValidator`", text);
    }

    [Fact]
    public void MultipleRules_AreAnOrWithinTheRuleDimension()
    {
        var text = AgentChecksSummaryGenerator.Generate(
            BuildResult(), Filter(rules: ["MissingHandler", "OrphanValidator"]));

        Assert.Contains("**Matching findings**: 2", text);
        Assert.Contains("`MissingHandler`", text);
        Assert.Contains("`OrphanValidator`", text);
        Assert.DoesNotContain("`CommandWithoutValidator`", text);
    }

    [Fact]
    public void SeverityFilterAlone_SelectsOnlyThatSeverity()
    {
        var text = AgentChecksSummaryGenerator.Generate(BuildResult(), Filter(severity: "error"));

        Assert.Contains("**Matching findings**: 1", text);
        Assert.Contains("[ERROR]", text);
        Assert.DoesNotContain("[WARN]", text);
    }

    [Fact]
    public void TargetFilter_MatchesFindingTargetSubstring()
    {
        var text = AgentChecksSummaryGenerator.Generate(BuildResult(), Filter(target: "Bar"));

        Assert.Contains("**Matching findings**: 2", text);
        Assert.Contains("App.Orders.BarCommand", text);
        Assert.Contains("App.Orders.BarValidator", text);
        Assert.DoesNotContain("App.Orders.FooCommand", text);
    }

    [Fact]
    public void TargetFilter_MatchesProjectNameExactly()
    {
        var text = AgentChecksSummaryGenerator.Generate(BuildResult(), Filter(target: "Projection"));

        Assert.Contains("**Matching findings**: 1", text);
        Assert.Contains("`LayerViolation`", text);
        Assert.DoesNotContain("`MissingHandler`", text);
    }

    [Fact]
    public void ComposedFilters_AreAnded()
    {
        var text = AgentChecksSummaryGenerator.Generate(
            BuildResult(),
            Filter(rules: ["CommandWithoutValidator"], severity: "warning", target: "Bar"));

        Assert.Contains("**Matching findings**: 1", text);
        Assert.Contains("App.Orders.BarCommand", text);
        Assert.DoesNotContain("`MissingHandler`", text);
    }

    [Fact]
    public void ComposedFilters_ThatCannotAllMatch_YieldNothing()
    {
        var text = AgentChecksSummaryGenerator.Generate(
            BuildResult(),
            Filter(rules: ["CommandWithoutValidator"], severity: "error", target: "Bar"));

        Assert.Contains("**Matching findings**: 0", text);
        Assert.Contains("No findings match the filters", text);
    }

    [Fact]
    public void Paging_StatesTheTotalAndThePage()
    {
        var text = AgentChecksSummaryGenerator.Generate(BuildResult(), Filter(severity: "warning", limit: 1));

        Assert.Contains("**Matching findings**: 3", text);
        Assert.Contains("**Showing**: 1 (limit 1)", text);
        Assert.Contains("_+2 more matching finding(s)", text);
        Assert.Contains("(max 200)", text);
    }

    [Fact]
    public void EmptyFilter_ReturnsTheLegacyAnswerByteForByte()
    {
        var result = BuildResult();

        Assert.Equal(AgentChecksSummaryGenerator.Generate(result), AgentChecksSummaryGenerator.Generate(result, CheckFilter.None));
    }

    [Fact]
    public void FilteringNeverAltersAFinding()
    {
        var result = BuildResult();
        var legacy = AgentChecksSummaryGenerator.Generate(result);
        var filtered = AgentChecksSummaryGenerator.Generate(result, Filter(rules: ["MissingHandler"]));

        // The selected finding's rule, severity, target and file:line survive filtering verbatim.
        Assert.Contains("**[ERROR] `MissingHandler`** target=`App.Orders.FooCommand`", filtered);
        Assert.Contains("(`App/Orders/FooCommand.cs:3`)", filtered);
        Assert.Contains("`MissingHandler`", legacy);
    }

    // ------------------------------------------------------------------- tool-level behaviour

    [Fact]
    public void Tool_NoArguments_IsByteIdenticalToTheUnfilteredAnswer()
    {
        var result = McpScanProvider.GetOrRun(
            GoldenRoot, "text", new CliOptions { Path = GoldenRoot, Mcp = true }, new McpScanCache());
        var legacy = AgentChecksSummaryGenerator.Generate(result);

        Assert.Equal(legacy, CallCheck(new JsonObject { ["path"] = GoldenRoot }));
    }

    [Fact]
    public void Tool_AcceptsCommaSeparatedRulesList()
    {
        var text = CallCheck(new JsonObject
        {
            ["path"] = GoldenRoot,
            ["rules"] = "CommandWithoutValidator,OrphanValidator",
        });

        Assert.Contains("**Matching findings**: 1", text);
        Assert.Contains("rules=`CommandWithoutValidator`, `OrphanValidator`", text);
    }

    [Fact]
    public void Tool_AcceptsRulesArray()
    {
        var text = CallCheck(new JsonObject
        {
            ["path"] = GoldenRoot,
            ["rules"] = new JsonArray("CommandWithoutValidator"),
        });

        Assert.Contains("**Matching findings**: 1", text);
    }

    [Fact]
    public void Tool_UnknownRuleCode_IsAnErrorThatListsKnownCodes()
    {
        var result = CallCheckResult(new JsonObject { ["path"] = GoldenRoot, ["rules"] = new JsonArray("NoSuchRule") });

        Assert.True(result["isError"]!.GetValue<bool>());
        var text = result["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("unknown rule code", text);
        Assert.Contains("MissingHandler", text);
    }

    [Fact]
    public void Tool_InvalidSeverity_IsAnError()
    {
        var result = CallCheckResult(new JsonObject { ["path"] = GoldenRoot, ["severity"] = "fatal" });

        Assert.True(result["isError"]!.GetValue<bool>());
        Assert.Contains("'severity' must be 'error' or 'warning'", result["content"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public void Tool_LimitAboveMaximum_IsAnError()
    {
        var result = CallCheckResult(new JsonObject { ["path"] = GoldenRoot, ["limit"] = 201 });

        Assert.True(result["isError"]!.GetValue<bool>());
        Assert.Contains("between 1 and 200", result["content"]![0]!["text"]!.GetValue<string>());
    }

    // ------------------------------------------------------------------------------ helpers

    private static CheckFilter Filter(
        IReadOnlyList<string>? rules = null,
        string? severity = null,
        string? target = null,
        int limit = 0)
        => new(rules ?? Array.Empty<string>(), severity, target, limit);

    private static AnalysisResult BuildResult()
    {
        var result = new AnalysisResult { RootPath = "/tmp/cqrslens-check-filter" };
        result.Checks.Add(new CheckFinding(
            CheckSeverity.Error, "MissingHandler",
            "Command `App.Orders.FooCommand` has no handler.",
            Target: "App.Orders.FooCommand",
            Location: new SourceLocation("App/Orders/FooCommand.cs", 3, 1),
            ProjectName: "App"));
        result.Checks.Add(new CheckFinding(
            CheckSeverity.Warning, "CommandWithoutValidator",
            "Command `App.Orders.BarCommand` has no validator.",
            Target: "App.Orders.BarCommand",
            Location: new SourceLocation("App/Orders/BarCommand.cs", 5, 1),
            ProjectName: "App"));
        result.Checks.Add(new CheckFinding(
            CheckSeverity.Warning, "OrphanValidator",
            "Validator `App.Orders.BarValidator` targets `App.Foo`, which does not match any request.",
            Target: "App.Orders.BarValidator",
            Location: new SourceLocation("App/Orders/BarValidator.cs", 7, 1),
            ProjectName: "App"));
        result.Checks.Add(new CheckFinding(
            CheckSeverity.Warning, "LayerViolation",
            "Domain project `Projection` references Infrastructure project `Infra`.",
            Target: "Projection -> Infra",
            Location: new SourceLocation("Projection/Projection.csproj", 1, 1),
            ProjectName: "Projection"));
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
