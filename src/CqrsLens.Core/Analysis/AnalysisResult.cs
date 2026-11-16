namespace CqrsLens;

public sealed class AnalysisResult
{
    public required string RootPath { get; init; }

    public ArchitectureSpine Spine { get; } = new();
    internal SpineIdentityResolver IdentityResolver { get; }

    public SpineTrackedList<ProjectInfo> Projects { get; }
    public SpineTrackedList<CqrsRequest> Requests { get; }
    public SpineTrackedList<HandlerInfo> Handlers { get; }

    /// <summary>
    /// Simple names of request declarations discovered as open generics. An open
    /// generic type is never dispatched as-is (MediatR closes it per inner
    /// request), so it is not a concrete request and is kept out of
    /// <see cref="Requests"/>. The identity is still recorded so a validator that
    /// targets such a wrapper is not misreported as an orphan.
    /// </summary>
    internal HashSet<string> NonConcreteRequestNames { get; } = new(StringComparer.Ordinal);
    public SpineTrackedList<ValidatorInfo> Validators { get; }
    public SpineTrackedList<BehaviorInfo> Behaviors { get; }
    public SpineTrackedList<DbContextInfo> DbContexts { get; }

    /// <summary>
    /// Service-resolution index: interface → implementation, built from concrete class
    /// declarations and explicit MS-DI/Autofac registrations. Additive model data used
    /// by consumers to resolve constructor-injected interfaces through DI; it never
    /// changes an existing count, finding or report field on its own.
    /// </summary>
    public SpineTrackedList<ServiceResolution> ServiceResolutions { get; }

    public SpineTrackedList<CqrsNotification> Notifications { get; }
    public SpineTrackedList<NotificationHandlerInfo> NotificationHandlers { get; }
    public SpineTrackedList<EndpointInfo> Endpoints { get; }

    /// <summary>
    /// Resolved solution-wide flow edges (endpoint → request, request → handler,
    /// handler → request, handler → notification, handler → DbContext). Populated by
    /// the flow pass; projected live from <see cref="Spine"/>.
    /// </summary>
    public SpineFlowEdgeList FlowEdges { get; }

    /// <summary>
    /// Edges resolved through the service-resolution index rather than written directly
    /// in source (currently handler → DbContext through an injected interface). Projected
    /// live from <see cref="Spine"/>.
    /// </summary>
    public SpineResolvedEdgeList ResolvedEdges { get; }

    /// <summary>
    /// Method-body call edges between scanned classes: a handler or service invoking a method
    /// on one of its injected members, resolved through the service-resolution index when the
    /// member is an interface. Projected live from <see cref="Spine"/>.
    /// </summary>
    public SpineServiceCallEdgeList ServiceCallEdges { get; }

    /// <summary>
    /// Every resolved dispatch call site, including senders that are neither handlers
    /// nor endpoints. Projected live from <see cref="Spine"/>.
    /// </summary>
    public SpineDispatchCallSiteList DispatchCallSites { get; }

    /// <summary>
    /// Dispatch-looking call sites whose target type could not be statically resolved.
    /// Projected live from <see cref="UnprovenPopulations.UnresolvedDispatches"/> on <see cref="Spine"/>.
    /// </summary>
    public SpineUnresolvedDispatchList UnresolvedDispatches { get; }

    public AnalysisResult()
    {
        IdentityResolver = new SpineIdentityResolver(this);

        Projects = new SpineTrackedList<ProjectInfo>(
            onAdded: OnProjectAdded,
            onRemoved: p =>
            {
                if (p.SpineId is not null)
                    Spine.RemoveProject(p.SpineId);
            },
            onCleared: () =>
            {
                foreach (var p in Spine.Projects.ToArray())
                    Spine.RemoveProject(p.Id);
            },
            onRangeAdded: OnProjectsAddedRange);

        Requests = new SpineTrackedList<CqrsRequest>(
            onAdded: r =>
            {
                var id = IdentityResolver.ResolveComponentId(r.ProjectName, r.Namespace, r.ContainingTypes, r.Name);
                Spine.AddOrMergeComponent(
                    id,
                    r.Location,
                    ComponentRole.Request,
                    SpineIdentityResolver.SyntheticOccurrence(
                        EvidenceRule.RequestMarker,
                        EvidenceOrigin.Declaration,
                        r.Location,
                        EvidenceStrength.Proven,
                        scopeProjectName: r.ProjectName));
                r.AttachSpine(this, id);
            },
            onRemoved: r =>
            {
                var id = r.SpineId ?? IdentityResolver.ResolveComponentId(r.ProjectName, r.Namespace, r.ContainingTypes, r.Name);
                Spine.RemoveComponentRole(id, ComponentRole.Request);
            },
            onCleared: () =>
            {
                foreach (var c in Spine.GetComponentsByRole(ComponentRole.Request).ToArray())
                    Spine.RemoveComponentRole(c.Id, ComponentRole.Request);
            });

        Handlers = new SpineTrackedList<HandlerInfo>(
            onAdded: h =>
            {
                var id = IdentityResolver.ResolveComponentId(h.ProjectName, h.Namespace, h.ContainingTypes, h.Name);
                Spine.AddOrMergeComponent(
                    id,
                    h.Location,
                    ComponentRole.Handler,
                    SpineIdentityResolver.SyntheticOccurrence(
                        EvidenceRule.HandlerMarker,
                        EvidenceOrigin.Declaration,
                        h.Location,
                        EvidenceStrength.Proven,
                        scopeProjectName: h.ProjectName));
                h.AttachSpine(this, id);
            },
            onRemoved: h =>
            {
                var id = h.SpineId ?? IdentityResolver.ResolveComponentId(h.ProjectName, h.Namespace, h.ContainingTypes, h.Name);
                Spine.RemoveComponentRole(id, ComponentRole.Handler);
            },
            onCleared: () =>
            {
                foreach (var c in Spine.GetComponentsByRole(ComponentRole.Handler).ToArray())
                    Spine.RemoveComponentRole(c.Id, ComponentRole.Handler);
            });

        Validators = new SpineTrackedList<ValidatorInfo>(
            onAdded: v =>
            {
                var id = IdentityResolver.ResolveComponentId(v.ProjectName, v.Namespace, v.ContainingTypes, v.Name);
                v.SpineId = id;
                Spine.AddOrMergeComponent(
                    id,
                    v.Location,
                    ComponentRole.Validator,
                    SpineIdentityResolver.SyntheticOccurrence(
                        EvidenceRule.ValidatorMarker,
                        EvidenceOrigin.Declaration,
                        v.Location,
                        EvidenceStrength.Proven,
                        scopeProjectName: v.ProjectName));
            },
            onRemoved: v =>
            {
                var id = v.SpineId ?? IdentityResolver.ResolveComponentId(v.ProjectName, v.Namespace, v.ContainingTypes, v.Name);
                Spine.RemoveComponentRole(id, ComponentRole.Validator);
            },
            onCleared: () =>
            {
                foreach (var c in Spine.GetComponentsByRole(ComponentRole.Validator).ToArray())
                    Spine.RemoveComponentRole(c.Id, ComponentRole.Validator);
            });

        Behaviors = new SpineTrackedList<BehaviorInfo>(
            onAdded: b =>
            {
                var id = IdentityResolver.ResolveComponentId(b.ProjectName, b.Namespace, b.ContainingTypes, b.Name, b.GenericArity);
                b.SpineId = id;
                var rule = string.IsNullOrWhiteSpace(b.ClosedRequestTarget)
                    ? EvidenceRule.BehaviorOpenGenericRegistration
                    : EvidenceRule.BehaviorClosedBinding;
                Spine.AddOrMergeComponent(
                    id,
                    b.Location,
                    ComponentRole.Behavior,
                    SpineIdentityResolver.SyntheticOccurrence(
                        rule,
                        EvidenceOrigin.Declaration,
                        b.Location,
                        EvidenceStrength.Proven,
                        scopeProjectName: b.ProjectName));
            },
            onRemoved: b =>
            {
                var id = b.SpineId ?? IdentityResolver.ResolveComponentId(b.ProjectName, b.Namespace, b.ContainingTypes, b.Name, b.GenericArity);
                Spine.RemoveComponentRole(id, ComponentRole.Behavior);
            },
            onCleared: () =>
            {
                foreach (var c in Spine.GetComponentsByRole(ComponentRole.Behavior).ToArray())
                    Spine.RemoveComponentRole(c.Id, ComponentRole.Behavior);
            });

        DbContexts = new SpineTrackedList<DbContextInfo>(
            onAdded: db =>
            {
                var id = IdentityResolver.ResolveComponentId(db.ProjectName, db.Namespace, db.ContainingTypes, db.Name);
                Spine.AddOrMergeComponent(
                    id,
                    db.Location,
                    ComponentRole.DbContext,
                    SpineIdentityResolver.SyntheticOccurrence(
                        EvidenceRule.DbContextInheritance,
                        EvidenceOrigin.Declaration,
                        db.Location,
                        EvidenceStrength.Proven,
                        scopeProjectName: db.ProjectName));
                db.AttachSpine(this, id);
            },
            onRemoved: db =>
            {
                var id = db.SpineId ?? IdentityResolver.ResolveComponentId(db.ProjectName, db.Namespace, db.ContainingTypes, db.Name);
                Spine.RemoveComponentRole(id, ComponentRole.DbContext);
            },
            onCleared: () =>
            {
                foreach (var c in Spine.GetComponentsByRole(ComponentRole.DbContext).ToArray())
                    Spine.RemoveComponentRole(c.Id, ComponentRole.DbContext);
            });

        ServiceResolutions = new SpineTrackedList<ServiceResolution>(
            onAdded: OnServiceResolutionAdded,
            onRemoved: res =>
            {
                Spine.RemoveRelations(r =>
                    r.Kind == RelationKind.ResolvesTo &&
                    string.Equals(r.From.Name, res.Interface, StringComparison.Ordinal));
                Spine.Unproven.RemoveAmbiguousResolution(res.Interface);
            },
            onCleared: () =>
            {
                Spine.RemoveRelations(r => r.Kind == RelationKind.ResolvesTo);
                Spine.Unproven.ClearAmbiguousResolutions();
            });

        Notifications = new SpineTrackedList<CqrsNotification>(
            onAdded: n =>
            {
                var id = IdentityResolver.ResolveComponentId(n.ProjectName, n.Namespace, n.ContainingTypes, n.Name);
                Spine.AddOrMergeComponent(
                    id,
                    n.Location,
                    ComponentRole.Notification,
                    SpineIdentityResolver.SyntheticOccurrence(
                        EvidenceRule.NotificationMarker,
                        EvidenceOrigin.Declaration,
                        n.Location,
                        EvidenceStrength.Proven,
                        scopeProjectName: n.ProjectName));
                n.AttachSpine(this, id);
            },
            onRemoved: n =>
            {
                var id = n.SpineId ?? IdentityResolver.ResolveComponentId(n.ProjectName, n.Namespace, n.ContainingTypes, n.Name);
                Spine.RemoveComponentRole(id, ComponentRole.Notification);
            },
            onCleared: () =>
            {
                foreach (var c in Spine.GetComponentsByRole(ComponentRole.Notification).ToArray())
                    Spine.RemoveComponentRole(c.Id, ComponentRole.Notification);
            });

        NotificationHandlers = new SpineTrackedList<NotificationHandlerInfo>(
            onAdded: nh =>
            {
                var id = IdentityResolver.ResolveComponentId(nh.ProjectName, nh.Namespace, nh.ContainingTypes, nh.Name);
                Spine.AddOrMergeComponent(
                    id,
                    nh.Location,
                    ComponentRole.NotificationHandler,
                    SpineIdentityResolver.SyntheticOccurrence(
                        EvidenceRule.NotificationHandlerMarker,
                        EvidenceOrigin.Declaration,
                        nh.Location,
                        EvidenceStrength.Proven,
                        scopeProjectName: nh.ProjectName));
                nh.AttachSpine(this, id);
            },
            onRemoved: nh =>
            {
                var id = nh.SpineId ?? IdentityResolver.ResolveComponentId(nh.ProjectName, nh.Namespace, nh.ContainingTypes, nh.Name);
                Spine.RemoveComponentRole(id, ComponentRole.NotificationHandler);
            },
            onCleared: () =>
            {
                foreach (var c in Spine.GetComponentsByRole(ComponentRole.NotificationHandler).ToArray())
                    Spine.RemoveComponentRole(c.Id, ComponentRole.NotificationHandler);
            });

        Endpoints = new SpineTrackedList<EndpointInfo>(
            onAdded: OnEndpointAdded,
            onRemoved: ep =>
            {
                if (ep.SpineId is not null)
                    Spine.RemoveEndpoint(ep.SpineId);
            },
            onCleared: () =>
            {
                foreach (var ep in Spine.Endpoints.ToArray())
                    Spine.RemoveEndpoint(ep.Id);
            });

        FlowEdges = new SpineFlowEdgeList(this);
        ResolvedEdges = new SpineResolvedEdgeList(this);
        ServiceCallEdges = new SpineServiceCallEdgeList(this);
        DispatchCallSites = new SpineDispatchCallSiteList(this);
        UnresolvedDispatches = new SpineUnresolvedDispatchList(this);
    }

    /// <summary>
    /// True once the flow pass has run over a scanned solution. Reachability checks
    /// are only meaningful for a result produced by a scan; a hand-built model with
    /// no flow data is not treated as a solution where nothing is ever dispatched.
    /// </summary>
    internal bool FlowAnalyzed { get; set; }

    public List<CheckFinding> Checks { get; } = new();

    /// <summary>True when checks were compared with a baseline file.</summary>
    public bool BaselineApplied { get; set; }

    /// <summary>Baseline file used for this analysis, when any.</summary>
    public string? BaselinePath { get; set; }

    public List<AnalysisDiagnostic> Diagnostics { get; } = new();

    /// <summary>Scanner implementation that produced the shape model.</summary>
    public string ScannerName { get; set; } = "unknown";

    private DateTimeOffset _generatedAt = DateTimeOffset.UtcNow;

    /// <summary>True when <see cref="GeneratedAt"/> was explicitly pinned by the caller.</summary>
    internal bool HasExplicitGeneratedAt { get; set; }

    /// <summary>One timestamp shared by every report artifact.</summary>
    public DateTimeOffset GeneratedAt
    {
        get => _generatedAt;
        set
        {
            _generatedAt = value;
            HasExplicitGeneratedAt = true;
        }
    }

    /// <summary>Optional human intent specification loaded from <c>.cqrslens.json</c> (R4).</summary>
    public IntentSpecification? Intent { get; set; }

    /// <summary>Built-in mediator conventions detected in the source (e.g. Mediator, Wolverine).</summary>
    public List<string> Conventions { get; } = new();

    /// <summary>
    /// True when the caller supplied explicit project layer overrides, so the layer
    /// model is configured rather than name-guessed and must not be second-guessed
    /// by the confidence heuristic.
    /// </summary>
    internal bool HasConfiguredLayers { get; set; }

    /// <summary>
    /// The resolved configuration this scan ran under, for the reports' declared-vs-actual
    /// view. Populated from the CLI scan path; it never feeds detection and is not
    /// part of the JSON contract.
    /// </summary>
    public ConfigurationProvenance Configuration { get; set; } = ConfigurationProvenance.Empty;

    /// <summary>Optional custom watermark stamp rendered in console and generated reports.</summary>
    public string? Watermark { get; set; }

    /// <summary>
    /// Strict architecture errors are trustworthy only when scanning completed and
    /// linking was unambiguous. Incomplete scans are reported as diagnostics.
    /// </summary>
    public bool CanRunStrictChecks => !Diagnostics.Any(d =>
        d.Severity == AnalysisDiagnosticSeverity.Error ||
        d.Code is SourceFileSkippedCode or RoslynScanFailureCode or
            RoslynUnresolvedContractsCode or AmbiguousLinkCode);

    public const string SourceFileSkippedCode = "SourceFileSkipped";
    public const string RoslynScanFailureCode = "RoslynScanFailure";
    public const string RoslynUnresolvedContractsCode = "RoslynUnresolvedContracts";
    public const string AmbiguousLinkCode = "AmbiguousLink";

    /// <summary>
    /// A shape contract has multiple source candidates and no local/using match.
    /// Informational only: it explains a gap without changing check severity.
    /// </summary>
    public const string AmbiguousContractCode = "AmbiguousContract";

    /// <summary>
    /// A CQRS-looking wrapper/base type is not declared in the scanned source.
    /// Informational only; external handler wrappers are tracked by #54.
    /// </summary>
    public const string UnresolvedContractCode = "UnresolvedContract";

    /// <summary>A DbSet entity type could not be resolved to scanned source.</summary>
    public const string EfEntityUnresolvedCode = "EfEntityUnresolved";

    /// <summary>A navigation points at a type that cannot safely be modeled as an entity.</summary>
    public const string EfNavigationUnresolvedCode = "EfNavigationUnresolved";

    /// <summary>An explicit EF Core configuration could not be associated with an entity.</summary>
    public const string EfConfigurationUnresolvedCode = "EfConfigurationUnresolved";

    /// <summary>Source files discovered before any read/scan failures.</summary>
    public int DiscoveredFileCount { get; set; }

    /// <summary>Source files successfully read and scanned.</summary>
    public int FileCount { get; set; }

    /// <summary>Root-relative paths that could not be read or scanned.</summary>
    public List<string> SkippedFiles { get; } = new();

    public long ElapsedMilliseconds { get; set; }

    /// <summary>Managed bytes allocated by the whole analysis (process-wide counter).</summary>
    public long AllocatedBytes { get; set; }

    /// <summary>
    /// Wall-clock and allocation cost of each analysis stage. Populated on every run;
    /// surfaced by <c>--profile</c> and the benchmark harness.
    /// </summary>
    public List<AnalysisPhaseTiming> PhaseTimings { get; } = new();

    internal void RecordSkippedFile(string path)
    {
        var relative = Path.GetRelativePath(RootPath, path)
            .Replace(Path.DirectorySeparatorChar, '/');
        if (!SkippedFiles.Contains(relative, StringComparer.OrdinalIgnoreCase))
            SkippedFiles.Add(relative);
    }

    internal void RecordDiagnostic(AnalysisDiagnostic diagnostic)
    {
        if (!Diagnostics.Contains(diagnostic))
            Diagnostics.Add(diagnostic);

        switch (diagnostic.Code)
        {
            case AmbiguousLinkCode:
            case AmbiguousContractCode:
                Spine.Unproven.AddAmbiguousLink(new UnprovenDiagnosticEntry(
                    UnprovenItemKind.AmbiguousLink,
                    diagnostic.Code,
                    diagnostic.ProjectName ?? diagnostic.Location?.File ?? diagnostic.Code,
                    diagnostic.Message,
                    diagnostic.Location,
                    diagnostic.ProjectName));
                break;

            case UnresolvedContractCode:
                Spine.Unproven.AddUnresolvedContract(new UnprovenDiagnosticEntry(
                    UnprovenItemKind.UnresolvedContract,
                    diagnostic.Code,
                    diagnostic.ProjectName ?? diagnostic.Location?.File ?? diagnostic.Code,
                    diagnostic.Message,
                    diagnostic.Location,
                    diagnostic.ProjectName));
                break;

            case EfEntityUnresolvedCode:
                Spine.Unproven.AddUnresolvedContract(new UnprovenDiagnosticEntry(
                    UnprovenItemKind.UnresolvedEntity,
                    diagnostic.Code,
                    diagnostic.ProjectName ?? diagnostic.Location?.File ?? diagnostic.Code,
                    diagnostic.Message,
                    diagnostic.Location,
                    diagnostic.ProjectName));
                break;

            case EfNavigationUnresolvedCode:
                Spine.Unproven.AddUnresolvedContract(new UnprovenDiagnosticEntry(
                    UnprovenItemKind.UnresolvedNavigation,
                    diagnostic.Code,
                    diagnostic.ProjectName ?? diagnostic.Location?.File ?? diagnostic.Code,
                    diagnostic.Message,
                    diagnostic.Location,
                    diagnostic.ProjectName));
                break;

            case EfConfigurationUnresolvedCode:
                Spine.Unproven.AddUnresolvedContract(new UnprovenDiagnosticEntry(
                    UnprovenItemKind.UnresolvedConfiguration,
                    diagnostic.Code,
                    diagnostic.ProjectName ?? diagnostic.Location?.File ?? diagnostic.Code,
                    diagnostic.Message,
                    diagnostic.Location,
                    diagnostic.ProjectName));
                break;
        }
    }

    private void OnProjectAdded(ProjectInfo project)
    {
        var id = IdentityResolver.EnsureProjectId(project);
        if (Spine.HasAnySyntheticProjects)
        {
            var synthetic = SpineIdentityResolver.SyntheticProjectId(project.Name);
            Spine.RehomeSyntheticProject(synthetic, id);
            RebindShapeProjectIds(synthetic, id);
        }

        if (!Spine.TryGetProject(id, out _))
        {
            Spine.AddProject(new ProjectNode(
                id,
                project.Name,
                project.FilePath,
                project.DirectoryPath,
                project.Layer,
                project.LayerProvenance,
                project.PackageReferences));
        }

        project.AttachSpine(this, id);
    }

    private void OnProjectsAddedRange(IReadOnlyList<ProjectInfo> projects)
    {
        var ids = new ProjectId[projects.Count];
        var checkSynthetic = Spine.HasAnySyntheticProjects;
        for (var i = 0; i < projects.Count; i++)
        {
            var project = projects[i];
            var id = IdentityResolver.EnsureProjectId(project);
            ids[i] = id;
            if (checkSynthetic)
            {
                var synthetic = SpineIdentityResolver.SyntheticProjectId(project.Name);
                Spine.RehomeSyntheticProject(synthetic, id);
                RebindShapeProjectIds(synthetic, id);
            }

            if (!Spine.TryGetProject(id, out _))
            {
                Spine.AddProject(new ProjectNode(
                    id,
                    project.Name,
                    project.FilePath,
                    project.DirectoryPath,
                    project.Layer,
                    project.LayerProvenance,
                    project.PackageReferences));
            }
        }

        for (var i = 0; i < projects.Count; i++)
            projects[i].AttachSpine(this, ids[i]);
    }

    private void RebindShapeProjectIds(ProjectId oldProject, ProjectId newProject)
    {
        foreach (var r in Requests)
        {
            if (r.SpineId?.Project.Equals(oldProject) == true)
                r.SpineId = ComponentId.FromSymbol(SymbolId.Create(newProject, r.Namespace, r.ContainingTypes, r.Name));
        }
        foreach (var h in Handlers)
        {
            if (h.SpineId?.Project.Equals(oldProject) == true)
                h.SpineId = ComponentId.FromSymbol(SymbolId.Create(newProject, h.Namespace, h.ContainingTypes, h.Name));
        }
        foreach (var v in Validators)
        {
            if (v.SpineId?.Project.Equals(oldProject) == true)
                v.SpineId = ComponentId.FromSymbol(SymbolId.Create(newProject, v.Namespace, v.ContainingTypes, v.Name));
        }
        foreach (var b in Behaviors)
        {
            if (b.SpineId?.Project.Equals(oldProject) == true)
                b.SpineId = ComponentId.FromSymbol(SymbolId.Create(newProject, b.Namespace, b.ContainingTypes, b.Name, b.GenericArity));
        }
        foreach (var db in DbContexts)
        {
            if (db.SpineId?.Project.Equals(oldProject) == true)
                db.SpineId = ComponentId.FromSymbol(SymbolId.Create(newProject, db.Namespace, db.ContainingTypes, db.Name));
        }
        foreach (var n in Notifications)
        {
            if (n.SpineId?.Project.Equals(oldProject) == true)
                n.SpineId = ComponentId.FromSymbol(SymbolId.Create(newProject, n.Namespace, n.ContainingTypes, n.Name));
        }
        foreach (var nh in NotificationHandlers)
        {
            if (nh.SpineId?.Project.Equals(oldProject) == true)
                nh.SpineId = ComponentId.FromSymbol(SymbolId.Create(newProject, nh.Namespace, nh.ContainingTypes, nh.Name));
        }
        foreach (var ep in Endpoints)
        {
            if (ep.SpineId?.Project.Equals(oldProject) == true)
            {
                ep.SpineId = ep.SpineId.MappingSite is not null
                    ? EndpointId.Create(newProject, ep.SpineId.MappingSite, ep.SpineId.Ordinal)
                    : EndpointId.FromSyntheticSite(newProject, ep.SpineId.SiteKey, ep.SpineId.Ordinal);
            }
        }
    }

    private void OnEndpointAdded(EndpointInfo endpoint)
    {
        var projectId = IdentityResolver.ResolveProjectId(endpoint.ProjectName);
        EndpointId epId;
        if (endpoint.Location is not null)
        {
            var normalized = endpoint.Location.File.Replace('\\', '/');
            var site = normalized == endpoint.Location.File
                ? endpoint.Location
                : endpoint.Location with { File = normalized };
            var ordinal = Spine.NextEndpointOrdinal(projectId, site.ToString());
            epId = EndpointId.Create(projectId, site, ordinal);
        }
        else
        {
            var siteKey = $"{endpoint.HttpMethod} {endpoint.Route}|{endpoint.Name}|{endpoint.TargetRequestFullName ?? endpoint.TargetRequestName}";
            var ordinal = Spine.NextEndpointOrdinal(projectId, siteKey);
            epId = EndpointId.FromSyntheticSite(projectId, siteKey, ordinal);
        }

        endpoint.SpineId = epId;
        var occurrence = SpineIdentityResolver.SyntheticOccurrence(
            EvidenceRule.EndpointMapping,
            EvidenceOrigin.MappingOrAttribute,
            endpoint.Location,
            endpoint.IsInferred ? EvidenceStrength.Inferred : EvidenceStrength.Proven,
            scopeProjectName: endpoint.ProjectName);

        Spine.AddEndpoint(new EndpointNode(
            epId,
            endpoint.Name,
            endpoint.HttpMethod,
            endpoint.Route,
            endpoint.VerbStrength,
            endpoint.RouteStrength,
            occurrence));
    }

    private void OnServiceResolutionAdded(ServiceResolution resolution)
    {
        if (resolution.IsAmbiguous || string.IsNullOrWhiteSpace(resolution.Implementation))
        {
            if (resolution.IsAmbiguous)
            {
                var hasExplicitRegistrations = false;
                for (var i = 0; i < resolution.Candidates.Count; i++)
                {
                    if (resolution.Candidates[i].FromRegistration)
                    {
                        hasExplicitRegistrations = true;
                        break;
                    }
                }

                var candidates = hasExplicitRegistrations
                    ? resolution.Candidates.Where(c => c.FromRegistration).ToArray()
                    : resolution.Candidates.ToArray();

                Spine.Unproven.AddAmbiguousResolution(new AmbiguousResolutionEntry(
                    resolution.Interface,
                    resolution.InterfaceName,
                    candidates,
                    resolution.InterfaceLocation));
            }
            return;
        }

        var impl = resolution.Implementation;
        var chosen = resolution.Candidates.FirstOrDefault(c => string.Equals(c.Type, impl, StringComparison.Ordinal));
        var from = IdentityResolver.ResolveInterfaceRef(resolution.Interface, resolution.InterfaceLocation);
        var implProject = chosen?.Location is not null
            ? IdentityResolver.ProjectNameFor(
                IdentityResolver.ResolveProjectForLocation(chosen.Location)
                ?? IdentityResolver.ResolveProjectId(null))
            : null;
        var to = IdentityResolver.ResolveTypeOrComponentRef(impl, implProject);

        switch (resolution.Source)
        {
            case ServiceResolutionSource.Declaration:
                Spine.AddOrMergeRelation(
                    RelationKind.ResolvesTo,
                    from,
                    to,
                    new EvidenceOccurrence(
                        EvidenceRule.ServiceDeclarationCandidate,
                        EvidenceOrigin.Declaration,
                        chosen?.Location ?? resolution.InterfaceLocation,
                        EvidenceStrength.Inferred,
                        locationOmittedReason: (chosen?.Location ?? resolution.InterfaceLocation) is null
                            ? "Service declaration candidate without source location"
                            : null,
                        resolutionSemantics: ResolutionSemantics.StaticCandidate,
                        scopeProjectName: implProject));
                break;

            case ServiceResolutionSource.Registration:
            {
                var regLoc = chosen?.RegistrationLocation ?? chosen?.Location ?? resolution.InterfaceLocation;
                Spine.AddOrMergeRelation(
                    RelationKind.ResolvesTo,
                    from,
                    to,
                    new EvidenceOccurrence(
                        EvidenceRule.ServiceRegistrationBinding,
                        EvidenceOrigin.Registration,
                        regLoc,
                        EvidenceStrength.Proven,
                        locationOmittedReason: regLoc is null
                            ? "Service registration binding without source location"
                            : null,
                        resolutionSemantics: ResolutionSemantics.DeclaredBinding,
                        scopeProjectName: implProject));
                break;
            }

            case ServiceResolutionSource.Both:
            {
                var regLoc = chosen?.RegistrationLocation ?? chosen?.Location ?? resolution.InterfaceLocation;
                Spine.AddOrMergeRelation(
                    RelationKind.ResolvesTo,
                    from,
                    to,
                    new EvidenceOccurrence(
                        EvidenceRule.ServiceConfirmedBinding,
                        EvidenceOrigin.Registration,
                        regLoc,
                        EvidenceStrength.Proven,
                        locationOmittedReason: regLoc is null
                            ? "Confirmed service binding without source location"
                            : null,
                        resolutionSemantics: ResolutionSemantics.Confirmed,
                        scopeProjectName: implProject));

                if (chosen?.Location is not null && !Equals(chosen.Location, regLoc))
                {
                    Spine.AddOrMergeRelation(
                        RelationKind.ResolvesTo,
                        from,
                        to,
                        new EvidenceOccurrence(
                            EvidenceRule.ServiceDeclarationCandidate,
                            EvidenceOrigin.Declaration,
                            chosen.Location,
                            EvidenceStrength.Inferred,
                            resolutionSemantics: ResolutionSemantics.StaticCandidate,
                            scopeProjectName: implProject));
                }
                break;
            }
        }
    }
}
