namespace CqrsLens;

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

    // Set when the request name in IRequestHandler<...> could be resolved to a namespace.
    public string? RequestNamespace { get; set; }

    public string? ProjectName { get; set; }
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
    public string Namespace { get; set; } = "";
    public List<string> ContainingTypes { get; } = new();
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

    public List<CheckFinding> Checks { get; } = new();

    /// <summary>Source files discovered before any read/scan failures.</summary>
    public int DiscoveredFileCount { get; set; }

    /// <summary>Source files successfully read and scanned.</summary>
    public int FileCount { get; set; }

    /// <summary>Root-relative paths that could not be read or scanned.</summary>
    public List<string> SkippedFiles { get; } = new();

    public long ElapsedMilliseconds { get; set; }

    internal void RecordSkippedFile(string path)
    {
        var relative = Path.GetRelativePath(RootPath, path)
            .Replace(Path.DirectorySeparatorChar, '/');
        if (!SkippedFiles.Contains(relative, StringComparer.OrdinalIgnoreCase))
            SkippedFiles.Add(relative);
    }
}
