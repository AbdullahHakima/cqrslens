namespace CqrsLens;

public sealed class AnalysisResult
{
    public required string RootPath { get; init; }
    public List<ProjectInfo> Projects { get; } = new();
    public List<CqrsRequest> Requests { get; } = new();
    public List<HandlerInfo> Handlers { get; } = new();

    /// <summary>
    /// Simple names of request declarations discovered as open generics. An open
    /// generic type is never dispatched as-is (MediatR closes it per inner
    /// request), so it is not a concrete request and is kept out of
    /// <see cref="Requests"/>. The identity is still recorded so a validator that
    /// targets such a wrapper is not misreported as an orphan.
    /// </summary>
    internal HashSet<string> NonConcreteRequestNames { get; } = new(StringComparer.Ordinal);
    public List<ValidatorInfo> Validators { get; } = new();
    public List<BehaviorInfo> Behaviors { get; } = new();
    public List<DbContextInfo> DbContexts { get; } = new();
    public List<CqrsNotification> Notifications { get; } = new();
    public List<NotificationHandlerInfo> NotificationHandlers { get; } = new();
    public List<EndpointInfo> Endpoints { get; } = new();

    /// <summary>
    /// Resolved solution-wide flow edges (endpoint → request, request → handler,
    /// handler → request, handler → notification, handler → DbContext). Populated by
    /// the flow pass; the JSON report is the contract that exposes them.
    /// </summary>
    public List<FlowEdge> FlowEdges { get; } = new();

    /// <summary>
    /// Every resolved dispatch call site, including senders that are neither handlers
    /// nor endpoints. Drives production-reachability, so a service that sends a
    /// command still counts as a call site even though it is not a modelled edge.
    /// </summary>
    public List<DispatchCallSite> DispatchCallSites { get; } = new();

    /// <summary>
    /// Dispatch-looking call sites whose target type could not be resolved. Reported
    /// as the population that could reach shapes with no resolved call site.
    /// </summary>
    public List<UnresolvedDispatch> UnresolvedDispatches { get; } = new();

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

    /// <summary>One timestamp shared by every report artifact.</summary>
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Built-in mediator conventions detected in the source (e.g. Mediator, Wolverine).</summary>
    public List<string> Conventions { get; } = new();

    /// <summary>
    /// True when the caller supplied explicit project layer overrides, so the layer
    /// model is configured rather than name-guessed and must not be second-guessed
    /// by the confidence heuristic.
    /// </summary>
    internal bool HasConfiguredLayers { get; set; }

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
    }
}
