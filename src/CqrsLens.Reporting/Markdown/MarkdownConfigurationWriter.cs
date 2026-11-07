using System.Text;

namespace CqrsLens;

/// <summary>Writes the declared-vs-actual configuration and conventions view.</summary>
internal static class MarkdownConfigurationWriter
{
    internal static void Write(StringBuilder md, AnalysisResult result)
    {
        var view = ConfigurationView.Build(result);

        md.AppendLine("## Configuration & Conventions");
        md.AppendLine();
        md.AppendLine("_The settings this scan ran under (declared) and what it found under them (actual). " +
                      "Declared values are the resolved configuration; actual values are the scan outcome._");
        md.AppendLine();

        md.AppendLine("### Declared");
        md.AppendLine();
        md.AppendLine("| Setting | Value |");
        md.AppendLine("| --- | --- |");
        foreach (var row in view.Declared)
            md.AppendLine($"| {MarkdownEscaping.TableCell(row.Label)} | {MarkdownEscaping.TableCell(row.Value)} |");
        md.AppendLine();

        md.AppendLine("### Actual");
        md.AppendLine();
        md.AppendLine("| Fact | Value |");
        md.AppendLine("| --- | --- |");
        md.AppendLine($"| Layer-model status | {MarkdownEscaping.TableCell(view.LayerStatus)} |");
        md.AppendLine($"| Layer checks | {MarkdownEscaping.TableCell(view.LayerChecks)} |");
        md.AppendLine($"| Requests discovered | {view.RequestsDiscovered} |");
        md.AppendLine($"| Conventions detected | {MarkdownEscaping.TableCell(view.Conventions)} |");
        md.AppendLine();
    }
}
