namespace CqrsLens;

/// <summary>One framed MCP message and the framing style it arrived with.</summary>
internal readonly record struct McpMessage(string? Payload, bool UseContentLength);
