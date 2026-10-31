namespace CqrsLens;

/// <summary>Projects the analysis model onto the versioned JSON report contract.</summary>
internal static class JsonReportDocumentFactory
{
    internal static JsonReportDocument Create(AnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var rootName = new DirectoryInfo(result.RootPath).Name;
        var activeChecks = result.BaselineApplied
            ? result.Checks.Where(c => !c.IsKnown).ToList()
            : result.Checks;
        var errorCount = activeChecks.Count(c => c.Severity == CheckSeverity.Error);
        var warningCount = activeChecks.Count - errorCount;
        var knownCount = result.BaselineApplied
            ? result.Checks.Count(c => c.IsKnown)
            : 0;

        return new JsonReportDocument
        {
            SchemaVersion = JsonReportGenerator.CurrentSchemaVersion,
            ToolVersion = ToolInfo.Version,
            Scanner = result.ScannerName,
            GeneratedAt = result.GeneratedAt.UtcDateTime.ToString("O"),
            ElapsedMilliseconds = result.ElapsedMilliseconds,
            RootName = rootName,
            Watermark = result.Watermark,
            Conventions = result.Conventions.Count > 0 ? result.Conventions.ToList() : null,
            DiscoveredFileCount = result.DiscoveredFileCount,
            FileCount = result.FileCount,
            BaselineApplied = result.BaselineApplied,
            CanRunStrictChecks = result.CanRunStrictChecks,
            Summary = new JsonSummary
            {
                Projects = result.Projects.Count,
                Requests = result.Requests.Count,
                Handlers = result.Handlers.Count,
                Validators = result.Validators.Count,
                Behaviors = result.Behaviors.Count,
                DbContexts = result.DbContexts.Count,
                Notifications = result.Notifications.Count,
                NotificationHandlers = result.NotificationHandlers.Count,
                Endpoints = result.Endpoints.Count,
                Errors = errorCount,
                Warnings = warningCount,
                KnownFindings = knownCount,
                Diagnostics = result.Diagnostics.Count,
                FlowEdges = result.FlowEdges.Count,
                DispatchCallSites = result.DispatchCallSites.Count,
                UnresolvedDispatches = result.UnresolvedDispatches.Count,
            },
            Projects = result.Projects
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.FilePath, StringComparer.Ordinal)
                .Select(p => new JsonProject
                {
                    Name = p.Name,
                    Layer = p.Layer,
                    References = p.ReferenceNames
                        .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(r => r, StringComparer.Ordinal)
                        .ToList(),
                    Packages = p.PackageReferences
                        .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(r => r, StringComparer.Ordinal)
                        .ToList(),
                })
                .ToList(),
            Requests = result.Requests
                .Select(r => new JsonRequest
                {
                    Name = r.Name,
                    Namespace = EmptyToNull(r.Namespace),
                    FullName = r.FullName,
                    Kind = r.Kind,
                    ResponseType = r.ResponseType,
                    ViaMarker = r.ViaMarker,
                    Project = r.ProjectName,
                    Handlers = r.Handlers.ToList(),
                    Validators = r.Validators.ToList(),
                    Properties = r.Properties.Count > 0
                        ? r.Properties.Select(p => new JsonRequestProperty { Name = p.Name, Type = p.Type }).ToList()
                        : null,
                    Entrypoints = r.Entrypoints.Count > 0 ? r.Entrypoints.ToList() : null,
                    TouchedEntities = r.TouchedEntities.Count > 0 ? r.TouchedEntities.ToList() : null,
                    PublishedNotifications = r.PublishedNotifications.Count > 0 ? r.PublishedNotifications.ToList() : null,
                    DispatchedRequests = r.DispatchedRequests.Count > 0 ? r.DispatchedRequests.ToList() : null,
                    Location = ToJsonLocation(r.Location),
                })
                .ToList(),
            Handlers = result.Handlers
                .OrderBy(h => h.FullName, StringComparer.Ordinal)
                .ThenBy(h => h.RequestName, StringComparer.Ordinal)
                .Select(h => new JsonHandler
                {
                    Name = h.Name,
                    Namespace = EmptyToNull(h.Namespace),
                    FullName = h.FullName,
                    RequestName = h.RequestName,
                    Dependencies = h.Dependencies.Count > 0 ? h.Dependencies.ToList() : null,
                    TouchedEntities = h.TouchedEntities.Count > 0 ? h.TouchedEntities.ToList() : null,
                    PublishedNotifications = h.PublishedNotifications.Count > 0 ? h.PublishedNotifications.ToList() : null,
                    DispatchedRequests = h.DispatchedRequests.Count > 0 ? h.DispatchedRequests.ToList() : null,
                    Project = h.ProjectName,
                    Location = ToJsonLocation(h.Location),
                })
                .ToList(),
            Validators = result.Validators
                .OrderBy(v => v.FullName, StringComparer.Ordinal)
                .ThenBy(v => v.TargetType, StringComparer.Ordinal)
                .Select(v => new JsonValidator
                {
                    Name = v.Name,
                    Namespace = EmptyToNull(v.Namespace),
                    FullName = v.FullName,
                    TargetType = v.TargetType,
                    Project = v.ProjectName,
                    Location = ToJsonLocation(v.Location),
                })
                .ToList(),
            Behaviors = result.Behaviors
                .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(b => b.ProjectName, StringComparer.Ordinal)
                .Select(b => new JsonBehavior
                {
                    Name = b.Name,
                    Project = b.ProjectName,
                    Location = ToJsonLocation(b.Location),
                })
                .ToList(),
            DbContexts = result.DbContexts
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Namespace, StringComparer.Ordinal)
                .Select(c => new JsonDbContext
                {
                    Name = c.Name,
                    Namespace = EmptyToNull(c.Namespace),
                    Project = c.ProjectName,
                    Location = ToJsonLocation(c.Location),
                    DbSets = c.DbSets
                        .Select(s => new JsonDbSet
                        {
                            Property = s.Property,
                            Entity = s.Entity,
                            EntityFullName = s.EntityFullName,
                        })
                        .ToList(),
                    Entities = c.Entities
                        .OrderBy(e => e.FullName, StringComparer.Ordinal)
                        .Select(e => new JsonEntity
                        {
                            Name = e.Name,
                            Namespace = EmptyToNull(e.Namespace),
                            FullName = e.FullName,
                            Project = e.ProjectName,
                            Location = ToJsonLocation(e.Location),
                            Properties = e.Properties
                                .Select(p => new JsonEntityProperty
                                {
                                    Name = p.Name,
                                    Type = p.Type,
                                    IsKey = p.IsKey,
                                    IsForeignKey = p.IsForeignKey,
                                    IsNavigation = p.IsNavigation,
                                    IsCollection = p.IsCollection,
                                    TargetEntity = p.TargetEntity,
                                    IsRequired = p.IsRequired,
                                    Location = ToJsonLocation(p.Location),
                                })
                                .ToList(),
                        })
                        .ToList(),
                    Relationships = c.Relationships
                        .OrderBy(r => r.PrincipalEntity, StringComparer.Ordinal)
                        .ThenBy(r => r.DependentEntity, StringComparer.Ordinal)
                        .ThenBy(r => r.PrincipalNavigation, StringComparer.Ordinal)
                        .ThenBy(r => r.DependentNavigation, StringComparer.Ordinal)
                        .Select(r => new JsonEntityRelationship
                        {
                            PrincipalEntity = r.PrincipalEntity,
                            DependentEntity = r.DependentEntity,
                            PrincipalNavigation = r.PrincipalNavigation,
                            DependentNavigation = r.DependentNavigation,
                            ForeignKey = r.ForeignKey,
                            IsRequired = r.IsRequired,
                            Kind = r.Kind switch
                            {
                                EntityRelationshipKind.OneToOne => "oneToOne",
                                EntityRelationshipKind.ManyToMany => "manyToMany",
                                _ => "oneToMany",
                            },
                            ConfiguredBy = r.ConfiguredBy,
                        })
                        .ToList(),
                })
                .ToList(),
            Checks = result.Checks
                .Select(c => new JsonCheckFinding
                {
                    Code = c.Code,
                    Severity = c.Severity == CheckSeverity.Error ? "error" : "warning",
                    Message = c.Message,
                    Target = c.Target,
                    Project = c.ProjectName,
                    Known = c.IsKnown,
                    Location = ToJsonLocation(c.Location),
                })
                .ToList(),
            Diagnostics = result.Diagnostics
                .Select(d => new JsonDiagnostic
                {
                    Code = d.Code,
                    Severity = d.Severity switch
                    {
                        AnalysisDiagnosticSeverity.Error => "error",
                        AnalysisDiagnosticSeverity.Warning => "warning",
                        _ => "info",
                    },
                    Message = d.Message,
                    Project = d.ProjectName,
                    Location = ToJsonLocation(d.Location),
                })
                .ToList(),
            SkippedFiles = result.SkippedFiles.ToList(),
            Notifications = result.Notifications
                .Select(n => new JsonNotification
                {
                    Name = n.Name,
                    Namespace = EmptyToNull(n.Namespace),
                    FullName = n.FullName,
                    ViaMarker = n.ViaMarker,
                    Project = n.ProjectName,
                    Handlers = n.Handlers.ToList(),
                    Location = ToJsonLocation(n.Location),
                })
                .ToList(),
            NotificationHandlers = result.NotificationHandlers
                .OrderBy(h => h.FullName, StringComparer.Ordinal)
                .ThenBy(h => h.NotificationName, StringComparer.Ordinal)
                .Select(h => new JsonNotificationHandler
                {
                    Name = h.Name,
                    Namespace = EmptyToNull(h.Namespace),
                    FullName = h.FullName,
                    NotificationName = h.NotificationName,
                    Dependencies = h.Dependencies.Count > 0 ? h.Dependencies.ToList() : null,
                    TouchedEntities = h.TouchedEntities.Count > 0 ? h.TouchedEntities.ToList() : null,
                    PublishedNotifications = h.PublishedNotifications.Count > 0 ? h.PublishedNotifications.ToList() : null,
                    DispatchedRequests = h.DispatchedRequests.Count > 0 ? h.DispatchedRequests.ToList() : null,
                    Project = h.ProjectName,
                    Location = ToJsonLocation(h.Location),
                })
                .ToList(),
            Endpoints = result.Endpoints.Count > 0
                ? result.Endpoints
                    .Select(e => new JsonEndpoint
                    {
                        Name = e.Name,
                        HttpMethod = e.HttpMethod,
                        Route = e.Route,
                        TargetRequest = e.TargetRequestName,
                        TargetRequestFullName = e.TargetRequestFullName,
                        Inferred = e.IsInferred,
                        Project = e.ProjectName,
                        Location = ToJsonLocation(e.Location),
                    })
                    .ToList()
                : null,
            FlowEdges = result.FlowEdges.Count > 0
                ? result.FlowEdges
                    .Select(e => new JsonFlowEdge
                    {
                        Kind = FlowEdgeKindName(e.Kind),
                        From = e.From,
                        To = e.To,
                        Project = e.ProjectName,
                        Location = ToJsonLocation(e.Location),
                    })
                    .ToList()
                : null,
            DispatchCallSites = result.DispatchCallSites.Count > 0
                ? result.DispatchCallSites
                    .Select(s => new JsonDispatchCallSite
                    {
                        Target = s.TargetFullName,
                        TargetKind = s.TargetKind == DispatchTargetKind.Request ? "request" : "notification",
                        Sender = s.SenderFullName,
                        Project = s.ProjectName,
                        TestProject = s.IsTestProject,
                        Location = ToJsonLocation(s.Location),
                    })
                    .ToList()
                : null,
            UnresolvedDispatches = result.UnresolvedDispatches.Count > 0
                ? result.UnresolvedDispatches
                    .Select(u => new JsonUnresolvedDispatch
                    {
                        Method = u.Method,
                        Receiver = u.ReceiverName,
                        Project = u.ProjectName,
                        Location = ToJsonLocation(u.Location),
                    })
                    .ToList()
                : null,
        };
    }

    private static string FlowEdgeKindName(FlowEdgeKind kind) => kind switch
    {
        FlowEdgeKind.EndpointToRequest => "endpointToRequest",
        FlowEdgeKind.RequestToHandler => "requestToHandler",
        FlowEdgeKind.HandlerToRequest => "handlerToRequest",
        FlowEdgeKind.HandlerToNotification => "handlerToNotification",
        FlowEdgeKind.HandlerToDbContext => "handlerToDbContext",
        _ => "unknown",
    };

    private static string? EmptyToNull(string? value)
        => string.IsNullOrEmpty(value) ? null : value;

    private static JsonLocation? ToJsonLocation(SourceLocation? location)
        => location is null
            ? null
            : new JsonLocation
            {
                File = location.File,
                Line = location.Line,
                Column = location.Column,
            };
}
