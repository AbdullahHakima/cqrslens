using System.Text.Json;

namespace CqrsLens;

/// <summary>Reads and validates a JSON report produced by <see cref="JsonReportGenerator"/>.</summary>
internal static class JsonReportDocumentReader
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    internal static JsonReportDocument Read(string path)
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
        if (document.SchemaVersion != JsonReportGenerator.CurrentSchemaVersion)
        {
            throw new DiffException(
                $"unsupported diff JSON schema version {document.SchemaVersion} in '{path}' " +
                $"- expected {JsonReportGenerator.CurrentSchemaVersion}; regenerate it with --format json.");
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
        document.Notifications ??= new List<JsonNotification>();
        document.NotificationHandlers ??= new List<JsonNotificationHandler>();
        foreach (var notification in document.Notifications)
            notification.Handlers ??= new List<string>();

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
}
