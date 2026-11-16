namespace CqrsLens;

/// <summary>One drifted, missing, or unexpected file detected by <c>--check-docs</c>.</summary>
internal sealed record DocsDriftEntry(
    string RelativePath,
    string Status,
    IReadOnlyList<string> ChangedSections);
