using System.Globalization;

namespace CqrsLens;

/// <summary>Testable command-line parsing and validation.</summary>
public static class CliParser
{
    private static readonly HashSet<string> SupportedFormats = new(StringComparer.Ordinal)
    {
        "md",
        "html",
        "json",
        "agent",
    };

    public static CliCommand Parse(string[] args)
    {
        try
        {
            return Parse(args, new ArgumentReader(args));
        }
        catch (CliParseException ex)
        {
            return new CliCommand(CliCommandKind.Error, Error: ex.Message, ShowUsage: ex.ShowUsage);
        }
    }

    private static CliCommand Parse(string[] args, ArgumentReader reader)
    {
        var options = new CliOptions();
        var helpRequested = false;
        var versionRequested = false;

        while (reader.MoveNext())
        {
            switch (reader.Current)
            {
                case "-h" or "--help":
                    helpRequested = true;
                    break;
                case "--version":
                    versionRequested = true;
                    break;
                case "-o" or "--output":
                    options.Output = reader.ReadValue("-o", "a value, e.g. -o ARCHITECTURE.md");
                    break;
                case "--config":
                    options.ConfigPath = reader.ReadValue("--config", "a value, e.g. --config .cqrslens.json");
                    break;
                case "--format":
                    options.Format = reader.ReadValue("--format", "a value: md, html, json or agent").ToLowerInvariant();
                    break;
                case "--slice":
                    options.SliceFilter = ReadSliceFilter(reader);
                    break;
                case "--render":
                    options.RenderMode = ParseRenderMode(reader.ReadValue("--render", "a value: mermaid or svg"));
                    options.RenderModeSpecified = true;
                    break;
                case "--analysis":
                    options.AnalysisMode = ParseAnalysisMode(reader.ReadValue("--analysis", "a value: text or roslyn"));
                    options.AnalysisModeSpecified = true;
                    break;
                case "--generated-at":
                    options.GeneratedAt = ReadGeneratedAt(reader);
                    break;
                case "--markers":
                    options.MarkersSpecified = true;
                    AddMarkerNames(options.Markers, reader.ReadValue("--markers", "a value, e.g. --markers ICommand,IQuery"));
                    break;
                case "--handler-markers":
                    options.HandlerMarkersSpecified = true;
                    AddMarkerNames(
                        options.HandlerMarkers,
                        reader.ReadValue("--handler-markers", "a value, e.g. --handler-markers ICommandHandler,IQueryHandler"));
                    break;
                case "--baseline":
                    options.BaselinePath = reader.ReadValue("--baseline", "a value, e.g. --baseline cqrslens-baseline.json");
                    break;
                case "--write-baseline":
                    options.WriteBaselinePath = reader.ReadValue("--write-baseline", "a value, e.g. --write-baseline cqrslens-baseline.json");
                    break;
                case "--diff":
                    options.DiffPath = reader.ReadValue("--diff", "a value, e.g. --diff base-architecture.json");
                    break;
                case "--diff-output":
                    options.DiffOutputPath = reader.ReadValue("--diff-output", "a value, e.g. --diff-output cqrslens-diff.md");
                    break;
                case "--sarif":
                    options.SarifPath = reader.ReadValue("--sarif", "a value, e.g. --sarif cqrslens.sarif");
                    break;
                case "--summary":
                    options.SummaryPath = reader.ReadValue("--summary", "a value, e.g. --summary cqrslens-summary.md");
                    break;
                case "--export-svg":
                    options.ExportSvgDirectory = ReadExportSvgDirectory(reader);
                    break;
                case "--export-mermaid":
                    options.ExportMermaidDirectory = ReadExportMermaidDirectory(reader);
                    break;
                case "--cqrs-map-split":
                    options.CqrsMapSplit = ParseCqrsMapSplit(
                        reader.ReadValue("--cqrs-map-split", "a value: count, feature or unsplit"));
                    options.CqrsMapSplitSpecified = true;
                    break;
                case "--ci-root":
                    options.CiRoot = reader.ReadValue("--ci-root", "a value, e.g. --ci-root $GITHUB_WORKSPACE");
                    break;
                case "--github-annotations":
                    options.GitHubAnnotations = true;
                    break;
                case "--strict":
                    options.Strict = true;
                    break;
                case "--watermark":
                    options.Watermark = ReadWatermark(reader);
                    options.WatermarkSpecified = true;
                    break;
                case "--banner":
                    options.Banner = true;
                    break;
                case "--no-banner":
                    options.Banner = false;
                    break;
                case "--color":
                    options.Color = true;
                    break;
                case "--no-color":
                    options.Color = false;
                    break;
                case "--ascii":
                    options.Ascii = true;
                    break;
                case "--unicode":
                    options.Ascii = false;
                    break;
                case "--mcp":
                    options.Mcp = true;
                    break;
                case "--profile":
                    options.Profile = true;
                    break;
                default:
                    // Help and version are decided after parsing so that the
                    // display flags around them still apply; stray arguments
                    // after --help must not turn it into a usage error.
                    if (helpRequested || versionRequested)
                        break;

                    options.Path = ReadPath(options.Path, reader.Current);
                    break;
            }
        }

        if (helpRequested)
            return new CliCommand(CliCommandKind.Help, options);

        if (versionRequested)
            return new CliCommand(CliCommandKind.Version, options);

        Validate(options);
        return new CliCommand(CliCommandKind.Run, options);
    }

    private static string ReadPath(string? current, string value)
    {
        if (value.StartsWith('-'))
            throw new CliParseException($"error: unknown option '{value}'", showUsage: true);

        if (current is not null)
            throw new CliParseException("error: pass exactly one path to scan.");

        return value;
    }

    private static string ReadSliceFilter(ArgumentReader reader)
    {
        var slice = reader.ReadValue("--slice", "a filter value, e.g. --slice Order").Trim();
        if (slice.Length == 0)
            throw new CliParseException("error: --slice filter cannot be empty.");

        return slice;
    }

    private static string ReadExportSvgDirectory(ArgumentReader reader)
    {
        var directory = reader.ReadValue("--export-svg", "a directory, e.g. --export-svg diagrams").Trim();
        if (directory.Length == 0)
            throw new CliParseException("error: --export-svg directory cannot be empty.");

        return directory;
    }

    private static string ReadExportMermaidDirectory(ArgumentReader reader)
    {
        var directory = reader.ReadValue("--export-mermaid", "a directory, e.g. --export-mermaid diagrams").Trim();
        if (directory.Length == 0)
            throw new CliParseException("error: --export-mermaid directory cannot be empty.");

        return directory;
    }

    private static string ReadWatermark(ArgumentReader reader)
    {
        var watermark = reader.ReadValue("--watermark", "a value, e.g. --watermark \"ACME CORP\"");
        if (string.IsNullOrWhiteSpace(watermark))
            throw new CliParseException("error: --watermark cannot be empty.");

        return watermark.Trim();
    }

    private static DateTimeOffset ReadGeneratedAt(ArgumentReader reader)
    {
        var raw = reader.ReadValue("--generated-at", "an ISO-8601 timestamp.");
        if (!DateTimeOffset.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var generatedAt))
        {
            throw new CliParseException($"error: invalid --generated-at timestamp '{raw}'.");
        }

        return generatedAt;
    }

    private static CqrsMapSplitMode ParseCqrsMapSplit(string value)
        => CqrsMapSplitValues.TryParse(value, out var mode)
            ? mode
            : throw new CliParseException(
                $"error: unknown --cqrs-map-split value '{value}' - use {CqrsMapSplitValues.Accepted}.");

    private static DiagramRenderMode ParseRenderMode(string value)
        => value.ToLowerInvariant() switch
        {
            "mermaid" => DiagramRenderMode.Mermaid,
            "svg" => DiagramRenderMode.Svg,
            _ => throw new CliParseException($"error: unknown render mode '{value}' - use mermaid or svg."),
        };

    private static AnalysisMode ParseAnalysisMode(string value)
        => value.ToLowerInvariant() switch
        {
            "text" => AnalysisMode.Text,
            "roslyn" => AnalysisMode.Roslyn,
            _ => throw new CliParseException($"error: unknown analysis mode '{value}' - use text or roslyn."),
        };

    private static void AddMarkerNames(HashSet<string> target, string value)
    {
        foreach (var name in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            target.Add(CSharpScanner.SimpleName(name));
    }

    private static void Validate(CliOptions options)
    {
        if (options.Path is null && options.Mcp)
            options.Path = Directory.GetCurrentDirectory();

        if (options.Path is null)
            throw new CliParseException("error: pass a folder to scan.", showUsage: true);

        if (options.Format is not null && !SupportedFormats.Contains(options.Format))
            throw new CliParseException($"error: unknown format '{options.Format}' - use md, html, json or agent.");

        if (options.BaselinePath is not null && options.WriteBaselinePath is not null)
            throw new CliParseException("error: use either --baseline or --write-baseline, not both.");

        if (options.DiffOutputPath is not null && options.DiffPath is null)
            throw new CliParseException("error: --diff-output requires --diff <path-or-json>.");
    }
}
