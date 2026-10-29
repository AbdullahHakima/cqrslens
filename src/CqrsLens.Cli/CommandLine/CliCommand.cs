namespace CqrsLens;

/// <summary>A parsed command: run a scan, print help/version, or report a usage error.</summary>
public sealed record CliCommand(
    CliCommandKind Kind,
    CliOptions? Options = null,
    string? Error = null,
    bool ShowUsage = false);
