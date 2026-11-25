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

    private static readonly HashSet<string> SupportedImpactKinds = new(StringComparer.Ordinal)
    {
        "project",
        "entity",
        "dbcontext",
        "service",
        "notification",
        "handler",
        "request",
        "entrypoint",
        "event",
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
                case "--proposal":
                    options.ProposalPath = reader.ReadValue("--proposal", "a JSON file, e.g. --proposal proposal.json");
                    break;
                case "--slice-design":
                    options.SliceDesignPath = reader.ReadValue("--slice-design", "a JSON file or directory, e.g. --slice-design feature.slice.json");
                    break;
                case "--draft-slice":
                    options.DraftSliceRequest = ReadNonEmptyValue(reader, "--draft-slice", "a value, e.g. --draft-slice CreateOrderCommand");
                    break;
                case "--impact":
                    options.ImpactTarget = ReadNonEmptyValue(reader, "--impact", "a value, e.g. --impact Order");
                    break;
                case "--impact-kind":
                    options.ImpactKind = ParseImpactKind(
                        reader.ReadValue("--impact-kind", "a value: project, entity, dbcontext, service, notification, handler, request, entrypoint, or event"));
                    break;
                case "--tdd-check":
                    options.TddCheckPath = ReadNonEmptyValue(reader, "--tdd-check", "a value, e.g. --tdd-check feature.slice.json");
                    break;
                case "--test-results":
                    options.TestResultsPath = ReadNonEmptyValue(reader, "--test-results", "a value, e.g. --test-results TestResults/run.trx");
                    break;
                case "--render":
                    options.RenderMode = ParseRenderMode(reader.ReadValue("--render", "a value: mermaid or svg"));
                    options.RenderModeSpecified = true;
                    break;
                case "--view":
                    options.ViewMode = ParseViewMode(reader.ReadValue("--view", "a value: overview or full"));
                    options.ViewModeSpecified = true;
                    break;
                case "--focus":
                    options.FocusService = ReadNonEmptyValue(
                        reader,
                        "--focus",
                        "a value, e.g. --focus Ordering.API");
                    break;
                case "--exclude-tests":
                    options.ExcludeTests = true;
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
                case "--allow-root":
                    options.AllowedRoots.Add(ReadNonEmptyValue(reader, "--allow-root", "a value, e.g. --allow-root ../shared"));
                    break;
                case "--allow-external-paths":
                    options.AllowExternalPaths = true;
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

    private static string ReadNonEmptyValue(ArgumentReader reader, string flag, string hint)
    {
        var value = reader.ReadValue(flag, hint).Trim();
        if (value.Length == 0)
            throw new CliParseException($"error: {flag} cannot be empty.");

        return value;
    }

    private static string ParseImpactKind(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        if (!SupportedImpactKinds.Contains(normalized))
        {
            throw new CliParseException(
                $"error: unknown --impact-kind value '{value}' - use project, entity, dbcontext, service, notification, handler, request, entrypoint, or event.");
        }

        return normalized;
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

    private static ReportViewMode ParseViewMode(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "full" => ReportViewMode.Full,
            "overview" => ReportViewMode.Overview,
            _ => throw new CliParseException($"error: unknown view mode '{value}' - use overview or full."),
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

        if (options.ImpactKind is not null && options.ImpactTarget is null)
            throw new CliParseException("error: --impact-kind requires --impact <target>.");

        if (options.TestResultsPath is not null && options.TddCheckPath is null)
            throw new CliParseException("error: --test-results requires --tdd-check <file>.");

        var activeJsonModes =
            (options.ProposalPath is not null ? 1 : 0) +
            (options.SliceDesignPath is not null ? 1 : 0) +
            (options.DraftSliceRequest is not null ? 1 : 0) +
            (options.ImpactTarget is not null ? 1 : 0) +
            (options.TddCheckPath is not null ? 1 : 0);
        if (activeJsonModes > 1)
            throw new CliParseException("error: use only one of --proposal, --slice-design, --draft-slice, --impact, or --tdd-check.");

        if (options.ProposalPath is not null)
            ValidateModelingCommand(options, "--proposal", allowStrict: true);

        if (options.SliceDesignPath is not null)
            ValidateModelingCommand(options, "--slice-design", allowStrict: true);

        if (options.DraftSliceRequest is not null && (HasReportOptions(options) || options.Strict))
            throw new CliParseException("error: --draft-slice writes JSON to stdout and cannot be combined with scan report, CI, or MCP options.");

        if (options.ImpactTarget is not null)
            ValidateModelingCommand(options, "--impact", allowStrict: false);

        if (options.TddCheckPath is not null)
            ValidateModelingCommand(options, "--tdd-check", allowStrict: true);
    }

    private static void ValidateModelingCommand(CliOptions options, string flag, bool allowStrict)
    {
        if (!allowStrict && options.Strict)
            throw new CliParseException($"error: {flag} cannot be combined with --strict.");

        if (string.Equals(options.Format, "agent", StringComparison.Ordinal))
            throw new CliParseException($"error: {flag} supports --format json, md, or html (not agent).");

        if (HasScanOnlyOptions(options))
            throw new CliParseException($"error: {flag} cannot be combined with full-scan report, CI diff/baseline, or MCP options.");
    }

    private static bool HasScanOnlyOptions(CliOptions options)
        => options.Mcp || options.SliceFilter is not null ||
           options.ViewModeSpecified || options.FocusService is not null || options.ExcludeTests ||
           options.CqrsMapSplitSpecified || options.DiffPath is not null ||
           options.BaselinePath is not null || options.WriteBaselinePath is not null ||
           options.SarifPath is not null || options.SummaryPath is not null || options.GitHubAnnotations ||
           options.CiRoot is not null || options.ExportSvgDirectory is not null || options.ExportMermaidDirectory is not null ||
           options.Profile || options.WatermarkSpecified || options.GeneratedAt is not null;

    private static bool HasReportOptions(CliOptions options)
        => options.Output is not null || options.Format is not null || options.RenderModeSpecified ||
           HasScanOnlyOptions(options);
}
