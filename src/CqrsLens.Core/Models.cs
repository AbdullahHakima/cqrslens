namespace CqrsLens;

/// <summary>A 1-based source location. File paths are relative to the scan root when possible.</summary>
public sealed record SourceLocation(string File, int Line, int Column)
{
    public string FilePath => File;

    public override string ToString() => $"{File}:{Line}:{Column}";

    public static SourceLocation? Create(
        string? filePath,
        string rootPath,
        string code,
        int index)
    {
        if (string.IsNullOrEmpty(filePath) || index < 0 || index > code.Length)
            return null;

        var line = 1;
        var lineStart = 0;
        for (var i = 0; i < index; i++)
        {
            if (code[i] != '\n') continue;
            line++;
            lineStart = i + 1;
        }

        return new SourceLocation(
            RelativeFile(filePath, rootPath),
            line,
            index - lineStart + 1);
    }

    public static SourceLocation? FromLine(
        string? filePath,
        string rootPath,
        int line,
        int column)
    {
        if (string.IsNullOrEmpty(filePath))
            return null;

        return new SourceLocation(
            RelativeFile(filePath, rootPath),
            Math.Max(1, line),
            Math.Max(1, column));
    }

    private static string RelativeFile(string filePath, string rootPath)
    {
        var relative = Path.GetRelativePath(rootPath, filePath)
            .Replace(Path.DirectorySeparatorChar, '/');
        return relative;
    }
}

public enum AnalysisDiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record AnalysisDiagnostic(
    string Code,
    AnalysisDiagnosticSeverity Severity,
    string Message,
    SourceLocation? Location = null,
    string? ProjectName = null)
{
    public string? Project => ProjectName;
}

/// <summary>One measured analysis stage.</summary>
public sealed record AnalysisPhaseTiming(string Name, double ElapsedMilliseconds, long AllocatedBytes);

/// <summary>Progress snapshot emitted during solution scanning.</summary>
public readonly record struct ScanProgress(
    string Phase,
    int Completed,
    int Total,
    string? Detail = null);

public sealed class ProjectInfo
{
    public required string Name { get; init; }
    public required string FilePath { get; init; }
    public required string DirectoryPath { get; init; }
    public string Layer { get; set; } = "Other";

    // Raw Include values as written in the .csproj.
    public List<string> ReferenceIncludes { get; } = new();

    // ReferenceIncludes resolved to project names found in the same scan.
    public List<string> ReferenceNames { get; } = new();

    // PackageReference Include values evaluated from Directory.Build.props/targets and .csproj.
    public List<string> PackageReferences { get; } = new();

    // Global <Using> items evaluated from Directory.Build.props/targets and .csproj.
    public List<UsingDirective> ProjectUsings { get; } = new();

    // True when <ImplicitUsings> is enable/true in Directory.Build.props/targets or .csproj.
    public bool ImplicitUsings { get; set; }
}

public sealed class CqrsRequest
{
    public required string Name { get; init; }
    public string Namespace { get; set; } = "";
    public List<string> ContainingTypes { get; } = new();
    public string FullName
    {
        get
        {
            var typePath = ContainingTypes.Count == 0 ? Name : string.Join(".", ContainingTypes) + "." + Name;
            return string.IsNullOrEmpty(Namespace) ? typePath : $"{Namespace}.{typePath}";
        }
    }

    public string? ResponseType { get; set; }
    public string? ProjectName { get; set; }
    public SourceLocation? Location { get; set; }

    /// <summary>
    /// Marker interface through which this request was discovered when it does not
    /// directly implement IRequest (null for direct IRequest requests).
    /// </summary>
    public string? ViaMarker { get; set; }

    private string? _kindOverride;

    public string Kind
    {
        get
        {
            if (_kindOverride is not null) return _kindOverride;
            return Name.EndsWith("Command", StringComparison.Ordinal) ? "Command" :
                   Name.EndsWith("Query", StringComparison.Ordinal) ? "Query" : "Request";
        }
        set => _kindOverride = value;
    }

    public List<string> Handlers { get; } = new();
    public List<string> Validators { get; } = new();
    public List<RequestPropertyInfo> Properties { get; } = new();
    public List<string> Entrypoints { get; } = new();
    public List<string> TouchedEntities { get; } = new();
    public List<string> PublishedNotifications { get; } = new();
    public List<string> DispatchedRequests { get; } = new();
}

public sealed record RequestPropertyInfo(string Name, string Type);

public sealed class EndpointInfo
{
    public required string Name { get; init; }
    public required string HttpMethod { get; init; }
    public required string Route { get; init; }
    public required string TargetRequestName { get; init; }
    public string? TargetRequestNamespace { get; set; }

    /// <summary>
    /// Full name of the request this endpoint dispatches, when it could be resolved.
    /// Simple names alone are ambiguous across modules with same-named commands.
    /// </summary>
    public string? TargetRequestFullName { get; set; }

    public string? ProjectName { get; set; }
    public SourceLocation? Location { get; set; }

    /// <summary>
    /// True when the route or HTTP verb was guessed from a naming convention (e.g. an
    /// EndpointGroup's class name, a verb implied by Query/Command) rather than read from source.
    /// Agent-facing output must flag these so consumers do not treat them as facts.
    /// </summary>
    public bool IsInferred { get; init; }

    public string DisplayLabel => string.IsNullOrEmpty(Route)
        ? $"{HttpMethod} ({Name})"
        : $"{HttpMethod} {Route}";
}

public sealed class CqrsNotification
{
    public required string Name { get; init; }
    public string Namespace { get; set; } = "";
    public List<string> ContainingTypes { get; } = new();
    public string FullName
    {
        get
        {
            var typePath = ContainingTypes.Count == 0 ? Name : string.Join(".", ContainingTypes) + "." + Name;
            return string.IsNullOrEmpty(Namespace) ? typePath : $"{Namespace}.{typePath}";
        }
    }

    public string? ProjectName { get; set; }
    public SourceLocation? Location { get; set; }

    /// <summary>
    /// Marker interface or base type through which this notification was discovered when it
    /// does not directly implement INotification (e.g. IDomainEvent or DomainEventBase).
    /// </summary>
    public string? ViaMarker { get; set; }

    public List<string> Handlers { get; } = new();
}

public sealed class NotificationHandlerInfo
{
    public required string Name { get; init; }
    public string Namespace { get; set; } = "";
    public List<string> ContainingTypes { get; } = new();
    public string FullName
    {
        get
        {
            var typePath = ContainingTypes.Count == 0 ? Name : string.Join(".", ContainingTypes) + "." + Name;
            return string.IsNullOrEmpty(Namespace) ? typePath : $"{Namespace}.{typePath}";
        }
    }

    public required string NotificationName { get; init; }
    public List<string> Dependencies { get; } = new();
    public List<string> TouchedEntities { get; } = new();
    public List<string> PublishedNotifications { get; } = new();
    public List<string> DispatchedRequests { get; } = new();
    public string? NotificationNamespace { get; set; }

    /// <summary>Namespaces imported by the declaring file, used by the linker.</summary>
    internal List<string> ImportedNamespaces { get; } = new();

    public string? ProjectName { get; set; }
    public SourceLocation? Location { get; set; }
}

public sealed class HandlerInfo
{
    public required string Name { get; init; }
    public string Namespace { get; set; } = "";
    public List<string> ContainingTypes { get; } = new();
    public string FullName
    {
        get
        {
            var typePath = ContainingTypes.Count == 0 ? Name : string.Join(".", ContainingTypes) + "." + Name;
            return string.IsNullOrEmpty(Namespace) ? typePath : $"{Namespace}.{typePath}";
        }
    }
    public required string RequestName { get; init; }
    public List<string> Dependencies { get; } = new();
    public List<string> TouchedEntities { get; } = new();
    public List<string> PublishedNotifications { get; } = new();
    public List<string> DispatchedRequests { get; } = new();

    // Set when the request name in IRequestHandler<...> could be resolved to a namespace.
    public string? RequestNamespace { get; set; }

    /// <summary>Namespaces imported by the declaring file, used by the linker.</summary>
    internal List<string> ImportedNamespaces { get; } = new();

    public string? ProjectName { get; set; }
    public SourceLocation? Location { get; set; }
}

public sealed class ValidatorInfo
{
    public required string Name { get; init; }
    public string Namespace { get; set; } = "";
    public List<string> ContainingTypes { get; } = new();
    public string FullName
    {
        get
        {
            var typePath = ContainingTypes.Count == 0 ? Name : string.Join(".", ContainingTypes) + "." + Name;
            return string.IsNullOrEmpty(Namespace) ? typePath : $"{Namespace}.{typePath}";
        }
    }
    public required string TargetType { get; init; }

    // Set when the validated type name could be resolved to a namespace.
    public string? TargetNamespace { get; set; }

    /// <summary>Namespaces imported by the declaring file, used by the linker.</summary>
    internal List<string> ImportedNamespaces { get; } = new();

    public string? ProjectName { get; set; }
    public SourceLocation? Location { get; set; }
}

public sealed class BehaviorInfo
{
    public required string Name { get; init; }
    public string? ProjectName { get; set; }
    public SourceLocation? Location { get; set; }
}

public sealed record DbSetInfo(string Entity, string Property)
{
    /// <summary>
    /// Full source name of the entity when it resolves to a scanned class. This is
    /// informational: <see cref="Entity"/> remains the type name as written in
    /// the DbSet so existing consumers keep their current behavior.
    /// </summary>
    public string? EntityFullName { get; set; }
}

/// <summary>
/// One property on an EF Core entity. Scalar properties become ER attributes;
/// navigation properties become relationships and are omitted from attributes.
/// </summary>
public sealed class EntityPropertyInfo
{
    public required string Name { get; init; }

    /// <summary>The property type as written (generics preserved for navigations).</summary>
    public required string Type { get; init; }

    /// <summary>True for a primary-key property (including one part of a composite key).</summary>
    public bool IsKey { get; set; }

    /// <summary>True when the property is used as a foreign key by a resolved relationship.</summary>
    public bool IsForeignKey { get; set; }

    public bool IsNavigation { get; init; }
    public bool IsCollection { get; init; }

    /// <summary>Full source name of the navigation target, when it resolves.</summary>
    public string? TargetEntity { get; init; }

    /// <summary>
    /// EF requiredness when it can be established from a value type, a data
    /// annotation or explicit configuration. Reference navigation requiredness is
    /// never guessed from naming alone.
    /// </summary>
    public bool IsRequired { get; set; }

    public SourceLocation? Location { get; init; }

    /// <summary>Raw data-annotation payloads used during model construction.</summary>
    internal List<string> Attributes { get; } = new();
}

/// <summary>An entity class in one EF Core model.</summary>
public sealed class EntityInfo
{
    public required string Name { get; init; }
    public string Namespace { get; set; } = "";
    public List<string> ContainingTypes { get; } = new();
    public string? ProjectName { get; set; }
    public SourceLocation? Location { get; set; }

    public string FullName
    {
        get
        {
            var typePath = ContainingTypes.Count == 0 ? Name : string.Join(".", ContainingTypes) + "." + Name;
            return string.IsNullOrEmpty(Namespace) ? typePath : $"{Namespace}.{typePath}";
        }
    }

    public List<EntityPropertyInfo> Properties { get; } = new();
}

public enum EntityRelationshipKind
{
    OneToOne,
    OneToMany,
    ManyToMany,
}

/// <summary>
/// A relationship between two entities. For one-to-many, <see cref="PrincipalEntity"/>
/// is the "one" side and <see cref="DependentEntity"/> is the "many" side. For
/// one-to-one the principal/dependent orientation comes from the configured or
/// conventional foreign key; many-to-many has no meaningful dependent side.
/// </summary>
public sealed record EntityRelationshipInfo(
    string PrincipalEntity,
    string DependentEntity,
    string? PrincipalNavigation,
    string? DependentNavigation,
    string? ForeignKey,
    bool IsRequired,
    EntityRelationshipKind Kind,
    string? ConfiguredBy = null);

public sealed class DbContextInfo
{
    public required string Name { get; init; }
    public string Namespace { get; set; } = "";
    public List<string> ContainingTypes { get; } = new();
    public string? ProjectName { get; set; }
    public SourceLocation? Location { get; set; }
    public List<DbSetInfo> DbSets { get; } = new();

    /// <summary>Entities reachable from this context's DbSets through navigations.</summary>
    public List<EntityInfo> Entities { get; } = new();

    /// <summary>Relationships discovered for <see cref="Entities"/>.</summary>
    public List<EntityRelationshipInfo> Relationships { get; } = new();
}

public enum CheckSeverity
{
    Warning,
    Error,
}

public sealed record CheckFinding(
    CheckSeverity Severity,
    string Code,
    string Message,
    string? Target = null,
    SourceLocation? Location = null,
    string? ProjectName = null,
    bool IsKnown = false)
{
    /// <summary>
    /// Stable baseline identity: rule code + target + root-relative file. Line
    /// numbers deliberately do not participate, so moving code does not turn a
    /// known finding into a new one.
    /// </summary>
    public string BaselineKey =>
        Baseline.CreateKey(Code, Target, Location?.File);
}

public sealed class AnalysisResult
{
    public required string RootPath { get; init; }
    public List<ProjectInfo> Projects { get; } = new();
    public List<CqrsRequest> Requests { get; } = new();
    public List<HandlerInfo> Handlers { get; } = new();
    public List<ValidatorInfo> Validators { get; } = new();
    public List<BehaviorInfo> Behaviors { get; } = new();
    public List<DbContextInfo> DbContexts { get; } = new();
    public List<CqrsNotification> Notifications { get; } = new();
    public List<NotificationHandlerInfo> NotificationHandlers { get; } = new();
    public List<EndpointInfo> Endpoints { get; } = new();

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
