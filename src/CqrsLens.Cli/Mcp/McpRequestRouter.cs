using System.Text.Json;
using System.Text.Json.Nodes;

namespace CqrsLens;

/// <summary>Parses JSON-RPC requests and routes them to initialize, tools/list or tools/call.</summary>
internal static class McpRequestRouter
{
    private const string DefaultProtocolVersion = "2024-11-05";

    private static readonly HashSet<string> SupportedProtocolVersions = new(StringComparer.Ordinal)
    {
        "2024-11-05",
        "2025-03-26",
        "2025-06-18",
    };

    public static string? Process(
        string jsonPayload,
        CliOptions defaultOptions,
        McpScanCache cache,
        TextWriter? stderr = null)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(jsonPayload);
        }
        catch (JsonException ex)
        {
            stderr?.WriteLine($"mcp parse error: {ex.Message}");
            return McpResponses.BuildError(null, -32700, "Parse error");
        }

        if (root is not JsonObject request)
            return McpResponses.BuildError(null, -32600, "Invalid Request");

        var hasId = request.TryGetPropertyValue("id", out var idNode);
        var method = McpResponses.ReadString(request["method"]);

        if (string.IsNullOrEmpty(method))
        {
            return hasId
                ? McpResponses.BuildError(idNode, -32600, "Invalid Request: missing method")
                : null;
        }

        // JSON-RPC notifications (no "id" property) do not receive a response.
        if (!hasId)
            return null;

        var parameters = request["params"] as JsonObject;
        return method switch
        {
            "initialize" => HandleInitialize(idNode, parameters),
            "ping" => McpResponses.BuildResult(idNode, new JsonObject()),
            "tools/list" => McpToolCatalog.List(idNode),
            "tools/call" => McpToolInvoker.Call(idNode, parameters, defaultOptions, cache),
            _ => McpResponses.BuildError(idNode, -32601, $"Method not found: {method}"),
        };
    }

    private static string HandleInitialize(JsonNode? idNode, JsonObject? parameters)
    {
        var requestedVersion = McpResponses.ReadString(parameters?["protocolVersion"]);
        var protocolVersion = !string.IsNullOrWhiteSpace(requestedVersion) &&
                              SupportedProtocolVersions.Contains(requestedVersion)
            ? requestedVersion
            : DefaultProtocolVersion;

        var result = new JsonObject
        {
            ["protocolVersion"] = protocolVersion,
            ["capabilities"] = new JsonObject
            {
                ["tools"] = new JsonObject
                {
                    ["listChanged"] = false,
                },
            },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = "cqrslens",
                ["version"] = ToolInfo.Version,
            },
        };

        return McpResponses.BuildResult(idNode, result);
    }
}
