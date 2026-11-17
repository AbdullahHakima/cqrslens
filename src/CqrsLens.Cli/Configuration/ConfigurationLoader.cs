using System.Text.Json;
using System.Text.Json.Serialization;

namespace CqrsLens;

/// <summary>
/// Reads and deserializes .noctua.json (with permanent .cqrslens.json fallback). By default the file
/// is looked up only at the scan root so a parent repository's config cannot silently affect a
/// nested scan. --config can point at an explicit file.
/// </summary>
public static class ConfigurationLoader
{
    public const string FileName = ".noctua.json";
    public const string LegacyFileName = ".cqrslens.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static CqrsLensSettings Load(string scanRoot, string? explicitPath = null)
    {
        var (path, bothPresent) = ResolvePath(scanRoot, explicitPath);
        if (path is null)
            return CqrsLensSettings.Empty;

        CqrsLensConfiguration? config;
        try
        {
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
                throw new ConfigurationException($"config file is empty: {path}");

            config = JsonSerializer.Deserialize<CqrsLensConfiguration>(json, JsonOptions);
        }
        catch (ConfigurationException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            throw new ConfigurationException($"invalid config '{path}': {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ConfigurationException($"could not read config '{path}': {ex.Message}");
        }

        if (config is null)
            throw new ConfigurationException($"config file is empty: {path}");

        return ConfigurationValidator.Validate(path, config, bothPresent);
    }

    private static (string? Path, bool BothPresent) ResolvePath(string scanRoot, string? explicitPath)
    {
        if (explicitPath is null)
        {
            var primaryPath = Path.Combine(scanRoot, FileName);
            var legacyPath = Path.Combine(scanRoot, LegacyFileName);
            var hasPrimary = File.Exists(primaryPath);
            var hasLegacy = File.Exists(legacyPath);

            if (hasPrimary)
                return (primaryPath, hasLegacy);
            if (hasLegacy)
                return (legacyPath, false);
            return (null, false);
        }

        string path;
        try
        {
            path = Path.GetFullPath(explicitPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ConfigurationException($"invalid config path '{explicitPath}': {ex.Message}");
        }

        return File.Exists(path)
            ? (path, false)
            : throw new ConfigurationException($"config file not found: {explicitPath}");
    }
}
