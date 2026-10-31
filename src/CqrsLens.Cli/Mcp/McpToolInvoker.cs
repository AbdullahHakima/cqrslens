using System.Text.Json.Nodes;

namespace CqrsLens;

/// <summary>Validates one tools/call request, runs the scan and formats the tool result.</summary>
internal static class McpToolInvoker
{
    public static string Call(
        JsonNode? idNode,
        JsonObject? parameters,
        CliOptions defaultOptions,
        McpScanCache cache)
    {
        var toolName = McpResponses.ReadString(parameters?["name"]);
        if (string.IsNullOrEmpty(toolName))
            return McpResponses.BuildError(idNode, -32602, "Invalid params: missing tool name");

        if (!McpToolCatalog.KnownTools.Contains(toolName))
            return McpResponses.BuildError(idNode, -32602, $"Unknown tool: {toolName}");

        if (!McpToolArguments.TryRead(parameters?["arguments"] as JsonObject, out var arguments, out var error))
            return McpResponses.BuildError(idNode, -32602, error!);

        var validationError = Validate(toolName, arguments);
        if (validationError is not null)
            return McpResponses.BuildToolResult(idNode, validationError, isError: true);

        var targetPath = string.IsNullOrWhiteSpace(arguments.Path)
            ? defaultOptions.Path ?? Directory.GetCurrentDirectory()
            : arguments.Path;

        if (!Directory.Exists(targetPath))
            return McpResponses.BuildToolResult(idNode, $"error: path not found: {targetPath}", isError: true);

        AnalysisResult result;
        try
        {
            result = McpScanProvider.GetOrRun(targetPath, arguments.Analysis, defaultOptions, cache);
        }
        catch (Exception ex)
        {
            return McpResponses.BuildToolResult(idNode, $"error: {ex.Message}", isError: true);
        }

        return toolName switch
        {
            "get_architecture_dna" => McpResponses.BuildToolResult(
                idNode,
                AgentReportGenerator.GenerateDnaAndIndex(result, arguments.IncludeIndex),
                isError: false),
            "trace_slice" => McpResponses.BuildToolResult(
                idNode,
                AgentReportGenerator.Generate(result, arguments.Query, arguments.Limit),
                isError: false),
            "trace_entity_impact" => McpResponses.BuildToolResult(
                idNode,
                AgentReportGenerator.GenerateEntityImpact(
                    result,
                    arguments.Entity!,
                    string.Equals(arguments.Match, "contains", StringComparison.OrdinalIgnoreCase),
                    arguments.Limit),
                isError: false),
            "check_architecture" => McpResponses.BuildToolResult(
                idNode,
                AgentReportGenerator.GenerateChecksSummary(result),
                isError: false),
            _ => McpResponses.BuildError(idNode, -32602, $"Unknown tool: {toolName}"),
        };
    }

    private static string? Validate(string toolName, McpToolArguments arguments)
    {
        if (toolName == "trace_slice" && string.IsNullOrWhiteSpace(arguments.Query))
            return "error: 'query' argument is required for trace_slice.";

        if (toolName == "trace_entity_impact" && string.IsNullOrWhiteSpace(arguments.Entity))
            return "error: 'entity' argument is required for trace_entity_impact.";

        if (!string.IsNullOrWhiteSpace(arguments.Analysis) &&
            !string.Equals(arguments.Analysis, "text", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(arguments.Analysis, "roslyn", StringComparison.OrdinalIgnoreCase))
        {
            return "error: 'analysis' must be 'text' or 'roslyn'.";
        }

        var maxLimit = toolName == "trace_entity_impact" ? 50 : 25;
        if (arguments.Limit < 0 || arguments.Limit > maxLimit)
            return $"error: 'limit' must be between 1 and {maxLimit} (omit it for the default).";

        if (!string.IsNullOrWhiteSpace(arguments.Match) &&
            !string.Equals(arguments.Match, "exact", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(arguments.Match, "contains", StringComparison.OrdinalIgnoreCase))
        {
            return "error: 'match' must be 'exact' or 'contains'.";
        }

        return null;
    }
}
