using System.Text.Json;
using System.Text.Json.Serialization;

namespace CqrsLens;

/// <summary>Reads and writes the on-disk baseline document.</summary>
public static class BaselineStore
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
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static BaselineDocument Read(string path)
    {
        if (Directory.Exists(path))
            throw new BaselineException($"baseline path is a directory: {path}");
        if (!File.Exists(path))
            throw new BaselineException($"baseline file not found: {path}");

        BaselineDocument? document;
        try
        {
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
                throw new BaselineException($"baseline file is empty: {path}");
            document = JsonSerializer.Deserialize<BaselineDocument>(json, ReadOptions);
        }
        catch (BaselineException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            throw new BaselineException($"invalid baseline '{path}': {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new BaselineException($"could not read baseline '{path}': {ex.Message}");
        }

        if (document is null)
            throw new BaselineException($"baseline file is empty: {path}");
        if (document.SchemaVersion is null)
            throw new BaselineException($"invalid baseline '{path}': missing schemaVersion.");
        if (document.SchemaVersion != CurrentSchemaVersion)
        {
            throw new BaselineException(
                $"unsupported baseline schema version {document.SchemaVersion} in '{path}' " +
                $"- expected {CurrentSchemaVersion}; regenerate it with --write-baseline.");
        }

        document.Findings ??= new List<BaselineEntry>();
        foreach (var finding in document.Findings)
        {
            if (string.IsNullOrWhiteSpace(finding.Code))
                throw new BaselineException($"invalid baseline '{path}': every finding needs a code.");
        }

        return document;
    }

    public static void Write(IReadOnlyList<CheckFinding> checks, string path)
    {
        if (Directory.Exists(path))
            throw new BaselineException($"baseline path is a directory: {path}");

        var document = new BaselineDocument
        {
            SchemaVersion = CurrentSchemaVersion,
            Findings = checks
                .OrderBy(c => c.BaselineKey, StringComparer.Ordinal)
                .ThenBy(c => c.Message, StringComparer.Ordinal)
                .Select(c => new BaselineEntry
                {
                    Code = c.Code,
                    Target = c.Target,
                    File = c.Location?.File,
                    Project = c.ProjectName,
                    Severity = c.Severity == CheckSeverity.Error ? "error" : "warning",
                    Message = c.Message,
                    Line = c.Location?.Line,
                    Column = c.Location?.Column,
                })
                .ToList(),
        };

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(
                path,
                JsonSerializer.Serialize(document, WriteOptions) + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new BaselineException($"could not write baseline '{path}': {ex.Message}");
        }
    }
}
