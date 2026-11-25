using System.Text.Json;
using System.Xml;

namespace CqrsLens;

internal static class TddCycleCommand
{
    private static readonly JsonSerializerOptions OutputOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static readonly string[] PhasePriority =
    {
        "drifted",
        "inconclusive",
        "red",
        "spec_draft",
        "green_incomplete",
        "verified",
    };

    public static int Run(CliOptions options, TextWriter stdout)
    {
        var tddPath = options.TddCheckPath!;
        if (Directory.Exists(tddPath))
            return RunDirectory(options, tddPath, stdout);

        var design = ModelingOutputWriter.ReadSliceDesign(tddPath);
        var runtimeResults = ReadTestResults(options.TestResultsPath);
        var scan = ScanPreparer.Prepare(options, requireProjects: false);
        var current = Analyzer.Analyze(scan.RootPath, scan.AnalysisMode, scan.CreateAnalysisOptions());
        var evaluation = TddCycleEvaluator.Evaluate(current, design, runtimeResults);

        var output = ModelingOutputWriter.ResolveFormat(options) switch
        {
            "md" => TddReportGenerator.GenerateMarkdown(evaluation),
            "html" => TddReportGenerator.GenerateHtml(evaluation, scan.RenderMode),
            _ => JsonSerializer.Serialize(evaluation, OutputOptions),
        };
        ModelingOutputWriter.Write(options, stdout, output);

        return options.Strict && evaluation.Phase != "verified"
            ? ExitCodes.StrictFailure
            : ExitCodes.Success;
    }

    private static int RunDirectory(CliOptions options, string designDirectory, TextWriter stdout)
    {
        var fullDesignDir = Path.GetFullPath(designDirectory);
        var scan = ScanPreparer.Prepare(options, requireProjects: false);
        var rootBase = ModelingOutputWriter.IsWithinRoot(scan.RootPath, fullDesignDir) ? scan.RootPath : fullDesignDir;

        var designFiles = ModelingOutputWriter.DiscoverSliceDesignFiles(fullDesignDir, rootBase);
        if (designFiles.Count == 0)
            throw new CliFailureException($"no '*.slice.json' files found under '{designDirectory}'.", ExitCodes.Usage);

        var designs = designFiles
            .Select(entry => (entry.DisplayPath, Design: ModelingOutputWriter.ReadSliceDesign(entry.FullPath)))
            .ToList();

        var runtimeResults = ReadTestResults(options.TestResultsPath);
        var current = Analyzer.Analyze(scan.RootPath, scan.AnalysisMode, scan.CreateAnalysisOptions());

        var items = new List<TddBatchItem>(designs.Count);
        var verified = 0;
        foreach (var (displayPath, design) in designs)
        {
            var evaluation = TddCycleEvaluator.Evaluate(current, design, runtimeResults);
            items.Add(new TddBatchItem(displayPath, evaluation));
            if (string.Equals(evaluation.Phase, "verified", StringComparison.Ordinal))
                verified++;
        }

        var overallPhase = PhasePriority.FirstOrDefault(p =>
            items.Any(item => string.Equals(item.Evaluation.Phase, p, StringComparison.Ordinal)))
            ?? "verified";

        var rootDisplay = ModelingOutputWriter.IsWithinRoot(scan.RootPath, fullDesignDir)
            ? Path.GetRelativePath(scan.RootPath, fullDesignDir).Replace('\\', '/')
            : fullDesignDir.Replace('\\', '/');

        var batch = new TddBatchEvaluation(overallPhase, rootDisplay, items.Count, verified, items);
        var output = ModelingOutputWriter.ResolveFormat(options) switch
        {
            "md" => TddReportGenerator.GenerateMarkdown(batch),
            "html" => TddReportGenerator.GenerateHtml(batch, scan.RenderMode),
            _ => JsonSerializer.Serialize(batch, OutputOptions),
        };
        ModelingOutputWriter.Write(options, stdout, output);

        return options.Strict && (items.Count == 0 || verified != items.Count)
            ? ExitCodes.StrictFailure
            : ExitCodes.Success;
    }

    private static TrxTestSummary? ReadTestResults(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            return TrxTestResultsParser.ParseFile(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or XmlException or ArgumentException)
        {
            throw new CliFailureException($"invalid test results '{path}': {ex.Message}", ExitCodes.Usage);
        }
    }
}
