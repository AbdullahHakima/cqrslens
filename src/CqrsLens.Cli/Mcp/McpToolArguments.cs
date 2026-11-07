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
    int Limit,
    string? Subject,
    string? Relation,
    string? ClaimObject,
    IReadOnlyList<string>? Rules,
    string? Severity,
    string? CheckTarget,
    string? Format,
    string? Request,
    string? GroupBy,
    string? Base)
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
                raw?["limit"] is { } limitNode ? limitNode.GetValue<int>() : 0,
                raw?["subject"]?.GetValue<string>(),
                raw?["relation"]?.GetValue<string>(),
                raw?["object"]?.GetValue<string>(),
                ReadStringList(raw?["rules"]),
                raw?["severity"]?.GetValue<string>(),
                raw?["target"]?.GetValue<string>(),
                raw?["format"]?.GetValue<string>(),
                raw?["request"]?.GetValue<string>(),
                raw?["groupBy"]?.GetValue<string>(),
                raw?["base"]?.GetValue<string>());
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

    /// <summary>Reads a rule list given either as an array of strings or a comma-separated string.</summary>
    private static IReadOnlyList<string>? ReadStringList(JsonNode? node)
    {
        switch (node)
        {
            case null:
                return null;
            case JsonArray array:
                var items = new List<string>();
                foreach (var element in array)
                {
                    if (element is null)
                        continue;
                    var text = element.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(text))
                        items.Add(text.Trim());
                }
                return items;
            case JsonValue value:
                var raw = value.GetValue<string>();
                var split = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return split;
            default:
                throw new InvalidOperationException("'rules' must be a string or an array of strings.");
        }
    }
}
