namespace CqrsLens;

/// <summary>Fully resolved output paths for one scan.</summary>
internal sealed record ReportOutputPaths(
    string Report,
    string? Sarif,
    string? Summary,
    string? Diff);
