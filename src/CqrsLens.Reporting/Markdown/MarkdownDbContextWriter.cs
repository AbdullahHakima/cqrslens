using System.Text;

namespace CqrsLens;

/// <summary>Writes the DbContext inventory and ER diagram sections.</summary>
internal static class MarkdownDbContextWriter
{
    internal static void Write(StringBuilder md, AnalysisResult result)
    {
        md.AppendLine("## Data Access (DbContexts)");
        md.AppendLine();
        if (result.DbContexts.Count == 0)
        {
            md.AppendLine("_No DbContext classes found._");
        }
        else
        {
            var orderedContexts = result.DbContexts
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Namespace, StringComparer.Ordinal)
                .ToList();
            var contextNameCounts = orderedContexts
                .GroupBy(c => c.Name, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

            md.AppendLine("| DbContext | Project | DbSets (property → entity) |");
            md.AppendLine("| --- | --- | --- |");
            foreach (var ctx in orderedContexts)
            {
                var sets = ctx.DbSets.Count > 0
                    ? string.Join(", ", ctx.DbSets.Select(s => $"{s.Property} → {s.Entity}"))
                    : "—";
                md.AppendLine(
                    $"| {MarkdownEscaping.TableCell(ctx.Name)} | {MarkdownEscaping.TableCell(ctx.ProjectName ?? "—")} | {MarkdownEscaping.TableCell(sets)} |");
            }

            foreach (var ctx in orderedContexts)
            {
                var erDiagram = DiagramBuilder.ErDiagram(ctx);
                if (erDiagram is null) continue;

                var title = contextNameCounts[ctx.Name] > 1 && !string.IsNullOrEmpty(ctx.Namespace)
                    ? $"{ctx.Namespace}.{ctx.Name}"
                    : ctx.Name;
                md.AppendLine();
                md.AppendLine($"### {MarkdownEscaping.MarkdownText(title)} ER Diagram");
                md.AppendLine();
                MarkdownDiagramWriter.AppendDiagramOrLimitNote(md, erDiagram, $"{MarkdownEscaping.InlineText(title)} ER diagram");
            }
        }
        md.AppendLine();
    }
}
