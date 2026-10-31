namespace CqrsLens;

/// <summary>Progress snapshot emitted during solution scanning.</summary>
public readonly record struct ScanProgress(
    string Phase,
    int Completed,
    int Total,
    string? Detail = null);
