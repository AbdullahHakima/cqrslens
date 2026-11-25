namespace CqrsLens;

/// <summary>One unit test execution entry parsed from a Visual Studio TRX report.</summary>
public sealed record TrxUnitTestResult(
    string TestName,
    string Outcome,
    string? Duration,
    string? ErrorMessage);
