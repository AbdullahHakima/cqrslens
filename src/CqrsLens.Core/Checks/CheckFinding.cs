namespace CqrsLens;

public sealed record CheckFinding(
    CheckSeverity Severity,
    string Code,
    string Message,
    string? Target = null,
    SourceLocation? Location = null,
    string? ProjectName = null,
    bool IsKnown = false)
{
    /// <summary>
    /// Stable baseline identity: rule code + target + root-relative file. Line
    /// numbers deliberately do not participate, so moving code does not turn a
    /// known finding into a new one.
    /// </summary>
    public string BaselineKey =>
        BaselineKeys.Create(Code, Target, Location?.File);
}
