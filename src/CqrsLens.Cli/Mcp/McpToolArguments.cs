using System.Text.Json.Nodes;

namespace CqrsLens;

/// <summary>The arguments accepted by the MCP tools, read without throwing on wrong types.</summary>
internal sealed record McpToolArguments(
    string? Path,
    string? Analysis,
    string? Query,
    string? Entity,
    string? Match,
    bool IncludeIndex,
    int Limit)
{
    public static bool TryRead(
        JsonObject? raw,
        out McpToolArguments arguments,
        out string? error)
    {
        try
        {
            arguments = new McpToolArguments(
                raw?["path"]?.GetValue<string>(),
                raw?["analysis"]?.GetValue<string>(),
                raw?["query"]?.GetValue<string>() ?? raw?["filter"]?.GetValue<string>(),
                raw?["entity"]?.GetValue<string>(),
                raw?["match"]?.GetValue<string>(),
                raw?["includeIndex"]?.GetValue<bool>() ?? false,
                raw?["limit"] is { } limitNode ? limitNode.GetValue<int>() : 0);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            arguments = null!;
            error = "Invalid params: argument values have the wrong type";
            return false;
        }
    }
}
