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

        if (toolName == "diff_architecture")
            return RunDiffTool(idNode, arguments, targetPath, result, defaultOptions);

        return toolName switch
        {
            "get_architecture_dna" => McpResponses.BuildToolResult(
                idNode,
                AgentReportGenerator.GenerateDnaAndIndex(result, arguments.IncludeIndex),
                isError: false),
            "trace_slice" => McpResponses.BuildToolResult(
                idNode,
                IsJsonFormat(arguments.Format)
                    ? AgentReportGenerator.GenerateSliceData(result, arguments.Query, arguments.Limit)
                    : AgentReportGenerator.Generate(result, arguments.Query, arguments.Limit),
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
                IsGrouped(arguments.GroupBy)
                    ? AgentReportGenerator.GenerateChecksGrouped(
                        result,
                        new CheckFilter(
                            arguments.Rules ?? Array.Empty<string>(),
                            arguments.Severity,
                            arguments.CheckTarget,
                            arguments.Limit),
                        arguments.GroupBy!)
                    : AgentReportGenerator.GenerateChecksSummary(
                        result,
                        new CheckFilter(
                            arguments.Rules ?? Array.Empty<string>(),
                            arguments.Severity,
                            arguments.CheckTarget,
                            arguments.Limit)),
                isError: false),
            "trace_request_impact" => McpResponses.BuildToolResult(
                idNode,
                AgentReportGenerator.GenerateRequestImpact(
                    result,
                    arguments.Request!,
                    string.Equals(arguments.Match, "contains", StringComparison.OrdinalIgnoreCase),
                    arguments.Limit),
                isError: false),
            "verify_claim" => McpResponses.BuildToolResult(
                idNode,
                AgentReportGenerator.VerifyClaim(result, arguments.Subject, arguments.Relation, arguments.ClaimObject),
                isError: false),
            _ => McpResponses.BuildError(idNode, -32602, $"Unknown tool: {toolName}"),
        };
    }

    /// <summary>
    /// Runs the <c>diff_architecture</c> tool: load the base operand with the CLI's own snapshot
    /// loader, diff it against the head scan with the CLI's own calculator, then render the answer.
    /// A base that cannot be read or parsed is a clean tool error, never a partial delta.
    /// </summary>
    private static string RunDiffTool(
        JsonNode? idNode,
        McpToolArguments arguments,
        string headPath,
        AnalysisResult head,
        CliOptions defaultOptions)
    {
        var basePath = arguments.Base!;
        JsonReportDocument baseline;
        try
        {
            var (mode, options) = McpScanProvider.ResolveScanInputs(
                Path.GetFullPath(headPath), arguments.Analysis, defaultOptions);
            baseline = ArchitectureDiffer.LoadSnapshot(basePath, mode, options);
        }
        catch (Exception ex) when (ex is DiffException or IOException or UnauthorizedAccessException)
        {
            return McpResponses.BuildToolResult(idNode, $"error: {ex.Message}", isError: true);
        }

        var diff = ArchitectureDiffer.Compare(baseline, JsonReportGenerator.CreateDocument(head));
        var text = McpDiffAnswerGenerator.Generate(
            diff,
            basePath,
            headPath,
            Directory.Exists(basePath),
            arguments.Limit);
        return McpResponses.BuildToolResult(idNode, text, isError: false);
    }

    private static string? Validate(string toolName, McpToolArguments arguments)
    {
        if (toolName == "trace_slice" && string.IsNullOrWhiteSpace(arguments.Query))
            return "error: 'query' argument is required for trace_slice.";

        if (toolName == "trace_entity_impact" && string.IsNullOrWhiteSpace(arguments.Entity))
            return "error: 'entity' argument is required for trace_entity_impact.";

        if (toolName == "trace_request_impact" && string.IsNullOrWhiteSpace(arguments.Request))
            return "error: 'request' argument is required for trace_request_impact.";

        if (toolName == "diff_architecture" && string.IsNullOrWhiteSpace(arguments.Base))
            return "error: 'base' argument is required for diff_architecture.";

        if (toolName == "verify_claim" && string.IsNullOrWhiteSpace(arguments.Subject))
            return "error: 'subject' argument is required for verify_claim.";

        if (toolName == "verify_claim" && string.IsNullOrWhiteSpace(arguments.Relation))
            return "error: 'relation' argument is required for verify_claim.";

        if (!string.IsNullOrWhiteSpace(arguments.Analysis) &&
            !string.Equals(arguments.Analysis, "text", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(arguments.Analysis, "roslyn", StringComparison.OrdinalIgnoreCase))
        {
            return "error: 'analysis' must be 'text' or 'roslyn'.";
        }

        var maxLimit = toolName switch
        {
            "check_architecture" => AgentReportLimits.MaxCheckLimit,
            "diff_architecture" => AgentReportLimits.MaxDiffLimit,
            "trace_entity_impact" => 50,
            "trace_request_impact" => 50,
            _ => 25,
        };
        if (arguments.Limit < 0 || arguments.Limit > maxLimit)
            return $"error: 'limit' must be between 1 and {maxLimit} (omit it for the default).";

        if (!string.IsNullOrWhiteSpace(arguments.Match) &&
            !string.Equals(arguments.Match, "exact", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(arguments.Match, "contains", StringComparison.OrdinalIgnoreCase))
        {
            return "error: 'match' must be 'exact' or 'contains'.";
        }

        if (toolName == "trace_slice" &&
            !string.IsNullOrWhiteSpace(arguments.Format) &&
            !string.Equals(arguments.Format, "text", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(arguments.Format, "json", StringComparison.OrdinalIgnoreCase))
        {
            return "error: 'format' must be 'text' or 'json'.";
        }

        if (toolName == "check_architecture" && arguments.Rules is { Count: > 0 })
        {
            var unknown = arguments.Rules
                .Where(r => !AgentCheckRules.All.Contains(r, StringComparer.OrdinalIgnoreCase))
                .ToList();
            if (unknown.Count > 0)
            {
                return $"error: unknown rule code(s): {string.Join(", ", unknown)}. " +
                       $"Known codes: {string.Join(", ", AgentCheckRules.All)}.";
            }
        }

        if (toolName == "check_architecture" &&
            !string.IsNullOrWhiteSpace(arguments.Severity) &&
            !string.Equals(arguments.Severity, "error", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(arguments.Severity, "warning", StringComparison.OrdinalIgnoreCase))
        {
            return "error: 'severity' must be 'error' or 'warning'.";
        }

        if (toolName == "check_architecture" &&
            !string.IsNullOrWhiteSpace(arguments.GroupBy) &&
            !IsGrouped(arguments.GroupBy))
        {
            return "error: 'groupBy' must be 'project' (omit it for the flat finding list).";
        }

        return null;
    }

    private static bool IsJsonFormat(string? format)
        => string.Equals(format, "json", StringComparison.OrdinalIgnoreCase);

    private static bool IsGrouped(string? groupBy)
        => string.Equals(groupBy, "project", StringComparison.OrdinalIgnoreCase);
}
