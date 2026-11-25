namespace CqrsLens;

/// <summary>One entry in a directory-wide <see cref="TddBatchEvaluation"/>.</summary>
public sealed record TddBatchItem(string File, TddCycleEvaluation Evaluation);
