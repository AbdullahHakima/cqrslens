using System.Globalization;

namespace CqrsLens;

public sealed class CliOptions
{
    public string? Path { get; set; }
    public string? Output { get; set; }
    public string? Format { get; set; }
    public string? SliceFilter { get; set; }
    public DiagramRenderMode RenderMode { get; set; } = DiagramRenderMode.Mermaid;
    public bool RenderModeSpecified { get; set; }
    public string? ConfigPath { get; set; }
    public string? BaselinePath { get; set; }
    public string? WriteBaselinePath { get; set; }
    public string? DiffPath { get; set; }
    public string? DiffOutputPath { get; set; }
    public string? SarifPath { get; set; }
    public string? SummaryPath { get; set; }
    public string? CiRoot { get; set; }
    public bool GitHubAnnotations { get; set; }
    public AnalysisMode AnalysisMode { get; set; } = AnalysisMode.Text;
    public bool AnalysisModeSpecified { get; set; }
    public DateTimeOffset? GeneratedAt { get; set; }
    public bool Strict { get; set; }
    public bool MarkersSpecified { get; set; }
    public HashSet<string> Markers { get; } = new(StringComparer.Ordinal);
    public bool HandlerMarkersSpecified { get; set; }
    public HashSet<string> HandlerMarkers { get; } = new(StringComparer.Ordinal);
    public string? Watermark { get; set; }
    public bool WatermarkSpecified { get; set; }
    public bool? Banner { get; set; }
    public bool? Color { get; set; }
    public bool Mcp { get; set; }
    public bool Profile { get; set; }
}

public enum CliCommandKind
{
    Run,
    Help,
    Version,
    Error,
}

public sealed record CliCommand(
    CliCommandKind Kind,
    CliOptions? Options = null,
    string? Error = null,
    bool ShowUsage = false);

/// <summary>Testable command-line parsing and execution.</summary>
public static class CliParser
{
    public static CliCommand Parse(string[] args)
    {
        var options = new CliOptions();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-h" or "--help":
                    return new CliCommand(CliCommandKind.Help);
                case "--version":
                    return new CliCommand(CliCommandKind.Version);
                case "-o" or "--output":
                    if (i + 1 >= args.Length)
                        return Error("error: -o needs a value, e.g. -o ARCHITECTURE.md");
                    options.Output = args[++i];
                    break;
                case "--config":
                    if (i + 1 >= args.Length)
                        return Error("error: --config needs a value, e.g. --config .cqrslens.json");
                    options.ConfigPath = args[++i];
                    break;
                case "--format":
                    if (i + 1 >= args.Length)
                        return Error("error: --format needs a value: md, html, json or agent");
                    options.Format = args[++i].ToLowerInvariant();
                    break;
                case "--slice":
                    if (i + 1 >= args.Length)
                        return Error("error: --slice needs a filter value, e.g. --slice Order");
                    var slice = args[++i].Trim();
                    if (string.IsNullOrEmpty(slice))
                        return Error("error: --slice filter cannot be empty.");
                    options.SliceFilter = slice;
                    break;
                case "--render":
                    if (i + 1 >= args.Length)
                        return Error("error: --render needs a value: mermaid or svg");
                    var render = args[++i].ToLowerInvariant();
                    if (render == "mermaid")
                        options.RenderMode = DiagramRenderMode.Mermaid;
                    else if (render == "svg")
                        options.RenderMode = DiagramRenderMode.Svg;
                    else
                        return Error($"error: unknown render mode '{render}' - use mermaid or svg.");
                    options.RenderModeSpecified = true;
                    break;
                case "--analysis":
                    if (i + 1 >= args.Length)
                        return Error("error: --analysis needs a value: text or roslyn");
                    var analysis = args[++i].ToLowerInvariant();
                    if (analysis == "text")
                        options.AnalysisMode = AnalysisMode.Text;
                    else if (analysis == "roslyn")
                        options.AnalysisMode = AnalysisMode.Roslyn;
                    else
                        return Error($"error: unknown analysis mode '{analysis}' - use text or roslyn.");
                    options.AnalysisModeSpecified = true;
                    break;
                case "--generated-at":
                    if (i + 1 >= args.Length)
                        return Error("error: --generated-at needs an ISO-8601 timestamp.");
                    if (!DateTimeOffset.TryParse(
                            args[++i],
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                            out var generatedAt))
                        return Error($"error: invalid --generated-at timestamp '{args[i]}'.");
                    options.GeneratedAt = generatedAt;
                    break;
                case "--markers":
                    if (i + 1 >= args.Length)
                        return Error("error: --markers needs a value, e.g. --markers ICommand,IQuery");
                    options.MarkersSpecified = true;
                    foreach (var name in args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        options.Markers.Add(CSharpScanner.SimpleName(name));
                    break;
                case "--handler-markers":
                    if (i + 1 >= args.Length)
                        return Error("error: --handler-markers needs a value, e.g. --handler-markers ICommandHandler,IQueryHandler");
                    options.HandlerMarkersSpecified = true;
                    foreach (var name in args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        options.HandlerMarkers.Add(CSharpScanner.SimpleName(name));
                    break;
                case "--baseline":
                    if (i + 1 >= args.Length)
                        return Error("error: --baseline needs a value, e.g. --baseline cqrslens-baseline.json");
                    options.BaselinePath = args[++i];
                    break;
                case "--write-baseline":
                    if (i + 1 >= args.Length)
                        return Error("error: --write-baseline needs a value, e.g. --write-baseline cqrslens-baseline.json");
                    options.WriteBaselinePath = args[++i];
                    break;
                case "--diff":
                    if (i + 1 >= args.Length)
                        return Error("error: --diff needs a value, e.g. --diff base-architecture.json");
                    options.DiffPath = args[++i];
                    break;
                case "--diff-output":
                    if (i + 1 >= args.Length)
                        return Error("error: --diff-output needs a value, e.g. --diff-output cqrslens-diff.md");
                    options.DiffOutputPath = args[++i];
                    break;
                case "--sarif":
                    if (i + 1 >= args.Length)
                        return Error("error: --sarif needs a value, e.g. --sarif cqrslens.sarif");
                    options.SarifPath = args[++i];
                    break;
                case "--summary":
                    if (i + 1 >= args.Length)
                        return Error("error: --summary needs a value, e.g. --summary cqrslens-summary.md");
                    options.SummaryPath = args[++i];
                    break;
                case "--ci-root":
                    if (i + 1 >= args.Length)
                        return Error("error: --ci-root needs a value, e.g. --ci-root $GITHUB_WORKSPACE");
                    options.CiRoot = args[++i];
                    break;
                case "--github-annotations":
                    options.GitHubAnnotations = true;
                    break;
                case "--strict":
                    options.Strict = true;
                    break;
                case "--watermark":
                    if (i + 1 >= args.Length)
                        return Error("error: --watermark needs a value, e.g. --watermark \"ACME CORP\"");
                    var wm = args[++i];
                    if (string.IsNullOrWhiteSpace(wm))
                        return Error("error: --watermark cannot be empty.");
                    options.Watermark = wm.Trim();
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
                case "--mcp":
                    options.Mcp = true;
                    break;
                case "--profile":
                    options.Profile = true;
                    break;
                case "--no-color":
                    options.Color = false;
                    break;
                default:
                    if (args[i].StartsWith('-'))
                        return Error($"error: unknown option '{args[i]}'", showUsage: true);
                    if (options.Path is not null)
                        return Error("error: pass exactly one path to scan.");
                    options.Path = args[i];
                    break;
            }
        }

        if (options.Path is null && options.Mcp)
            options.Path = Directory.GetCurrentDirectory();

        if (options.Path is null)
            return Error("error: pass a folder to scan.", showUsage: true);

        if (options.Format is not null &&
            options.Format != "md" &&
            options.Format != "html" &&
            options.Format != "json" &&
            options.Format != "agent")
        {
            return Error($"error: unknown format '{options.Format}' - use md, html, json or agent.");
        }

        if (options.BaselinePath is not null && options.WriteBaselinePath is not null)
            return Error("error: use either --baseline or --write-baseline, not both.");

        if (options.DiffOutputPath is not null && options.DiffPath is null)
            return Error("error: --diff-output requires --diff <path-or-json>.");

        return new CliCommand(CliCommandKind.Run, options);
    }

    private static CliCommand Error(string message, bool showUsage = false)
        => new(CliCommandKind.Error, Error: message, ShowUsage: showUsage);
}

public static class ExitCodes
{
    public const int Success = 0;
    public const int Usage = 1;
    public const int PathNotFound = 2;
    public const int NoProjects = 3;
    public const int StrictFailure = 4;
    public const int Unexpected = 5;
}

public static class CliRunner
{
    public static int Run(string[] args, TextWriter? stdout = null, TextWriter? stderr = null)
    {
        stdout ??= Console.Out;
        stderr ??= Console.Error;

        var command = CliParser.Parse(args);
        switch (command.Kind)
        {
            case CliCommandKind.Help:
                WriteUsage(stdout);
                return ExitCodes.Success;
            case CliCommandKind.Version:
                stdout.WriteLine($"CqrsLens {ToolInfo.Version}");
                return ExitCodes.Success;
            case CliCommandKind.Error:
                stderr.WriteLine(command.Error);
                if (command.ShowUsage) WriteUsage(stderr);
                return ExitCodes.Usage;
        }

        try
        {
            if (command.Options!.Mcp)
                return McpServer.Run(command.Options, Console.In, stdout, stderr);
            return Execute(command.Options!, stdout, stderr);
        }
        catch (Exception ex) when (ex is ConfigurationException or BaselineException or DiffException)
        {
            stderr.WriteLine($"error: {ex.Message}");
            return ExitCodes.Usage;
        }
        catch (Exception ex)
        {
            // Keep the CLI's failure output concise; never dump a stack trace on users.
            stderr.WriteLine($"error: {ex.Message}");
            return ExitCodes.Unexpected;
        }
    }

    private static int Execute(CliOptions options, TextWriter stdout, TextWriter stderr)
    {
        var path = options.Path!;
        if (!Directory.Exists(path))
        {
            stderr.WriteLine($"error: path not found: {path}");
            return ExitCodes.PathNotFound;
        }

        var fullRoot = Path.GetFullPath(path);
        var settings = ConfigurationLoader.Load(fullRoot, options.ConfigPath);

        string? ciRoot = null;
        if (options.CiRoot is not null)
        {
            try
            {
                ciRoot = Path.GetFullPath(options.CiRoot);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                stderr.WriteLine($"error: invalid --ci-root '{options.CiRoot}': {ex.Message}");
                return ExitCodes.Usage;
            }

            if (!Directory.Exists(ciRoot))
            {
                stderr.WriteLine($"error: --ci-root directory not found: {options.CiRoot}");
                return ExitCodes.Usage;
            }
        }

        if (CsprojParser.FindProjectFiles(fullRoot).Count == 0)
        {
            stderr.WriteLine($"error: no .csproj files found under: {path}");
            stderr.WriteLine("CqrsLens scans .NET solutions - point it at a folder containing at least one project.");
            return ExitCodes.NoProjects;
        }

        var analysisMode = options.AnalysisModeSpecified
            ? options.AnalysisMode
            : settings.AnalysisMode ?? options.AnalysisMode;
        var renderMode = options.RenderModeSpecified
            ? options.RenderMode
            : settings.RenderMode ?? options.RenderMode;
        var markers = options.MarkersSpecified
            ? options.Markers
            : settings.Markers;
        var handlerMarkers = options.HandlerMarkersSpecified
            ? options.HandlerMarkers
            : settings.HandlerMarkers;
        var watermark = options.WatermarkSpecified
            ? options.Watermark
            : settings.Watermark;







        var streamSliceToStdout = options.SliceFilter is not null && options.Output is null && options.Format is null;
        var useColor = ConsoleArtwork.ShouldUseColor(stdout, options.Color);
        var showBanner = !streamSliceToStdout &&
                         ConsoleArtwork.ShouldShowBanner(stdout, options.Banner, options.GitHubAnnotations);
        var isInteractiveConsole = !streamSliceToStdout &&
                                   ReferenceEquals(stdout, Console.Out) &&
                                   !Console.IsOutputRedirected &&
                                   !options.GitHubAnnotations;

        if (showBanner)
        {
            stdout.WriteLine(ConsoleArtwork.RenderLogoBanner(ToolInfo.Version, useColor));
            if (!string.IsNullOrWhiteSpace(watermark))
            {
                stdout.WriteLine(ConsoleArtwork.RenderWatermarkStamp(watermark, useColor));
                stdout.WriteLine();
            }
        }
        else if (!string.IsNullOrWhiteSpace(watermark) &&
                 options.WatermarkSpecified &&
                 options.Banner != false &&
                 !options.GitHubAnnotations)
        {
            stdout.WriteLine(ConsoleArtwork.RenderWatermarkStamp(watermark, useColor));
            stdout.WriteLine();
        }

        var lastProgressLen = 0;
        Action<ScanProgress>? progressCallback = null;
        if (isInteractiveConsole)
        {
            progressCallback = progress =>
            {
                var line = ConsoleArtwork.FormatProgressBar(progress, useColor);
                var pad = Math.Max(0, lastProgressLen - line.Length);
                lastProgressLen = line.Length;
                stdout.Write("\r" + line + new string(' ', pad));
                stdout.Flush();
            };
        }

        var analysisOptions = new AnalysisOptions
        {
            Markers = markers,
            HandlerMarkers = handlerMarkers,
            GeneratedAt = options.GeneratedAt,
            Watermark = watermark,
            Progress = progressCallback,
            Include = settings.Include,
            Exclude = settings.Exclude,
            LayerOverrides = settings.LayerOverrides,
            RuleSeverities = settings.RuleSeverities,
        };

        var result = Analyzer.Analyze(path, analysisMode, analysisOptions);

        if (options.Profile)
            WriteProfile(result, stderr);

        if (streamSliceToStdout)
        {
            stdout.Write(AgentReportGenerator.Generate(result, options.SliceFilter));
            return ExitCodes.Success;
        }

        if (isInteractiveConsole && lastProgressLen > 0)
        {
            stdout.Write("\r" + new string(' ', lastProgressLen) + "\r");
            stdout.Flush();
        }

        var isHtml = options.Format == "html" ||
                     (options.Format is null &&
                      options.Output is not null &&
                      options.Output.EndsWith(".html", StringComparison.OrdinalIgnoreCase));
        var isJson = options.Format == "json" ||
                     (options.Format is null &&
                      options.Output is not null &&
                      options.Output.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
        var isAgent = options.Format == "agent" ||
                      options.SliceFilter is not null ||
                      (options.Format is null &&
                       options.Output is not null &&
                       options.Output.EndsWith(".agent.md", StringComparison.OrdinalIgnoreCase));

        var defaultFileName = isHtml
            ? "ARCHITECTURE.html"
            : isJson
                ? "ARCHITECTURE.json"
                : isAgent
                    ? "ARCHITECTURE.agent.md" : "ARCHITECTURE.md";
    

        var output = options.Output ?? Path.Combine(
            Directory.GetCurrentDirectory(), defaultFileName);
        var fullOutput = Path.GetFullPath(output);

        if (IsDirectoryOutput(output, fullOutput))
        {
            stderr.WriteLine($"error: output path is a directory: {output}");
            return ExitCodes.Usage;
        }

        var fullSarif = options.SarifPath is null ? null : Path.GetFullPath(options.SarifPath);
        if (fullSarif is not null && IsDirectoryOutput(options.SarifPath!, fullSarif))
        {
            stderr.WriteLine($"error: SARIF path is a directory: {options.SarifPath}");
            return ExitCodes.Usage;
        }

        var fullSummary = options.SummaryPath is null ? null : Path.GetFullPath(options.SummaryPath);
        if (fullSummary is not null && IsDirectoryOutput(options.SummaryPath!, fullSummary))
        {
            stderr.WriteLine($"error: summary path is a directory: {options.SummaryPath}");
            return ExitCodes.Usage;
        }

        var fullDiffOutput = options.DiffOutputPath is null ? null : Path.GetFullPath(options.DiffOutputPath);
        if (fullDiffOutput is not null && IsDirectoryOutput(options.DiffOutputPath!, fullDiffOutput))
        {
            stderr.WriteLine($"error: diff output path is a directory: {options.DiffOutputPath}");
            return ExitCodes.Usage;
        }

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (fullSarif is not null && string.Equals(fullSarif, fullOutput, comparison))
        {
            stderr.WriteLine("error: --sarif and the report output must be different files.");
            return ExitCodes.Usage;
        }

        if (fullSummary is not null &&
            (string.Equals(fullSummary, fullOutput, comparison) ||
             string.Equals(fullSummary, fullSarif, comparison)))
        {
            stderr.WriteLine("error: --summary and the other output paths must be different files.");
            return ExitCodes.Usage;
        }

        if (fullDiffOutput is not null &&
            (string.Equals(fullDiffOutput, fullOutput, comparison) ||
             string.Equals(fullDiffOutput, fullSarif, comparison) ||
             string.Equals(fullDiffOutput, fullSummary, comparison)))
        {
            stderr.WriteLine("error: --diff-output and the other output paths must be different files.");
            return ExitCodes.Usage;
        }

        if (options.WriteBaselinePath is not null)
        {
            BaselineStore.Write(result.Checks, options.WriteBaselinePath);
            BaselineMatcher.MarkAllKnown(result, options.WriteBaselinePath);
        }
        else if (options.BaselinePath is not null)
        {
            var baseline = BaselineStore.Read(options.BaselinePath);
            BaselineMatcher.Apply(result, baseline, options.BaselinePath);
        }

        ArchitectureDiff? diff = null;
        if (options.DiffPath is not null)
        {
            var baseSnapshot = ArchitectureDiffer.LoadSnapshot(options.DiffPath, analysisMode, analysisOptions);
            diff = ArchitectureDiffer.Compare(baseSnapshot, JsonReportGenerator.CreateDocument(result));

            if (fullDiffOutput is not null)
            {
                EnsureOutputDirectory(fullDiffOutput);
                var diffContent = fullDiffOutput.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                    ? ArchitectureDiffer.GenerateJson(diff)
                    : ArchitectureDiffer.GenerateMarkdown(diff, result, ciRoot);
                File.WriteAllText(fullDiffOutput, diffContent);
            }
        }

        var report = isHtml
            ? HtmlReportGenerator.Generate(result, renderMode)
            : isJson
                ? JsonReportGenerator.Generate(result)
                : isAgent
                    ? AgentReportGenerator.Generate(result, options.SliceFilter) : ReportGenerator.Generate(result);
    


        var outputDirectory = Path.GetDirectoryName(fullOutput);
        if (!string.IsNullOrEmpty(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        File.WriteAllText(fullOutput, report);

        if (fullSarif is not null)
        {
            EnsureOutputDirectory(fullSarif);
            File.WriteAllText(fullSarif, SarifReportGenerator.Generate(result, ciRoot));
        }

        if (fullSummary is not null)
        {
            EnsureOutputDirectory(fullSummary);
            File.WriteAllText(fullSummary, SummaryGenerator.Generate(result, ciRoot, diff));
        }

        if (options.GitHubAnnotations)
            GitHubAnnotations.Write(result, stdout, ciRoot);

        if (showBanner)
        {
            stdout.WriteLine(ConsoleArtwork.RenderSummaryCard(result, fullOutput, useColor));
            stdout.WriteLine();
        }

        stdout.WriteLine($"Scanned {result.Projects.Count} project(s): " +
                         $"{result.Requests.Count} request(s), {result.Handlers.Count} handler(s), " +
                         $"{result.Validators.Count} validator(s), {result.Behaviors.Count} behavior(s), " +
                         $"{result.DbContexts.Count} DbContext(s).");
        stdout.WriteLine($"Report written to {fullOutput}");
        if (fullSarif is not null)
            stdout.WriteLine($"SARIF written to {fullSarif}");
        if (fullSummary is not null)
            stdout.WriteLine($"Summary written to {fullSummary}");
        if (diff is not null)
            stdout.WriteLine(ArchitectureDiffer.FormatConsoleSummary(diff));
        if (fullDiffOutput is not null)
            stdout.WriteLine($"Diff written to {fullDiffOutput}");
        if (result.SkippedFiles.Count > 0)
            stderr.WriteLine($"warning: {result.SkippedFiles.Count} source file(s) skipped - see the report.");

        var nonSkipDiagnostics = result.Diagnostics.Count(d =>
            d.Code != AnalysisResult.SourceFileSkippedCode);
        if (nonSkipDiagnostics > 0)
            stderr.WriteLine($"warning: {nonSkipDiagnostics} scan diagnostic(s) - see the report.");

        var errors = result.Checks.Count(c => c.Severity == CheckSeverity.Error);
        var warnings = result.Checks.Count - errors;
        if (result.Checks.Count > 0)
            stdout.WriteLine($"Architecture checks: {errors} error(s), {warnings} warning(s) - see the report.");

        if (result.BaselineApplied)
        {
            var known = result.Checks.Count(c => c.IsKnown);
            var newFindings = result.Checks.Count - known;
            if (options.WriteBaselinePath is not null)
                stdout.WriteLine($"Baseline written to {options.WriteBaselinePath}: {known} finding(s) recorded.");
            else
                stdout.WriteLine($"Baseline: {known} known finding(s), {newFindings} new finding(s).");
        }

        var newErrors = result.Checks.Count(c =>
            c.Severity == CheckSeverity.Error && (!result.BaselineApplied || !c.IsKnown));
        if (options.Strict && newErrors > 0)
        {
            if (!result.CanRunStrictChecks)
            {
                stderr.WriteLine(
                    "warning: --strict skipped because the scan is incomplete or ambiguous; " +
                    "see the diagnostics in the report.");
                return ExitCodes.Success;
            }

            var suffix = result.BaselineApplied ? "new architecture check error(s)" : "architecture check error(s)";
            stderr.WriteLine($"error: --strict - {newErrors} {suffix} found.");
            return ExitCodes.StrictFailure;
        }

        return ExitCodes.Success;
    }

    private static bool IsDirectoryOutput(string raw, string fullPath)
        => Directory.Exists(fullPath) ||
           raw.EndsWith(Path.DirectorySeparatorChar) ||
           raw.EndsWith(Path.AltDirectorySeparatorChar);

    private static void EnsureOutputDirectory(string fullPath)
    {
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
    }

    private static void WriteProfile(AnalysisResult result, TextWriter writer)
    {
        static string Bytes(long value) => value >= 1_048_576
            ? $"{value / 1_048_576.0:0.0} MB"
            : $"{value / 1024.0:0.0} KB";

        writer.WriteLine($"Profile ({result.ScannerName}): {result.ElapsedMilliseconds} ms, {Bytes(result.AllocatedBytes)} allocated");
        foreach (var phase in result.PhaseTimings)
            writer.WriteLine($"  {phase.Name,-30} {phase.ElapsedMilliseconds,8:0.00} ms {Bytes(phase.AllocatedBytes),10}");
    }

    public static void WriteUsage(TextWriter writer)
    {
        if (ConsoleArtwork.ShouldShowBanner(writer, null, false))
        {
            var useColor = ConsoleArtwork.ShouldUseColor(writer, null);
            writer.WriteLine(ConsoleArtwork.RenderLogoBanner(ToolInfo.Version, useColor));
        }

        writer.WriteLine($"CqrsLens {ToolInfo.Version} - see your MediatR codebase.");
        writer.WriteLine();
        writer.WriteLine("Usage: cqrslens <path> [-o OUTPUT] [--format md|html|json|agent] [--slice FILTER] [--render mermaid|svg] [--config FILE] [--analysis text|roslyn] [--generated-at ISO8601] [--markers ICommand,IQuery] [--handler-markers ICommandHandler,IQueryHandler] [--watermark TEXT] [--banner|--no-banner] [--color|--no-color] [--baseline FILE] [--write-baseline FILE] [--diff PATH_OR_JSON] [--diff-output FILE] [--sarif FILE] [--summary FILE] [--github-annotations] [--ci-root DIR] [--strict]");
        writer.WriteLine();
        writer.WriteLine("  <path>        Folder of the .NET solution to scan (searched recursively).");
        writer.WriteLine("  -o, --output  Report path. Default: ARCHITECTURE.md (.html or .json) in the current directory.");
        writer.WriteLine("  --config      Config file. Default: <path>/.cqrslens.json when present.");
        writer.WriteLine("                Settings: analysis, render, watermark, markers, handlerMarkers, include/exclude globs, layers, rules.");
        writer.WriteLine("  --format      Report format: md (default), html, json, or agent. A .html, .json, or .agent.md -o path implies that format.");
        writer.WriteLine("  --slice       Filter and trace vertical slices matching FILTER (endpoint, request, handler, entity, or event).");
        writer.WriteLine("                Streams directly to stdout when -o is omitted.");
        writer.WriteLine("  --mcp         Start the built-in Model Context Protocol (MCP) server over JSON-RPC 2.0 stdio.");
        writer.WriteLine("  --profile     Print per-stage timing and allocation totals to stderr.");
        writer.WriteLine("  --render      HTML diagram rendering: mermaid (default) or svg (zero-JS inline SVG).");
        writer.WriteLine("  --analysis    Analysis backend: text (default) or roslyn (opt-in).");
        writer.WriteLine("  --generated-at  Fixed ISO-8601 timestamp for reproducible reports (tests/CI);");
        writer.WriteLine("                timing is reported as 0 ms in this mode.");
        writer.WriteLine("  --markers     Comma-separated custom marker interface names that also count");
        writer.WriteLine("                as request roots (e.g. ICommand,IQuery,IMessage). Requests hidden");
        writer.WriteLine("                behind interfaces deriving from IRequest are always followed.");
        writer.WriteLine("  --handler-markers  Comma-separated custom handler roots for external wrappers");
        writer.WriteLine("                (e.g. ICommandHandler,IQueryHandler). Source-declared wrappers");
        writer.WriteLine("                are followed automatically.");
        writer.WriteLine("  --watermark   Custom watermark text rendered as block ASCII art in the console");
        writer.WriteLine("                and embedded as a watermark stamp in Markdown, HTML, and JSON reports.");
        writer.WriteLine("  --banner      Force rendering the ASCII art logo banner and summary card.");
        writer.WriteLine("  --no-banner   Suppress the ASCII art logo banner and summary card.");
        writer.WriteLine("  --color       Force ANSI 24-bit TrueColor gradients in console output.");
        writer.WriteLine("  --no-color    Disable ANSI colors in console output (also respects NO_COLOR).");
        writer.WriteLine("  --baseline    Compare checks with a baseline file; --strict then fails only");
        writer.WriteLine("                on errors not present in the baseline.");
        writer.WriteLine("  --write-baseline  Record current checks as the baseline, then continue.");
        writer.WriteLine("  --diff        Compare the current scan against a base ARCHITECTURE.json file or");
        writer.WriteLine("                base solution folder to see what changed in the CQRS surface.");
        writer.WriteLine("  --diff-output Write the architecture diff report (.md for PR comments, or .json).");
        writer.WriteLine("  --sarif       Also write SARIF 2.1.0 for code scanning.");
        writer.WriteLine("  --summary     Also write a compact Markdown job summary.");
        writer.WriteLine("  --github-annotations  Emit file/line GitHub Actions annotations.");
        writer.WriteLine("  --ci-root     Repository root used to rebase annotation/SARIF paths.");
        writer.WriteLine("  --strict      Exit with code 4 when architecture checks report an error");
        writer.WriteLine("                and the scan is complete; incomplete scans report diagnostics.");
        writer.WriteLine("  -h, --help    Show this help.");
        writer.WriteLine("  --version     Print the tool version and exit.");
        writer.WriteLine();
        writer.WriteLine("Exit codes: 0 ok - 1 usage error - 2 path not found - 3 no projects found - 4 strict check failure - 5 unexpected failure.");
    }
}
