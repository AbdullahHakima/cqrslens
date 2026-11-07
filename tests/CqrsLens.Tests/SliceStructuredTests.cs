using System.Text.Json.Nodes;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// The <c>trace_slice</c> structured form: the default stays the rendered text trace, while
/// <c>format: "json"</c> returns the same slice as typed data read from the scan model alone.
/// </summary>
public sealed class SliceStructuredTests
{
    private static readonly string GoldenRoot = Path.Combine(
        FindRepositoryRoot(), "tests", "fixtures", "GoldenCorpus");

    [Fact]
    public void NoFormat_IsTheRenderedTextTrace_ByteForByte()
    {
        var result = Scan();
        var legacy = AgentReportGenerator.Generate(result, "CreateOrderCommand", 0);

        Assert.Equal(legacy, CallSlice(new JsonObject { ["query"] = "CreateOrderCommand" }));
    }

    [Fact]
    public void TextFormat_EqualsTheDefault()
    {
        Assert.Equal(
            CallSlice(new JsonObject { ["query"] = "CreateOrderCommand" }),
            CallSlice(new JsonObject { ["query"] = "CreateOrderCommand", ["format"] = "text" }));
    }

    [Fact]
    public void JsonFormat_IsParsableStructuredDataAndDiffersFromText()
    {
        var json = CallSlice(new JsonObject { ["query"] = "CreateOrderCommand", ["format"] = "json" });
        var parsed = JsonNode.Parse(json)!.AsObject();

        Assert.Equal("GoldenCorpus", parsed["root"]!.GetValue<string>());
        Assert.Equal("CreateOrderCommand", parsed["query"]!.GetValue<string>());
        Assert.Equal(1, parsed["totalMatched"]!.GetValue<int>());
        Assert.NotEqual(AgentReportGenerator.Generate(Scan(), "CreateOrderCommand", 0), json);
    }

    [Fact]
    public void JsonFormat_CarriesRequestHandlerValidatorsAndNotifications()
    {
        var slice = OnlySlice(CallJson(new JsonObject { ["query"] = "CreateOrderCommand" }));

        var request = slice["request"]!.AsObject();
        Assert.Equal("CreateOrderCommand", request["name"]!.GetValue<string>());
        Assert.Equal("Golden.Application.Orders.CreateOrderCommand", request["fullName"]!.GetValue<string>());
        Assert.Equal("Guid", request["responseType"]!.GetValue<string>());

        Assert.Equal("CreateOrderCommandValidator", slice["validators"]!.AsArray()[0]!["name"]!.GetValue<string>());

        var handler = slice["handlers"]!.AsArray()[0]!.AsObject();
        Assert.Equal("CreateOrderCommandHandler", handler["name"]!.GetValue<string>());
        Assert.Contains("IOrderRepository", handler["dependencies"]!.AsArray().Select(d => d!.GetValue<string>()));
        Assert.Contains("Orders (Order)", handler["touchedDbSets"]!.AsArray().Select(d => d!.GetValue<string>()));

        var notification = slice["notifications"]!.AsArray()[0]!.AsObject();
        Assert.Equal("OrderPlacedNotification", notification["name"]!.GetValue<string>());
        var notificationHandlers = notification["handlers"]!.AsArray()
            .Select(h => h!["name"]!.GetValue<string>())
            .ToList();
        Assert.Contains("OrderPlacedNotificationHandler", notificationHandlers);
        Assert.Contains("OrderPlacedEmailHandler", notificationHandlers);
    }

    [Fact]
    public void JsonFormat_EndpointEdgesAreTheModelsOwnEdges()
    {
        var slice = OnlySlice(CallJson(new JsonObject { ["query"] = "CreateOrderCommand" }));

        var edge = slice["endpointEdges"]!.AsArray()[0]!.AsObject();
        Assert.Equal("endpointToRequest", edge["kind"]!.GetValue<string>());
        Assert.Equal("Golden.Application.Orders.CreateOrderCommand", edge["to"]!.GetValue<string>());
        Assert.False(string.IsNullOrEmpty(edge["location"]!.GetValue<string>()));
    }

    [Fact]
    public void JsonFormat_BehaviorsCarryTheRegisteredOrderLabel()
    {
        var behaviors = CallJson(new JsonObject { ["query"] = "CreateOrderCommand" })["behaviors"]!.AsObject();

        var label = behaviors["label"]!.GetValue<string>();
        Assert.Contains("registered order", label);
        Assert.Contains("Execution order is not guaranteed", label);

        // The Golden behavior has no resolvable registration site, so it is not order-determinable.
        var unregistered = behaviors["unregistered"]!.AsArray();
        var behavior = unregistered[0]!.AsObject();
        Assert.Equal("LoggingBehavior", behavior["name"]!.GetValue<string>());
        Assert.False(behavior["orderDeterminable"]!.GetValue<bool>());
        Assert.Empty(behaviors["registered"]!.AsArray());
    }

    [Fact]
    public void JsonFormat_AbsentValuesAreAbsentNotInvented()
    {
        var slice = OnlySlice(CallJson(new JsonObject { ["query"] = "GetOrderQuery" }));

        // The model holds no validator and no published notification for this request, so the
        // arrays are empty rather than completed with a plausible value.
        Assert.Empty(slice["validators"]!.AsArray());
        Assert.Empty(slice["notifications"]!.AsArray());
        Assert.Equal("GetOrderQuery", slice["request"]!["name"]!.GetValue<string>());
    }

    [Fact]
    public void JsonFormat_LimitCapsShownAndCountsTheTotal()
    {
        var parsed = CallJson(new JsonObject { ["query"] = "Order", ["format"] = "json", ["limit"] = 1 });

        Assert.Equal(4, parsed["totalMatched"]!.GetValue<int>());
        Assert.Equal(1, parsed["shown"]!.GetValue<int>());
        Assert.Single(parsed["slices"]!.AsArray());
    }

    [Fact]
    public void InvalidFormat_IsAnError()
    {
        var result = CallSliceResult(new JsonObject { ["query"] = "CreateOrderCommand", ["format"] = "yaml" });

        Assert.True(result["isError"]!.GetValue<bool>());
        Assert.Contains("'format' must be 'text' or 'json'", result["content"]![0]!["text"]!.GetValue<string>());
    }

    // ------------------------------------------------------------------------------ helpers

    private static JsonObject OnlySlice(JsonObject parsed)
    {
        var slices = parsed["slices"]!.AsArray();
        Assert.Single(slices);
        return slices[0]!.AsObject();
    }

    private static JsonObject CallJson(JsonObject arguments)
    {
        arguments["format"] = "json";
        return JsonNode.Parse(CallSlice(arguments))!.AsObject();
    }

    private static string CallSlice(JsonObject arguments)
        => CallSliceResult(arguments)["content"]![0]!["text"]!.GetValue<string>();

    private static JsonObject CallSliceResult(JsonObject arguments)
    {
        var parameters = new JsonObject
        {
            ["name"] = "trace_slice",
            ["arguments"] = arguments,
        };

        var json = McpToolInvoker.Call(
            JsonValue.Create(1),
            parameters,
            new CliOptions { Path = GoldenRoot, Mcp = true },
            new McpScanCache());

        return JsonNode.Parse(json)!.AsObject()["result"]!.AsObject();
    }

    private static AnalysisResult Scan()
        => McpScanProvider.GetOrRun(
            GoldenRoot, "text", new CliOptions { Path = GoldenRoot, Mcp = true }, new McpScanCache());

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
