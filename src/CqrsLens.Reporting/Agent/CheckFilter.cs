namespace CqrsLens;

/// <summary>
/// The optional filters <c>check_architecture</c> applies to the scan's findings. Filters
/// compose (AND): a finding must match every supplied dimension. Within the rule dimension a
/// finding matches when its code is any of the requested rules. Filtering only selects; it never
/// alters, rewords or re-severities a finding.
/// </summary>
internal sealed record CheckFilter(
    IReadOnlyList<string> Rules,
    string? Severity,
    string? Target,
    int Limit)
{
    internal static CheckFilter None { get; } = new(Array.Empty<string>(), null, null, 0);

    /// <summary>True when no filter or page was requested, so the legacy answer shape applies.</summary>
    internal bool IsEmpty =>
        Rules.Count == 0 &&
        string.IsNullOrWhiteSpace(Severity) &&
        string.IsNullOrWhiteSpace(Target) &&
        Limit <= 0;
}
