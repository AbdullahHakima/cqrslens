using System.Text;

using static CqrsLens.JsonReportGenerator;

namespace CqrsLens;

/// <summary>Renders an architecture diff as the Markdown report used in pull requests and CI summaries.</summary>
internal static class ArchitectureDiffMarkdownRenderer
{
    internal static string Generate(
        ArchitectureDiff diff,
        AnalysisResult? headResult = null,
        string? ciRoot = null)
    {
        ArgumentNullException.ThrowIfNull(diff);

        var md = new StringBuilder();
        md.AppendLine(ArchitectureDiffer.CommentMarker);
        md.AppendLine("## CqrsLens Architecture Diff");
        md.AppendLine();

        var timestampText = DateTimeOffset.TryParse(diff.GeneratedAt, out var dto)
            ? $"{dto.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC"
            : ReportGenerator.InlineText(diff.GeneratedAt);

        var rootComparison = string.Equals(diff.BaseRootName, diff.HeadRootName, StringComparison.Ordinal) ||
                             string.IsNullOrEmpty(diff.BaseRootName)
            ? ReportGenerator.CodeSpan(string.IsNullOrEmpty(diff.HeadRootName) ? "." : diff.HeadRootName)
            : $"{ReportGenerator.CodeSpan(diff.BaseRootName)} → {ReportGenerator.CodeSpan(diff.HeadRootName)}";

        md.AppendLine(
            $"Compared {rootComparison} with **CqrsLens {ToolInfo.Version}** " +
            $"({ReportGenerator.InlineText(diff.Scanner)} scanner) on {timestampText}.");
        md.AppendLine();

        if (!string.IsNullOrWhiteSpace(diff.Watermark))
        {
            md.AppendLine("```text");
            md.AppendLine(ConsoleArtwork.RenderWatermarkStamp(diff.Watermark, useColor: false));
            md.AppendLine("```");
            md.AppendLine();
        }

        if (!diff.HasChanges)
        {
            md.AppendLine("✅ **No architecture changes detected** between the base scan and this revision.");
            md.AppendLine();
        }
        else
        {
            md.AppendLine(BuildHeadline(diff));
            md.AppendLine();
        }

        // ---- Summary Table ---------------------------------------------------
        md.AppendLine("| Metric | Base | Head | Delta |");
        md.AppendLine("| --- | ---: | ---: | ---: |");
        AppendMetricRow(md, "Projects", diff.Summary.Projects);
        AppendMetricRow(md, "Requests (commands + queries)", diff.Summary.Requests);
        AppendMetricRow(md, "Handlers", diff.Summary.Handlers);
        AppendMetricRow(md, "Validators", diff.Summary.Validators);
        AppendMetricRow(md, "Pipeline behaviors", diff.Summary.Behaviors);
        AppendMetricRow(md, "DbContexts", diff.Summary.DbContexts);
        AppendMetricRow(md, "Check errors", diff.Summary.Errors);
        AppendMetricRow(md, "Check warnings", diff.Summary.Warnings);
        md.AppendLine();

        // ---- Architecture Checks ---------------------------------------------
        md.AppendLine("### Architecture Checks");
        md.AppendLine();
        if (!diff.Checks.HasChanges)
        {
            if (diff.Summary.Errors.Head == 0 && diff.Summary.Warnings.Head == 0)
                md.AppendLine("All checks passed (no new or resolved findings).");
            else
                md.AppendLine(
                    $"No change in architecture check findings " +
                    $"({diff.Summary.Errors.Head} error(s), {diff.Summary.Warnings.Head} warning(s)).");
            md.AppendLine();
        }
        else
        {
            var newErrors = diff.Checks.NewFindings.Count(f => string.Equals(f.Severity, "error", StringComparison.OrdinalIgnoreCase));
            var newWarnings = diff.Checks.NewFindings.Count - newErrors;
            md.AppendLine(
                $"**{newErrors}** new error(s), **{newWarnings}** new warning(s), " +
                $"**{diff.Checks.ResolvedFindings.Count}** resolved finding(s).");
            md.AppendLine();

            if (diff.Checks.NewFindings.Count > 0)
                AppendFindingsTable(md, $"New findings ({diff.Checks.NewFindings.Count})", diff.Checks.NewFindings, headResult, ciRoot, isResolved: false);

            if (diff.Checks.ResolvedFindings.Count > 0)
                AppendFindingsTable(md, $"Resolved findings ({diff.Checks.ResolvedFindings.Count})", diff.Checks.ResolvedFindings, headResult: null, ciRoot: null, isResolved: true);
        }

        // ---- CQRS Surface Changes --------------------------------------------
        if (diff.Requests.HasChanges || diff.Handlers.HasChanges || diff.Validators.HasChanges || diff.Behaviors.HasChanges)
        {
            md.AppendLine("### CQRS Surface Changes");
            md.AppendLine();

            var deltaDiagram = BuildCqrsDeltaMermaid(diff.Requests);
            if (deltaDiagram is not null && DiagramBuilder.IsWithinMermaidLimits(deltaDiagram))
            {
                md.AppendLine("**Delta legend:** green = added request · amber = modified request · dashed red/gray = removed request.");
                md.AppendLine();
                md.AppendLine("```mermaid");
                md.AppendLine(deltaDiagram);
                md.AppendLine("```");
                md.AppendLine();
            }

            if (diff.Requests.Added.Count > 0)
            {
                md.AppendLine($"#### Added requests ({diff.Requests.Added.Count})");
                md.AppendLine();
                md.AppendLine("| Request | Type | Response | Handler | Validators | Project |");
                md.AppendLine("| --- | --- | --- | --- | --- | --- |");
                foreach (var r in diff.Requests.Added)
                {
                    var handlers = r.Handlers.Count > 0
                        ? string.Join(", ", r.Handlers.Select(CSharpScanner.SimpleName))
                        : "—";
                    var validators = r.Validators.Count > 0
                        ? string.Join(", ", r.Validators.Select(CSharpScanner.SimpleName))
                        : "—";
                    md.AppendLine(
                        $"| + {ReportGenerator.TableCell(r.Name)} | {ReportGenerator.TableCell(r.Kind)} | " +
                        $"{ReportGenerator.TableCell(r.ResponseType ?? "—")} | {ReportGenerator.TableCell(handlers)} | " +
                        $"{ReportGenerator.TableCell(validators)} | {ReportGenerator.TableCell(r.Project ?? "—")} |");
                }
                md.AppendLine();
            }

            if (diff.Requests.Modified.Count > 0)
            {
                md.AppendLine($"#### Modified requests ({diff.Requests.Modified.Count})");
                md.AppendLine();
                md.AppendLine("| Request | Project | Changes |");
                md.AppendLine("| --- | --- | --- |");
                foreach (var m in diff.Requests.Modified)
                {
                    var changes = FormatRequestModifications(m);
                    md.AppendLine(
                        $"| ~ {ReportGenerator.TableCell(m.Name)} | " +
                        $"{ReportGenerator.TableCell(m.Project ?? "—")} | " +
                        $"{ReportGenerator.TableCell(changes)} |");
                }
                md.AppendLine();
            }

            if (diff.Requests.Removed.Count > 0)
            {
                md.AppendLine($"#### Removed requests ({diff.Requests.Removed.Count})");
                md.AppendLine();
                md.AppendLine("| Request | Type | Response | Project |");
                md.AppendLine("| --- | --- | --- | --- |");
                foreach (var r in diff.Requests.Removed)
                {
                    md.AppendLine(
                        $"| - {ReportGenerator.TableCell(r.Name)} | {ReportGenerator.TableCell(r.Kind)} | " +
                        $"{ReportGenerator.TableCell(r.ResponseType ?? "—")} | {ReportGenerator.TableCell(r.Project ?? "—")} |");
                }
                md.AppendLine();
            }

            if (diff.Behaviors.HasChanges)
            {
                md.AppendLine("#### Pipeline behaviors");
                md.AppendLine();
                foreach (var b in diff.Behaviors.Added)
                    md.AppendLine($"- ➕ Added {ReportGenerator.CodeSpan(b.Name)}{(b.Project is null ? "" : $" ({ReportGenerator.InlineText(b.Project)})")}");
                foreach (var b in diff.Behaviors.Removed)
                    md.AppendLine($"- ➖ Removed {ReportGenerator.CodeSpan(b.Name)}{(b.Project is null ? "" : $" ({ReportGenerator.InlineText(b.Project)})")}");
                md.AppendLine();
            }
        }

        // ---- Project & Layer Changes -----------------------------------------
        if (diff.Projects.HasChanges)
        {
            md.AppendLine("### Project & Layer Changes");
            md.AppendLine();
            foreach (var p in diff.Projects.Added)
            {
                var refs = p.References.Count > 0 ? $" → references: {string.Join(", ", p.References)}" : "";
                md.AppendLine($"- ➕ Added project **{ReportGenerator.InlineText(p.Name)}** (`{ReportGenerator.InlineText(p.Layer)}`){ReportGenerator.InlineText(refs)}");
            }
            foreach (var p in diff.Projects.Removed)
            {
                md.AppendLine($"- ➖ Removed project **{ReportGenerator.InlineText(p.Name)}** (`{ReportGenerator.InlineText(p.Layer)}`)");
            }
            foreach (var m in diff.Projects.Modified)
            {
                var parts = new List<string>();
                if (m.OldLayer is not null && m.NewLayer is not null)
                    parts.Add($"layer `{m.OldLayer}` → `{m.NewLayer}`");
                if (m.AddedReferences.Count > 0)
                    parts.Add($"+refs: {string.Join(", ", m.AddedReferences)}");
                if (m.RemovedReferences.Count > 0)
                    parts.Add($"-refs: {string.Join(", ", m.RemovedReferences)}");
                if (m.AddedPackages.Count > 0)
                    parts.Add($"+packages: {string.Join(", ", m.AddedPackages)}");
                if (m.RemovedPackages.Count > 0)
                    parts.Add($"-packages: {string.Join(", ", m.RemovedPackages)}");
                md.AppendLine($"- 🔄 Modified project **{ReportGenerator.InlineText(m.Name)}**: {ReportGenerator.InlineText(string.Join("; ", parts))}");
            }
            md.AppendLine();
        }

        // ---- Data Access (DbContexts) Changes --------------------------------
        if (diff.DbContexts.HasChanges)
        {
            md.AppendLine("### Data Access Changes (DbContexts)");
            md.AppendLine();
            foreach (var c in diff.DbContexts.Added)
            {
                var sets = c.DbSets.Count > 0
                    ? string.Join(", ", c.DbSets.Select(s => $"{s.Property} → {s.Entity}"))
                    : "no DbSets";
                md.AppendLine(
                    $"- ➕ Added DbContext **{ReportGenerator.InlineText(c.Name)}** " +
                    $"({ReportGenerator.InlineText(c.Project ?? "—")}): {ReportGenerator.InlineText(sets)}");
            }
            foreach (var c in diff.DbContexts.Removed)
            {
                md.AppendLine(
                    $"- ➖ Removed DbContext **{ReportGenerator.InlineText(c.Name)}** " +
                    $"({ReportGenerator.InlineText(c.Project ?? "—")})");
            }
            foreach (var m in diff.DbContexts.Modified)
            {
                var parts = new List<string>();
                if (m.AddedDbSets.Count > 0)
                    parts.Add($"+DbSets: {string.Join(", ", m.AddedDbSets)}");
                if (m.RemovedDbSets.Count > 0)
                    parts.Add($"-DbSets: {string.Join(", ", m.RemovedDbSets)}");
                if (m.AddedEntities.Count > 0)
                    parts.Add($"+Entities: {string.Join(", ", m.AddedEntities.Select(CSharpScanner.SimpleName))}");
                if (m.RemovedEntities.Count > 0)
                    parts.Add($"-Entities: {string.Join(", ", m.RemovedEntities.Select(CSharpScanner.SimpleName))}");
                if (m.AddedRelationships.Count > 0)
                    parts.Add($"+Relationships: {string.Join(", ", m.AddedRelationships)}");
                if (m.RemovedRelationships.Count > 0)
                    parts.Add($"-Relationships: {string.Join(", ", m.RemovedRelationships)}");
                md.AppendLine($"- 🔄 Modified DbContext **{ReportGenerator.InlineText(m.Name)}**: {ReportGenerator.InlineText(string.Join("; ", parts))}");
            }
            md.AppendLine();
        }

        md.AppendLine("---");
        md.AppendLine($"_Diff generated by [CqrsLens](https://github.com/AbdullahHakima/cqrslens) {ToolInfo.Version}._");

        return md.ToString();
    }

    private static string BuildHeadline(ArchitectureDiff diff)
    {
        var items = new List<string>();
        if (diff.Requests.HasChanges)
            items.Add($"+{diff.Requests.Added.Count}/-{diff.Requests.Removed.Count}/~{diff.Requests.Modified.Count} request(s)");
        if (diff.Handlers.HasChanges)
            items.Add($"+{diff.Handlers.Added.Count}/-{diff.Handlers.Removed.Count} handler(s)");
        if (diff.Validators.HasChanges)
            items.Add($"+{diff.Validators.Added.Count}/-{diff.Validators.Removed.Count} validator(s)");
        if (diff.Behaviors.HasChanges)
            items.Add($"+{diff.Behaviors.Added.Count}/-{diff.Behaviors.Removed.Count} behavior(s)");
        if (diff.Projects.HasChanges)
            items.Add($"+{diff.Projects.Added.Count}/-{diff.Projects.Removed.Count}/~{diff.Projects.Modified.Count} project(s)");
        if (diff.DbContexts.HasChanges)
            items.Add($"+{diff.DbContexts.Added.Count}/-{diff.DbContexts.Removed.Count}/~{diff.DbContexts.Modified.Count} DbContext(s)");
        if (diff.Checks.HasChanges)
            items.Add($"+{diff.Checks.NewFindings.Count} new / -{diff.Checks.ResolvedFindings.Count} resolved check finding(s)");

        return $"🔄 **Changes detected:** {string.Join(", ", items)}.";
    }

    private static void AppendMetricRow(StringBuilder md, string label, DiffMetric metric)
    {
        var deltaText = metric.Delta switch
        {
            > 0 => $"+{metric.Delta}",
            < 0 => metric.Delta.ToString(),
            _ => "0",
        };
        md.AppendLine($"| {label} | {metric.Base} | {metric.Head} | {deltaText} |");
    }

    private static void AppendFindingsTable(
        StringBuilder md,
        string title,
        IReadOnlyList<JsonCheckFinding> findings,
        AnalysisResult? headResult,
        string? ciRoot,
        bool isResolved)
    {
        md.AppendLine($"#### {title}");
        md.AppendLine();
        md.AppendLine("| Status | Rule | Finding | Location |");
        md.AppendLine("| --- | --- | --- | --- |");
        foreach (var finding in findings)
        {
            var status = isResolved
                ? "✅ resolved"
                : string.Equals(finding.Severity, "error", StringComparison.OrdinalIgnoreCase)
                    ? "❌ error"
                    : "⚠️ warning";
            var location = FormatLocation(finding.Location, headResult, ciRoot);
            md.AppendLine(
                $"| {status} | {ReportGenerator.TableCell(finding.Code)} | " +
                $"{ReportGenerator.TableCell(finding.Message)} | {location} |");
        }
        md.AppendLine();
    }

    private static string FormatLocation(JsonLocation? location, AnalysisResult? headResult, string? ciRoot)
    {
        if (location is null || string.IsNullOrEmpty(location.File))
            return "—";

        var file = headResult is not null
            ? CiPath.Relative(headResult, location.File, ciRoot)
            : location.File.Replace('\\', '/');
        return ReportGenerator.TableCell($"{file}:{location.Line}:{location.Column}");
    }

    private static string FormatRequestModifications(ModifiedRequestDiff m)
    {
        var parts = new List<string>();
        if (m.OldKind is not null && m.NewKind is not null)
            parts.Add($"Kind: {m.OldKind} → {m.NewKind}");
        if (m.OldResponseType != m.NewResponseType)
            parts.Add($"Response: {m.OldResponseType ?? "—"} → {m.NewResponseType ?? "—"}");
        if (m.OldProject != m.NewProject)
            parts.Add($"Project: {m.OldProject ?? "—"} → {m.NewProject ?? "—"}");
        if (m.AddedHandlers.Count > 0)
            parts.Add($"+Handler: {string.Join(", ", m.AddedHandlers.Select(CSharpScanner.SimpleName))}");
        if (m.RemovedHandlers.Count > 0)
            parts.Add($"-Handler: {string.Join(", ", m.RemovedHandlers.Select(CSharpScanner.SimpleName))}");
        if (m.AddedValidators.Count > 0)
            parts.Add($"+Validator: {string.Join(", ", m.AddedValidators.Select(CSharpScanner.SimpleName))}");
        if (m.RemovedValidators.Count > 0)
            parts.Add($"-Validator: {string.Join(", ", m.RemovedValidators.Select(CSharpScanner.SimpleName))}");
        return string.Join("; ", parts);
    }

    private static string? BuildCqrsDeltaMermaid(RequestDiffSection requests)
    {
        var totalChanged = requests.Added.Count + requests.Modified.Count + requests.Removed.Count;
        if (totalChanged == 0 || totalChanged > DiagramBuilder.MaxRequestsPerCqrsDiagram)
            return null;

        var sb = new StringBuilder();
        sb.AppendLine("flowchart LR");
        sb.AppendLine("    accTitle: CQRS diff map");
        sb.AppendLine(
            $"    accDescr: {requests.Added.Count} added, {requests.Modified.Count} modified, " +
            $"{requests.Removed.Count} removed request(s).");

        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        var idByKey = new Dictionary<string, string>(StringComparer.Ordinal);

        string Allocate(string key, string prefix, string name)
        {
            if (idByKey.TryGetValue(key, out var existing))
                return existing;
            var baseId = prefix + "_" + DiagramBuilder.MermaidId(name);
            var id = baseId;
            var suffix = 2;
            while (!usedIds.Add(id))
                id = $"{baseId}_{suffix++}";
            idByKey[key] = id;
            return id;
        }

        var items = new List<(JsonRequest Req, string StatusClass, string Prefix)>();
        foreach (var r in requests.Added)
            items.Add((r, "clsAdded", "+ "));
        foreach (var m in requests.Modified)
            items.Add((m.HeadSnapshot, "clsModified", "~ "));
        foreach (var r in requests.Removed)
            items.Add((r, "clsRemoved", "- "));

        foreach (var (req, statusClass, prefix) in items)
        {
            var reqId = Allocate("req:" + req.FullName + ":" + statusClass, "req", req.Name);
            var label = DiagramBuilder.MermaidLabel(prefix + req.Name);
            sb.AppendLine($"    {reqId}[\"{label}\"]:::{statusClass}");

            foreach (var h in req.Handlers.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
            {
                var hSimple = CSharpScanner.SimpleName(h);
                var hId = Allocate("hdl:" + h, "hdl", hSimple);
                sb.AppendLine($"    {hId}([\"{DiagramBuilder.MermaidLabel(hSimple)}\"]):::clsHandler");
                sb.AppendLine($"    {reqId} --> {hId}");
            }

            foreach (var v in req.Validators.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
            {
                var vSimple = CSharpScanner.SimpleName(v);
                var vId = Allocate("val:" + v, "val", vSimple);
                sb.AppendLine($"    {vId}[\"{DiagramBuilder.MermaidLabel(vSimple)}\"]:::clsValidator");
                sb.AppendLine($"    {vId} -.->|validates| {reqId}");
            }
        }

        sb.AppendLine("    classDef clsAdded fill:#ecfdf5,stroke:#059669,stroke-width:2px,color:#064e3b");
        sb.AppendLine("    classDef clsModified fill:#eff6ff,stroke:#d97706,stroke-width:2px,color:#1e3a8a");
        sb.AppendLine("    classDef clsRemoved fill:#fef2f2,stroke:#dc2626,stroke-width:2px,stroke-dasharray:4 3,color:#7f1d1d");
        sb.AppendLine("    classDef clsHandler fill:#f8fafc,stroke:#475569,stroke-width:1.5px,color:#1e293b");
        sb.AppendLine("    classDef clsValidator fill:#fff7ed,stroke:#c2410c,stroke-width:1.5px,color:#431407");

        return sb.ToString().TrimEnd();
    }
}