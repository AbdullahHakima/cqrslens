namespace CqrsLens;

public static class Program
{
    public static int Main(string[] args)
    {
        string? path = null;
        string? output = null;

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

        var result = Analyzer.Analyze(path);
        var report = ReportGenerator.Generate(result);

        var outputPath = output ?? Path.Combine(Directory.GetCurrentDirectory(), "ARCHITECTURE.md");
        File.WriteAllText(outputPath, report);

        Console.WriteLine($"Scanned {result.Projects.Count} project(s): " +
                          $"{result.Requests.Count} request(s), {result.Handlers.Count} handler(s), " +
                          $"{result.Validators.Count} validator(s), {result.Behaviors.Count} behavior(s), " +
                          $"{result.DbContexts.Count} DbContext(s).");
        Console.WriteLine($"Report written to {Path.GetFullPath(outputPath)}");
        return 0;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("CqrsLens 0.1.0 — see your MediatR codebase.");
        Console.WriteLine();
        Console.WriteLine("Usage: cqrslens <path> [-o OUTPUT.md]");
        Console.WriteLine();
        Console.WriteLine("  <path>        Folder of the .NET solution to scan (searched recursively).");
        Console.WriteLine("  -o, --output  Report path. Default: ARCHITECTURE.md in the current directory.");
        Console.WriteLine("  -h, --help    Show this help.");
    }
}
