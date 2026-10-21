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
    public string? ResponseType { get; set; }
    public string? ProjectName { get; set; }

    public string Kind =>
        Name.EndsWith("Command", StringComparison.Ordinal) ? "Command" :
        Name.EndsWith("Query", StringComparison.Ordinal) ? "Query" : "Request";

    public List<string> Handlers { get; } = new();
    public List<string> Validators { get; } = new();
}

public sealed class HandlerInfo
{
    public required string Name { get; init; }
    public required string RequestName { get; init; }
    public string? ProjectName { get; set; }
}

public sealed class ValidatorInfo
{
    public required string Name { get; init; }
    public required string TargetType { get; init; }
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
    public string? ProjectName { get; set; }
    public List<DbSetInfo> DbSets { get; } = new();
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
}
