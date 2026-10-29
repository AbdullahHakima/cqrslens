using System.Text;

namespace CqrsLens;

/// <summary>Writes the base/head/delta metric table.</summary>
internal static class DiffSummaryTableWriter
{
    public static void Write(StringBuilder markdown, DiffSummary summary)
    {
        markdown.AppendLine("| Metric | Base | Head | Delta |");
        markdown.AppendLine("| --- | ---: | ---: | ---: |");
        AppendMetricRow(markdown, "Projects", summary.Projects);
        AppendMetricRow(markdown, "Requests (commands + queries)", summary.Requests);
        AppendMetricRow(markdown, "Handlers", summary.Handlers);
        AppendMetricRow(markdown, "Validators", summary.Validators);
        AppendMetricRow(markdown, "Pipeline behaviors", summary.Behaviors);
        AppendMetricRow(markdown, "DbContexts", summary.DbContexts);
        AppendMetricRow(markdown, "Check errors", summary.Errors);
        AppendMetricRow(markdown, "Check warnings", summary.Warnings);
        markdown.AppendLine();
    }

    private static void AppendMetricRow(StringBuilder markdown, string label, DiffMetric metric)
    {
        var deltaText = metric.Delta switch
        {
            > 0 => $"+{metric.Delta}",
            < 0 => metric.Delta.ToString(),
            _ => "0",
        };
        markdown.AppendLine($"| {label} | {metric.Base} | {metric.Head} | {deltaText} |");
    }
}
