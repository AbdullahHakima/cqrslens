namespace CqrsLens;

/// <summary>
/// Canonical ordering for check findings: errors first, then by code and message.
/// </summary>
internal static class CheckFindingOrdering
{
    internal static void Sort(List<CheckFinding> findings)
        => findings.Sort((a, b) =>
        {
            var bySeverity = b.Severity.CompareTo(a.Severity); // errors first
            if (bySeverity != 0) return bySeverity;
            var byCode = string.Compare(a.Code, b.Code, StringComparison.Ordinal);
            return byCode != 0 ? byCode : string.Compare(a.Message, b.Message, StringComparison.Ordinal);
        });
}
