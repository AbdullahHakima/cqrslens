using System.Text.Json;
using System.Text.Json.Serialization;

namespace CqrsLens;

/// <summary>
/// Reads and deserializes .cqrslens.json. By default the file is looked up only
/// at the scan root so a parent repository's config cannot silently affect a
/// nested scan. --config can point at an explicit file.
/// </summary>
public static class ConfigurationLoader
{
    public const string FileName = ".cqrslens.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static CqrsLensSettings Load(string scanRoot, string? explicitPath = null)
    {
        var path = ResolvePath(scanRoot, explicitPath);
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

        return ConfigurationValidator.Validate(path, config);
    }

    private static string? ResolvePath(string scanRoot, string? explicitPath)
    {
        if (explicitPath is null)
        {
            var defaultPath = Path.Combine(scanRoot, FileName);
            return File.Exists(defaultPath) ? defaultPath : null;
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
            ? path
            : throw new ConfigurationException($"config file not found: {explicitPath}");
    }
}
