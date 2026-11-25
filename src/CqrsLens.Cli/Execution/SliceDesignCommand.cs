using System.Text.Json;
using System.Text.Json.Nodes;

namespace CqrsLens;

internal static class SliceDesignCommand
{
    private static readonly JsonSerializerOptions OutputOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static int Run(CliOptions options, TextWriter stdout)
    {
        var designPath = options.SliceDesignPath!;
        if (Directory.Exists(designPath))
            return RunDirectory(options, designPath, stdout);

        var design = ModelingOutputWriter.ReadSliceDesign(designPath);
        var scan = ScanPreparer.Prepare(options, requireProjects: false);
        var current = Analyzer.Analyze(scan.RootPath, scan.AnalysisMode, scan.CreateAnalysisOptions());
        var evaluation = SliceDesignEvaluator.Evaluate(current, design);

        var output = ModelingOutputWriter.ResolveFormat(options) switch
        {
            "md" => SliceDesignReportGenerator.GenerateMarkdown(evaluation),
            "html" => SliceDesignReportGenerator.GenerateHtml(evaluation, scan.RenderMode),
            _ => JsonSerializer.Serialize(evaluation, OutputOptions),
        };
        ModelingOutputWriter.Write(options, stdout, output);

        return options.Strict && evaluation.Status != SliceDesignEvaluator.Matched
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

        var current = Analyzer.Analyze(scan.RootPath, scan.AnalysisMode, scan.CreateAnalysisOptions());

        var evaluations = new List<(string File, SliceDesignEvaluation Evaluation)>(designs.Count);
        var resultsArray = new JsonArray();
        var statuses = new List<string>(designs.Count);
        var matched = 0;

        foreach (var (displayPath, design) in designs)
        {
            var evaluation = SliceDesignEvaluator.Evaluate(current, design);
            evaluations.Add((displayPath, evaluation));
            statuses.Add(evaluation.Status);
            if (evaluation.Status == SliceDesignEvaluator.Matched)
                matched++;

            resultsArray.Add(new JsonObject
            {
                ["file"] = displayPath,
                ["evaluation"] = JsonSerializer.SerializeToNode(evaluation, OutputOptions),
            });
        }

        var overallStatus = new[]
        {
            SliceDesignEvaluator.Mismatch,
            SliceDesignEvaluator.Missing,
            SliceDesignEvaluator.NotDeterminable,
            SliceDesignEvaluator.Matched,
        }.First(candidate => statuses.Contains(candidate, StringComparer.Ordinal));

        var rootDisplay = ModelingOutputWriter.IsWithinRoot(scan.RootPath, fullDesignDir)
            ? Path.GetRelativePath(scan.RootPath, fullDesignDir).Replace('\\', '/')
            : fullDesignDir.Replace('\\', '/');

        var format = ModelingOutputWriter.ResolveFormat(options);
        var output = format switch
        {
            "md" => SliceDesignReportGenerator.GenerateMarkdown(rootDisplay, evaluations),
            "html" => SliceDesignReportGenerator.GenerateHtml(rootDisplay, evaluations, scan.RenderMode),
            _ => new JsonObject
            {
                ["status"] = overallStatus,
                ["root"] = rootDisplay,
                ["total"] = designs.Count,
                ["matched"] = matched,
                ["results"] = resultsArray,
            }.ToJsonString(OutputOptions),
        };

        ModelingOutputWriter.Write(options, stdout, output);
        return options.Strict && (designs.Count == 0 || matched != designs.Count)
            ? ExitCodes.StrictFailure
            : ExitCodes.Success;
    }
}
