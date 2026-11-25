namespace CqrsLens;

/// <summary>
/// Built-in Model Context Protocol (MCP) server (<c>cqrslens --mcp</c>) over JSON-RPC 2.0 stdio.
/// Lets AI coding agents inspect a .NET CQRS solution, evaluate proposed project changes, and
/// prepare test-first plans grounded in its architecture.
/// </summary>
public static class McpServer
{
    public static int Run(
        CliOptions defaultOptions,
        TextReader? stdin = null,
        TextWriter? stdout = null,
        TextWriter? stderr = null)
    {
        stdin ??= Console.In;
        stdout ??= Console.Out;
        stderr ??= Console.Error;

        var cache = new McpScanCache();

        while (true)
        {
            var message = McpMessageFraming.Read(stdin);
            if (message.Error is { } framingError)
            {
                McpMessageFraming.Write(stdout, McpResponses.BuildError(null, -32700, framingError), message.UseContentLength);
                continue;
            }

            if (message.Payload is null)
                break;

            if (string.IsNullOrWhiteSpace(message.Payload))
                continue;

            var response = McpRequestRouter.Process(message.Payload, defaultOptions, cache, stderr);
            if (response is not null)
                McpMessageFraming.Write(stdout, response, message.UseContentLength);
        }

        return ExitCodes.Success;
    }

    internal static string? ProcessMessage(
        string jsonPayload,
        CliOptions defaultOptions,
        McpScanCache? cache = null,
        TextWriter? stderr = null)
        => McpRequestRouter.Process(jsonPayload, defaultOptions, cache ?? new McpScanCache(), stderr);
}
