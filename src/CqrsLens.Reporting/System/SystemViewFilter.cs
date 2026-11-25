namespace CqrsLens;

/// <summary>
/// Shared focus (<c>--focus</c>) and test-exclusion (<c>--exclude-tests</c>) rules for the
/// system overview, the C4 landscape, the project/layer graphs and the agent system overview.
/// </summary>
internal static class SystemViewFilter
{
    /// <summary>Whether the analysis carries any system-level (Wave 1) data.</summary>
    internal static bool HasSystemData(AnalysisResult result)
        => result.ServiceProfiles.Count > 0 ||
           result.SystemEntrypoints.Count > 0 ||
           result.IntegrationFlows.Count > 0;

    /// <summary>Whether a project is a test project (runtime role or layer).</summary>
    internal static bool IsTestProject(AnalysisResult result, string? projectName)
    {
        if (string.IsNullOrWhiteSpace(projectName))
            return false;

        var profile = FindProfile(result, projectName);
        if (profile is not null)
            return profile.RuntimeRole == ServiceRuntimeRole.Tests;

        var project = result.Projects.FirstOrDefault(p =>
            string.Equals(p.Name, projectName, StringComparison.OrdinalIgnoreCase));
        return project is not null &&
               string.Equals(project.Layer, "Tests", StringComparison.OrdinalIgnoreCase);
    }

    internal static ServiceProfileInfo? FindProfile(AnalysisResult result, string? projectName)
        => string.IsNullOrWhiteSpace(projectName)
            ? null
            : result.ServiceProfiles.FirstOrDefault(p =>
                string.Equals(p.ProjectName, projectName, StringComparison.OrdinalIgnoreCase));

    /// <summary>All known project/service names (projects, profiles and entrypoint owners), sorted.</summary>
    internal static IReadOnlyList<string> KnownProjectNames(AnalysisResult result)
        => result.Projects.Select(p => p.Name)
            .Concat(result.ServiceProfiles.Select(p => p.ProjectName))
            .Concat(result.SystemEntrypoints
                .Where(e => !string.IsNullOrWhiteSpace(e.ProjectName))
                .Select(e => e.ProjectName!))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Resolves a focus filter to project names: an exact case-insensitive match wins;
    /// otherwise every project whose name contains the filter. Empty when nothing matches.
    /// </summary>
    internal static HashSet<string> ResolveFocus(AnalysisResult result, string? focus)
    {
        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(focus))
            return matched;

        var needle = focus.Trim();
        var names = KnownProjectNames(result);
        foreach (var name in names)
        {
            if (string.Equals(name, needle, StringComparison.OrdinalIgnoreCase))
                matched.Add(name);
        }

        if (matched.Count > 0)
            return matched;

        foreach (var name in names)
        {
            if (name.Contains(needle, StringComparison.OrdinalIgnoreCase))
                matched.Add(name);
        }

        return matched;
    }

    /// <summary>
    /// The focused projects plus their direct neighbors: project references in both
    /// directions and integration flows in both directions.
    /// </summary>
    internal static HashSet<string> FocusNeighborhood(AnalysisResult result, IReadOnlySet<string> focused)
    {
        var set = new HashSet<string>(focused, StringComparer.OrdinalIgnoreCase);
        foreach (var project in result.Projects)
        {
            if (focused.Contains(project.Name))
            {
                foreach (var reference in project.ReferenceNames)
                    set.Add(reference);
            }
            else if (project.ReferenceNames.Any(focused.Contains))
            {
                set.Add(project.Name);
            }
        }

        foreach (var flow in result.IntegrationFlows)
        {
            if (focused.Contains(flow.FromProject))
                set.Add(flow.ToProject);
            if (focused.Contains(flow.ToProject))
                set.Add(flow.FromProject);
        }

        return set;
    }

    /// <summary>
    /// Returns the projects the project/layer graphs should draw, or <see langword="null"/>
    /// when no filtering applies (so callers keep the unfiltered, byte-identical output).
    /// A focus that matches nothing is ignored.
    /// </summary>
    internal static IReadOnlyList<ProjectInfo>? FilterProjects(
        AnalysisResult result,
        bool excludeTests,
        string? focusService)
    {
        var focused = ResolveFocus(result, focusService);
        if (!excludeTests && focused.Count == 0)
            return null;

        var neighborhood = focused.Count > 0 ? FocusNeighborhood(result, focused) : null;
        return result.Projects
            .Where(p => !excludeTests || !IsTestProject(result, p.Name))
            .Where(p => neighborhood is null || neighborhood.Contains(p.Name))
            .ToList();
    }

    /// <summary>A one-line description of the active filters, or <see langword="null"/> when none.</summary>
    internal static string? DescribeFilters(AnalysisResult result, bool excludeTests, string? focusService)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(focusService))
        {
            var focused = ResolveFocus(result, focusService);
            parts.Add(focused.Count == 0
                ? $"focus \"{focusService.Trim()}\" matched no project, so the whole system is shown"
                : $"focused on {string.Join(", ", focused.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))} and direct neighbors");
        }

        if (excludeTests)
            parts.Add("test projects excluded");
        return parts.Count == 0 ? null : string.Join("; ", parts);
    }

    internal static string RoleLabel(ServiceRuntimeRole role) => role switch
    {
        ServiceRuntimeRole.Api => "API",
        ServiceRuntimeRole.Worker => "Worker",
        ServiceRuntimeRole.Ui => "UI",
        ServiceRuntimeRole.Orchestrator => "Orchestrator",
        ServiceRuntimeRole.Tests => "Tests",
        _ => "Library",
    };

    internal static string RoleCssClass(ServiceRuntimeRole role) => role switch
    {
        ServiceRuntimeRole.Api => "role-api",
        ServiceRuntimeRole.Worker => "role-worker",
        ServiceRuntimeRole.Ui => "role-ui",
        ServiceRuntimeRole.Orchestrator => "role-orchestrator",
        ServiceRuntimeRole.Tests => "role-tests",
        _ => "role-library",
    };

    internal static int RoleRank(ServiceRuntimeRole role) => role switch
    {
        ServiceRuntimeRole.Ui => 0,
        ServiceRuntimeRole.Api => 1,
        ServiceRuntimeRole.Worker => 2,
        ServiceRuntimeRole.Orchestrator => 3,
        ServiceRuntimeRole.Library => 4,
        _ => 5,
    };

    internal static bool HasCapabilities(ServiceProfileInfo profile)
        => profile.HttpEndpointCount > 0 || profile.GrpcEndpointCount > 0 ||
           profile.WorkerCount > 0 || profile.MessageConsumerCount > 0 ||
           profile.RequestCount > 0 || profile.DbContextCount > 0 ||
           profile.PublishedEventCount > 0 || profile.ConsumedEventCount > 0;

    /// <summary>
    /// The service profiles shown in capability tables/cards: never test projects; pure
    /// libraries only when they expose a capability. Honors the focus neighborhood.
    /// </summary>
    internal static List<ServiceProfileInfo> VisibleProfiles(
        AnalysisResult result,
        string? focusService,
        bool includeTests = false)
    {
        var focused = ResolveFocus(result, focusService);
        var neighborhood = focused.Count > 0 ? FocusNeighborhood(result, focused) : null;
        return result.ServiceProfiles
            .Where(p => includeTests || p.RuntimeRole != ServiceRuntimeRole.Tests)
            .Where(p => p.RuntimeRole != ServiceRuntimeRole.Library || HasCapabilities(p) ||
                        result.SystemEntrypoints.Any(e => SameProject(e.ProjectName, p.ProjectName)))
            .Where(p => neighborhood is null || neighborhood.Contains(p.ProjectName))
            .OrderBy(p => RoleRank(p.RuntimeRole))
            .ThenBy(p => p.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>System entrypoints honoring focus (focused projects only) and test exclusion.</summary>
    internal static List<SystemEntrypointInfo> VisibleEntrypoints(
        AnalysisResult result,
        string? focusService,
        bool excludeTests)
    {
        var focused = ResolveFocus(result, focusService);
        return result.SystemEntrypoints
            .Where(e => !excludeTests || !IsTestProject(result, e.ProjectName))
            .Where(e => focused.Count == 0 || (e.ProjectName is not null && focused.Contains(e.ProjectName)))
            .ToList();
    }

    /// <summary>Integration flows honoring focus (either end focused) and test exclusion.</summary>
    internal static List<IntegrationFlowEdge> VisibleFlows(
        AnalysisResult result,
        string? focusService,
        bool excludeTests)
    {
        var focused = ResolveFocus(result, focusService);
        return result.IntegrationFlows
            .Where(f => !excludeTests ||
                        (!IsTestProject(result, f.FromProject) && !IsTestProject(result, f.ToProject)))
            .Where(f => focused.Count == 0 || focused.Contains(f.FromProject) || focused.Contains(f.ToProject))
            .OrderBy(f => f.FromProject, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.ToProject, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.Kind)
            .ThenBy(f => f.Label, StringComparer.Ordinal)
            .ToList();
    }

    internal static string FlowKindLabel(IntegrationFlowKind kind) => kind switch
    {
        IntegrationFlowKind.IntegrationEvent => "Integration event",
        IntegrationFlowKind.AspireReference => "Aspire reference",
        IntegrationFlowKind.HttpClient => "HTTP client",
        IntegrationFlowKind.GrpcClient => "gRPC client",
        IntegrationFlowKind.SharedDbContext => "Shared DbContext",
        _ => kind.ToString(),
    };

    internal static string StyleLabel(SystemExecutionStyle style) => style switch
    {
        SystemExecutionStyle.Cqrs => "CQRS",
        SystemExecutionStyle.Direct => "Direct",
        SystemExecutionStyle.Worker => "Worker",
        SystemExecutionStyle.EventConsumer => "Event consumer",
        _ => style.ToString(),
    };

    internal static string KindLabel(SystemEntrypointKind kind) => kind switch
    {
        SystemEntrypointKind.Http => "HTTP",
        SystemEntrypointKind.Grpc => "gRPC",
        SystemEntrypointKind.Blazor => "Blazor",
        SystemEntrypointKind.RazorPage => "Razor Page",
        SystemEntrypointKind.Worker => "Worker",
        SystemEntrypointKind.MessageConsumer => "Consumer",
        _ => kind.ToString(),
    };

    /// <summary>CQRS / Direct HTTP-style endpoint counts (HTTP, Blazor, Razor Pages) for one project.</summary>
    internal static (int Cqrs, int Direct) EndpointStyleCounts(AnalysisResult result, string projectName)
    {
        var (httpCqrs, httpDirect, uiRoutes) = ProjectEndpointCounts(result, projectName);
        return (httpCqrs, httpDirect + uiRoutes);
    }

    /// <summary>Per-project HTTP (split by CQRS vs Direct) and UI (Blazor/RazorPage) route counts.</summary>
    internal static (int HttpCqrs, int HttpDirect, int UiRoutes) ProjectEndpointCounts(
        AnalysisResult result,
        string projectName)
    {
        var httpCqrs = 0;
        var httpDirect = 0;
        var uiRoutes = 0;
        foreach (var entrypoint in result.SystemEntrypoints)
        {
            if (!SameProject(entrypoint.ProjectName, projectName))
                continue;
            if (entrypoint.Kind == SystemEntrypointKind.Http)
            {
                if (entrypoint.ExecutionStyle == SystemExecutionStyle.Cqrs)
                    httpCqrs++;
                else
                    httpDirect++;
            }
            else if (entrypoint.Kind is SystemEntrypointKind.Blazor or SystemEntrypointKind.RazorPage)
            {
                uiRoutes++;
            }
        }

        return (httpCqrs, httpDirect, uiRoutes);
    }

    internal static bool SameProject(string? left, string? right)
        => !string.IsNullOrEmpty(left) && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    /// <summary>The traced downstream of one entrypoint as compact "kind: a, b" parts.</summary>
    internal static List<string> DownstreamParts(SystemEntrypointInfo entrypoint, int maxPerKind = 4)
    {
        var parts = new List<string>();
        AddPart(parts, "services", entrypoint.CalledServices, maxPerKind);
        AddPart(parts, "db", entrypoint.TouchedDbContexts, maxPerKind);
        AddPart(parts, "entities", entrypoint.TouchedEntities, maxPerKind);
        AddPart(parts, "publishes", entrypoint.PublishedMessages, maxPerKind);
        return parts;
    }

    internal static string JoinShort(IEnumerable<string> values, int max)
    {
        var list = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => MermaidDiagramText.ShortTypeName(v))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (list.Count <= max)
            return string.Join(", ", list);
        return string.Join(", ", list.Take(max)) + $" +{list.Count - max} more";
    }

    private static void AddPart(List<string> parts, string label, IReadOnlyCollection<string> values, int max)
    {
        if (values.Count == 0)
            return;
        parts.Add($"{label}: {JoinShort(values, max)}");
    }
}
