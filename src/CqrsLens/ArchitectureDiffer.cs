using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using static CqrsLens.JsonReportGenerator;

namespace CqrsLens;

/// <summary>A diff input or comparison error that should be reported as a CLI usage failure.</summary>
public sealed class DiffException : Exception
{
    public DiffException(string message) : base(message)
    {
    }
}

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
    {
        if (Directory.Exists(path))
        {
            var fullDir = Path.GetFullPath(path);
            if (CsprojParser.FindProjectFiles(fullDir).Count == 0)
                throw new DiffException($"no .csproj files found under diff directory: {path}");

            var baseSettings = ConfigurationLoader.Load(fullDir, null);
            var baseResult = Analyzer.Analyze(
                path,
                baseSettings.AnalysisMode ?? analysisMode,
                new AnalysisOptions
                {
                    Markers = options.Markers.Count > 0 ? options.Markers : baseSettings.Markers,
                    HandlerMarkers = options.HandlerMarkers.Count > 0 ? options.HandlerMarkers : baseSettings.HandlerMarkers,
                    GeneratedAt = options.GeneratedAt,
                    Include = baseSettings.Include.Count > 0 ? baseSettings.Include : options.Include,
                    Exclude = baseSettings.Exclude.Count > 0 ? baseSettings.Exclude : options.Exclude,
                    LayerOverrides = baseSettings.LayerOverrides.Count > 0 ? baseSettings.LayerOverrides : options.LayerOverrides,
                    RuleSeverities = baseSettings.RuleSeverities.Count > 0 ? baseSettings.RuleSeverities : options.RuleSeverities,
                });
            return CreateDocument(baseResult);
        }

        if (File.Exists(path))
            return ReadDocument(path);

        throw new DiffException($"diff path not found: {path}");
    }

    internal static ArchitectureDiff Compare(AnalysisResult baseline, AnalysisResult current)
        => Compare(CreateDocument(baseline), CreateDocument(current));

    internal static ArchitectureDiff Compare(JsonReportDocument baseline, JsonReportDocument current)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);

        var baseErrors = ActiveChecks(baseline).Count(c => string.Equals(c.Severity, "error", StringComparison.OrdinalIgnoreCase));
        var baseWarnings = ActiveChecks(baseline).Count() - baseErrors;
        var headErrors = ActiveChecks(current).Count(c => string.Equals(c.Severity, "error", StringComparison.OrdinalIgnoreCase));
        var headWarnings = ActiveChecks(current).Count() - headErrors;

        var diff = new ArchitectureDiff
        {
            SchemaVersion = CurrentSchemaVersion,
            ToolVersion = ToolInfo.Version,
            Scanner = current.Scanner,
            GeneratedAt = current.GeneratedAt,
            BaseRootName = baseline.RootName,
            HeadRootName = current.RootName,
            Watermark = current.Watermark,
            Summary = new DiffSummary
            {
                Projects = new DiffMetric(baseline.Projects.Count, current.Projects.Count),
                Requests = new DiffMetric(baseline.Requests.Count, current.Requests.Count),
                Handlers = new DiffMetric(baseline.Handlers.Count, current.Handlers.Count),
                Validators = new DiffMetric(baseline.Validators.Count, current.Validators.Count),
                Behaviors = new DiffMetric(baseline.Behaviors.Count, current.Behaviors.Count),
                DbContexts = new DiffMetric(baseline.DbContexts.Count, current.DbContexts.Count),
                Errors = new DiffMetric(baseErrors, headErrors),
                Warnings = new DiffMetric(baseWarnings, headWarnings),
            },
            Projects = CompareProjects(baseline.Projects, current.Projects),
            Requests = CompareRequests(baseline.Requests, current.Requests),
            Handlers = CompareHandlers(baseline.Handlers, current.Handlers),
            Validators = CompareValidators(baseline.Validators, current.Validators),
            Behaviors = CompareBehaviors(baseline.Behaviors, current.Behaviors),
            DbContexts = CompareDbContexts(baseline.DbContexts, current.DbContexts),
            Checks = CompareChecks(baseline.Checks, current.Checks),
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

    internal static string FormatConsoleSummary(ArchitectureDiff diff)
    {
        if (!diff.HasChanges)
            return "Architecture diff: no changes detected.";

        return $"Architecture diff: " +
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
    {
        ArgumentNullException.ThrowIfNull(diff);

        var md = new StringBuilder();
        md.AppendLine(CommentMarker);
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

    private static IEnumerable<JsonCheckFinding> ActiveChecks(JsonReportDocument doc)
        => doc.BaselineApplied ? doc.Checks.Where(c => !c.Known) : doc.Checks;

    private static ProjectDiffSection CompareProjects(
        IReadOnlyList<JsonProject> baseProjects,
        IReadOnlyList<JsonProject> headProjects)
    {
        var section = new ProjectDiffSection();
        var baseByName = baseProjects
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var headByName = headProjects
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var h in headProjects.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!baseByName.TryGetValue(h.Name, out var b))
            {
                section.Added.Add(h);
                continue;
            }

            var layerChanged = !string.Equals(b.Layer, h.Layer, StringComparison.OrdinalIgnoreCase);
            var addedRefs = h.References
                .Except(b.References, StringComparer.OrdinalIgnoreCase)
                .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var removedRefs = b.References
                .Except(h.References, StringComparer.OrdinalIgnoreCase)
                .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var addedPkgs = h.Packages
                .Except(b.Packages, StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var removedPkgs = b.Packages
                .Except(h.Packages, StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (layerChanged || addedRefs.Count > 0 || removedRefs.Count > 0 || addedPkgs.Count > 0 || removedPkgs.Count > 0)
            {
                section.Modified.Add(new ModifiedProjectDiff
                {
                    Name = h.Name,
                    OldLayer = layerChanged ? b.Layer : null,
                    NewLayer = layerChanged ? h.Layer : null,
                    AddedReferences = addedRefs,
                    RemovedReferences = removedRefs,
                    AddedPackages = addedPkgs,
                    RemovedPackages = removedPkgs,
                });
            }
        }

        foreach (var b in baseProjects.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!headByName.ContainsKey(b.Name))
                section.Removed.Add(b);
        }

        return section;
    }

    private static RequestDiffSection CompareRequests(
        IReadOnlyList<JsonRequest> baseRequests,
        IReadOnlyList<JsonRequest> headRequests)
    {
        var section = new RequestDiffSection();

        var baseCounts = baseRequests
            .GroupBy(r => r.FullName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var headCounts = headRequests
            .GroupBy(r => r.FullName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        string RequestKey(JsonRequest r)
        {
            var duplicateInBase = baseCounts.TryGetValue(r.FullName, out var bc) && bc > 1;
            var duplicateInHead = headCounts.TryGetValue(r.FullName, out var hc) && hc > 1;
            return duplicateInBase || duplicateInHead
                ? $"{r.FullName}\u001f{r.Project ?? ""}\u001f{r.ResponseType ?? ""}"
                : r.FullName;
        }

        var baseByKey = baseRequests
            .GroupBy(RequestKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var headByKey = headRequests
            .GroupBy(RequestKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var h in headRequests.OrderBy(r => r.FullName, StringComparer.Ordinal))
        {
            var key = RequestKey(h);
            if (!baseByKey.TryGetValue(key, out var b))
            {
                section.Added.Add(h);
                continue;
            }

            var kindChanged = !string.Equals(b.Kind, h.Kind, StringComparison.Ordinal);
            var responseChanged = !string.Equals(b.ResponseType ?? "", h.ResponseType ?? "", StringComparison.Ordinal);
            var projectChanged = !string.Equals(b.Project ?? "", h.Project ?? "", StringComparison.Ordinal);
            var addedHandlers = h.Handlers.Except(b.Handlers, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var removedHandlers = b.Handlers.Except(h.Handlers, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var addedValidators = h.Validators.Except(b.Validators, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var removedValidators = b.Validators.Except(h.Validators, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();

            if (kindChanged || responseChanged || projectChanged ||
                addedHandlers.Count > 0 || removedHandlers.Count > 0 ||
                addedValidators.Count > 0 || removedValidators.Count > 0)
            {
                section.Modified.Add(new ModifiedRequestDiff
                {
                    FullName = h.FullName,
                    Name = h.Name,
                    Project = h.Project,
                    OldKind = kindChanged ? b.Kind : null,
                    NewKind = kindChanged ? h.Kind : null,
                    OldResponseType = responseChanged ? b.ResponseType : null,
                    NewResponseType = responseChanged ? h.ResponseType : null,
                    OldProject = projectChanged ? b.Project : null,
                    NewProject = projectChanged ? h.Project : null,
                    AddedHandlers = addedHandlers,
                    RemovedHandlers = removedHandlers,
                    AddedValidators = addedValidators,
                    RemovedValidators = removedValidators,
                    HeadSnapshot = h,
                });
            }
        }

        foreach (var b in baseRequests.OrderBy(r => r.FullName, StringComparer.Ordinal))
        {
            var key = RequestKey(b);
            if (!headByKey.ContainsKey(key))
                section.Removed.Add(b);
        }

        return section;
    }

    private static HandlerDiffSection CompareHandlers(
        IReadOnlyList<JsonHandler> baseHandlers,
        IReadOnlyList<JsonHandler> headHandlers)
    {
        static string Key(JsonHandler h) => $"{h.FullName}\u001f{h.RequestName}";
        var baseKeys = baseHandlers.Select(Key).ToHashSet(StringComparer.Ordinal);
        var headKeys = headHandlers.Select(Key).ToHashSet(StringComparer.Ordinal);

        return new HandlerDiffSection
        {
            Added = headHandlers
                .Where(h => !baseKeys.Contains(Key(h)))
                .OrderBy(h => h.FullName, StringComparer.Ordinal)
                .ThenBy(h => h.RequestName, StringComparer.Ordinal)
                .ToList(),
            Removed = baseHandlers
                .Where(h => !headKeys.Contains(Key(h)))
                .OrderBy(h => h.FullName, StringComparer.Ordinal)
                .ThenBy(h => h.RequestName, StringComparer.Ordinal)
                .ToList(),
        };
    }

    private static ValidatorDiffSection CompareValidators(
        IReadOnlyList<JsonValidator> baseValidators,
        IReadOnlyList<JsonValidator> headValidators)
    {
        static string Key(JsonValidator v) => $"{v.FullName}\u001f{v.TargetType}";
        var baseKeys = baseValidators.Select(Key).ToHashSet(StringComparer.Ordinal);
        var headKeys = headValidators.Select(Key).ToHashSet(StringComparer.Ordinal);

        return new ValidatorDiffSection
        {
            Added = headValidators
                .Where(v => !baseKeys.Contains(Key(v)))
                .OrderBy(v => v.FullName, StringComparer.Ordinal)
                .ThenBy(v => v.TargetType, StringComparer.Ordinal)
                .ToList(),
            Removed = baseValidators
                .Where(v => !headKeys.Contains(Key(v)))
                .OrderBy(v => v.FullName, StringComparer.Ordinal)
                .ThenBy(v => v.TargetType, StringComparer.Ordinal)
                .ToList(),
        };
    }

    private static BehaviorDiffSection CompareBehaviors(
        IReadOnlyList<JsonBehavior> baseBehaviors,
        IReadOnlyList<JsonBehavior> headBehaviors)
    {
        static string Key(JsonBehavior b) => $"{b.Name}\u001f{b.Project ?? ""}";
        var baseKeys = baseBehaviors.Select(Key).ToHashSet(StringComparer.Ordinal);
        var headKeys = headBehaviors.Select(Key).ToHashSet(StringComparer.Ordinal);

        return new BehaviorDiffSection
        {
            Added = headBehaviors
                .Where(b => !baseKeys.Contains(Key(b)))
                .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Removed = baseBehaviors
                .Where(b => !headKeys.Contains(Key(b)))
                .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };
    }

    private static DbContextDiffSection CompareDbContexts(
        IReadOnlyList<JsonDbContext> baseContexts,
        IReadOnlyList<JsonDbContext> headContexts)
    {
        var section = new DbContextDiffSection();
        static string Key(JsonDbContext c)
            => string.IsNullOrEmpty(c.Namespace) ? c.Name : $"{c.Namespace}.{c.Name}";
        static string DbSetKey(JsonDbSet s) => $"{s.Property} -> {s.Entity}";
        static string RelKey(JsonEntityRelationship r)
            => $"{CSharpScanner.SimpleName(r.PrincipalEntity)} -> {CSharpScanner.SimpleName(r.DependentEntity)} ({r.Kind})";

        var baseByKey = baseContexts
            .GroupBy(Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var headByKey = headContexts
            .GroupBy(Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var h in headContexts.OrderBy(Key, StringComparer.Ordinal))
        {
            var key = Key(h);
            if (!baseByKey.TryGetValue(key, out var b))
            {
                section.Added.Add(h);
                continue;
            }

            var bSets = b.DbSets.Select(DbSetKey).ToList();
            var hSets = h.DbSets.Select(DbSetKey).ToList();
            var addedSets = hSets.Except(bSets, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var removedSets = bSets.Except(hSets, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();

            var bEntities = b.Entities.Select(e => e.FullName).ToList();
            var hEntities = h.Entities.Select(e => e.FullName).ToList();
            var addedEntities = hEntities.Except(bEntities, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var removedEntities = bEntities.Except(hEntities, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();

            var bRels = b.Relationships.Select(RelKey).ToList();
            var hRels = h.Relationships.Select(RelKey).ToList();
            var addedRels = hRels.Except(bRels, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var removedRels = bRels.Except(hRels, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();

            if (addedSets.Count > 0 || removedSets.Count > 0 ||
                addedEntities.Count > 0 || removedEntities.Count > 0 ||
                addedRels.Count > 0 || removedRels.Count > 0)
            {
                section.Modified.Add(new ModifiedDbContextDiff
                {
                    Name = h.Name,
                    Project = h.Project,
                    AddedDbSets = addedSets,
                    RemovedDbSets = removedSets,
                    AddedEntities = addedEntities,
                    RemovedEntities = removedEntities,
                    AddedRelationships = addedRels,
                    RemovedRelationships = removedRels,
                });
            }
        }

        foreach (var b in baseContexts.OrderBy(Key, StringComparer.Ordinal))
        {
            if (!headByKey.ContainsKey(Key(b)))
                section.Removed.Add(b);
        }

        return section;
    }

    private static CheckDiffSection CompareChecks(
        IReadOnlyList<JsonCheckFinding> baseChecks,
        IReadOnlyList<JsonCheckFinding> headChecks)
    {
        static string Key(JsonCheckFinding c)
            => Baseline.CreateKey(c.Code, c.Target, c.Location?.File);

        var activeBase = baseChecks.Where(c => !c.Known).ToList();
        var activeHead = headChecks.Where(c => !c.Known).ToList();

        var baseRemaining = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var c in activeBase)
        {
            var key = Key(c);
            baseRemaining[key] = baseRemaining.TryGetValue(key, out var count) ? count + 1 : 1;
        }

        var newFindings = new List<JsonCheckFinding>();
        foreach (var c in activeHead)
        {
            var key = Key(c);
            if (baseRemaining.TryGetValue(key, out var count) && count > 0)
                baseRemaining[key] = count - 1;
            else
                newFindings.Add(c);
        }

        var headRemaining = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var c in activeHead)
        {
            var key = Key(c);
            headRemaining[key] = headRemaining.TryGetValue(key, out var count) ? count + 1 : 1;
        }

        var resolvedFindings = new List<JsonCheckFinding>();
        foreach (var c in activeBase)
        {
            var key = Key(c);
            if (headRemaining.TryGetValue(key, out var count) && count > 0)
                headRemaining[key] = count - 1;
            else
                resolvedFindings.Add(c);
        }

        return new CheckDiffSection
        {
            NewFindings = newFindings,
            ResolvedFindings = resolvedFindings,
        };
    }

    internal sealed class ArchitectureDiff
    {
        public int SchemaVersion { get; set; }
        public string ToolVersion { get; set; } = "";
        public string Scanner { get; set; } = "";
        public string GeneratedAt { get; set; } = "";
        public string BaseRootName { get; set; } = "";
        public string HeadRootName { get; set; } = "";
        public string? Watermark { get; set; }
        public bool HasChanges { get; set; }
        public DiffSummary Summary { get; set; } = new();
        public ProjectDiffSection Projects { get; set; } = new();
        public RequestDiffSection Requests { get; set; } = new();
        public HandlerDiffSection Handlers { get; set; } = new();
        public ValidatorDiffSection Validators { get; set; } = new();
        public BehaviorDiffSection Behaviors { get; set; } = new();
        public DbContextDiffSection DbContexts { get; set; } = new();
        public CheckDiffSection Checks { get; set; } = new();
    }

    internal sealed record DiffMetric(int Base, int Head)
    {
        public int Delta => Head - Base;
    }

    internal sealed class DiffSummary
    {
        public DiffMetric Projects { get; set; } = new(0, 0);
        public DiffMetric Requests { get; set; } = new(0, 0);
        public DiffMetric Handlers { get; set; } = new(0, 0);
        public DiffMetric Validators { get; set; } = new(0, 0);
        public DiffMetric Behaviors { get; set; } = new(0, 0);
        public DiffMetric DbContexts { get; set; } = new(0, 0);
        public DiffMetric Errors { get; set; } = new(0, 0);
        public DiffMetric Warnings { get; set; } = new(0, 0);
    }

    internal sealed class ProjectDiffSection
    {
        public List<JsonProject> Added { get; set; } = new();
        public List<JsonProject> Removed { get; set; } = new();
        public List<ModifiedProjectDiff> Modified { get; set; } = new();

        [JsonIgnore]
        public bool HasChanges => Added.Count > 0 || Removed.Count > 0 || Modified.Count > 0;
    }

    internal sealed class ModifiedProjectDiff
    {
        public string Name { get; set; } = "";
        public string? OldLayer { get; set; }
        public string? NewLayer { get; set; }
        public List<string> AddedReferences { get; set; } = new();
        public List<string> RemovedReferences { get; set; } = new();
        public List<string> AddedPackages { get; set; } = new();
        public List<string> RemovedPackages { get; set; } = new();
    }

    internal sealed class RequestDiffSection
    {
        public List<JsonRequest> Added { get; set; } = new();
        public List<JsonRequest> Removed { get; set; } = new();
        public List<ModifiedRequestDiff> Modified { get; set; } = new();

        [JsonIgnore]
        public bool HasChanges => Added.Count > 0 || Removed.Count > 0 || Modified.Count > 0;
    }

    internal sealed class ModifiedRequestDiff
    {
        public string FullName { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Project { get; set; }
        public string? OldKind { get; set; }
        public string? NewKind { get; set; }
        public string? OldResponseType { get; set; }
        public string? NewResponseType { get; set; }
        public string? OldProject { get; set; }
        public string? NewProject { get; set; }
        public List<string> AddedHandlers { get; set; } = new();
        public List<string> RemovedHandlers { get; set; } = new();
        public List<string> AddedValidators { get; set; } = new();
        public List<string> RemovedValidators { get; set; } = new();

        [JsonIgnore]
        public JsonRequest HeadSnapshot { get; set; } = new();
    }

    internal sealed class HandlerDiffSection
    {
        public List<JsonHandler> Added { get; set; } = new();
        public List<JsonHandler> Removed { get; set; } = new();

        [JsonIgnore]
        public bool HasChanges => Added.Count > 0 || Removed.Count > 0;
    }

    internal sealed class ValidatorDiffSection
    {
        public List<JsonValidator> Added { get; set; } = new();
        public List<JsonValidator> Removed { get; set; } = new();

        [JsonIgnore]
        public bool HasChanges => Added.Count > 0 || Removed.Count > 0;
    }

    internal sealed class BehaviorDiffSection
    {
        public List<JsonBehavior> Added { get; set; } = new();
        public List<JsonBehavior> Removed { get; set; } = new();

        [JsonIgnore]
        public bool HasChanges => Added.Count > 0 || Removed.Count > 0;
    }

    internal sealed class DbContextDiffSection
    {
        public List<JsonDbContext> Added { get; set; } = new();
        public List<JsonDbContext> Removed { get; set; } = new();
        public List<ModifiedDbContextDiff> Modified { get; set; } = new();

        [JsonIgnore]
        public bool HasChanges => Added.Count > 0 || Removed.Count > 0 || Modified.Count > 0;
    }

    internal sealed class ModifiedDbContextDiff
    {
        public string Name { get; set; } = "";
        public string? Project { get; set; }
        public List<string> AddedDbSets { get; set; } = new();
        public List<string> RemovedDbSets { get; set; } = new();
        public List<string> AddedEntities { get; set; } = new();
        public List<string> RemovedEntities { get; set; } = new();
        public List<string> AddedRelationships { get; set; } = new();
        public List<string> RemovedRelationships { get; set; } = new();
    }

    internal sealed class CheckDiffSection
    {
        public List<JsonCheckFinding> NewFindings { get; set; } = new();
        public List<JsonCheckFinding> ResolvedFindings { get; set; } = new();

        [JsonIgnore]
        public bool HasChanges => NewFindings.Count > 0 || ResolvedFindings.Count > 0;
    }
}
