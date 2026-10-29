using System.Text;

namespace CqrsLens;

/// <summary>Writes the pipeline behaviors section.</summary>
internal static class MarkdownBehaviorWriter
{
    internal static void Write(StringBuilder md, AnalysisResult result)
    {
        md.AppendLine("## Pipeline Behaviors");
        md.AppendLine();
        if (result.Behaviors.Count == 0)
        {
            md.AppendLine("_No IPipelineBehavior implementations found._");
        }
        else
        {
            foreach (var b in result.Behaviors.OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase))
                md.AppendLine($"- {MarkdownEscaping.CodeSpan(b.Name)}{(b.ProjectName is null ? "" : $" — {MarkdownEscaping.InlineText(b.ProjectName)}")}");
        }
        md.AppendLine();
    }
}
