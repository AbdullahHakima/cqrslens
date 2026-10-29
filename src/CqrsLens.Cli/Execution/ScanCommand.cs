namespace CqrsLens;

/// <summary>Runs one scan: validate inputs, analyze, apply baselines/diffs and write reports.</summary>
internal static class ScanCommand
{
    public static int Run(CliOptions options, TextWriter stdout, TextWriter stderr)
    {
        var scan = ScanPreparer.Prepare(options);
        var console = new ScanConsole(options, stdout);

        console.WriteBanner(scan.Watermark);

        var analysisOptions = scan.CreateAnalysisOptions(console.CreateProgressCallback());
        var result = Analyzer.Analyze(scan.RootPath, scan.AnalysisMode, analysisOptions);

        if (options.Profile)
            ProfileWriter.Write(result, stderr);

        if (console.StreamsSlice)
        {
            stdout.Write(AgentReportGenerator.Generate(result, options.SliceFilter));
            return ExitCodes.Success;
        }

        console.ClearProgress();

        var format = ReportFormatResolver.Resolve(options);
        var outputs = ReportOutputPathsResolver.Resolve(options, format);

        BaselineProcessor.Apply(options, result);
        var diff = DiffProcessor.Run(
            options.DiffPath,
            outputs.Diff,
            scan.AnalysisMode,
            analysisOptions,
            result,
            scan.CiRoot);

        ReportWriter.Write(result, options, scan, outputs, format, diff);

        if (options.GitHubAnnotations)
            GitHubAnnotations.Write(result, stdout, scan.CiRoot);

        console.WriteSummaryCard(result, outputs.Report);
        ScanReporter.Write(result, options, outputs, diff, stdout, stderr);

        return ScanReporter.StrictExitCode(result, options, stderr);
    }
}
