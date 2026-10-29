namespace CqrsLens;

/// <summary>Builds the architecture diff and, when requested, writes its report.</summary>
internal static class DiffProcessor
{
    public static ArchitectureDiff? Run(
        string? diffPath,
        string? diffOutputPath,
        AnalysisMode analysisMode,
        AnalysisOptions analysisOptions,
        AnalysisResult result,
        string? ciRoot)
    {
        if (diffPath is null)
            return null;

        var baseline = ArchitectureDiffer.LoadSnapshot(diffPath, analysisMode, analysisOptions);
        var diff = ArchitectureDiffer.Compare(baseline, JsonReportGenerator.CreateDocument(result));

        if (diffOutputPath is not null)
        {
            var content = diffOutputPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? ArchitectureDiffer.GenerateJson(diff)
                : ArchitectureDiffer.GenerateMarkdown(diff, result, ciRoot);
            OutputFileWriter.Write(diffOutputPath, content);
        }

        return diff;
    }
}
