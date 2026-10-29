using System.Text.Json;
using System.Text.Json.Serialization;

using static CqrsLens.JsonReportGenerator;

namespace CqrsLens;

/// <summary>
/// Compares two CqrsLens scans (from <see cref="AnalysisResult"/> or
/// <c>--format json</c> reports) and generates Markdown (for pull request
/// comments and human review) or JSON diff reports.
/// </summary>
public static class ArchitectureDiffer
{
    public const string CommentMarker = "<!-- cqrslens-architecture-diff -->";
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal static JsonReportDocument LoadSnapshot(
        string path,
        AnalysisMode analysisMode,
        AnalysisOptions options)
        => ArchitectureSnapshotLoader.LoadSnapshot(path, analysisMode, options);

    internal static ArchitectureDiff Compare(AnalysisResult baseline, AnalysisResult current)
        => ArchitectureDiffCalculator.Compare(baseline, current);

    internal static ArchitectureDiff Compare(JsonReportDocument baseline, JsonReportDocument current)
        => ArchitectureDiffCalculator.Compare(baseline, current);

    internal static string FormatConsoleSummary(ArchitectureDiff diff)
    {
        if (!diff.HasChanges)
            return "Architecture diff: no changes detected.";

        return "Architecture diff: " +
               $"+{diff.Requests.Added.Count}/-{diff.Requests.Removed.Count}/~{diff.Requests.Modified.Count} request(s), " +
               $"+{diff.Handlers.Added.Count}/-{diff.Handlers.Removed.Count} handler(s), " +
               $"+{diff.Validators.Added.Count}/-{diff.Validators.Removed.Count} validator(s), " +
               $"+{diff.Checks.NewFindings.Count}/-{diff.Checks.ResolvedFindings.Count} check finding(s).";
    }

    internal static string GenerateJson(ArchitectureDiff diff)
    {
        ArgumentNullException.ThrowIfNull(diff);
        return JsonSerializer.Serialize(diff, WriteOptions) + "\n";
    }

    internal static string GenerateMarkdown(
        ArchitectureDiff diff,
        AnalysisResult? headResult = null,
        string? ciRoot = null)
        => ArchitectureDiffMarkdownRenderer.Generate(diff, headResult, ciRoot);
}
