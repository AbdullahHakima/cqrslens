namespace CqrsLens;

/// <summary>Normalized summary of runtime unit test results parsed from a TRX document.</summary>
public sealed record TrxTestSummary(
    string SourceFile,
    int Total,
    int Passed,
    int Failed,
    int Skipped,
    IReadOnlyList<TrxUnitTestResult> Results);
