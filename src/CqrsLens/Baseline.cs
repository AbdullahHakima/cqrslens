using System.Text.Json;
using System.Text.Json.Serialization;

namespace CqrsLens;

/// <summary>A baseline error that should be reported as a CLI usage failure.</summary>
public sealed class BaselineException : Exception
{
    public BaselineException(string message) : base(message)
    {
    }
}

/// <summary>On-disk baseline document. Kept small and deterministic so it is reviewable in git.</summary>
public sealed class BaselineDocument
{
    public int? SchemaVersion { get; set; }

    public List<BaselineEntry> Findings { get; set; } = new();
}

public sealed class BaselineEntry
{
    public string Code { get; set; } = "";
    public string? Target { get; set; }
    public string? File { get; set; }
    public string? Project { get; set; }
    public string? Severity { get; set; }
    public string? Message { get; set; }
    public int? Line { get; set; }
    public int? Column { get; set; }
}

/// <summary>
/// Reads, writes and applies architecture-check baselines. Matching uses the
/// stable key in <see cref="CheckFinding.BaselineKey"/> rather than messages or
/// line numbers, so refactors and message wording changes keep old findings
/// known while a new symbol/file is correctly treated as new.
/// </summary>
public static class Baseline
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

    public static string CreateKey(string code, string? target, string? file)
    {
        var normalizedFile = (file ?? "")
            .Replace('\\', '/')
            .Trim()
            .TrimStart('/')
            .ToLowerInvariant();
        return $"{code}\u001f{target ?? ""}\u001f{normalizedFile}";
    }

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

    public static void Write(AnalysisResult result, string path)
    {
        if (Directory.Exists(path))
            throw new BaselineException($"baseline path is a directory: {path}");

        var document = new BaselineDocument
        {
            SchemaVersion = CurrentSchemaVersion,
            Findings = result.Checks
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

    /// <summary>Marks the current checks as known after generating a new baseline.</summary>
    public static void MarkAllKnown(AnalysisResult result, string path)
    {
        for (var i = 0; i < result.Checks.Count; i++)
            result.Checks[i] = result.Checks[i] with { IsKnown = true };

        result.BaselineApplied = true;
        result.BaselinePath = path;
    }

    public static void Apply(AnalysisResult result, BaselineDocument document, string path)
    {
        var remaining = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in document.Findings)
        {
            var key = CreateKey(entry.Code, entry.Target, entry.File);
            remaining[key] = remaining.TryGetValue(key, out var count) ? count + 1 : 1;
        }

        for (var i = 0; i < result.Checks.Count; i++)
        {
            var finding = result.Checks[i];
            if (remaining.TryGetValue(finding.BaselineKey, out var count) && count > 0)
            {
                remaining[finding.BaselineKey] = count - 1;
                result.Checks[i] = finding with { IsKnown = true };
            }
        }

        result.BaselineApplied = true;
        result.BaselinePath = path;
    }
}
