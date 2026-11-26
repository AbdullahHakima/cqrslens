using System.Text;

namespace CqrsLens;

/// <summary>
/// Appends the <c>## System at a Glance</c> Markdown section: service capability table,
/// C4 System Landscape Mermaid diagram, cross-service integration flows, and the full
/// system entrypoints catalog grouped by project.
/// </summary>
internal static class SystemOverviewMarkdownWriter
{
    internal static void Write(
        StringBuilder md,
        AnalysisResult result,
        string? focusService = null,
        bool excludeTests = false)
    {
        if (!SystemViewFilter.HasSystemData(result))
            return;

        md.AppendLine("## System at a Glance");
        md.AppendLine();

        var filterNote = SystemViewFilter.DescribeFilters(result, excludeTests, focusService);
        if (filterNote is not null)
        {
            md.AppendLine($"_Filtered view: {MarkdownEscaping.MarkdownText(filterNote)}._");
            md.AppendLine();
        }

        var profiles = SystemViewFilter.VisibleProfiles(result, focusService, includeTests: false);
        WriteServicesTable(md, result, profiles);

        var landscape = DiagramBuilder.SystemLandscape(result, focusService);
        if (landscape is not null)
        {
            md.AppendLine("### System Landscape");
            md.AppendLine();
            MarkdownDiagramWriter.AppendDiagramOrLimitNote(md, landscape, "system landscape");
            md.AppendLine();
            md.AppendLine("**Legend:** purple = UI · blue = API · amber = Worker · violet = Orchestrator · " +
                          "green = Data store · gold stadium = Message broker · red = Focused service.");
            md.AppendLine();
        }

        var flows = SystemViewFilter.VisibleFlows(result, focusService, excludeTests);
        if (flows.Count > 0)
            WriteIntegrationFlows(md, flows);

        var dataStores = SystemViewFilter.VisibleNonEfDataStores(result, focusService, excludeTests);
        if (dataStores.Count > 0)
            WriteDataStores(md, dataStores);

        var entrypoints = SystemViewFilter.VisibleEntrypoints(result, focusService, excludeTests);
        if (entrypoints.Count > 0)
            WriteEntrypointsCatalog(md, entrypoints);
    }

    private static void WriteServicesTable(
        StringBuilder md,
        AnalysisResult result,
        IReadOnlyList<ServiceProfileInfo> profiles)
    {
        if (profiles.Count == 0)
            return;

        md.AppendLine("### Services & Capabilities");
        md.AppendLine();
        md.AppendLine("| Service | Role | Tech Stack | Endpoints (CQRS / Direct / gRPC) | Workers & Consumers | Data / Events | Start Here |");
        md.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");
        foreach (var profile in profiles)
        {
            var tech = profile.Technologies.Count > 0
                ? MarkdownEscaping.TableCell(string.Join(", ", profile.Technologies))
                : "—";
            var (cqrs, direct) = SystemViewFilter.EndpointStyleCounts(result, profile.ProjectName);
            var endpoints = cqrs == 0 && direct == 0 && profile.GrpcEndpointCount == 0
                ? "—"
                : $"{cqrs} / {direct} / {profile.GrpcEndpointCount}";
            var workers = profile.WorkerCount == 0 && profile.MessageConsumerCount == 0
                ? "—"
                : $"{profile.WorkerCount}w / {profile.MessageConsumerCount}c";
            var dataParts = new List<string>();
            if (profile.DbContextCount > 0)
                dataParts.Add($"{profile.DbContextCount} DbCtx");
            var nonEfStoreCount = Math.Max(0, profile.DataStoreCount - profile.DbContextCount);
            if (nonEfStoreCount > 0)
                dataParts.Add($"{nonEfStoreCount} Store{(nonEfStoreCount == 1 ? "" : "s")}");
            if (profile.PublishedEventCount > 0)
                dataParts.Add($"{profile.PublishedEventCount} pub");
            if (profile.ConsumedEventCount > 0)
                dataParts.Add($"{profile.ConsumedEventCount} sub");
            var dataEvents = dataParts.Count > 0 ? string.Join(", ", dataParts) : "—";
            var startHere = profile.EntryFiles.Count > 0
                ? string.Join(", ", profile.EntryFiles.Select(TableCode))
                : "—";

            md.AppendLine($"| {TableCode(profile.ProjectName)} | " +
                          $"{MarkdownEscaping.TableCell(SystemViewFilter.RoleLabel(profile.RuntimeRole))} | " +
                          $"{tech} | {endpoints} | {workers} | {dataEvents} | {startHere} |");
        }
        md.AppendLine();
    }

    private static void WriteIntegrationFlows(
        StringBuilder md,
        IReadOnlyList<IntegrationFlowEdge> flows)
    {
        md.AppendLine("### Cross-Service Integration Flows");
        md.AppendLine();
        md.AppendLine("| From | To | Kind | Details | Source |");
        md.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var flow in flows)
        {
            var detail = !string.IsNullOrWhiteSpace(flow.MessageType) &&
                         !string.Equals(flow.MessageType, flow.Label, StringComparison.Ordinal)
                ? $"{flow.Label} ({flow.MessageType})"
                : flow.Label;
            var source = flow.Location is null
                ? "—"
                : TableCode($"{flow.Location.File}:{flow.Location.Line}");
            md.AppendLine($"| {TableCode(flow.FromProject)} | {TableCode(flow.ToProject)} | " +
                          $"{MarkdownEscaping.TableCell(SystemViewFilter.FlowKindLabel(flow.Kind))} | " +
                          $"{MarkdownEscaping.TableCell(detail)} | {source} |");
        }
        md.AppendLine();
    }

    private static void WriteDataStores(
        StringBuilder md,
        IReadOnlyList<DataStoreInfo> stores)
    {
        md.AppendLine("### Data Stores & Persistence");
        md.AppendLine();
        md.AppendLine("| Store | Technology | Provider | Project | Entities / Tables | Accessed By |");
        md.AppendLine("| --- | --- | --- | --- | --- | --- |");
        foreach (var store in stores)
        {
            var provider = string.IsNullOrWhiteSpace(store.Provider) ? "—" : MarkdownEscaping.TableCell(store.Provider);
            var project = string.IsNullOrWhiteSpace(store.ProjectName) ? "—" : TableCode(store.ProjectName);
            var entities = store.Entities.Count > 0
                ? MarkdownEscaping.TableCell(FormatLimited(store.Entities, 10))
                : "—";
            var accessed = store.AccessedBy.Count > 0
                ? MarkdownEscaping.TableCell(FormatLimited(store.AccessedBy.Select(CSharpScanner.SimpleName), 5))
                : "—";
            md.AppendLine($"| {TableCode(store.Name)} | {MarkdownEscaping.TableCell(store.Technology)} | {provider} | {project} | {entities} | {accessed} |");
        }
        md.AppendLine();
    }

    private static string FormatLimited(IEnumerable<string> values, int limit)
    {
        var items = values.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.Ordinal).ToList();
        if (items.Count <= limit)
            return string.Join(", ", items);
        return string.Join(", ", items.Take(limit)) + $" +{items.Count - limit} more";
    }

    private static void WriteEntrypointsCatalog(
        StringBuilder md,
        IReadOnlyList<SystemEntrypointInfo> entrypoints)
    {
        md.AppendLine("### System Entrypoints Catalog");
        md.AppendLine();

        var groups = entrypoints
            .GroupBy(e => string.IsNullOrWhiteSpace(e.ProjectName) ? MermaidDiagramText.UnassignedProject : e.ProjectName!)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var ordered = group
                .OrderBy(e => e.Kind)
                .ThenBy(e => e.RouteOrTrigger, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Method, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            md.AppendLine($"#### {MarkdownEscaping.CodeSpan(group.Key)} ({ordered.Count})");
            md.AppendLine();
            md.AppendLine("| Method | Route / Trigger | Entrypoint | Style | Target Requests | Services & Data |");
            md.AppendLine("| --- | --- | --- | --- | --- | --- |");
            foreach (var entry in ordered)
            {
                var route = string.IsNullOrWhiteSpace(entry.RouteOrTrigger)
                    ? "—"
                    : TableCode(entry.RouteOrTrigger);
                var name = entry.IsInferred ? $"{entry.Name} (inferred)" : entry.Name;
                var targets = entry.TargetRequests.Count > 0
                    ? MarkdownEscaping.TableCell(SystemViewFilter.JoinShort(entry.TargetRequests, 4))
                    : "—";
                var downstreamParts = SystemViewFilter.DownstreamParts(entry);
                var downstream = downstreamParts.Count > 0
                    ? MarkdownEscaping.TableCell(string.Join(" · ", downstreamParts))
                    : "—";

                md.AppendLine($"| {MarkdownEscaping.TableCell(entry.Method)} | {route} | " +
                              $"{MarkdownEscaping.TableCell(name)} | " +
                              $"{MarkdownEscaping.TableCell(SystemViewFilter.StyleLabel(entry.ExecutionStyle))} | " +
                              $"{targets} | {downstream} |");
            }
            md.AppendLine();
        }
    }

    private static string TableCode(string? value)
        => MarkdownEscaping.CodeSpan(value).Replace("|", "\\|", StringComparison.Ordinal);
}
