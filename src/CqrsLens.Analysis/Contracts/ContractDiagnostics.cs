namespace CqrsLens;

/// <summary>
/// Diagnostics for contract references that the text scanner could not resolve.
/// Ambiguous candidates are reported rather than guessed; unresolved non-contract
/// bases stay quiet.
/// </summary>
internal static class ContractDiagnostics
{
    public static void RecordRequestResolutionGap(
        AnalysisResult result,
        SourceFile file,
        int sourceIndex,
        string owner,
        RequestShapeResolution shape)
    {
        if (shape.AmbiguousCandidates.Count > 0)
        {
            RecordAmbiguous(
                result, file, sourceIndex, owner, "base",
                shape.AmbiguousCandidates, "request base");
            return;
        }

        if (!owner.EndsWith("Command", StringComparison.Ordinal) &&
            !owner.EndsWith("Query", StringComparison.Ordinal))
            return;

        var unresolved = shape.UnresolvedBases
            .Where(name => name.EndsWith("Base", StringComparison.Ordinal) ||
                           name.Contains("Command", StringComparison.Ordinal) ||
                           name.Contains("Query", StringComparison.Ordinal) ||
                           name.Contains("Request", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        if (unresolved.Count == 0) return;

        result.RecordDiagnostic(new AnalysisDiagnostic(
            AnalysisResult.UnresolvedContractCode,
            AnalysisDiagnosticSeverity.Info,
            $"`{owner}` looks like a request, but its base chain references " +
            $"{string.Join(", ", unresolved.Select(n => $"`{n}`"))}, which has no source " +
            "declaration. Request discovery stopped at that base type.",
            SourceLocation.Create(file.Path, result.RootPath, file.Code, sourceIndex),
            file.ProjectName));
    }

    public static void RecordShapeResolutionGap(
        AnalysisResult result,
        SourceFile file,
        int sourceIndex,
        string owner,
        string reference,
        TypeHierarchy hierarchy,
        IReadOnlyList<string> ambiguousCandidates,
        string role)
    {
        if (ambiguousCandidates.Count > 0)
        {
            RecordAmbiguous(result, file, sourceIndex, owner, reference, ambiguousCandidates, role);
            return;
        }

        // A source-declared interface that simply does not reach a known root is
        // not an external wrapper; stay quiet. Only an unknown type is a gap.
        if (hierarchy.HasInterface(reference)) return;

        var hint = role == "handler"
            ? "Name an external handler wrapper with --handler-markers / handlerMarkers to link it."
            : "FastEndpoints `Validator<T>` is recognized; other external validator wrappers are not linked yet.";
        result.RecordDiagnostic(new AnalysisDiagnostic(
            AnalysisResult.UnresolvedContractCode,
            AnalysisDiagnosticSeverity.Info,
            $"`{owner}` looks like a {role} through `{reference}`, but that contract has no source declaration. " +
            hint,
            SourceLocation.Create(file.Path, result.RootPath, file.Code, sourceIndex),
            file.ProjectName));
    }

    public static void RecordAmbiguous(
        AnalysisResult result,
        SourceFile file,
        int sourceIndex,
        string owner,
        string reference,
        IReadOnlyList<string> candidates,
        string role)
    {
        if (candidates.Count < 2) return;

        result.RecordDiagnostic(new AnalysisDiagnostic(
            AnalysisResult.AmbiguousContractCode,
            AnalysisDiagnosticSeverity.Info,
            $"`{owner}` references `{CSharpScanner.SimpleName(reference)}` as a {role}, " +
            $"but it matches {candidates.Count} source types ({string.Join(", ", candidates)}); " +
            "the reference was not resolved.",
            SourceLocation.Create(file.Path, result.RootPath, file.Code, sourceIndex),
            file.ProjectName));
    }
}
