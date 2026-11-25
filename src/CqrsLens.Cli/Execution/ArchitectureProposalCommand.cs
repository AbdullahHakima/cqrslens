using System.Text.Json;
using System.Text.Json.Nodes;

namespace CqrsLens;

internal static class ArchitectureProposalCommand
{
    private static readonly JsonSerializerOptions OutputOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static int Run(CliOptions options, TextWriter stdout)
    {
        var input = ReadInput(options.ProposalPath!);
        var scan = ScanPreparer.Prepare(options, requireProjects: false);
        var current = Analyzer.Analyze(scan.RootPath, scan.AnalysisMode, scan.CreateAnalysisOptions());
        var evaluation = ArchitectureProposalEvaluator.Evaluate(current, input.Projects, input.References);

        var output = ModelingOutputWriter.ResolveFormat(options) switch
        {
            "md" => ProposalReportGenerator.GenerateMarkdown(current, evaluation),
            "html" => ProposalReportGenerator.GenerateHtml(current, evaluation, scan.RenderMode),
            _ => JsonSerializer.Serialize(evaluation, OutputOptions),
        };
        ModelingOutputWriter.Write(options, stdout, output);

        return options.Strict && evaluation.Status != "checks_passed"
            ? ExitCodes.StrictFailure
            : ExitCodes.Success;
    }

    private static ArchitectureProposalInput ReadInput(string path)
    {
        try
        {
            var json = BoundedFileReader.ReadAllText(path);
            if (JsonNode.Parse(json) is not JsonObject root)
                throw new ArgumentException("Expected a JSON object with 'projects' or 'references'.");

            ModelJson.ValidateKeys(root, "proposal", "projects", "references");
            return ArchitectureProposalInput.Read(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException)
        {
            throw new CliFailureException($"invalid proposal '{path}': {ex.Message}", ExitCodes.Usage);
        }
    }
}
