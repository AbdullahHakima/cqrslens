namespace CqrsLens;

public sealed record AnalysisDiagnostic(
    string Code,
    AnalysisDiagnosticSeverity Severity,
    string Message,
    SourceLocation? Location = null,
    string? ProjectName = null)
{
    public string? Project => ProjectName;
}
