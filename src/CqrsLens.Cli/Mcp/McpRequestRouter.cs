using System.Text.Json;
using System.Text.Json.Nodes;

namespace CqrsLens;

/// <summary>Parses JSON-RPC requests and routes MCP tool and prompt operations.</summary>
internal static class McpRequestRouter
{
    private const string LatestSupportedProtocolVersion = "2025-06-18";

    private static readonly HashSet<string> SupportedProtocolVersions = new(StringComparer.Ordinal)
    {
        "2024-11-05",
        "2025-03-26",
        LatestSupportedProtocolVersion,
    };

    public static string? Process(
        string jsonPayload,
        CliOptions defaultOptions,
        McpScanCache cache,
        TextWriter? stderr = null)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(jsonPayload);
        }
        catch (JsonException ex)
        {
            stderr?.WriteLine($"mcp parse error: {ex.Message}");
            return McpResponses.BuildError(null, -32700, "Parse error");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return McpResponses.BuildError(null, -32600, "Invalid Request");

            // JsonObject rejects duplicate property names when it materializes its dictionary.
            // Check before routing so a duplicate key cannot terminate the server.
            if (HasDuplicateKeys(root))
                return McpResponses.BuildError(null, -32600, "Invalid Request: duplicate object property");

            return Route(JsonObject.Create(root)!, defaultOptions, cache);
        }
    }

    private static string? Route(JsonObject request, CliOptions defaultOptions, McpScanCache cache)
    {
        var hasId = request.TryGetPropertyValue("id", out var idNode);
        var method = McpResponses.ReadString(request["method"]);

        if (hasId && idNode?.GetValueKind() is not (JsonValueKind.String or JsonValueKind.Number))
            return McpResponses.BuildError(null, -32600, "Invalid Request: id must be a string or number");

        if (McpResponses.ReadString(request["jsonrpc"]) != "2.0" || string.IsNullOrWhiteSpace(method))
            return McpResponses.BuildError(idNode, -32600, "Invalid Request: expected jsonrpc 2.0 and a method");

        // JSON-RPC notifications (no "id" property) do not receive a response.
        if (!hasId)
            return null;

        var rawParameters = request["params"];
        if (rawParameters is not null and not JsonObject)
            return McpResponses.BuildError(idNode, -32602, "Invalid params: expected an object");

        var parameters = rawParameters as JsonObject;
        return method switch
        {
            "initialize" => HandleInitialize(idNode, parameters),
            "ping" => McpResponses.BuildResult(idNode, new JsonObject()),
            "tools/list" => McpToolCatalog.List(idNode),
            "tools/call" => McpToolInvoker.Call(idNode, parameters, defaultOptions, cache),
            "prompts/list" => McpModelFeaturePrompt.List(idNode),
            "prompts/get" => McpModelFeaturePrompt.Get(idNode, parameters),
            _ => McpResponses.BuildError(idNode, -32601, $"Method not found: {method}"),
        };
    }

    private static bool HasDuplicateKeys(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!keys.Add(property.Name) || HasDuplicateKeys(property.Value))
                    return true;
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                if (HasDuplicateKeys(item))
                    return true;
            }
        }

        return false;
    }

    private static string HandleInitialize(JsonNode? idNode, JsonObject? parameters)
    {
        var requestedVersion = McpResponses.ReadString(parameters?["protocolVersion"]);
        var protocolVersion = !string.IsNullOrWhiteSpace(requestedVersion) &&
                              SupportedProtocolVersions.Contains(requestedVersion)
            ? requestedVersion
            : LatestSupportedProtocolVersion;

        var result = new JsonObject
        {
            ["protocolVersion"] = protocolVersion,
            ["capabilities"] = new JsonObject
            {
                ["tools"] = new JsonObject
                {
                    ["listChanged"] = false,
                },
                ["prompts"] = new JsonObject
                {
                    ["listChanged"] = false,
                },
            },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = "cqrslens",
                ["version"] = ToolInfo.Version,
            },
            ["instructions"] = "Use the model_feature, plan_refactor, and review_architecture_change prompts to guide feature design, safe refactoring, and architecture review. Ground changes with get_system_overview, get_architecture_dna, trace_slice, trace_request_impact, trace_entity_impact, and analyze_change_impact; evaluate project changes with evaluate_architecture_proposal; draft and verify slices with draft_slice_design and verify_slice_design; plan and verify test-first execution with plan_slice_tests and verify_tdd_cycle; and verify diffs and claims with diff_architecture, check_architecture, and verify_claim.",
        };

        return McpResponses.BuildResult(idNode, result);
    }
}
