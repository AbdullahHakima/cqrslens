using System.Text;

namespace CqrsLens;

/// <summary>Writes the CQRS map diagrams and request inventory table.</summary>
internal static class MarkdownCqrsWriter
{
    internal static void Write(StringBuilder md, AnalysisResult result)
    {
        md.AppendLine("## CQRS Map");
        md.AppendLine();
        var cqrsSections = DiagramBuilder.CqrsMapSections(result);
        if (cqrsSections.Count == 0)
        {
            md.AppendLine("_No MediatR requests found._");
        }
        else
        {
            md.AppendLine(CqrsLegend(result));
            md.AppendLine();
            md.AppendLine("_Requests are grouped by declaring project. Handler and validator nodes are shown " +
                          "where linked and may be declared in a different project._");
            md.AppendLine();

            if (cqrsSections.Count > 1)
            {
                var projectCount = result.Requests
                    .Select(r => string.IsNullOrEmpty(r.ProjectName) ? "(unassigned)" : r.ProjectName)
                    .Distinct(StringComparer.Ordinal)
                    .Count();
                var projectText = projectCount == 1 ? "1 project" : $"{projectCount} projects";
                md.AppendLine($"_{result.Requests.Count} requests across {projectText}. " +
                              "Diagrams are split by project and balanced so each stays within GitHub's " +
                              "rendering limits; every request appears exactly once._");
                md.AppendLine();
            }

            foreach (var section in cqrsSections)
            {
                if (cqrsSections.Count > 1)
                {
                    md.AppendLine($"### {MarkdownEscaping.MarkdownText(section.Heading)}");
                    md.AppendLine();
                }

                if (!DiagramBuilder.IsWithinMermaidLimits(section.Source))
                {
                    md.AppendLine($"_Diagram omitted: {section.Requests.Count} requests exceed the CqrsLens " +
                                  "safety limit (45,000 characters / 400 edges). The Requests table below " +
                                  "lists every request._");
                }
                else
                {
                    MarkdownDiagramWriter.AppendMermaid(md, section.Source);
                    md.AppendLine();
                    md.AppendLine($"_{MarkdownEscaping.MarkdownText(section.Caption)}_");
                }
                md.AppendLine();
            }

            WriteRequestsTable(md, result);
        }
        md.AppendLine();
    }

    private static void WriteRequestsTable(StringBuilder md, AnalysisResult result)
    {
        md.AppendLine("### Requests");
        md.AppendLine();
        md.AppendLine("| Request | Type | Response | Handler | Validators | Project |");
        md.AppendLine("| --- | --- | --- | --- | --- | --- |");
        foreach (var r in result.Requests)
        {
            md.AppendLine($"| {MarkdownEscaping.TableCell(r.Name)} | {MarkdownEscaping.TableCell(r.Kind)} | {MarkdownEscaping.TableCell(r.ResponseType ?? "—")} | " +
                          $"{MarkdownEscaping.TableCell(r.Handlers.Count > 0 ? string.Join(", ", r.Handlers.Select(CSharpScanner.SimpleName)) : "—")} | " +
                          $"{MarkdownEscaping.TableCell(r.Validators.Count > 0 ? string.Join(", ", r.Validators.Select(CSharpScanner.SimpleName)) : "—")} | " +
                          $"{MarkdownEscaping.TableCell(r.ProjectName ?? "—")} |");
        }
    }

    private static string CqrsLegend(AnalysisResult result)
    {
        var text = "**Legend:** blue = command · teal = query · violet = other request · " +
                   "gray stadium = handler · amber = validator.";
        if (result.Checks.Any(c => c.Code == ArchitectureChecker.MissingHandlerCode))
            text += " Red dashed request = `MissingHandler` finding.";
        if (result.Checks.Any(c => c.Code == ArchitectureChecker.CommandWithoutValidatorCode))
            text += " Amber dashed request = `CommandWithoutValidator` finding.";
        return text;
    }
}
