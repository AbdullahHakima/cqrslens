using System.Text.Json;
using System.Text.Json.Serialization;

namespace CqrsLens;

/// <summary>
/// Generates a deterministic, machine-readable JSON architecture report
/// (<c>--format json</c>) for external dashboards, diffing tools, and custom
/// automation.
/// </summary>
public static class JsonReportGenerator
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string Generate(AnalysisResult result)
    {
        var document = CreateDocument(result);
        return JsonSerializer.Serialize(document, WriteOptions) + "\n";
    }

    internal static JsonReportDocument CreateDocument(AnalysisResult result)
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
            SchemaVersion = CurrentSchemaVersion,
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
                Errors = errorCount,
                Warnings = warningCount,
                KnownFindings = knownCount,
                Diagnostics = result.Diagnostics.Count,
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
        };
    }

    internal static JsonReportDocument ReadDocument(string path)
    {
        if (!File.Exists(path))
            throw new DiffException($"diff file not found: {path}");

        JsonReportDocument? document;
        try
        {
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
                throw new DiffException($"diff file is empty: {path}");
            document = JsonSerializer.Deserialize<JsonReportDocument>(json, ReadOptions);
        }
        catch (DiffException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            throw new DiffException($"invalid diff JSON '{path}': {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DiffException($"could not read diff file '{path}': {ex.Message}");
        }

        if (document is null)
            throw new DiffException($"diff file is empty: {path}");
        if (document.SchemaVersion is null)
            throw new DiffException($"invalid diff JSON '{path}': missing schemaVersion.");
        if (document.SchemaVersion != CurrentSchemaVersion)
        {
            throw new DiffException(
                $"unsupported diff JSON schema version {document.SchemaVersion} in '{path}' " +
                $"- expected {CurrentSchemaVersion}; regenerate it with --format json.");
        }

        document.Summary ??= new JsonSummary();
        document.Projects ??= new List<JsonProject>();
        document.Requests ??= new List<JsonRequest>();
        document.Handlers ??= new List<JsonHandler>();
        document.Validators ??= new List<JsonValidator>();
        document.Behaviors ??= new List<JsonBehavior>();
        document.DbContexts ??= new List<JsonDbContext>();
        document.Checks ??= new List<JsonCheckFinding>();
        document.Diagnostics ??= new List<JsonDiagnostic>();
        document.SkippedFiles ??= new List<string>();

        foreach (var project in document.Projects)
        {
            project.References ??= new List<string>();
            project.Packages ??= new List<string>();
        }

        foreach (var request in document.Requests)
        {
            request.Handlers ??= new List<string>();
            request.Validators ??= new List<string>();
        }

        foreach (var context in document.DbContexts)
        {
            context.DbSets ??= new List<JsonDbSet>();
            context.Entities ??= new List<JsonEntity>();
            context.Relationships ??= new List<JsonEntityRelationship>();
            foreach (var entity in context.Entities)
                entity.Properties ??= new List<JsonEntityProperty>();
        }

        return document;
    }

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

    internal sealed class JsonReportDocument
    {
        public int? SchemaVersion { get; set; }
        public string ToolVersion { get; set; } = "";
        public string Scanner { get; set; } = "";
        public string GeneratedAt { get; set; } = "";
        public long ElapsedMilliseconds { get; set; }
        public string RootName { get; set; } = "";
        public int DiscoveredFileCount { get; set; }
        public int FileCount { get; set; }
        public string? Watermark { get; set; }
        public List<string>? Conventions { get; set; }
        public bool BaselineApplied { get; set; }
        public bool CanRunStrictChecks { get; set; }
        public JsonSummary Summary { get; set; } = new();
        public List<JsonProject> Projects { get; set; } = new();
        public List<JsonRequest> Requests { get; set; } = new();
        public List<JsonHandler> Handlers { get; set; } = new();
        public List<JsonValidator> Validators { get; set; } = new();
        public List<JsonBehavior> Behaviors { get; set; } = new();
        public List<JsonDbContext> DbContexts { get; set; } = new();
        public List<JsonCheckFinding> Checks { get; set; } = new();
        public List<JsonDiagnostic> Diagnostics { get; set; } = new();
        public List<string> SkippedFiles { get; set; } = new();
    }

    internal sealed class JsonSummary
    {
        public int Projects { get; set; }
        public int Requests { get; set; }
        public int Handlers { get; set; }
        public int Validators { get; set; }
        public int Behaviors { get; set; }
        public int DbContexts { get; set; }
        public int Errors { get; set; }
        public int Warnings { get; set; }
        public int KnownFindings { get; set; }
        public int Diagnostics { get; set; }
    }

    internal sealed class JsonProject
    {
        public string Name { get; set; } = "";
        public string Layer { get; set; } = "";
        public List<string> References { get; set; } = new();
        public List<string> Packages { get; set; } = new();
    }

    internal sealed class JsonRequest
    {
        public string Name { get; set; } = "";
        public string? Namespace { get; set; }
        public string FullName { get; set; } = "";
        public string Kind { get; set; } = "";
        public string? ResponseType { get; set; }
        public string? ViaMarker { get; set; }
        public string? Project { get; set; }
        public List<string> Handlers { get; set; } = new();
        public List<string> Validators { get; set; } = new();
        public JsonLocation? Location { get; set; }
    }

    internal sealed class JsonHandler
    {
        public string Name { get; set; } = "";
        public string? Namespace { get; set; }
        public string FullName { get; set; } = "";
        public string RequestName { get; set; } = "";
        public string? Project { get; set; }
        public JsonLocation? Location { get; set; }
    }

    internal sealed class JsonValidator
    {
        public string Name { get; set; } = "";
        public string? Namespace { get; set; }
        public string FullName { get; set; } = "";
        public string TargetType { get; set; } = "";
        public string? Project { get; set; }
        public JsonLocation? Location { get; set; }
    }

    internal sealed class JsonBehavior
    {
        public string Name { get; set; } = "";
        public string? Project { get; set; }
        public JsonLocation? Location { get; set; }
    }

    internal sealed class JsonDbContext
    {
        public string Name { get; set; } = "";
        public string? Namespace { get; set; }
        public string? Project { get; set; }
        public JsonLocation? Location { get; set; }
        public List<JsonDbSet> DbSets { get; set; } = new();
        public List<JsonEntity> Entities { get; set; } = new();
        public List<JsonEntityRelationship> Relationships { get; set; } = new();
    }

    internal sealed class JsonDbSet
    {
        public string Property { get; set; } = "";
        public string Entity { get; set; } = "";
        public string? EntityFullName { get; set; }
    }

    internal sealed class JsonEntity
    {
        public string Name { get; set; } = "";
        public string? Namespace { get; set; }
        public string FullName { get; set; } = "";
        public string? Project { get; set; }
        public JsonLocation? Location { get; set; }
        public List<JsonEntityProperty> Properties { get; set; } = new();
    }

    internal sealed class JsonEntityProperty
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public bool IsKey { get; set; }
        public bool IsForeignKey { get; set; }
        public bool IsNavigation { get; set; }
        public bool IsCollection { get; set; }
        public string? TargetEntity { get; set; }
        public bool IsRequired { get; set; }
        public JsonLocation? Location { get; set; }
    }

    internal sealed class JsonEntityRelationship
    {
        public string PrincipalEntity { get; set; } = "";
        public string DependentEntity { get; set; } = "";
        public string? PrincipalNavigation { get; set; }
        public string? DependentNavigation { get; set; }
        public string? ForeignKey { get; set; }
        public bool IsRequired { get; set; }
        public string Kind { get; set; } = "";
        public string? ConfiguredBy { get; set; }
    }

    internal sealed class JsonCheckFinding
    {
        public string Code { get; set; } = "";
        public string Severity { get; set; } = "";
        public string Message { get; set; } = "";
        public string? Target { get; set; }
        public string? Project { get; set; }
        public bool Known { get; set; }
        public JsonLocation? Location { get; set; }
    }

    internal sealed class JsonDiagnostic
    {
        public string Code { get; set; } = "";
        public string Severity { get; set; } = "";
        public string Message { get; set; } = "";
        public string? Project { get; set; }
        public JsonLocation? Location { get; set; }
    }

    internal sealed class JsonLocation
    {
        public string File { get; set; } = "";
        public int Line { get; set; }
        public int Column { get; set; }
    }
}
