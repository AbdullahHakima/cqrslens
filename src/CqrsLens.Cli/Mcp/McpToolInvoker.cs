using System.Text.Json.Nodes;
using System.Xml;

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

        var input = parameters?["arguments"];
        if (input is not null and not JsonObject)
            return McpResponses.BuildError(idNode, -32602, "Invalid params: 'arguments' must be an object");

        var rawArguments = input as JsonObject ?? new JsonObject();
        if (!McpToolArguments.TryRead(rawArguments, out var arguments, out var error))
            return McpResponses.BuildError(idNode, -32602, error!);

        var validationError = Validate(toolName, arguments);
        if (validationError is not null)
            return McpResponses.BuildToolResult(idNode, validationError, isError: true);

        if (toolName == "get_system_overview")
            return McpSystemOverviewTool.Execute(idNode, arguments, defaultOptions, cache);

        Func<AnalysisResult, string>? modelingTool = null;
        (SliceDesign Design, string? TestResultsPath)? tddSetup = null;
        try
        {
            switch (toolName)
            {
                case McpModelingTools.EvaluateArchitectureProposal:
                    modelingTool = McpModelingTools.PrepareProposal(idNode, rawArguments);
                    break;
                case McpModelingTools.PlanSliceTests:
                    modelingTool = McpModelingTools.PrepareTestPlan(idNode, rawArguments, arguments.Request);
                    break;
                case McpModelingTools.VerifySliceDesign:
                    modelingTool = McpModelingTools.PrepareSliceDesign(idNode, rawArguments);
                    break;
                case McpModelingTools.DraftSliceDesign:
                    modelingTool = McpModelingTools.PrepareDraftSlice(idNode, rawArguments);
                    break;
                case McpModelingTools.AnalyzeChangeImpact:
                    modelingTool = McpModelingTools.PrepareChangeImpact(idNode, rawArguments, arguments);
                    break;
                case McpModelingTools.VerifyTddCycle:
                    tddSetup = McpModelingTools.PrepareVerifyTddCycle(rawArguments);
                    break;
            }
        }
        catch (ArgumentException ex)
        {
            return McpResponses.BuildToolResult(idNode, $"error: {ex.Message}", isError: true);
        }

        var workspaceRoot = defaultOptions.Path ?? Directory.GetCurrentDirectory();
        string targetPath;
        if (!string.IsNullOrWhiteSpace(arguments.Path))
        {
            if (!McpPathGuard.TryValidateDirectory(
                    arguments.Path,
                    workspaceRoot,
                    defaultOptions.AllowedRoots,
                    defaultOptions.AllowExternalPaths,
                    out targetPath,
                    out var pathError))
            {
                return McpResponses.BuildToolResult(idNode, $"error: {pathError}", isError: true);
            }
        }
        else
        {
            targetPath = workspaceRoot;
            if (!Directory.Exists(targetPath))
                return McpResponses.BuildToolResult(idNode, $"error: path not found: {targetPath}", isError: true);
        }

        string? validatedBasePath = null;
        if (toolName == "diff_architecture")
        {
            if (!McpPathGuard.TryValidatePath(
                    arguments.Base!,
                    workspaceRoot,
                    defaultOptions.AllowedRoots,
                    defaultOptions.AllowExternalPaths,
                    out validatedBasePath,
                    out var baseError))
            {
                return McpResponses.BuildToolResult(idNode, $"error: {baseError}", isError: true);
            }
        }

        if (tddSetup is { } tdd)
        {
            TrxTestSummary? runtimeResults = null;
            if (tdd.TestResultsPath is { } rawTrxPath)
            {
                if (!McpPathGuard.TryValidatePath(
                        rawTrxPath,
                        workspaceRoot,
                        defaultOptions.AllowedRoots,
                        defaultOptions.AllowExternalPaths,
                        out var fullTrxPath,
                        out var trxError))
                {
                    return McpResponses.BuildToolResult(idNode, $"error: {trxError}", isError: true);
                }

                try
                {
                    if (!File.Exists(fullTrxPath))
                        return McpResponses.BuildToolResult(idNode, $"error: test results file not found: {rawTrxPath}", isError: true);

                    runtimeResults = TrxTestResultsParser.ParseFile(fullTrxPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or XmlException or ArgumentException)
                {
                    return McpResponses.BuildToolResult(
                        idNode,
                        $"error: invalid testResultsPath '{rawTrxPath}': {ex.Message}",
                        isError: true);
                }
            }

            modelingTool = result => McpResponses.BuildStructuredToolResult(
                idNode,
                TddCycleEvaluator.Evaluate(result, tdd.Design, runtimeResults));
        }

        AnalysisResult result;
        try
        {
            result = McpScanProvider.GetOrRun(
                targetPath,
                arguments.Analysis,
                defaultOptions,
                cache,
                refresh: toolName is McpModelingTools.VerifySliceDesign or McpModelingTools.VerifyTddCycle);
        }
        catch (Exception ex)
        {
            return McpResponses.BuildToolResult(idNode, $"error: {ex.Message}", isError: true);
        }

        if (toolName == "diff_architecture")
            return RunDiffTool(idNode, arguments, validatedBasePath!, targetPath, result, defaultOptions);

        if (modelingTool is not null)
            return modelingTool(result);

        return toolName switch
        {
            "get_architecture_dna" => McpResponses.BuildToolResult(
                idNode,
                AgentReportGenerator.GenerateDnaAndIndex(result, arguments.IncludeIndex),
                isError: false),
            "trace_slice" => TraceSlice(idNode, result, arguments),
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

    private static string TraceSlice(JsonNode? idNode, AnalysisResult result, McpToolArguments arguments)
    {
        if (IsJsonFormat(arguments.Format))
        {
            var sliceJson = AgentReportGenerator.GenerateSliceData(result, arguments.Query, arguments.Limit);
            return McpResponses.BuildStructuredToolResult(idNode, JsonNode.Parse(sliceJson)!.AsObject());
        }

        return McpResponses.BuildToolResult(
            idNode, AgentReportGenerator.Generate(result, arguments.Query, arguments.Limit), isError: false);
    }

    /// <summary>
    /// Runs the <c>diff_architecture</c> tool: load the base operand with the CLI's own snapshot
    /// loader, diff it against the head scan with the CLI's own calculator, then render the answer.
    /// A base that cannot be read or parsed is a clean tool error, never a partial delta.
    /// </summary>
    private static string RunDiffTool(
        JsonNode? idNode,
        McpToolArguments arguments,
        string validatedBasePath,
        string headPath,
        AnalysisResult head,
        CliOptions defaultOptions)
    {
        var baseDisplay = arguments.Base!;
        JsonReportDocument baseline;
        try
        {
            var (mode, options) = McpScanProvider.ResolveScanInputs(
                Path.GetFullPath(headPath), arguments.Analysis, defaultOptions);
            baseline = ArchitectureDiffer.LoadSnapshot(validatedBasePath, mode, options);
        }
        catch (Exception ex) when (ex is DiffException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return McpResponses.BuildToolResult(idNode, $"error: {ex.Message}", isError: true);
        }

        var diff = ArchitectureDiffer.Compare(baseline, JsonReportGenerator.CreateDocument(head));
        var text = McpDiffAnswerGenerator.Generate(
            diff,
            baseDisplay,
            headPath,
            Directory.Exists(validatedBasePath),
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
        if (arguments.RequestedLimit is { } limit && (limit < 1 || limit > maxLimit))
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
