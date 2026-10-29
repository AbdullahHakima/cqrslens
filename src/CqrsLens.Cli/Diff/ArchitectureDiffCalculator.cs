using static CqrsLens.JsonReportGenerator;

namespace CqrsLens;

/// <summary>Computes an architecture diff from two report documents or analysis results.</summary>
internal static class ArchitectureDiffCalculator
{
    internal static ArchitectureDiff Compare(AnalysisResult baseline, AnalysisResult current)
        => Compare(CreateDocument(baseline), CreateDocument(current));

    internal static ArchitectureDiff Compare(JsonReportDocument baseline, JsonReportDocument current)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);

        var diff = new ArchitectureDiff
        {
            SchemaVersion = ArchitectureDiffer.CurrentSchemaVersion,
            ToolVersion = ToolInfo.Version,
            Scanner = current.Scanner,
            GeneratedAt = current.GeneratedAt,
            BaseRootName = baseline.RootName,
            HeadRootName = current.RootName,
            Watermark = current.Watermark,
            Summary = BuildSummary(baseline, current),
            Projects = ProjectDiffComparer.Compare(baseline.Projects, current.Projects),
            Requests = RequestDiffComparer.Compare(baseline.Requests, current.Requests),
            Handlers = HandlerDiffComparer.Compare(baseline.Handlers, current.Handlers),
            Validators = ValidatorDiffComparer.Compare(baseline.Validators, current.Validators),
            Behaviors = BehaviorDiffComparer.Compare(baseline.Behaviors, current.Behaviors),
            DbContexts = DbContextDiffComparer.Compare(baseline.DbContexts, current.DbContexts),
            Checks = CheckDiffComparer.Compare(baseline.Checks, current.Checks),
        };

        diff.HasChanges =
            diff.Projects.HasChanges ||
            diff.Requests.HasChanges ||
            diff.Handlers.HasChanges ||
            diff.Validators.HasChanges ||
            diff.Behaviors.HasChanges ||
            diff.DbContexts.HasChanges ||
            diff.Checks.HasChanges;

        return diff;
    }

    private static DiffSummary BuildSummary(JsonReportDocument baseline, JsonReportDocument current)
    {
        var baseErrors = ActiveChecks(baseline).Count(IsError);
        var headErrors = ActiveChecks(current).Count(IsError);

        return new DiffSummary
        {
            Projects = new DiffMetric(baseline.Projects.Count, current.Projects.Count),
            Requests = new DiffMetric(baseline.Requests.Count, current.Requests.Count),
            Handlers = new DiffMetric(baseline.Handlers.Count, current.Handlers.Count),
            Validators = new DiffMetric(baseline.Validators.Count, current.Validators.Count),
            Behaviors = new DiffMetric(baseline.Behaviors.Count, current.Behaviors.Count),
            DbContexts = new DiffMetric(baseline.DbContexts.Count, current.DbContexts.Count),
            Errors = new DiffMetric(baseErrors, headErrors),
            Warnings = new DiffMetric(
                ActiveChecks(baseline).Count() - baseErrors,
                ActiveChecks(current).Count() - headErrors),
        };
    }

    private static IEnumerable<JsonCheckFinding> ActiveChecks(JsonReportDocument document)
        => document.BaselineApplied ? document.Checks.Where(finding => !finding.Known) : document.Checks;

    private static bool IsError(JsonCheckFinding finding)
        => string.Equals(finding.Severity, "error", StringComparison.OrdinalIgnoreCase);
}
