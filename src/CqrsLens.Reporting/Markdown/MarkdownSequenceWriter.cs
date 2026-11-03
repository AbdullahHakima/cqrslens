using System.Text;

namespace CqrsLens;

/// <summary>Writes the per-request sequence diagram section.</summary>
internal static class MarkdownSequenceWriter
{
    internal static void Write(StringBuilder md, AnalysisResult result)
    {
        var sequences = RequestSequenceBuilder.Build(result);
        if (sequences.Count == 0)
            return;

        md.AppendLine("## Request sequences");
        md.AppendLine();
        md.AppendLine("_One sequence diagram per request with a resolved handler, built only from data " +
                      "the scan model holds. Participants: endpoint → mediator → pipeline behaviors " +
                      "(registered order) → handler → DbContext → published notifications. A DbContext " +
                      "participant appears only when the model holds a handler → DbContext edge; behavior " +
                      "order is registered order, not execution order._");
        md.AppendLine();

        foreach (var sequence in sequences)
        {
            md.AppendLine($"### {MarkdownEscaping.MarkdownText(sequence.RequestName)}");
            md.AppendLine();
            MarkdownDiagramWriter.AppendDiagramOrLimitNote(md, sequence.Source, "request sequence");
            md.AppendLine();
            md.AppendLine($"_{MarkdownEscaping.MarkdownText(sequence.Caption)}_");
            md.AppendLine();
        }
    }
}
