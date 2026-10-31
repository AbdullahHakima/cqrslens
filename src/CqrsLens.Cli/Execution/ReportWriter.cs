namespace CqrsLens;

/// <summary>Renders the primary report plus the optional SARIF and job summary files.</summary>
internal static class ReportWriter
{
    public static void Write(
        AnalysisResult result,
        CliOptions options,
        PreparedScan scan,
        ReportOutputPaths outputs,
        ReportFormat format,
        ArchitectureDiff? diff)
    {
        OutputFileWriter.Write(outputs.Report, Render(result, options, format, scan.RenderMode));

        if (outputs.Sarif is not null)
            OutputFileWriter.Write(outputs.Sarif, SarifReportGenerator.Generate(result, scan.CiRoot));

        if (outputs.Summary is not null)
            OutputFileWriter.Write(outputs.Summary, SummaryGenerator.Generate(result, scan.CiRoot, diff));
    }

    private static string Render(
        AnalysisResult result,
        CliOptions options,
        ReportFormat format,
        DiagramRenderMode renderMode)
        => format switch
        {
            ReportFormat.Html => HtmlReportGenerator.Generate(result, renderMode),
            ReportFormat.Json => JsonReportGenerator.Generate(result),
            ReportFormat.Agent => AgentReportGenerator.Generate(result, options.SliceFilter),
            _ => ReportGenerator.Generate(result),
        };
}
