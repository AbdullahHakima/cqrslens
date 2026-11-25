namespace CqrsLens;

/// <summary>
/// Infers high-level system architecture styles deterministically from projects,
/// service profiles, entrypoints, integration flows, and CQRS/DDD shapes.
/// </summary>
internal static class ArchitectureStyleDetector
{
    private static readonly string[] ModuleTokens = ["Module", "Modules"];
    private static readonly string[] ServiceTierTokens = ["Service", "Services", "Business", "BLL", "Logic"];
    private static readonly string[] DataTierTokens = ["Data", "DAL", "Infrastructure", "Persistence", "Repository", "Repositories"];

    internal static void Detect(SystemAnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        DetectCore(context.Result, context);
    }

    internal static void Detect(AnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        DetectCore(result, context: null);
    }

    private static void DetectCore(AnalysisResult result, SystemAnalysisContext? context)
    {
        result.ArchitectureStyles.Clear();

        var nonTestProfiles = result.ServiceProfiles
            .Where(p => p.RuntimeRole != ServiceRuntimeRole.Tests)
            .ToList();
        var nonTestProjects = result.Projects
            .Where(p => !IsTestProject(p, result.ServiceProfiles))
            .ToList();

        var hostCount = nonTestProfiles.Count > 0
            ? nonTestProfiles.Count(p => p.RuntimeRole is ServiceRuntimeRole.Api or ServiceRuntimeRole.Worker)
            : result.SystemEntrypoints
                .Where(e => (e.Kind is SystemEntrypointKind.Http or SystemEntrypointKind.Grpc or SystemEntrypointKind.Worker) &&
                            !string.IsNullOrWhiteSpace(e.ProjectName))
                .Select(e => e.ProjectName!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

        var hasOrchestratorWithFlows =
            nonTestProfiles.Any(p => p.RuntimeRole == ServiceRuntimeRole.Orchestrator) &&
            result.IntegrationFlows.Count > 0;

        var isMicroservices = hostCount >= 2 || hasOrchestratorWithFlows;

        var isCqrs =
            result.Requests.Count > 0 ||
            result.Handlers.Count > 0 ||
            result.Behaviors.Count > 0 ||
            result.Conventions.Count > 0 ||
            nonTestProfiles.Any(p => HasTechnology(p, "MediatR") || HasTechnology(p, "Wolverine"));

        var hasDomainLayer = nonTestProjects.Any(p => string.Equals(p.Layer, "Domain", StringComparison.OrdinalIgnoreCase));
        var hasApplicationLayer = nonTestProjects.Any(p => string.Equals(p.Layer, "Application", StringComparison.OrdinalIgnoreCase));
        var hasInfrastructureLayer = nonTestProjects.Any(p => string.Equals(p.Layer, "Infrastructure", StringComparison.OrdinalIgnoreCase));
        var layerModelAsserted = result.HasConfiguredLayers || LayerModelConfidence.IsAsserted(result.Projects);
        var isCleanArchitecture = layerModelAsserted && hasDomainLayer && (hasApplicationLayer || hasInfrastructureLayer);

        var isModularMonolith = !isMicroservices && HasModularMonolithSignals(result, nonTestProjects);

        var isLayeredNTier = !isCqrs && !isCleanArchitecture && HasLayeredNTierSignals(result, nonTestProjects, nonTestProfiles);

        var isEventDriven =
            result.SystemEntrypoints.Any(e => e.Kind == SystemEntrypointKind.MessageConsumer) ||
            result.IntegrationFlows.Any(f => f.Kind == IntegrationFlowKind.IntegrationEvent) ||
            nonTestProfiles.Any(p =>
                p.MessageConsumerCount > 0 ||
                HasTechnology(p, "MassTransit") ||
                HasTechnology(p, "CAP") ||
                HasTechnology(p, "RabbitMQ") ||
                HasTechnology(p, "Kafka") ||
                HasTechnology(p, "Azure Service Bus"));

        var isMinimalApi =
            nonTestProfiles.Any(p => HasTechnology(p, "Minimal API")) ||
            result.SystemEntrypoints.Any(e =>
                e.Kind == SystemEntrypointKind.Http &&
                (e.Name.StartsWith("Map", StringComparison.Ordinal) || e.Name.Contains(".Map", StringComparison.Ordinal)));

        var isMvcControllers = HasMvcControllerSignals(result, context);

        var isGrpcServices =
            result.SystemEntrypoints.Any(e => e.Kind == SystemEntrypointKind.Grpc) ||
            nonTestProfiles.Any(p => p.GrpcEndpointCount > 0);

        var isBlazorOrRazor =
            result.SystemEntrypoints.Any(e => e.Kind is SystemEntrypointKind.Blazor or SystemEntrypointKind.RazorPage) ||
            nonTestProfiles.Any(p => HasTechnology(p, "Blazor") || HasTechnology(p, "Razor Pages"));

        var isWorkers =
            result.SystemEntrypoints.Any(e => e.Kind == SystemEntrypointKind.Worker) ||
            nonTestProfiles.Any(p => p.WorkerCount > 0 || p.RuntimeRole == ServiceRuntimeRole.Worker);

        if (isMicroservices) result.ArchitectureStyles.Add("Microservices / Distributed");
        if (isModularMonolith) result.ArchitectureStyles.Add("Modular Monolith");
        if (isCleanArchitecture) result.ArchitectureStyles.Add("Clean Architecture");
        if (isCqrs) result.ArchitectureStyles.Add("CQRS / Mediator");
        if (isLayeredNTier) result.ArchitectureStyles.Add("Layered / N-Tier");
        if (isEventDriven) result.ArchitectureStyles.Add("Event-Driven");
        if (isMinimalApi) result.ArchitectureStyles.Add("Minimal API");
        if (isMvcControllers) result.ArchitectureStyles.Add("MVC / Controllers");
        if (isGrpcServices) result.ArchitectureStyles.Add("gRPC Services");
        if (isBlazorOrRazor) result.ArchitectureStyles.Add("Blazor / Razor UI");
        if (isWorkers) result.ArchitectureStyles.Add("Background Workers");
    }

    private static bool IsTestProject(ProjectInfo project, IReadOnlyList<ServiceProfileInfo> profiles)
    {
        if (string.Equals(project.Layer, "Tests", StringComparison.OrdinalIgnoreCase))
            return true;

        var profile = profiles.FirstOrDefault(p =>
            string.Equals(p.ProjectName, project.Name, StringComparison.OrdinalIgnoreCase));
        return profile?.RuntimeRole == ServiceRuntimeRole.Tests;
    }

    private static bool HasModularMonolithSignals(AnalysisResult result, List<ProjectInfo> nonTestProjects)
    {
        var moduleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in nonTestProjects)
        {
            var segments = project.Name.Split(['.', '-', '_'], StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < segments.Length - 1; i++)
            {
                if (ModuleTokens.Contains(segments[i], StringComparer.OrdinalIgnoreCase))
                    moduleNames.Add(segments[i + 1]);
            }

            if (!string.IsNullOrWhiteSpace(result.RootPath) && !string.IsNullOrWhiteSpace(project.DirectoryPath))
            {
                var rel = Path.GetRelativePath(result.RootPath, project.DirectoryPath);
                var pathSegments = rel.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
                for (var i = 0; i < pathSegments.Length - 1; i++)
                {
                    if (ModuleTokens.Contains(pathSegments[i], StringComparer.OrdinalIgnoreCase))
                        moduleNames.Add(pathSegments[i + 1]);
                }
            }
        }

        if (moduleNames.Count >= 2)
            return true;

        var dbContextProjects = result.DbContexts
            .Where(c => !string.IsNullOrWhiteSpace(c.ProjectName) &&
                        nonTestProjects.Any(p => string.Equals(p.Name, c.ProjectName, StringComparison.OrdinalIgnoreCase)))
            .Select(c => c.ProjectName!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        if (dbContextProjects >= 2)
            return true;

        var moduleNamespaces = result.Requests.Select(r => r.Namespace)
            .Concat(result.DbContexts.Select(d => d.Namespace))
            .Where(ns => !string.IsNullOrWhiteSpace(ns))
            .Select(ExtractModuleFromNamespace)
            .Where(m => m is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return moduleNamespaces >= 2;
    }

    private static string? ExtractModuleFromNamespace(string ns)
    {
        var parts = ns.Split('.', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (ModuleTokens.Contains(parts[i], StringComparer.OrdinalIgnoreCase))
                return parts[i + 1];
        }
        return null;
    }

    private static bool HasLayeredNTierSignals(
        AnalysisResult result,
        List<ProjectInfo> nonTestProjects,
        List<ServiceProfileInfo> nonTestProfiles)
    {
        var distinctLayers = nonTestProjects
            .Select(p => p.Layer)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        if (distinctLayers >= 2)
            return true;

        var hasServiceTier = nonTestProjects.Any(p => HasAnyToken(p.Name, ServiceTierTokens));
        var hasDataTier = nonTestProjects.Any(p => HasAnyToken(p.Name, DataTierTokens)) ||
                          result.DbContexts.Count > 0;
        if (hasServiceTier && hasDataTier)
            return true;

        var hasHost = nonTestProfiles.Any(p => p.RuntimeRole is ServiceRuntimeRole.Api or ServiceRuntimeRole.Ui or ServiceRuntimeRole.Worker);
        var hasLibrary = nonTestProfiles.Any(p => p.RuntimeRole == ServiceRuntimeRole.Library);
        if (hasHost && hasLibrary)
            return true;

        return result.SystemEntrypoints.Any(e =>
            e.CalledServices.Count > 0 &&
            (e.TouchedDbContexts.Count > 0 || result.DbContexts.Count > 0));
    }

    private static bool HasMvcControllerSignals(AnalysisResult result, SystemAnalysisContext? context)
    {
        if (result.SystemEntrypoints.Any(e =>
                e.Kind == SystemEntrypointKind.Http &&
                (e.Name.Contains("Controller.", StringComparison.Ordinal) ||
                 e.Name.Contains("ControllerBase.", StringComparison.Ordinal))))
        {
            return true;
        }

        if (context is null)
            return false;

        foreach (var (file, scan, mask) in context.ParsedFiles)
        {
            if (context.IsTestProject(file.ProjectName))
                continue;

            foreach (var decl in scan.Classes)
            {
                if (decl.IsAbstract)
                    continue;

                if (decl.BaseRawNames.Any(b => b is "ControllerBase" or "Controller" or "ApiController" or "BaseController"))
                    return true;
            }

            if (ContainsCodeToken(file.Code, mask, ".MapControllers(") ||
                ContainsCodeToken(file.Code, mask, ".MapControllerRoute(") ||
                ContainsCodeToken(file.Code, mask, ".MapDefaultControllerRoute("))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsCodeToken(string code, SourceMask mask, string token)
    {
        var idx = 0;
        while (idx < code.Length)
        {
            var found = code.IndexOf(token, idx, StringComparison.Ordinal);
            if (found < 0)
                return false;
            if (mask.IsCode(found))
                return true;
            idx = found + token.Length;
        }
        return false;
    }

    private static bool HasAnyToken(string name, string[] tokens)
        => name.Split(['.', '-', '_'], StringSplitOptions.RemoveEmptyEntries)
               .Any(seg => tokens.Contains(seg, StringComparer.OrdinalIgnoreCase));

    private static bool HasTechnology(ServiceProfileInfo profile, string tech)
        => profile.Technologies.Contains(tech, StringComparer.OrdinalIgnoreCase);
}
