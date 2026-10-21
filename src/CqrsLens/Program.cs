namespace CqrsLens;

public static class Program
{
    public static int Main(string[] args)
    {
        string? path = null;
        string? output = null;
        var strict = false;
        string? format = null;
        var markers = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-h" or "--help":
                    PrintUsage();
                    return 0;
                case "-o" or "--output":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("error: -o needs a value, e.g. -o ARCHITECTURE.md");
                        return 1;
                    }
                    output = args[++i];
                    break;
                case "--format":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("error: --format needs a value: md or html");
                        return 1;
                    }
                    format = args[++i].ToLowerInvariant();
                    break;
                case "--markers":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("error: --markers needs a value, e.g. --markers ICommand,IQuery");
                        return 1;
                    }
                    foreach (var name in args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        markers.Add(CSharpScanner.SimpleName(name));
                    break;
                case "--strict":
                    strict = true;
                    break;
                default:
                    if (args[i].StartsWith('-'))
                    {
                        Console.Error.WriteLine($"error: unknown option '{args[i]}'");
                        PrintUsage();
                        return 1;
                    }
                    if (path is not null)
                    {
                        Console.Error.WriteLine("error: pass exactly one path to scan.");
                        return 1;
                    }
                    path = args[i];
                    break;
            }
        }

        if (path is null)
        {
            PrintUsage();
            return 1;
        }

        if (format is not null && format != "md" && format != "html")
        {
            Console.Error.WriteLine($"error: unknown format '{format}' — use md or html.");
            return 1;
        }

        if (!Directory.Exists(path))
        {
            Console.Error.WriteLine($"error: path not found: {path}");
            return 2;
        }

        var csprojCount = CsprojParser.FindProjectFiles(Path.GetFullPath(path)).Count;
        if (csprojCount == 0)
        {
            Console.Error.WriteLine($"error: no .csproj files found under: {path}");
            Console.Error.WriteLine("CqrsLens scans .NET solutions — point it at a folder containing at least one project.");
            return 3;
        }

        var result = Analyzer.Analyze(path, markers);

        var isHtml = format == "html" ||
                     (format is null && output is not null &&
                      output.EndsWith(".html", StringComparison.OrdinalIgnoreCase));
        var report = isHtml ? HtmlReportGenerator.Generate(result) : ReportGenerator.Generate(result);

        var outputPath = output ?? Path.Combine(
            Directory.GetCurrentDirectory(), isHtml ? "ARCHITECTURE.html" : "ARCHITECTURE.md");
        File.WriteAllText(outputPath, report);

        Console.WriteLine($"Scanned {result.Projects.Count} project(s): " +
                          $"{result.Requests.Count} request(s), {result.Handlers.Count} handler(s), " +
                          $"{result.Validators.Count} validator(s), {result.Behaviors.Count} behavior(s), " +
                          $"{result.DbContexts.Count} DbContext(s).");
        Console.WriteLine($"Report written to {Path.GetFullPath(outputPath)}");

        var errors = result.Checks.Count(c => c.Severity == CheckSeverity.Error);
        var warnings = result.Checks.Count - errors;
        if (result.Checks.Count > 0)
            Console.WriteLine($"Architecture checks: {errors} error(s), {warnings} warning(s) — see the report.");

        if (strict && errors > 0)
        {
            Console.Error.WriteLine($"error: --strict — {errors} architecture check error(s) found.");
            return 4;
        }
        return 0;
    }

    private static void PrintUsage()
    {
        Console.WriteLine($"CqrsLens {ToolInfo.Version} — see your MediatR codebase.");
        Console.WriteLine();
        Console.WriteLine("Usage: cqrslens <path> [-o OUTPUT] [--format md|html] [--markers ICommand,IQuery] [--strict]");
        Console.WriteLine();
        Console.WriteLine("  <path>        Folder of the .NET solution to scan (searched recursively).");
        Console.WriteLine("  -o, --output  Report path. Default: ARCHITECTURE.md (or .html) in the current directory.");
        Console.WriteLine("  --format      Report format: md (default) or html. A .html -o path implies html.");
        Console.WriteLine("  --markers     Comma-separated custom marker interface names that also count");
        Console.WriteLine("                as request roots (e.g. ICommand,IQuery,IMessage). Requests hidden");
        Console.WriteLine("                behind interfaces deriving from IRequest are always followed.");
        Console.WriteLine("  --strict      Exit with code 4 when architecture checks report any error.");
        Console.WriteLine("  -h, --help    Show this help.");
        Console.WriteLine();
        Console.WriteLine("Exit codes: 0 ok · 1 usage error · 2 path not found · 3 no projects found · 4 strict check failure.");
    }
}
