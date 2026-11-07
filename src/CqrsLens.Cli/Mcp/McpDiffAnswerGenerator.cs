using System.Text;

namespace CqrsLens;

/// <summary>
/// Renders the MCP <c>diff_architecture</c> answer from the architecture diff the CLI's own
/// <see cref="ArchitectureDiffer"/> computes. Counts come first, then the findings delta (new and
/// resolved, each identified by its <see cref="BaselineKeys"/> key), then the paged item lists.
/// Nothing is recomputed here: every number and list is read from the passed
/// <see cref="ArchitectureDiff"/>.
/// </summary>
internal static class McpDiffAnswerGenerator
{
    internal static string Generate(
        ArchitectureDiff diff,
        string baseDisplay,
        string headDisplay,
        bool baseIsFolder,
        int limit)
    {
        ArgumentNullException.ThrowIfNull(diff);

        var sb = new StringBuilder();
        sb.AppendLine($"# CqrsLens Architecture Diff — `{diff.HeadRootName}` (base `{diff.BaseRootName}`)");
        sb.AppendLine();
        sb.AppendLine($"- **Base**: `{baseDisplay}` (`{diff.BaseRootName}`) · {(baseIsFolder ? "scanned folder" : "saved snapshot JSON")}");
        sb.AppendLine($"- **Head**: `{headDisplay}` (`{diff.HeadRootName}`)");
        // The CLI's serialized HasChanges covers the shape kinds and findings; the MCP answer also
        // sees the endpoint section (which the CLI diff deliberately does not model), so a change
        // confined to endpoints still reads "Changed: yes" here.
        sb.AppendLine($"- **Changed**: {(diff.HasChanges || diff.Endpoints.HasChanges ? "yes" : "no")}");
        sb.AppendLine();

        AppendShapeDelta(sb, diff);
        AppendFindingsDelta(sb, diff);
        AppendItemLists(sb, diff, limit);
        return sb.ToString();
    }

    // ----------------------------------------------------------------------- counts first

    private static void AppendShapeDelta(StringBuilder sb, ArchitectureDiff diff)
    {
        sb.AppendLine("## Shape delta");
        sb.AppendLine(Metric(
            "Requests",
            diff.Requests.Added.Count,
            diff.Requests.Removed.Count,
            diff.Summary.Requests.Base - diff.Requests.Removed.Count,
            diff.Requests.Modified.Count));
        sb.AppendLine(Metric(
            "Handlers",
            diff.Handlers.Added.Count,
            diff.Handlers.Removed.Count,
            diff.Summary.Handlers.Base - diff.Handlers.Removed.Count));
        sb.AppendLine(Metric(
            "Validators",
            diff.Validators.Added.Count,
            diff.Validators.Removed.Count,
            diff.Summary.Validators.Base - diff.Validators.Removed.Count));
        sb.AppendLine(Metric(
            "Pipeline behaviors",
            diff.Behaviors.Added.Count,
            diff.Behaviors.Removed.Count,
            diff.Summary.Behaviors.Base - diff.Behaviors.Removed.Count));
        sb.AppendLine(Metric(
            "DbContexts",
            diff.DbContexts.Added.Count,
            diff.DbContexts.Removed.Count,
            diff.Summary.DbContexts.Base - diff.DbContexts.Removed.Count,
            diff.DbContexts.Modified.Count));
        sb.AppendLine(Metric(
            "Endpoints",
            diff.Endpoints.Added.Count,
            diff.Endpoints.Removed.Count,
            diff.Endpoints.Unchanged));
        sb.AppendLine();
    }

    private static string Metric(string label, int added, int removed, int unchanged, int? modified = null)
    {
        var line = $"- **{label}**: +{added} added · -{removed} removed · ={unchanged} unchanged";
        return modified is null ? line : line + $" · ~{modified} modified";
    }

    private static void AppendFindingsDelta(StringBuilder sb, ArchitectureDiff diff)
    {
        var newFindings = diff.Checks.NewFindings;
        var resolvedFindings = diff.Checks.ResolvedFindings;

        sb.AppendLine("## Findings delta");
        sb.AppendLine($"- **New findings**: {newFindings.Count}");
        sb.AppendLine($"- **Resolved findings**: {resolvedFindings.Count}");
        sb.AppendLine();

        if (newFindings.Count > 0)
        {
            sb.AppendLine($"### New findings ({newFindings.Count})");
            foreach (var finding in newFindings)
                sb.AppendLine(FindingLine(finding));
            sb.AppendLine();
        }

        if (resolvedFindings.Count > 0)
        {
            sb.AppendLine($"### Resolved findings ({resolvedFindings.Count})");
            foreach (var finding in resolvedFindings)
                sb.AppendLine(FindingLine(finding));
            sb.AppendLine();
        }
    }

    private static string FindingLine(JsonCheckFinding finding)
    {
        var tag = string.Equals(finding.Severity, "error", StringComparison.OrdinalIgnoreCase) ? "ERROR" : "WARN";
        var target = string.IsNullOrEmpty(finding.Target) ? "—" : finding.Target;
        var key = BaselineKeys.Create(finding.Code, finding.Target, finding.Location?.File);
        return $"- **[{tag}] `{finding.Code}`** target=`{target}` ({FormatLocation(finding.Location)}) · key `{key}`";
    }

    // ------------------------------------------------------------------------- item lists

    private static void AppendItemLists(StringBuilder sb, ArchitectureDiff diff, int limit)
    {
        var anyItems = AppendSection(sb, "Requests", limit, RequestLines(diff.Requests));
        anyItems |= AppendSection(sb, "Handlers", limit, HandlerLines(diff.Handlers));
        anyItems |= AppendSection(sb, "Validators", limit, ValidatorLines(diff.Validators));
        anyItems |= AppendSection(sb, "Pipeline behaviors", limit, BehaviorLines(diff.Behaviors));
        anyItems |= AppendSection(sb, "DbContexts", limit, DbContextLines(diff.DbContexts));
        anyItems |= AppendSection(sb, "Endpoints", limit, EndpointLines(diff.Endpoints));

        if (!anyItems)
            sb.AppendLine("_No architecture delta: 0 added, 0 removed, 0 modified, 0 new, 0 resolved._");
    }

    /// <summary>Appends one kind's item list (only when it has items). Returns whether it did.</summary>
    private static bool AppendSection(StringBuilder sb, string label, int limit, IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
            return false;

        sb.AppendLine($"## {label}");
        var page = limit > 0 ? lines.Take(limit).ToList() : lines;
        foreach (var line in page)
            sb.AppendLine(line);

        var hidden = lines.Count - page.Count;
        if (hidden > 0)
            sb.AppendLine($"_{hidden} more {label.ToLowerInvariant()} item(s) — raise `limit` (max {AgentReportLimits.MaxDiffLimit})._");

        sb.AppendLine();
        return true;
    }

    private static List<string> RequestLines(RequestDiffSection section)
    {
        var lines = new List<string>();
        foreach (var request in section.Added)
            lines.Add($"- + `{request.Name}` · [{request.Kind}] → {request.ResponseType ?? "void"} · project `{request.Project ?? "—"}` ({FormatLocation(request.Location)})");
        foreach (var request in section.Removed)
            lines.Add($"- - `{request.Name}` · [{request.Kind}] → {request.ResponseType ?? "void"} · project `{request.Project ?? "—"}` ({FormatLocation(request.Location)})");
        foreach (var modified in section.Modified)
            lines.Add($"- ~ `{modified.Name}` · project `{modified.Project ?? "—"}` — {DiffRequestChangeFormatter.Format(modified)}");
        return lines;
    }

    private static List<string> HandlerLines(HandlerDiffSection section)
    {
        var lines = new List<string>();
        foreach (var handler in section.Added)
            lines.Add($"- + `{handler.FullName}` (request `{handler.RequestName}`) ({FormatLocation(handler.Location)}) · project `{handler.Project ?? "—"}`");
        foreach (var handler in section.Removed)
            lines.Add($"- - `{handler.FullName}` (request `{handler.RequestName}`) ({FormatLocation(handler.Location)}) · project `{handler.Project ?? "—"}`");
        return lines;
    }

    private static List<string> ValidatorLines(ValidatorDiffSection section)
    {
        var lines = new List<string>();
        foreach (var validator in section.Added)
            lines.Add($"- + `{validator.FullName}` (target `{validator.TargetType}`) ({FormatLocation(validator.Location)})");
        foreach (var validator in section.Removed)
            lines.Add($"- - `{validator.FullName}` (target `{validator.TargetType}`) ({FormatLocation(validator.Location)})");
        return lines;
    }

    private static List<string> BehaviorLines(BehaviorDiffSection section)
    {
        var lines = new List<string>();
        foreach (var behavior in section.Added)
            lines.Add($"- + `{behavior.Name}` ({FormatLocation(behavior.Location)}) · project `{behavior.Project ?? "—"}`");
        foreach (var behavior in section.Removed)
            lines.Add($"- - `{behavior.Name}` ({FormatLocation(behavior.Location)}) · project `{behavior.Project ?? "—"}`");
        return lines;
    }

    private static List<string> DbContextLines(DbContextDiffSection section)
    {
        var lines = new List<string>();
        foreach (var context in section.Added)
            lines.Add($"- + `{context.Name}` ({FormatLocation(context.Location)})");
        foreach (var context in section.Removed)
            lines.Add($"- - `{context.Name}` ({FormatLocation(context.Location)})");
        foreach (var modified in section.Modified)
            lines.Add($"- ~ `{modified.Name}` — {DbContextChanges(modified)}");
        return lines;
    }

    private static string DbContextChanges(ModifiedDbContextDiff modified)
    {
        var parts = new List<string>();
        if (modified.AddedDbSets.Count > 0)
            parts.Add($"+DbSets: {string.Join(", ", modified.AddedDbSets)}");
        if (modified.RemovedDbSets.Count > 0)
            parts.Add($"-DbSets: {string.Join(", ", modified.RemovedDbSets)}");
        if (modified.AddedEntities.Count > 0)
            parts.Add($"+Entities: {string.Join(", ", modified.AddedEntities)}");
        if (modified.RemovedEntities.Count > 0)
            parts.Add($"-Entities: {string.Join(", ", modified.RemovedEntities)}");
        if (modified.AddedRelationships.Count > 0)
            parts.Add($"+Relationships: {string.Join(", ", modified.AddedRelationships)}");
        if (modified.RemovedRelationships.Count > 0)
            parts.Add($"-Relationships: {string.Join(", ", modified.RemovedRelationships)}");
        return parts.Count == 0 ? "(no detail)" : string.Join("; ", parts);
    }

    private static List<string> EndpointLines(EndpointDiffSection section)
    {
        var lines = new List<string>();
        foreach (var endpoint in section.Added)
            lines.Add($"- + `{endpoint.Name}` `{endpoint.HttpMethod} {endpoint.Route}` ({FormatLocation(endpoint.Location)})");
        foreach (var endpoint in section.Removed)
            lines.Add($"- - `{endpoint.Name}` `{endpoint.HttpMethod} {endpoint.Route}` ({FormatLocation(endpoint.Location)})");
        return lines;
    }

    private static string FormatLocation(JsonLocation? location)
        => location is null || string.IsNullOrEmpty(location.File)
            ? "—"
            : $"{location.File.Replace('\\', '/')}:{location.Line}";
}
