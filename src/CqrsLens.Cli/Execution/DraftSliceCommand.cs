using System.Text.Json;

namespace CqrsLens;

internal static class DraftSliceCommand
{
    private static readonly JsonSerializerOptions OutputOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static int Run(CliOptions options, TextWriter stdout)
    {
        var scan = ScanPreparer.Prepare(options, requireProjects: false);
        var current = Analyzer.Analyze(scan.RootPath, scan.AnalysisMode, scan.CreateAnalysisOptions());
        var draft = SliceDesignDrafter.Draft(current, options.DraftSliceRequest!);
        stdout.WriteLine(JsonSerializer.Serialize(draft, OutputOptions));
        return ExitCodes.Success;
    }
}
