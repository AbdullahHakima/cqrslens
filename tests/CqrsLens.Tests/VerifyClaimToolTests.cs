using System.Text.Json.Nodes;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// Tool-level tests for the <c>verify_claim</c> MCP tool: catalog annotations, argument
/// validation, routing and the MCP-facing answer shape. Runs against the in-repo Golden corpus.
/// </summary>
public sealed class VerifyClaimToolTests
{
    private static readonly string GoldenRoot = Path.Combine(
        FindRepositoryRoot(), "tests", "fixtures", "GoldenCorpus");

    [Fact]
    public void ToolsList_IncludesVerifyClaim_AndEveryToolIsReadOnly()
    {
        var listed = JsonNode.Parse(McpToolCatalog.List(JsonValue.Create(1)))!.AsObject();
        var tools = listed["result"]!["tools"]!.AsArray();

        Assert.Equal(7, tools.Count);
        Assert.Contains(tools, t => t!["name"]!.GetValue<string>() == "verify_claim");
        Assert.Contains(tools, t => t!["name"]!.GetValue<string>() == "trace_request_impact");
        Assert.Contains(tools, t => t!["name"]!.GetValue<string>() == "diff_architecture");

        foreach (var tool in tools)
        {
            Assert.True(tool!["annotations"]!["readOnlyHint"]!.GetValue<bool>());
            Assert.NotNull(tool["inputSchema"]);
        }
    }

    [Fact]
    public void VerifyClaim_SupportedExistenceClaim_CarriesEvidenceWithProvenance()
    {
        var result = Call("CreateOrderCommand", "has handler", "Golden.Application.Orders.CreateOrderCommandHandler");

        Assert.False(result["isError"]!.GetValue<bool>());
        var text = result["content"]![0]!["text"]!.GetValue<string>();
        Assert.StartsWith("verdict: supported", text);
        Assert.Contains("lists handler", text);
        Assert.Contains("evidence: ", text);
        Assert.Contains(".cs:", text);
    }

    [Fact]
    public void VerifyClaim_SupportedAbsenceClaim()
    {
        var result = Call("CancelOrderCommand", "has no validator", null);

        var text = result["content"]![0]!["text"]!.GetValue<string>();
        Assert.StartsWith("verdict: supported", text);
        Assert.Contains("lists no validators", text);
    }

    [Fact]
    public void VerifyClaim_NotSupportedClaim_ShowsContradictingEvidence()
    {
        var result = Call("CreateOrderCommand", "has no validator", null);

        var text = result["content"]![0]!["text"]!.GetValue<string>();
        Assert.StartsWith("verdict: not supported", text);
        Assert.Contains("CreateOrderCommandValidator", text);
    }

    [Fact]
    public void VerifyClaim_OutOfVocabularyRelation_IsNotDeterminableNotAnError()
    {
        var result = Call("CreateOrderCommand", "executes before", "CancelOrderCommand");

        Assert.False(result["isError"]!.GetValue<bool>());
        var text = result["content"]![0]!["text"]!.GetValue<string>();
        Assert.StartsWith("verdict: not determinable", text);
        Assert.Contains("reason: outside the scan model", text);
    }

    [Fact]
    public void VerifyClaim_MissingSubject_IsAnError()
    {
        var result = Call(null, "has handler", "DailyHandler");

        Assert.True(result["isError"]!.GetValue<bool>());
        Assert.Contains("'subject' argument is required", result["content"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public void VerifyClaim_MissingRelation_IsAnError()
    {
        var result = Call("CreateOrderCommand", null, null);

        Assert.True(result["isError"]!.GetValue<bool>());
        Assert.Contains("'relation' argument is required", result["content"]![0]!["text"]!.GetValue<string>());
    }

    private static JsonObject Call(string? subject, string? relation, string? claimObject)
    {
        var arguments = new JsonObject();
        if (subject is not null) arguments["subject"] = subject;
        if (relation is not null) arguments["relation"] = relation;
        if (claimObject is not null) arguments["object"] = claimObject;

        var parameters = new JsonObject
        {
            ["name"] = "verify_claim",
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
