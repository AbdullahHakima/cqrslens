using System.Text;
using static CqrsLens.AgentReportFormatter;

namespace CqrsLens;

/// <summary>Generates the active architecture-checks summary for MCP consumers.</summary>
internal static class AgentChecksSummaryGenerator
{
    internal static string Generate(AnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var sb = new StringBuilder();
        var rootName = new DirectoryInfo(result.RootPath).Name;

        var activeChecks = result.BaselineApplied
            ? result.Checks.Where(c => !c.IsKnown).ToList()
            : result.Checks;
        var errors = activeChecks.Count(c => c.Severity == CheckSeverity.Error);
        var warnings = activeChecks.Count - errors;

        sb.AppendLine($"# CqrsLens Architecture Checks — `{rootName}`");
        sb.AppendLine();
        sb.AppendLine($"- **Errors**: {errors}");
        sb.AppendLine($"- **Warnings**: {warnings}");
        sb.AppendLine($"- **Scan Diagnostics**: {result.Diagnostics.Count}");
        sb.AppendLine();

        if (activeChecks.Count == 0)
        {
            sb.AppendLine("All architecture checks passed.");
        }
        else
        {
            foreach (var check in activeChecks)
            {
                var sev = check.Severity == CheckSeverity.Error ? "ERROR" : "WARN";
                sb.AppendLine($"- **[{sev}] `{check.Code}`**: {check.Message}{FormatLoc(check.Location)}");
            }
        }

        if (result.Diagnostics.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Scan Diagnostics");
            foreach (var d in result.Diagnostics)
                sb.AppendLine($"- **[{d.Severity}] `{d.Code}`**: {d.Message}{FormatLoc(d.Location)}");
        }

        return sb.ToString();
    }
}