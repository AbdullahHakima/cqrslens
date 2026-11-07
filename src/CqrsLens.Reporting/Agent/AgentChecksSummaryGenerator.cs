using System.Text;
using static CqrsLens.AgentReportFormatter;
using static CqrsLens.AgentReportLimits;

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

    /// <summary>
    /// Generates a filtered, paged view of the same active findings the unfiltered answer reports.
    /// The reply states the filter that produced the page and the total matching count before the
    /// page. With no filter and no page requested the legacy answer is returned byte-for-byte.
    /// </summary>
    internal static string Generate(AnalysisResult result, CheckFilter filter)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (filter.IsEmpty)
            return Generate(result);

        var selection = Select(result, filter);
        var matching = selection.Matching;
        var page = filter.Limit > 0 ? matching.Take(filter.Limit).ToList() : matching;

        var sb = new StringBuilder();
        var rootName = new DirectoryInfo(result.RootPath).Name;
        sb.AppendLine($"# CqrsLens Architecture Checks — `{rootName}`");
        sb.AppendLine($"- **Filters**: {DescribeFilters(selection.Rules, selection.Severity, selection.Target)}");
        sb.AppendLine($"- **Matching findings**: {matching.Count}");
        sb.AppendLine(filter.Limit > 0
            ? $"- **Showing**: {page.Count} (limit {filter.Limit})"
            : $"- **Showing**: {page.Count}");
        sb.AppendLine();

        if (matching.Count == 0)
        {
            sb.AppendLine("_No findings match the filters._");
        }
        else
        {
            foreach (var check in page)
            {
                var sev = check.Severity == CheckSeverity.Error ? "ERROR" : "WARN";
                var targetText = string.IsNullOrEmpty(check.Target) ? "" : $" target=`{check.Target}`";
                sb.AppendLine($"- **[{sev}] `{check.Code}`**{targetText}: {check.Message}{FormatLoc(check.Location)}");
            }

            var hidden = matching.Count - page.Count;
            if (hidden > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"_+{hidden} more matching finding(s) — narrow the filters or raise `limit` (max {MaxCheckLimit})._");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// The per-project (module) grouping form of the same active findings the filtered answer
    /// reports. Counts come first: for each project the total, error and warning counts and the
    /// rule breakdown, ordered by total descending and paged by <see cref="CheckFilter.Limit"/>.
    /// The same filters apply first, so a grouped query under a filter groups only the matching
    /// findings and the reply states the filter. Drilling into one project is the existing
    /// <c>target</c> scope on the ungrouped view, not a second path.
    /// </summary>
    internal static string GenerateGrouped(AnalysisResult result, CheckFilter filter, string groupBy)
    {
        ArgumentNullException.ThrowIfNull(result);

        var selection = Select(result, filter);
        var matching = selection.Matching;

        var groups = matching
            .GroupBy(c => string.IsNullOrEmpty(c.ProjectName) ? NoProjectLabel : c.ProjectName!)
            .Select(g => new ProjectGroup(
                g.Key,
                g.Count(),
                g.Count(c => c.Severity == CheckSeverity.Error),
                g.Count(c => c.Severity == CheckSeverity.Warning),
                g.GroupBy(c => c.Code)
                    .Select(r => new RuleCount(r.Key, r.Count()))
                    .OrderByDescending(r => r.Count)
                    .ThenBy(r => r.Code, StringComparer.Ordinal)
                    .ToList()))
            .OrderByDescending(g => g.Total)
            .ThenBy(g => g.Project, StringComparer.Ordinal)
            .ToList();

        var page = filter.Limit > 0 ? groups.Take(filter.Limit).ToList() : groups;
        var errors = groups.Sum(g => g.Errors);
        var warnings = groups.Sum(g => g.Warnings);

        var sb = new StringBuilder();
        var rootName = new DirectoryInfo(result.RootPath).Name;
        sb.AppendLine($"# CqrsLens Architecture Checks — `{rootName}`");
        sb.AppendLine($"- **Grouped by**: {groupBy}");
        sb.AppendLine($"- **Filters**: {DescribeFilters(selection.Rules, selection.Severity, selection.Target)}");
        sb.AppendLine($"- **Matching findings**: {matching.Count}");
        sb.AppendLine(filter.Limit > 0
            ? $"- **Projects**: {groups.Count} (showing {page.Count}, limit {filter.Limit})"
            : $"- **Projects**: {groups.Count}");
        sb.AppendLine($"- **Errors**: {errors} · **Warnings**: {warnings}");
        sb.AppendLine();

        if (groups.Count == 0)
        {
            sb.AppendLine("_No findings match the filters._");
            return sb.ToString();
        }

        foreach (var group in page)
        {
            sb.AppendLine($"- **`{group.Project}`** — **{group.Total}** finding(s): " +
                          $"{group.Errors} error(s), {group.Warnings} warning(s)");
            foreach (var rule in group.Rules)
                sb.AppendLine($"  - `{rule.Code}` × {rule.Count}");
        }

        var hidden = groups.Count - page.Count;
        if (hidden > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"_+{hidden} more project(s) — narrow the filters or raise `limit` (max {MaxCheckLimit})._");
        }

        return sb.ToString();
    }

    private const string NoProjectLabel = "(no project)";

    private sealed record RuleCount(string Code, int Count);

    private sealed record ProjectGroup(
        string Project,
        int Total,
        int Errors,
        int Warnings,
        IReadOnlyList<RuleCount> Rules);

    private sealed record Selection(
        List<string> Rules,
        CheckSeverity? Severity,
        string? Target,
        List<CheckFinding> Matching);

    /// <summary>Applies the filter dimensions (AND; rules OR within themselves) to the active findings.</summary>
    private static Selection Select(AnalysisResult result, CheckFilter filter)
    {
        var activeChecks = result.BaselineApplied
            ? result.Checks.Where(c => !c.IsKnown).ToList()
            : result.Checks;

        var rules = filter.Rules
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var ruleSet = new HashSet<string>(rules, StringComparer.OrdinalIgnoreCase);

        CheckSeverity? severity = string.IsNullOrWhiteSpace(filter.Severity)
            ? null
            : string.Equals(filter.Severity.Trim(), "error", StringComparison.OrdinalIgnoreCase)
                ? CheckSeverity.Error
                : CheckSeverity.Warning;

        var target = filter.Target?.Trim();

        var matching = activeChecks
            .Where(c =>
                (ruleSet.Count == 0 || ruleSet.Contains(c.Code)) &&
                (severity is null || c.Severity == severity) &&
                (string.IsNullOrEmpty(target) ||
                 string.Equals(c.ProjectName, target, StringComparison.OrdinalIgnoreCase) ||
                 (c.Target is not null && c.Target.Contains(target, StringComparison.OrdinalIgnoreCase))))
            .ToList();

        return new Selection(rules, severity, target, matching);
    }

    private static string DescribeFilters(
        IReadOnlyList<string> rules,
        CheckSeverity? severity,
        string? target)
    {
        var parts = new List<string>();
        if (rules.Count > 0)
            parts.Add($"rules={string.Join(", ", rules.Select(r => $"`{r}`"))}");
        if (severity is not null)
            parts.Add($"severity=`{(severity == CheckSeverity.Error ? "error" : "warning")}`");
        if (!string.IsNullOrEmpty(target))
            parts.Add($"target=`{target}`");

        return parts.Count == 0 ? "none (paged)" : string.Join("; ", parts);
    }
}
