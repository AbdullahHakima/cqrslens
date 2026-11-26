namespace CqrsLens;

/// <summary>
/// Builds a <see cref="ServiceProfileInfo"/> for every scanned project, classifying its
/// runtime role, detecting technologies, selecting up to 5 "Start Here" entry files, and
/// computing exact capability counts.
/// </summary>
internal static class ServiceProfileBuilder
{
    private const int MaxEntryFiles = 5;

    private static readonly string[] UiNameTokens =
    {
        "WebApp", "HybridApp", "ClientApp", "Blazor", "WinForms", "Wpf", "Maui", "Mobile", "Desktop",
    };

    private static readonly string[] UiPackages =
    {
        "Microsoft.AspNetCore.Components.WebAssembly",
        "Microsoft.Maui",
        "MudBlazor",
        "Radzen.Blazor",
        "Blazorise",
        "Avalonia",
        "Uno.UI",
        "Uno.WinUI",
    };

    public static void Build(
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        AnalysisResult result,
        SourcePathFilter? pathFilter = null)
        => Build(SystemAnalysisContext.Create(parsedFiles, result, pathFilter: pathFilter));

    internal static void Build(SystemAnalysisContext context)
    {
        var result = context.Result;
        if (result.Projects.Count == 0)
            return;

        var testProjectMap = TestProjectDiscovery.DiscoverTestProjects(result)
            .ToDictionary(t => t.Name, t => t, StringComparer.OrdinalIgnoreCase);

        var filesByProject = new Dictionary<string, List<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var parsed in context.ParsedFiles)
        {
            if (string.IsNullOrWhiteSpace(parsed.File.ProjectName))
                continue;
            if (!filesByProject.TryGetValue(parsed.File.ProjectName, out var list))
                filesByProject[parsed.File.ProjectName] = list = new List<(SourceFile, CSharpScanner.FileScan, SourceMask)>();
            list.Add(parsed);
        }

        var entrypointsByProject = result.SystemEntrypoints
            .Where(e => !string.IsNullOrWhiteSpace(e.ProjectName))
            .GroupBy(e => e.ProjectName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var requestsByProject = result.Requests
            .Where(r => !string.IsNullOrWhiteSpace(r.ProjectName))
            .GroupBy(r => r.ProjectName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var dbContextsByProject = result.DbContexts
            .Where(c => !string.IsNullOrWhiteSpace(c.ProjectName))
            .GroupBy(c => c.ProjectName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var handlersByProject = result.Handlers
            .Where(h => !string.IsNullOrWhiteSpace(h.ProjectName))
            .GroupBy(h => h.ProjectName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var notifHandlersByProject = result.NotificationHandlers
            .Where(nh => !string.IsNullOrWhiteSpace(nh.ProjectName))
            .GroupBy(nh => nh.ProjectName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var validatorsByProject = result.Validators
            .Where(v => !string.IsNullOrWhiteSpace(v.ProjectName))
            .GroupBy(v => v.ProjectName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var publishedByProject = CollectPublishedEventsByProject(context);

        var profiles = new List<ServiceProfileInfo>(result.Projects.Count);

        foreach (var project in result.Projects.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            filesByProject.TryGetValue(project.Name, out var projFiles);
            projFiles ??= new List<(SourceFile, CSharpScanner.FileScan, SourceMask)>();

            entrypointsByProject.TryGetValue(project.Name, out var projEntrypoints);
            projEntrypoints ??= new List<SystemEntrypointInfo>();

            requestsByProject.TryGetValue(project.Name, out var projRequests);
            projRequests ??= new List<CqrsRequest>();

            dbContextsByProject.TryGetValue(project.Name, out var projDbContexts);
            projDbContexts ??= new List<DbContextInfo>();

            handlersByProject.TryGetValue(project.Name, out var projHandlers);
            projHandlers ??= new List<HandlerInfo>();

            notifHandlersByProject.TryGetValue(project.Name, out var projNotifHandlers);
            projNotifHandlers ??= new List<NotificationHandlerInfo>();

            validatorsByProject.TryGetValue(project.Name, out var projValidators);
            projValidators ??= new List<ValidatorInfo>();

            testProjectMap.TryGetValue(project.Name, out var testProjInfo);

            var httpCount = projEntrypoints.Count(e => e.Kind == SystemEntrypointKind.Http);
            var grpcCount = projEntrypoints.Count(e => e.Kind == SystemEntrypointKind.Grpc);
            var workerCount = projEntrypoints.Count(e => e.Kind == SystemEntrypointKind.Worker);
            var consumerCount = projEntrypoints.Count(e => e.Kind == SystemEntrypointKind.MessageConsumer);
            var blazorCount = projEntrypoints.Count(e => e.Kind == SystemEntrypointKind.Blazor);
            var razorPageCount = projEntrypoints.Count(e => e.Kind == SystemEntrypointKind.RazorPage);

            var role = ClassifyRole(
                context, project, testProjInfo, projFiles,
                httpCount, grpcCount, workerCount, consumerCount, blazorCount, razorPageCount);

            var technologies = DetectTechnologies(
                context, project, testProjInfo, projFiles, projEntrypoints,
                projRequests, projDbContexts, projValidators,
                httpCount, grpcCount, blazorCount, razorPageCount);

            var entryFiles = SelectEntryFiles(
                result.RootPath, projFiles, projEntrypoints, projDbContexts, projRequests, projHandlers);

            var publishedCount = publishedByProject.TryGetValue(project.Name, out var pubSet)
                ? pubSet.Count
                : 0;

            var consumedSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (var ep in projEntrypoints)
            {
                if (ep.Kind == SystemEntrypointKind.MessageConsumer && !string.IsNullOrWhiteSpace(ep.RouteOrTrigger))
                    consumedSet.Add(ep.RouteOrTrigger);
            }
            foreach (var nh in projNotifHandlers)
            {
                if (!string.IsNullOrWhiteSpace(nh.NotificationName))
                    consumedSet.Add(nh.NotificationName);
            }

            var profile = new ServiceProfileInfo
            {
                ProjectName = project.Name,
                Layer = project.Layer,
                RuntimeRole = role,
                HttpEndpointCount = httpCount,
                GrpcEndpointCount = grpcCount,
                WorkerCount = workerCount,
                MessageConsumerCount = consumerCount,
                RequestCount = projRequests.Count,
                DbContextCount = projDbContexts.Count,
                DataStoreCount = result.DataStores.Count(d =>
                    string.Equals(d.ProjectName, project.Name, StringComparison.OrdinalIgnoreCase)),
                PublishedEventCount = publishedCount,
                ConsumedEventCount = consumedSet.Count,
            };
            profile.Technologies.AddRange(technologies);
            profile.EntryFiles.AddRange(entryFiles);
            profiles.Add(profile);
        }

        result.ServiceProfiles.AddRange(profiles);
    }

    private static ServiceRuntimeRole ClassifyRole(
        SystemAnalysisContext context,
        ProjectInfo project,
        TestProjectInfo? testProjInfo,
        List<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> projFiles,
        int httpCount,
        int grpcCount,
        int workerCount,
        int consumerCount,
        int blazorCount,
        int razorPageCount)
    {
        if (testProjInfo is not null || context.IsTestProject(project.Name))
            return ServiceRuntimeRole.Tests;

        var isOrchestrator =
            (!string.IsNullOrEmpty(project.Sdk) && project.Sdk.Contains("Aspire", StringComparison.OrdinalIgnoreCase)) ||
            HasPackagePrefix(project, "Aspire.Hosting") ||
            AnyCodeToken(projFiles, "DistributedApplication.CreateBuilder");
        if (isOrchestrator)
            return ServiceRuntimeRole.Orchestrator;

        var hasUiSdk =
            (!string.IsNullOrEmpty(project.Sdk) &&
             (project.Sdk.Contains("BlazorWebAssembly", StringComparison.OrdinalIgnoreCase) ||
              project.Sdk.Contains("WindowsDesktop", StringComparison.OrdinalIgnoreCase))) ||
            string.Equals(project.OutputType, "WinExe", StringComparison.OrdinalIgnoreCase) ||
            UiPackages.Any(pkg => HasPackagePrefix(project, pkg));

        var hasUiEntrypoints = blazorCount > 0 || razorPageCount > 0;
        var hasUiName = project.Name
            .Split(['.', '-', '_'], StringSplitOptions.RemoveEmptyEntries)
            .Any(seg => UiNameTokens.Contains(seg, StringComparer.OrdinalIgnoreCase));

        if (hasUiSdk ||
            (hasUiEntrypoints && httpCount == 0 && grpcCount == 0) ||
            (hasUiEntrypoints && hasUiName))
        {
            return ServiceRuntimeRole.Ui;
        }

        if (string.Equals(project.Sdk, "Microsoft.NET.Sdk.Worker", StringComparison.OrdinalIgnoreCase) ||
            ((workerCount > 0 || consumerCount > 0) && httpCount == 0 && grpcCount == 0))
        {
            return ServiceRuntimeRole.Worker;
        }

        if (string.Equals(project.Sdk, "Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase) ||
            httpCount > 0 ||
            grpcCount > 0)
        {
            return ServiceRuntimeRole.Api;
        }

        return ServiceRuntimeRole.Library;
    }

    private static List<string> DetectTechnologies(
        SystemAnalysisContext context,
        ProjectInfo project,
        TestProjectInfo? testProjInfo,
        List<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> projFiles,
        List<SystemEntrypointInfo> projEntrypoints,
        List<CqrsRequest> projRequests,
        List<DbContextInfo> projDbContexts,
        List<ValidatorInfo> projValidators,
        int httpCount,
        int grpcCount,
        int blazorCount,
        int razorPageCount)
    {
        var tech = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if ((!string.IsNullOrEmpty(project.Sdk) && project.Sdk.Contains("Aspire", StringComparison.OrdinalIgnoreCase)) ||
            HasPackagePrefix(project, "Aspire.") ||
            AnyCodeToken(projFiles, "DistributedApplication.CreateBuilder") ||
            AnyCodeToken(projFiles, "AddServiceDefaults"))
        {
            tech.Add(".NET Aspire");
        }

        if (string.Equals(project.Sdk, "Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase) ||
            HasPackagePrefix(project, "Microsoft.AspNetCore.") ||
            httpCount > 0 || blazorCount > 0 || razorPageCount > 0 ||
            AnyCodeToken(projFiles, "WebApplication.CreateBuilder") ||
            AnyCodeToken(projFiles, "ControllerBase"))
        {
            tech.Add("ASP.NET Core");
        }

        if (HasMinimalApiEndpoint(projEntrypoints, projFiles))
            tech.Add("Minimal API");

        if (grpcCount > 0 ||
            HasPackagePrefix(project, "Grpc.") ||
            AnyCodeToken(projFiles, "AddGrpc") ||
            AnyCodeToken(projFiles, "MapGrpcService"))
        {
            tech.Add("gRPC");
        }

        if (blazorCount > 0 ||
            (!string.IsNullOrEmpty(project.Sdk) && project.Sdk.Contains("Blazor", StringComparison.OrdinalIgnoreCase)) ||
            HasPackagePrefix(project, "Microsoft.AspNetCore.Components") ||
            AnyCodeToken(projFiles, "MapRazorComponents"))
        {
            tech.Add("Blazor");
        }

        if (razorPageCount > 0 || AnyCodeToken(projFiles, "MapRazorPages"))
            tech.Add("Razor Pages");

        if (HasPackagePrefix(project, "Microsoft.AspNetCore.SignalR") ||
            AnyCodeToken(projFiles, "AddSignalR") ||
            AnyCodeToken(projFiles, "MapHub<") ||
            projFiles.Any(p => p.Scan.Classes.Any(c => c.BaseRawNames.Contains("Hub"))))
        {
            tech.Add("SignalR");
        }

        var hasEf6 = HasPackageExact(project, "EntityFramework") ||
                     projFiles.Any(p => p.Scan.Usings.Any(u => string.Equals(u.Target, "System.Data.Entity", StringComparison.Ordinal)));
        if (hasEf6)
            tech.Add("Entity Framework 6");

        var hasEfCore = HasPackagePrefix(project, "Microsoft.EntityFrameworkCore") ||
                        (!hasEf6 && projDbContexts.Count > 0) ||
                        projFiles.Any(p => p.Scan.Usings.Any(u => u.Target.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)));
        if (hasEfCore)
            tech.Add("EF Core");

        if (HasPackagePrefix(project, "Dapper") ||
            projFiles.Any(p => p.Scan.Usings.Any(u => string.Equals(u.Target, "Dapper", StringComparison.Ordinal))))
        {
            tech.Add("Dapper");
        }

        if (HasPackageContains(project, "linq2db") ||
            projFiles.Any(p => p.Scan.Usings.Any(u => u.Target.StartsWith("LinqToDB", StringComparison.Ordinal))) ||
            AnyCodeToken(projFiles, "LinqToDB."))
        {
            tech.Add("Linq2Db");
        }

        if (HasPackagePrefix(project, "NHibernate") ||
            projFiles.Any(p => p.Scan.Usings.Any(u => u.Target.StartsWith("NHibernate", StringComparison.Ordinal))))
        {
            tech.Add("NHibernate");
        }

        if (HasPackagePrefix(project, "Microsoft.Azure.Cosmos") ||
            projFiles.Any(p => p.Scan.Usings.Any(u => u.Target.StartsWith("Microsoft.Azure.Cosmos", StringComparison.Ordinal))))
        {
            tech.Add("Cosmos DB");
        }

        if (HasPackagePrefix(project, "Marten") ||
            projFiles.Any(p => p.Scan.Usings.Any(u => u.Target.StartsWith("Marten", StringComparison.Ordinal))))
        {
            tech.Add("Marten");
        }

        if (HasPackagePrefix(project, "RavenDB.") ||
            projFiles.Any(p => p.Scan.Usings.Any(u => u.Target.StartsWith("Raven.Client", StringComparison.Ordinal))))
        {
            tech.Add("RavenDB");
        }

        if (context.Result.DataStores.Any(d =>
                d.Kind == DataStoreKind.AdoNet &&
                string.Equals(d.ProjectName, project.Name, StringComparison.OrdinalIgnoreCase)))
        {
            tech.Add("ADO.NET");
        }

        if (HasPackageContains(project, "MySql") ||
            AnyCodeToken(projFiles, "UseMySql") ||
            AnyCodeToken(projFiles, "MySqlConnection"))
        {
            tech.Add("MySQL");
        }

        if (HasPackagePrefix(project, "Npgsql") ||
            HasPackagePrefix(project, "Aspire.Npgsql") ||
            AnyCodeToken(projFiles, "UseNpgsql") ||
            AnyCodeToken(projFiles, "AddPostgres") ||
            AnyCodeToken(projFiles, "NpgsqlConnection"))
        {
            tech.Add("PostgreSQL");
        }

        if (HasPackageContains(project, "SqlServer") ||
            HasPackageExact(project, "Microsoft.Data.SqlClient") ||
            HasPackageExact(project, "System.Data.SqlClient") ||
            AnyCodeToken(projFiles, "UseSqlServer") ||
            AnyCodeToken(projFiles, "AddSqlServer") ||
            AnyCodeToken(projFiles, "SqlConnection"))
        {
            tech.Add("SQL Server");
        }

        if (HasPackageContains(project, "Sqlite") ||
            AnyCodeToken(projFiles, "UseSqlite") ||
            AnyCodeToken(projFiles, "SqliteConnection"))
        {
            tech.Add("SQLite");
        }

        if (HasPackageContains(project, "Redis") ||
            AnyCodeToken(projFiles, "AddRedis") ||
            AnyCodeToken(projFiles, "AddStackExchangeRedisCache") ||
            AnyCodeToken(projFiles, "IConnectionMultiplexer"))
        {
            tech.Add("Redis");
        }

        if (HasPackagePrefix(project, "MongoDB.") ||
            HasPackageContains(project, "MongoDB") ||
            AnyCodeToken(projFiles, "AddMongoDB") ||
            AnyCodeToken(projFiles, "IMongoClient"))
        {
            tech.Add("MongoDB");
        }

        if (HasPackagePrefix(project, "RabbitMQ.") ||
            HasPackageContains(project, "RabbitMQ") ||
            AnyCodeToken(projFiles, "AddRabbitMQ") ||
            AnyCodeToken(projFiles, "UsingRabbitMq"))
        {
            tech.Add("RabbitMQ");
        }

        if (HasPackagePrefix(project, "Confluent.Kafka") ||
            HasPackageContains(project, "Kafka") ||
            AnyCodeToken(projFiles, "AddKafka"))
        {
            tech.Add("Kafka");
        }

        if (HasPackagePrefix(project, "Azure.Messaging.ServiceBus") ||
            HasPackagePrefix(project, "Microsoft.Azure.ServiceBus") ||
            HasPackageContains(project, "ServiceBus") ||
            AnyCodeToken(projFiles, "AddAzureServiceBus"))
        {
            tech.Add("Azure Service Bus");
        }

        if (HasPackagePrefix(project, "MediatR") ||
            projFiles.Any(p => p.Scan.Usings.Any(u => string.Equals(u.Target, "MediatR", StringComparison.Ordinal))) ||
            (projRequests.Count > 0 && context.Result.Conventions.Contains(MediatorConventions.MediatorName) &&
             projRequests.Any(r => r.ViaMarker is null)))
        {
            tech.Add("MediatR");
        }

        if (HasPackagePrefix(project, "Wolverine") ||
            projFiles.Any(p => p.Scan.Usings.Any(u => u.Target.StartsWith("Wolverine", StringComparison.Ordinal))) ||
            projRequests.Any(r => string.Equals(r.ViaMarker, MediatorConventions.WolverineName, StringComparison.Ordinal)))
        {
            tech.Add("Wolverine");
        }

        if (HasPackagePrefix(project, "MassTransit") ||
            projFiles.Any(p => p.Scan.Usings.Any(u => u.Target.StartsWith("MassTransit", StringComparison.Ordinal))) ||
            AnyCodeToken(projFiles, "AddMassTransit"))
        {
            tech.Add("MassTransit");
        }

        if (HasPackagePrefix(project, "DotNetCore.CAP") ||
            AnyCodeToken(projFiles, "CapSubscribe"))
        {
            tech.Add("CAP");
        }

        if (HasPackagePrefix(project, "FastEndpoints") ||
            projFiles.Any(p => p.Scan.Usings.Any(u => u.Target.StartsWith("FastEndpoints", StringComparison.Ordinal))))
        {
            tech.Add("FastEndpoints");
        }

        if (HasPackagePrefix(project, "FluentValidation") ||
            projValidators.Count > 0 ||
            projFiles.Any(p => p.Scan.Usings.Any(u => u.Target.StartsWith("FluentValidation", StringComparison.Ordinal))))
        {
            tech.Add("FluentValidation");
        }

        if (testProjInfo is not null)
        {
            foreach (var fw in testProjInfo.Frameworks)
                tech.Add(fw);
            foreach (var mock in testProjInfo.MockingLibraries)
                tech.Add(mock);
            foreach (var assert in testProjInfo.AssertionLibraries)
                tech.Add(assert);
        }

        return tech.OrderBy(t => t, StringComparer.Ordinal).ToList();
    }

    private static List<string> SelectEntryFiles(
        string rootPath,
        List<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> projFiles,
        List<SystemEntrypointInfo> projEntrypoints,
        List<DbContextInfo> projDbContexts,
        List<CqrsRequest> projRequests,
        List<HandlerInfo> projHandlers)
    {
        var scores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        void AddScore(string relPath, int delta)
        {
            if (string.IsNullOrWhiteSpace(relPath))
                return;
            var normalized = relPath.Replace('\\', '/');
            scores[normalized] = scores.TryGetValue(normalized, out var existing)
                ? existing + delta
                : delta;
        }

        foreach (var (file, scan, _) in projFiles)
        {
            var rel = Path.GetRelativePath(rootPath, file.Path).Replace(Path.DirectorySeparatorChar, '/');
            var fileName = Path.GetFileName(rel);

            if (string.Equals(fileName, "Program.cs", StringComparison.OrdinalIgnoreCase))
                AddScore(rel, 10_000);
            else if (string.Equals(fileName, "Startup.cs", StringComparison.OrdinalIgnoreCase))
                AddScore(rel, 9_000);
            else if (fileName.Contains("DependencyInjection", StringComparison.OrdinalIgnoreCase) ||
                     fileName.EndsWith("Extensions.cs", StringComparison.OrdinalIgnoreCase))
                AddScore(rel, 1_200);

            AddScore(rel, scan.Classes.Count + scan.InterfaceDecls.Count);
        }

        var scoredEntrypointFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ep in projEntrypoints)
        {
            if (ep.Location is null)
                continue;

            var normalized = ep.Location.File.Replace('\\', '/');
            if (scoredEntrypointFiles.Add(normalized))
            {
                var baseWeight = ep.Kind switch
                {
                    SystemEntrypointKind.Http or SystemEntrypointKind.Grpc => 5_200,
                    SystemEntrypointKind.Worker or SystemEntrypointKind.MessageConsumer => 5_100,
                    _ => 4_800,
                };
                AddScore(normalized, baseWeight);
            }
            else
            {
                AddScore(normalized, 10);
            }
        }

        var scoredDbFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ctx in projDbContexts)
        {
            if (ctx.Location is not null && scoredDbFiles.Add(ctx.Location.File.Replace('\\', '/')))
                AddScore(ctx.Location.File, 3_000);
        }

        var scoredHandlerFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in projHandlers)
        {
            if (h.Location is not null && scoredHandlerFiles.Add(h.Location.File.Replace('\\', '/')))
                AddScore(h.Location.File, 1_500);
        }

        var scoredRequestFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var req in projRequests)
        {
            if (req.Location is not null && scoredRequestFiles.Add(req.Location.File.Replace('\\', '/')))
                AddScore(req.Location.File, 1_400);
        }

        return scores
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Take(MaxEntryFiles)
            .Select(kv => kv.Key)
            .ToList();
    }

    private static Dictionary<string, HashSet<string>> CollectPublishedEventsByProject(SystemAnalysisContext context)
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        void Record(string? projectName, string eventName)
        {
            if (string.IsNullOrWhiteSpace(projectName) || string.IsNullOrWhiteSpace(eventName) ||
                context.IsTestProject(projectName))
            {
                return;
            }
            if (!map.TryGetValue(projectName, out var set))
                map[projectName] = set = new HashSet<string>(StringComparer.Ordinal);
            set.Add(eventName);
        }

        foreach (var hit in context.GetPublishedEvents())
            Record(hit.ProjectName, hit.ResolvedFullName ?? hit.EventSimpleName);

        foreach (var site in context.Result.DispatchCallSites)
        {
            if (!site.IsTestProject && site.TargetKind == DispatchTargetKind.Notification)
                Record(site.ProjectName, site.TargetFullName);
        }

        foreach (var h in context.Result.Handlers)
        {
            foreach (var pub in h.PublishedNotifications)
                Record(h.ProjectName, pub);
        }

        foreach (var nh in context.Result.NotificationHandlers)
        {
            foreach (var pub in nh.PublishedNotifications)
                Record(nh.ProjectName, pub);
        }

        return map;
    }

    private static bool HasMinimalApiEndpoint(
        List<SystemEntrypointInfo> projEntrypoints,
        List<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> projFiles)
    {
        if (projEntrypoints.Any(e =>
                e.Kind == SystemEntrypointKind.Http &&
                (e.Name.StartsWith("Map", StringComparison.Ordinal) ||
                 e.Name.Contains(".Map", StringComparison.Ordinal))))
        {
            return true;
        }

        foreach (var (file, _, mask) in projFiles)
        {
            if (!file.Code.Contains(".Map", StringComparison.Ordinal))
                continue;
            foreach (var verb in new[] { ".MapGet", ".MapPost", ".MapPut", ".MapDelete", ".MapPatch" })
            {
                var idx = file.Code.IndexOf(verb, StringComparison.Ordinal);
                if (idx >= 0 && mask.IsCode(idx))
                    return true;
            }
        }

        return false;
    }

    private static bool AnyCodeToken(
        List<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> projFiles,
        string token)
    {
        foreach (var (file, _, mask) in projFiles)
        {
            var start = 0;
            while (start < file.Code.Length)
            {
                var idx = file.Code.IndexOf(token, start, StringComparison.Ordinal);
                if (idx < 0)
                    break;
                if (mask.IsCode(idx))
                    return true;
                start = idx + token.Length;
            }
        }
        return false;
    }

    private static bool HasPackagePrefix(ProjectInfo project, string prefix)
        => project.PackageReferences.Any(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static bool HasPackageExact(ProjectInfo project, string name)
        => project.PackageReferences.Any(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));

    private static bool HasPackageContains(ProjectInfo project, string fragment)
        => project.PackageReferences.Any(p => p.Contains(fragment, StringComparison.OrdinalIgnoreCase));
}
