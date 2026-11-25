using System.Security;
using System.Text.Json.Nodes;

namespace CqrsLens;

/// <summary>
/// Shared output formatting, directory discovery, and file loading helpers for
/// CLI modeling commands (<c>--proposal</c>, <c>--slice-design</c>, <c>--impact</c>,
/// and <c>--tdd-check</c>).
/// </summary>
internal static class ModelingOutputWriter
{
    private static readonly EnumerationOptions DiscoveryOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        ReturnSpecialDirectories = false,
    };

    public static string ResolveFormat(CliOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.Format))
            return options.Format;

        if (!string.IsNullOrWhiteSpace(options.Output))
        {
            if (options.Output.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
                options.Output.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
                return "html";

            if (options.Output.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                return "md";
        }

        return "json";
    }

    public static void Write(CliOptions options, TextWriter stdout, string content)
    {
        if (!string.IsNullOrWhiteSpace(options.Output))
        {
            OutputFileWriter.Write(Path.GetFullPath(options.Output), content);
            return;
        }

        stdout.WriteLine(content.TrimEnd());
    }

    public static List<(string FullPath, string DisplayPath)> DiscoverSliceDesignFiles(
        string fullDesignDir,
        string rootBase)
    {
        try
        {
            return Directory
                .EnumerateFiles(fullDesignDir, "*.slice.json", DiscoveryOptions)
                .Where(file => !CsprojParser.IsSkippedPath(file, fullDesignDir))
                .Select(file => (
                    FullPath: file,
                    DisplayPath: Path.GetRelativePath(rootBase, file).Replace('\\', '/')))
                .OrderBy(entry => entry.DisplayPath, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            throw new CliFailureException(
                $"failed to enumerate slice designs in '{fullDesignDir}': {ex.Message}",
                ExitCodes.Usage);
        }
    }

    public static bool IsWithinRoot(string rootPath, string candidatePath)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(rootPath), Path.GetFullPath(candidatePath));
        if (string.Equals(relative, ".", StringComparison.Ordinal))
            return true;

        return !Path.IsPathRooted(relative) &&
               !string.Equals(relative, "..", StringComparison.Ordinal) &&
               !relative.StartsWith("../", StringComparison.Ordinal) &&
               !relative.StartsWith("..\\", StringComparison.Ordinal);
    }

    public static SliceDesign ReadSliceDesign(string path)
    {
        try
        {
            var json = BoundedFileReader.ReadAllText(path);
            if (JsonNode.Parse(json) is not JsonObject root)
                throw new ArgumentException("Expected a slice design JSON object.");

            return SliceDesignInput.Read(root);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or ArgumentException)
        {
            throw new CliFailureException($"invalid slice design '{path}': {ex.Message}", ExitCodes.Usage);
        }
    }
}
