namespace CqrsLens;

public sealed class CliOptions
{
    public string? Path { get; set; }
    public string? Output { get; set; }
    public string? Format { get; set; }
    public bool Strict { get; set; }
    public HashSet<string> Markers { get; } = new(StringComparer.Ordinal);
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
                case "--format":
                    if (i + 1 >= args.Length)
                        return Error("error: --format needs a value: md or html");
                    options.Format = args[++i].ToLowerInvariant();
                    break;
                case "--markers":
                    if (i + 1 >= args.Length)
                        return Error("error: --markers needs a value, e.g. --markers ICommand,IQuery");
                    foreach (var name in args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        options.Markers.Add(CSharpScanner.SimpleName(name));
                    break;
                case "--strict":
                    options.Strict = true;
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

        if (options.Path is null)
            return Error("error: pass a folder to scan.", showUsage: true);

        if (options.Format is not null && options.Format != "md" && options.Format != "html")
            return Error($"error: unknown format '{options.Format}' - use md or html.");

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
            return Execute(command.Options!, stdout, stderr);
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
        if (CsprojParser.FindProjectFiles(fullRoot).Count == 0)
        {
            stderr.WriteLine($"error: no .csproj files found under: {path}");
            stderr.WriteLine("CqrsLens scans .NET solutions - point it at a folder containing at least one project.");
            return ExitCodes.NoProjects;
        }

        var result = Analyzer.Analyze(path, options.Markers);

        var isHtml = options.Format == "html" ||
                     (options.Format is null &&
                      options.Output is not null &&
                      options.Output.EndsWith(".html", StringComparison.OrdinalIgnoreCase));
        var report = isHtml ? HtmlReportGenerator.Generate(result) : ReportGenerator.Generate(result);

        var output = options.Output ?? Path.Combine(
            Directory.GetCurrentDirectory(), isHtml ? "ARCHITECTURE.html" : "ARCHITECTURE.md");
        var fullOutput = Path.GetFullPath(output);

        if (Directory.Exists(fullOutput) ||
            output.EndsWith(Path.DirectorySeparatorChar) ||
            output.EndsWith(Path.AltDirectorySeparatorChar))
        {
            stderr.WriteLine($"error: output path is a directory: {output}");
            return ExitCodes.Usage;
        }

        var outputDirectory = Path.GetDirectoryName(fullOutput);
        if (!string.IsNullOrEmpty(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        File.WriteAllText(fullOutput, report);

        stdout.WriteLine($"Scanned {result.Projects.Count} project(s): " +
                         $"{result.Requests.Count} request(s), {result.Handlers.Count} handler(s), " +
                         $"{result.Validators.Count} validator(s), {result.Behaviors.Count} behavior(s), " +
                         $"{result.DbContexts.Count} DbContext(s).");
        stdout.WriteLine($"Report written to {fullOutput}");
        if (result.SkippedFiles.Count > 0)
            stderr.WriteLine($"warning: {result.SkippedFiles.Count} source file(s) skipped - see the report.");

        var errors = result.Checks.Count(c => c.Severity == CheckSeverity.Error);
        var warnings = result.Checks.Count - errors;
        if (result.Checks.Count > 0)
            stdout.WriteLine($"Architecture checks: {errors} error(s), {warnings} warning(s) - see the report.");

        if (options.Strict && errors > 0)
        {
            stderr.WriteLine($"error: --strict - {errors} architecture check error(s) found.");
            return ExitCodes.StrictFailure;
        }

        return ExitCodes.Success;
    }

    public static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine($"CqrsLens {ToolInfo.Version} - see your MediatR codebase.");
        writer.WriteLine();
        writer.WriteLine("Usage: cqrslens <path> [-o OUTPUT] [--format md|html] [--markers ICommand,IQuery] [--strict]");
        writer.WriteLine();
        writer.WriteLine("  <path>        Folder of the .NET solution to scan (searched recursively).");
        writer.WriteLine("  -o, --output  Report path. Default: ARCHITECTURE.md (or .html) in the current directory.");
        writer.WriteLine("  --format      Report format: md (default) or html. A .html -o path implies html.");
        writer.WriteLine("  --markers     Comma-separated custom marker interface names that also count");
        writer.WriteLine("                as request roots (e.g. ICommand,IQuery,IMessage). Requests hidden");
        writer.WriteLine("                behind interfaces deriving from IRequest are always followed.");
        writer.WriteLine("  --strict      Exit with code 4 when architecture checks report any error.");
        writer.WriteLine("  --version     Print the tool version and exit.");
        writer.WriteLine("  -h, --help    Show this help.");
        writer.WriteLine();
        writer.WriteLine("Exit codes: 0 ok - 1 usage error - 2 path not found - 3 no projects found - 4 strict check failure - 5 unexpected failure.");
    }
}
