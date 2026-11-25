using System.Text.Json;

namespace CqrsLens;

internal static class ChangeImpactCommand
{
    private static readonly JsonSerializerOptions OutputOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static int Run(CliOptions options, TextWriter stdout)
    {
        var scan = ScanPreparer.Prepare(options, requireProjects: false);
        var current = Analyzer.Analyze(scan.RootPath, scan.AnalysisMode, scan.CreateAnalysisOptions());

        ChangeImpactReport report;
        try
        {
            report = ChangeImpactAnalyzer.Analyze(current, options.ImpactTarget!, options.ImpactKind);
        }
        catch (ArgumentException ex)
        {
            throw new CliFailureException(ex.Message, ExitCodes.Usage);
        }

        var output = ModelingOutputWriter.ResolveFormat(options) switch
        {
            "md" => ImpactReportGenerator.GenerateMarkdown(report),
            "html" => ImpactReportGenerator.GenerateHtml(report, scan.RenderMode),
            _ => JsonSerializer.Serialize(report, OutputOptions),
        };
        ModelingOutputWriter.Write(options, stdout, output);
        return ExitCodes.Success;
    }
}
