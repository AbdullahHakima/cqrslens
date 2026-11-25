using System.Text.Json.Nodes;

namespace CqrsLens;

/// <summary>
/// Executes the <c>get_system_overview</c> MCP tool: confines <c>path</c> through
/// <see cref="McpPathGuard"/>, scans via <see cref="McpScanProvider"/>, and renders
/// <see cref="AgentSystemOverviewGenerator"/> output.
/// </summary>
internal static class McpSystemOverviewTool
{
    public static string Execute(
        JsonNode? idNode,
        McpToolArguments arguments,
        CliOptions defaultOptions,
        McpScanCache cache)
    {
        var workspaceRoot = defaultOptions.Path ?? Directory.GetCurrentDirectory();
        string targetPath;
        if (!string.IsNullOrWhiteSpace(arguments.Path) ||
            (arguments.Path is not null && arguments.Path.Any(char.IsControl)))
        {
            if (!McpPathGuard.TryValidateDirectory(
                    arguments.Path!,
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

        AnalysisResult result;
        try
        {
            result = McpScanProvider.GetOrRun(
                targetPath,
                arguments.Analysis,
                defaultOptions,
                cache);
        }
        catch (Exception ex)
        {
            return McpResponses.BuildToolResult(idNode, $"error: {ex.Message}", isError: true);
        }

        var serviceFilter = string.IsNullOrWhiteSpace(arguments.Service)
            ? null
            : arguments.Service.Trim();
        var overview = AgentSystemOverviewGenerator.Generate(
            result,
            serviceFilter,
            arguments.IncludeTests);

        return McpResponses.BuildToolResult(idNode, overview, isError: false);
    }
}
