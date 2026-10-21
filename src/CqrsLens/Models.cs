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

    public string? ProjectName { get; set; }
    public SourceLocation? Location { get; set; }
}

public sealed class BehaviorInfo
{
    public required string Name { get; init; }
    public string? ProjectName { get; set; }
    public SourceLocation? Location { get; set; }
}

public sealed record DbSetInfo(string Entity, string Property);

public sealed class DbContextInfo
{
    public required string Name { get; init; }
    public string Namespace { get; set; } = "";
    public List<string> ContainingTypes { get; } = new();
    public string? ProjectName { get; set; }
    public SourceLocation? Location { get; set; }
    public List<DbSetInfo> DbSets { get; } = new();
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

    internal void RecordDiagnostic(AnalysisDiagnostic diagnostic)
    {
        if (!Diagnostics.Contains(diagnostic))
            Diagnostics.Add(diagnostic);
    }
}
