namespace CqrsLens;

public sealed class ProjectInfo
{
    public required string Name { get; init; }
    public required string FilePath { get; init; }
    public required string DirectoryPath { get; init; }
    public string Layer { get; set; } = "Other";

    /// <summary>Raw ProjectReference Include values from the .csproj.</summary>
    public List<string> ReferenceIncludes { get; } = new();

    /// <summary>Resolved names of referenced projects (only those found in the scan).</summary>
    public List<string> ReferenceNames { get; } = new();
}

public sealed class CqrsRequest
{
    public required string Name { get; init; }

    /// <summary>Namespace the request type was declared in ("" when none was found).</summary>
    public string Namespace { get; set; } = "";

    /// <summary>Fully-qualified name used for linking (Namespace.Name, or Name alone).</summary>
    public string FullName => string.IsNullOrEmpty(Namespace) ? Name : $"{Namespace}.{Name}";

    public string? ResponseType { get; set; }
    public string? ProjectName { get; set; }

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
}

public sealed class HandlerInfo
{
    public required string Name { get; init; }

    /// <summary>Namespace the handler class was declared in ("" when none was found).</summary>
    public string Namespace { get; set; } = "";

    /// <summary>Request type name as written in IRequestHandler&lt;...&gt; (simple or dotted).</summary>
    public required string RequestName { get; init; }

    /// <summary>Namespace the request name resolves to, when it can be determined.</summary>
    public string? RequestNamespace { get; set; }

    public string? ProjectName { get; set; }
}

public sealed class ValidatorInfo
{
    public required string Name { get; init; }

    /// <summary>Namespace the validator class was declared in ("" when none was found).</summary>
    public string Namespace { get; set; } = "";

    /// <summary>Validated type name as written (simple or dotted).</summary>
    public required string TargetType { get; init; }

    /// <summary>Namespace the target type resolves to, when it can be determined.</summary>
    public string? TargetNamespace { get; set; }

    public string? ProjectName { get; set; }
}

public sealed class BehaviorInfo
{
    public required string Name { get; init; }
    public string? ProjectName { get; set; }
}

public sealed record DbSetInfo(string Entity, string Property);

public sealed class DbContextInfo
{
    public required string Name { get; init; }

    /// <summary>Namespace the context class was declared in ("" when none was found).</summary>
    public string Namespace { get; set; } = "";

    public string? ProjectName { get; set; }
    public List<DbSetInfo> DbSets { get; } = new();
}

public enum CheckSeverity
{
    Warning,
    Error,
}

public sealed record CheckFinding(CheckSeverity Severity, string Code, string Message);

public sealed class AnalysisResult
{
    public required string RootPath { get; init; }
    public List<ProjectInfo> Projects { get; } = new();
    public List<CqrsRequest> Requests { get; } = new();
    public List<HandlerInfo> Handlers { get; } = new();
    public List<ValidatorInfo> Validators { get; } = new();
    public List<BehaviorInfo> Behaviors { get; } = new();
    public List<DbContextInfo> DbContexts { get; } = new();

    /// <summary>Architecture check findings, computed by <see cref="ArchitectureChecker"/>.</summary>
    public List<CheckFinding> Checks { get; } = new();

    /// <summary>Number of .cs files that were scanned.</summary>
    public int FileCount { get; set; }

    /// <summary>Wall-clock time the analysis took, in milliseconds.</summary>
    public long ElapsedMilliseconds { get; set; }
}
