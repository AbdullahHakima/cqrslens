namespace CqrsLens;

/// <summary>Shared diagnostics for links that were deliberately not made.</summary>
internal static class LinkDiagnostics
{
    public static void RecordAmbiguous(
        AnalysisResult result,
        string ownerKind,
        string owner,
        string target,
        SourceLocation? location,
        string? project,
        IReadOnlyList<string> candidates)
    {
        if (candidates.Count < 2) return;

        var names = string.Join(
            ", ",
            candidates
                .Select(candidate => candidate)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
        result.RecordDiagnostic(new AnalysisDiagnostic(
            AnalysisResult.AmbiguousLinkCode,
            AnalysisDiagnosticSeverity.Warning,
            $"{ownerKind} `{owner}` targets `{target}`, which matches {candidates.Count} " +
            $"{(ownerKind == "NotificationHandler" ? "notifications" : "requests")} ({names}); " +
            "the link was omitted.",
            location,
            project));
    }
}
