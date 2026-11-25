using System.Text;

namespace CqrsLens;

/// <summary>Writes the per-operation sequence diagrams for direct non-CQRS operations.</summary>
internal static class MarkdownDirectSequenceWriter
{
    internal static void Write(
        StringBuilder md,
        AnalysisResult result,
        string? focusService = null,
        bool excludeTests = false,
        string heading = "Operation Sequences")
    {
        var sequences = DirectSequenceBuilder.Build(result, focusService, excludeTests);
        if (sequences.Count == 0)
            return;

        md.AppendLine($"## {MarkdownEscaping.MarkdownText(heading)}");
        md.AppendLine();
        md.AppendLine("_Sequence diagrams for non-CQRS operations (direct HTTP endpoints, gRPC RPCs, workers, and event consumers) " +
                      "built strictly from traced service, DbContext, and integration-event dependencies._");
        md.AppendLine();

        foreach (var seq in sequences)
        {
            md.AppendLine($"### {MarkdownEscaping.MarkdownText(seq.OperationName)}");
            md.AppendLine();
            MarkdownDiagramWriter.AppendDiagramOrLimitNote(md, seq.Source, "operation sequence");
            md.AppendLine();
            md.AppendLine($"_{MarkdownEscaping.MarkdownText(seq.Caption)}_");
            md.AppendLine();
        }
    }
}
