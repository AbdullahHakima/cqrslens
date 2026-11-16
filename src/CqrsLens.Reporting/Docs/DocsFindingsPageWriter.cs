using System.Text;

namespace CqrsLens;

/// <summary>Renders <c>06-findings.md</c> (architecture and intent findings grouped per module/feature, with evidence).</summary>
internal static class DocsFindingsPageWriter
{
    internal static string Write(AnalysisResult result)
    {
        var md = new StringBuilder();
        DocsFormatting.WriteHeader(md, "06 — Findings & Diagnostics", result);

        var activeChecks = (result.BaselineApplied
                ? result.Checks.Where(c => !c.IsKnown)
                : result.Checks)
            .OrderByDescending(c => c.Severity)
            .ThenBy(c => c.Code, StringComparer.Ordinal)
            .ThenBy(c => c.ProjectName ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(c => c.Target ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(c => c.Location?.File ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(c => c.Location?.Line ?? 0)
            .ThenBy(c => c.Location?.Column ?? 0)
            .ThenBy(c => c.Message, StringComparer.Ordinal)
            .ToList();

        var knownChecks = result.BaselineApplied
            ? result.Checks
                .Where(c => c.IsKnown)
                .OrderByDescending(c => c.Severity)
                .ThenBy(c => c.Code, StringComparer.Ordinal)
                .ThenBy(c => c.Target ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(c => c.Message, StringComparer.Ordinal)
                .ToList()
            : new List<CheckFinding>();

        var activeErrors = activeChecks.Count(c => c.Severity == CheckSeverity.Error);
        var activeWarnings = activeChecks.Count - activeErrors;
        var intentChecks = activeChecks
            .Where(c => c.Code is ArchitectureChecker.IntentBoundaryCode
                or ArchitectureChecker.IntentSurfaceCode
                or ArchitectureChecker.IntentStaleCode)
            .ToList();

        DocsFormatting.Line(md, "## Findings Summary");
        DocsFormatting.Line(md);
        DocsFormatting.Line(md, "| Metric | Count |");
        DocsFormatting.Line(md, "| --- | ---: |");
        DocsFormatting.Line(md, $"| Active findings | {activeChecks.Count} |");
        DocsFormatting.Line(md, $"| Active errors | {activeErrors} |");
        DocsFormatting.Line(md, $"| Active warnings | {activeWarnings} |");
        DocsFormatting.Line(md, $"| Intent findings (`C-BOUNDARY` / `C-SURFACE` / `C-INTENT-STALE`) | {intentChecks.Count} |");
        DocsFormatting.Line(md, $"| Known baseline findings | {knownChecks.Count} |");
        DocsFormatting.Line(md, $"| Analysis diagnostics | {result.Diagnostics.Count} |");
        DocsFormatting.Line(md);

        DocsFormatting.Line(md, "## Architecture & Intent Findings");
        DocsFormatting.Line(md);
        if (activeChecks.Count == 0)
        {
            DocsFormatting.Line(md, "_No active architecture or intent findings._");
            DocsFormatting.Line(md);
        }
        else
        {
            var metrics = FindingsClustering.Analyze(activeChecks, result);
            WriteClusteringTables(md, metrics);
            WriteDetailedFindingsTable(md, "### Detailed Active Findings", activeChecks);
        }

        if (knownChecks.Count > 0)
        {
            WriteDetailedFindingsTable(md, "### Known Findings (Baseline)", knownChecks);
        }

        if (result.Intent is not null)
        {
            DocsFormatting.Line(md, "## Intent Verification Findings");
            DocsFormatting.Line(md);
            if (intentChecks.Count == 0)
            {
                DocsFormatting.Line(md, "_No intent violations or stale intent selectors found._");
                DocsFormatting.Line(md);
            }
            else
            {
                WriteDetailedFindingsTable(md, "### Intent Findings Detail", intentChecks);
            }

            DocsFormatting.Line(md, "_Ownership declarations are never checked and are rendered as **declared, unverified** in `02-building-blocks.md`._");
            DocsFormatting.Line(md);
        }

        WriteDiagnosticsSection(md, result.Diagnostics);

        return md.ToString();
    }

    private static void WriteClusteringTables(StringBuilder md, FindingsClustering.FindingsMetrics metrics)
    {
        DocsFormatting.Line(md, "### Findings by Rule");
        DocsFormatting.Line(md);
        DocsFormatting.Line(md, "| Rule | Errors | Warnings | Total |");
        DocsFormatting.Line(md, "| --- | ---: | ---: | ---: |");
        foreach (var rule in metrics.ByRule)
        {
            DocsFormatting.Line(
                md,
                $"| {MarkdownEscaping.CodeSpan(rule.Code)} | {rule.Errors} | {rule.Warnings} | {rule.Total} |");
        }
        DocsFormatting.Line(md);

        DocsFormatting.Line(md, "### Findings by Module");
        DocsFormatting.Line(md);
        DocsFormatting.Line(md, "| Module | Errors | Warnings | Total |");
        DocsFormatting.Line(md, "| --- | ---: | ---: | ---: |");
        foreach (var module in metrics.ByModule)
        {
            DocsFormatting.Line(
                md,
                $"| {MarkdownEscaping.TableCell(module.Module)} | {module.Errors} | {module.Warnings} | {module.Total} |");
        }
        DocsFormatting.Line(md);

        DocsFormatting.Line(md, "### Findings by Module & Feature");
        DocsFormatting.Line(md);
        DocsFormatting.Line(md, "| Module | Feature | Rules | Errors | Warnings | Total |");
        DocsFormatting.Line(md, "| --- | --- | --- | ---: | ---: | ---: |");
        foreach (var cluster in metrics.Clusters)
        {
            var rules = cluster.Rules.Count == 0
                ? "—"
                : string.Join(", ", cluster.Rules.Select(r => $"{r.Code} {r.Total}"));
            DocsFormatting.Line(
                md,
                $"| {MarkdownEscaping.TableCell(cluster.Module)} | " +
                $"{MarkdownEscaping.TableCell(cluster.Feature)} | " +
                $"{MarkdownEscaping.TableCell(rules)} | " +
                $"{cluster.Errors} | {cluster.Warnings} | {cluster.Total} |");
        }
        DocsFormatting.Line(md);
    }

    private static void WriteDetailedFindingsTable(
        StringBuilder md,
        string heading,
        IReadOnlyList<CheckFinding> findings)
    {
        DocsFormatting.Line(md, heading);
        DocsFormatting.Line(md);
        DocsFormatting.Line(md, "| Severity | Code | Project | Target | Location | Message |");
        DocsFormatting.Line(md, "| --- | --- | --- | --- | --- | --- |");
        foreach (var f in findings)
        {
            var sev = f.Severity == CheckSeverity.Error ? "error" : "warning";
            DocsFormatting.Line(
                md,
                $"| {sev} | {MarkdownEscaping.CodeSpan(f.Code)} | " +
                $"{MarkdownEscaping.TableCell(f.ProjectName ?? "—")} | " +
                $"{MarkdownEscaping.TableCell(f.Target ?? "—")} | " +
                $"{DocsFormatting.FormatLocation(f.Location)} | " +
                $"{MarkdownEscaping.TableCell(f.Message)} |");
        }
        DocsFormatting.Line(md);
    }

    private static void WriteDiagnosticsSection(StringBuilder md, IReadOnlyList<AnalysisDiagnostic> diagnostics)
    {
        DocsFormatting.Line(md, "## Analysis Diagnostics");
        DocsFormatting.Line(md);
        if (diagnostics.Count == 0)
        {
            DocsFormatting.Line(md, "_No analysis diagnostics._");
            DocsFormatting.Line(md);
            return;
        }

        DocsFormatting.Line(md, "| Severity | Code | Project | Location | Message |");
        DocsFormatting.Line(md, "| --- | --- | --- | --- | --- |");
        foreach (var d in diagnostics
                     .OrderByDescending(d => (int)d.Severity)
                     .ThenBy(d => d.Code, StringComparer.Ordinal)
                     .ThenBy(d => d.ProjectName ?? string.Empty, StringComparer.Ordinal)
                     .ThenBy(d => d.Location?.File ?? string.Empty, StringComparer.Ordinal)
                     .ThenBy(d => d.Location?.Line ?? 0)
                     .ThenBy(d => d.Message, StringComparer.Ordinal))
        {
            var sev = d.Severity switch
            {
                AnalysisDiagnosticSeverity.Error => "error",
                AnalysisDiagnosticSeverity.Warning => "warning",
                _ => "info",
            };
            DocsFormatting.Line(
                md,
                $"| {sev} | {MarkdownEscaping.CodeSpan(d.Code)} | " +
                $"{MarkdownEscaping.TableCell(d.ProjectName ?? "—")} | " +
                $"{DocsFormatting.FormatLocation(d.Location)} | " +
                $"{MarkdownEscaping.TableCell(d.Message)} |");
        }
        DocsFormatting.Line(md);
    }
}
