using System.Text;
using static CqrsLens.AgentReportFormatter;

namespace CqrsLens;

/// <summary>
/// Generates a crisp, token-efficient system architecture overview for the
/// <c>get_system_overview</c> MCP tool: architecture summary, service capability
/// inventory, cross-service integration flows, per-service entrypoints &amp; data
/// lineage, and uncovered risks / blind spots.
/// </summary>
public static class AgentSystemOverviewGenerator
{
    private const int MaxFlows = 50;
    private const int MaxEntrypointsPerServiceUnfiltered = 25;
    private const int MaxEntrypointsPerServiceFocused = 80;
    private const int MaxBlindSpots = 15;

    public static string Generate(
        AnalysisResult result,
        string? serviceFilter = null,
        bool includeTests = false)
    {
        ArgumentNullException.ThrowIfNull(result);

        var rootName = new DirectoryInfo(result.RootPath).Name;
        var sb = new StringBuilder();
        sb.AppendLine($"# CqrsLens System Overview — `{rootName}`");
        sb.AppendLine();

        var focused = SystemViewFilter.ResolveFocus(result, serviceFilter);
        if (!string.IsNullOrWhiteSpace(serviceFilter) && focused.Count == 0)
        {
            WriteServiceNotFound(sb, result, serviceFilter.Trim(), includeTests);
            return sb.ToString();
        }

        var excludeTests = !includeTests;
        var profiles = VisibleProfiles(result, focused, includeTests);
        var flows = SystemViewFilter.VisibleFlows(result, serviceFilter, excludeTests);
        var entrypoints = SystemViewFilter.VisibleEntrypoints(result, serviceFilter, excludeTests);

        WriteSummary(sb, result, focused, includeTests, profiles, flows, entrypoints);
        WriteServiceInventory(sb, result, focused, profiles);
        WriteIntegrationFlows(sb, flows);
        WriteEntrypointsAndLineage(sb, result, focused, entrypoints);
        WriteRisksAndBlindSpots(sb, result, focused, excludeTests, entrypoints);

        return sb.ToString();
    }

    private static List<ServiceProfileInfo> VisibleProfiles(
        AnalysisResult result,
        HashSet<string> focused,
        bool includeTests)
    {
        var neighborhood = focused.Count > 0 ? SystemViewFilter.FocusNeighborhood(result, focused) : null;
        return result.ServiceProfiles
            .Where(p => includeTests || p.RuntimeRole != ServiceRuntimeRole.Tests)
            .Where(p => neighborhood is null || neighborhood.Contains(p.ProjectName))
            .OrderByDescending(p => focused.Contains(p.ProjectName))
            .ThenBy(p => SystemViewFilter.RoleRank(p.RuntimeRole))
            .ThenBy(p => p.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void WriteServiceNotFound(
        StringBuilder sb,
        AnalysisResult result,
        string requested,
        bool includeTests)
    {
        sb.AppendLine($"No project or service matched `{requested}`.");
        sb.AppendLine();
        var available = SystemViewFilter.KnownProjectNames(result)
            .Where(n => includeTests || !SystemViewFilter.IsTestProject(result, n))
            .ToList();
        if (available.Count == 0)
            available = SystemViewFilter.KnownProjectNames(result).ToList();

        if (available.Count == 0)
        {
            sb.AppendLine("_No projects were discovered in this scan._");
            return;
        }

        sb.AppendLine($"Available projects/services ({available.Count}):");
        foreach (var name in available)
        {
            var profile = SystemViewFilter.FindProfile(result, name);
            var role = profile is null ? "" : $" [{SystemViewFilter.RoleLabel(profile.RuntimeRole)}]";
            sb.AppendLine($"- `{name}`{role}");
        }
    }

    private static void WriteSummary(
        StringBuilder sb,
        AnalysisResult result,
        HashSet<string> focused,
        bool includeTests,
        IReadOnlyList<ServiceProfileInfo> profiles,
        IReadOnlyList<IntegrationFlowEdge> flows,
        IReadOnlyList<SystemEntrypointInfo> entrypoints)
    {
        var totalProjects = result.Projects.Count;
        var testProjects = result.Projects.Count(p => SystemViewFilter.IsTestProject(result, p.Name));
        var prodProjects = totalProjects - testProjects;
        var activeChecks = result.BaselineApplied
            ? result.Checks.Where(c => !c.IsKnown).ToList()
            : result.Checks;
        var errors = activeChecks.Count(c => c.Severity == CheckSeverity.Error);
        var warnings = activeChecks.Count - errors;

        sb.AppendLine("## Architecture Summary");
        sb.AppendLine();
        if (focused.Count > 0)
        {
            var names = string.Join(", ", focused.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).Select(n => $"`{n}`"));
            sb.AppendLine($"- **Focus**: {names} (+ direct neighbors)");
        }
        sb.AppendLine($"- **Scanner**: {result.ScannerName} · **Files**: {result.FileCount}/{result.DiscoveredFileCount}" +
                      (result.Conventions.Count > 0 ? $" · **Conventions**: {string.Join(", ", result.Conventions)}" : ""));
        sb.AppendLine($"- **Projects**: {prodProjects} production, {testProjects} test" +
                      (includeTests ? " (tests included)" : " (tests excluded from inventory)"));
        sb.AppendLine($"- **Visible services**: {profiles.Count} · **Entrypoints**: {entrypoints.Count} " +
                      $"({FormatEntrypointBreakdown(entrypoints)}) · **Integration flows**: {flows.Count}");
        sb.AppendLine($"- **CQRS & Data**: {result.Requests.Count} request(s), {result.Handlers.Count} handler(s), " +
                      $"{result.Validators.Count} validator(s), {result.Behaviors.Count} behavior(s), " +
                      $"{result.DbContexts.Count} DbContext(s)");
        sb.AppendLine($"- **Checks**: {errors} error(s), {warnings} warning(s)");

        var tech = profiles
            .SelectMany(p => p.Technologies)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (tech.Count > 0)
            sb.AppendLine($"- **Detected Stack**: {string.Join(", ", tech)}");
        sb.AppendLine();
    }

    private static string FormatEntrypointBreakdown(IReadOnlyList<SystemEntrypointInfo> entrypoints)
    {
        if (entrypoints.Count == 0)
            return "none";
        var parts = new List<string>();
        var cqrs = entrypoints.Count(e => e.ExecutionStyle == SystemExecutionStyle.Cqrs);
        var direct = entrypoints.Count(e => e.ExecutionStyle == SystemExecutionStyle.Direct);
        var workers = entrypoints.Count(e => e.ExecutionStyle == SystemExecutionStyle.Worker);
        var consumers = entrypoints.Count(e => e.ExecutionStyle == SystemExecutionStyle.EventConsumer);
        if (cqrs > 0) parts.Add($"{cqrs} CQRS");
        if (direct > 0) parts.Add($"{direct} Direct");
        if (workers > 0) parts.Add($"{workers} Worker");
        if (consumers > 0) parts.Add($"{consumers} Consumer");
        return parts.Count > 0 ? string.Join(", ", parts) : $"{entrypoints.Count} total";
    }

    private static void WriteServiceInventory(
        StringBuilder sb,
        AnalysisResult result,
        HashSet<string> focused,
        IReadOnlyList<ServiceProfileInfo> profiles)
    {
        sb.AppendLine($"## Service Capability Inventory ({profiles.Count})");
        sb.AppendLine();
        if (profiles.Count == 0)
        {
            sb.AppendLine("_No matching service profiles discovered._");
            sb.AppendLine();
            return;
        }

        foreach (var profile in profiles)
        {
            var focusTag = focused.Contains(profile.ProjectName) ? " _(focused)_" : "";
            var (httpCqrs, httpDirect, uiRoutes) = SystemViewFilter.ProjectEndpointCounts(result, profile.ProjectName);
            var surface = new List<string>();
            if (httpCqrs > 0 || httpDirect > 0)
                surface.Add($"HTTP {httpCqrs} CQRS/{httpDirect} Direct");
            else if (profile.HttpEndpointCount > 0)
                surface.Add($"HTTP {profile.HttpEndpointCount}");
            if (uiRoutes > 0)
                surface.Add($"UI routes {uiRoutes}");
            if (profile.GrpcEndpointCount > 0)
                surface.Add($"gRPC {profile.GrpcEndpointCount}");
            if (profile.WorkerCount > 0)
                surface.Add($"Workers {profile.WorkerCount}");
            if (profile.MessageConsumerCount > 0)
                surface.Add($"Consumers {profile.MessageConsumerCount}");
            if (profile.RequestCount > 0)
                surface.Add($"Requests {profile.RequestCount}");
            if (profile.DbContextCount > 0)
                surface.Add($"DbContexts {profile.DbContextCount}");
            if (profile.PublishedEventCount > 0 || profile.ConsumedEventCount > 0)
                surface.Add($"Events {profile.PublishedEventCount} pub/{profile.ConsumedEventCount} sub");

            var techText = profile.Technologies.Count > 0
                ? string.Join(", ", profile.Technologies)
                : "—";
            var surfaceText = surface.Count > 0 ? string.Join(" · ", surface) : "support/shared";
            var entryText = profile.EntryFiles.Count > 0
                ? $" · Start here: {string.Join(", ", profile.EntryFiles.Select(f => $"`{f}`"))}"
                : "";

            sb.AppendLine($"- **`{profile.ProjectName}`**{focusTag} [{SystemViewFilter.RoleLabel(profile.RuntimeRole)}] " +
                          $"({profile.Layer}) — Stack: {techText} · {surfaceText}{entryText}");
        }
        sb.AppendLine();
    }

    private static void WriteIntegrationFlows(StringBuilder sb, IReadOnlyList<IntegrationFlowEdge> flows)
    {
        sb.AppendLine($"## Cross-Service Integration Flows ({flows.Count})");
        sb.AppendLine();
        if (flows.Count == 0)
        {
            sb.AppendLine("_No cross-service integration flows discovered._");
            sb.AppendLine();
            return;
        }

        foreach (var flow in flows.Take(MaxFlows))
        {
            var detail = !string.IsNullOrWhiteSpace(flow.MessageType) &&
                         !string.Equals(flow.MessageType, flow.Label, StringComparison.Ordinal)
                ? $"{flow.Label} ({flow.MessageType})"
                : flow.Label;
            sb.AppendLine($"- `{flow.FromProject}` → `{flow.ToProject}` " +
                          $"[{SystemViewFilter.FlowKindLabel(flow.Kind)}] `{detail}`{FormatLoc(flow.Location)}");
        }

        if (flows.Count > MaxFlows)
            sb.AppendLine($"- _+{flows.Count - MaxFlows} more integration flow(s)._");
        sb.AppendLine();
    }

    private static void WriteEntrypointsAndLineage(
        StringBuilder sb,
        AnalysisResult result,
        HashSet<string> focused,
        IReadOnlyList<SystemEntrypointInfo> entrypoints)
    {
        sb.AppendLine($"## Per-Service Entrypoints & Data Lineage ({entrypoints.Count})");
        sb.AppendLine();
        if (entrypoints.Count == 0)
        {
            sb.AppendLine("_No system entrypoints discovered._");
            sb.AppendLine();
            return;
        }

        var dbContextsByProject = result.DbContexts
            .Where(c => !string.IsNullOrWhiteSpace(c.ProjectName))
            .GroupBy(c => c.ProjectName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(c => c.Name).OrderBy(n => n, StringComparer.Ordinal).ToList(),
                StringComparer.OrdinalIgnoreCase);

        var maxPerGroup = focused.Count > 0 ? MaxEntrypointsPerServiceFocused : MaxEntrypointsPerServiceUnfiltered;
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

            var dbNote = dbContextsByProject.TryGetValue(group.Key, out var ownedContexts) && ownedContexts.Count > 0
                ? $" · Owns DbContext(s): {string.Join(", ", ownedContexts.Select(c => $"`{c}`"))}"
                : "";
            sb.AppendLine($"### `{group.Key}` ({ordered.Count}){dbNote}");
            sb.AppendLine();

            foreach (var entry in ordered.Take(maxPerGroup))
            {
                var inferred = entry.IsInferred ? " _(inferred)_" : "";
                var targets = entry.TargetRequests.Count > 0
                    ? $" → Requests: {JoinLimited(entry.TargetRequests, 3, r => $"`{MermaidDiagramText.ShortTypeName(r)}`")}"
                    : "";
                var downstreamParts = SystemViewFilter.DownstreamParts(entry, maxPerKind: 3);
                var downstream = downstreamParts.Count > 0
                    ? $" · {string.Join("; ", downstreamParts)}"
                    : "";
                sb.AppendLine($"- [{SystemViewFilter.KindLabel(entry.Kind)}/{SystemViewFilter.StyleLabel(entry.ExecutionStyle)}] " +
                              $"`{entry.DisplayLabel}` (`{entry.Name}`){inferred}{targets}{downstream}{FormatLoc(entry.Location)}");
            }

            if (ordered.Count > maxPerGroup)
                sb.AppendLine($"- _+{ordered.Count - maxPerGroup} more entrypoint(s) in `{group.Key}`._");
            sb.AppendLine();
        }
    }

    private static void WriteRisksAndBlindSpots(
        StringBuilder sb,
        AnalysisResult result,
        HashSet<string> focused,
        bool excludeTests,
        IReadOnlyList<SystemEntrypointInfo> entrypoints)
    {
        sb.AppendLine("## Uncovered Risks & Blind Spots");
        sb.AppendLine();

        var activeChecks = (result.BaselineApplied
                ? result.Checks.Where(c => !c.IsKnown)
                : result.Checks)
            .Where(c => !excludeTests || !SystemViewFilter.IsTestProject(result, c.ProjectName))
            .Where(c => focused.Count == 0 || (c.ProjectName is not null && focused.Contains(c.ProjectName)))
            .ToList();

        var unresolved = result.UnresolvedDispatches
            .Where(u => !excludeTests || !SystemViewFilter.IsTestProject(result, u.ProjectName))
            .Where(u => focused.Count == 0 || (u.ProjectName is not null && focused.Contains(u.ProjectName)))
            .ToList();

        var directCount = entrypoints.Count(e => e.ExecutionStyle == SystemExecutionStyle.Direct);
        var inferredCount = entrypoints.Count(e => e.IsInferred);
        var hasAnything = activeChecks.Count > 0 || unresolved.Count > 0 || directCount > 0 ||
                          inferredCount > 0 || result.Diagnostics.Count > 0 || result.SkippedFiles.Count > 0;

        if (!hasAnything)
        {
            sb.AppendLine("- No active check findings, unresolved dispatches, or scan blind spots.");
            return;
        }

        if (activeChecks.Count > 0)
        {
            var byRule = activeChecks
                .GroupBy(c => c.Code, StringComparer.Ordinal)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.Ordinal)
                .Take(MaxBlindSpots)
                .Select(g => $"`{g.Key}` ({g.Count()})");
            sb.AppendLine($"- **Architecture checks ({activeChecks.Count})**: {string.Join(", ", byRule)}");
        }

        if (unresolved.Count > 0)
        {
            var sample = JoinLimited(unresolved, 5, u =>
                $"`{u.ReceiverName}.{u.Method}`{FormatLoc(u.Location)}");
            sb.AppendLine($"- **Unresolved dispatches ({unresolved.Count})**: {sample}");
        }

        if (directCount > 0)
            sb.AppendLine($"- **Direct execution surface**: {directCount} entrypoint(s) execute services/DbContexts directly without MediatR CQRS.");

        if (inferredCount > 0)
            sb.AppendLine($"- **Inferred entrypoints**: {inferredCount} entrypoint(s) had routes/verbs inferred rather than read from literal attributes.");

        if (result.Diagnostics.Count > 0 || result.SkippedFiles.Count > 0)
            sb.AppendLine($"- **Scan coverage**: {result.Diagnostics.Count} diagnostic(s), {result.SkippedFiles.Count} skipped file(s).");
    }
}
