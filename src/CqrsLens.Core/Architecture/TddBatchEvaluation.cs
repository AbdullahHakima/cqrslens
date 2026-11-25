namespace CqrsLens;

/// <summary>Aggregated TDD cycle evaluation across every <c>*.slice.json</c> file in a directory.</summary>
public sealed record TddBatchEvaluation(
    string Phase,
    string Root,
    int Total,
    int Verified,
    IReadOnlyList<TddBatchItem> Results);
