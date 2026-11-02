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

        // Record the resolved configuration for the reports' declared-vs-actual view.
        // Facts the scan already resolved; nothing here feeds detection.
        result.Configuration = new ConfigurationProvenance
        {
            ConfigFilePath = scan.Settings.ConfigPath,
            DeclaredMarkers = MediatorConventions.EffectiveMarkers(scan.Markers, result.Conventions)
                .OrderBy(m => m, StringComparer.Ordinal).ToList(),
            DeclaredHandlerMarkers = MediatorConventions.EffectiveHandlerMarkers(scan.HandlerMarkers, result.Conventions)
                .OrderBy(m => m, StringComparer.Ordinal).ToList(),
            LayerOverrides = scan.Settings.LayerOverrides
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
            IncludeGlobs = scan.Settings.Include.ToList(),
            ExcludeGlobs = scan.Settings.Exclude.ToList(),
            RuleOverrides = scan.Settings.RuleSeverities
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
        };

        if (options.Profile)
            ProfileWriter.Write(result, stderr);

        if (console.StreamsSlice)
        {
            stdout.Write(AgentReportGenerator.Generate(result, options.SliceFilter));
            if (options.ExportSvgDirectory is not null)
                SvgExporter.Export(result, options.ExportSvgDirectory, scan.CqrsMapSplit);
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

        if (options.ExportSvgDirectory is not null)
            SvgExporter.Export(result, options.ExportSvgDirectory, scan.CqrsMapSplit);

        if (options.GitHubAnnotations)
            GitHubAnnotations.Write(result, stdout, scan.CiRoot);

        console.WriteSummaryCard(result, outputs.Report);
        ScanReporter.Write(result, options, outputs, diff, stdout, stderr);

        return ScanReporter.StrictExitCode(result, options, stderr);
    }
}
