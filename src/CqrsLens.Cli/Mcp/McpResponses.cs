using System.Text.Json;
using System.Text.Json.Nodes;

namespace CqrsLens;

/// <summary>Builds JSON-RPC 2.0 responses and safely reads JSON string values.</summary>
internal static class McpResponses
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    public static string? ReadString(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    public static string BuildResult(JsonNode? idNode, JsonObject result)
    {
        var response = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = idNode?.DeepClone(),
            ["result"] = result,
        };
        return response.ToJsonString(JsonOptions);
    }

    public static string BuildError(JsonNode? idNode, int code, string message)
    {
        var response = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = idNode?.DeepClone(),
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message,
            },
        };
        return response.ToJsonString(JsonOptions);
    }

    public static string BuildToolResult(JsonNode? idNode, string text, bool isError)
    {
        var result = new JsonObject
        {
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = text,
                },
            },
            ["isError"] = isError,
        };
        return BuildResult(idNode, result);
    }
}
