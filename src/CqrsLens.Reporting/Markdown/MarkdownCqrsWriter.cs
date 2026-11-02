using System.Text;

namespace CqrsLens;

/// <summary>Writes the CQRS map diagrams and request inventory table.</summary>
internal static class MarkdownCqrsWriter
{
    internal static void Write(StringBuilder md, AnalysisResult result)
        => Write(md, result, CqrsMapSplitMode.Count);

    internal static void Write(StringBuilder md, AnalysisResult result, CqrsMapSplitMode cqrsMapSplit)
    {
        md.AppendLine("## CQRS Map");
        md.AppendLine();
        var cqrsSections = DiagramBuilder.CqrsMapSections(result, cqrsMapSplit);
        if (cqrsSections.Count == 0)
        {
            md.AppendLine("_No MediatR requests found._");
        }
        else
        {
            md.AppendLine(CqrsLegend(result));
            md.AppendLine();
            md.AppendLine(GroupingNote(cqrsMapSplit));
            md.AppendLine();

            if (cqrsSections.Count > 1)
            {
                var projectCount = result.Requests
                    .Select(r => string.IsNullOrEmpty(r.ProjectName) ? "(unassigned)" : r.ProjectName)
                    .Distinct(StringComparer.Ordinal)
                    .Count();
                var projectText = projectCount == 1 ? "1 project" : $"{projectCount} projects";
                md.AppendLine($"_{result.Requests.Count} requests across {projectText}. " +
                              SplitNote(cqrsMapSplit) + "_");
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
        }
        md.AppendLine();
    }

    /// <summary>
    /// Writes the request inventory table. Rendered as a top-level section after the
    /// Flow section so the section order matches the HTML report.
    /// </summary>
    internal static void WriteRequestsTable(StringBuilder md, AnalysisResult result)
    {
        var callersByRequest = result.DispatchCallSites
            .Where(s => s.TargetKind == DispatchTargetKind.Request &&
                        !s.IsTestProject &&
                        !string.IsNullOrEmpty(s.SenderFullName))
            .GroupBy(s => s.TargetFullName, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Select(s => MermaidDiagramText.ShortTypeName(s.SenderFullName))
                      .Distinct(StringComparer.Ordinal)
                      .OrderBy(n => n, StringComparer.Ordinal)
                      .ToList(),
                StringComparer.Ordinal);

        md.AppendLine("## Requests");
        md.AppendLine();
        md.AppendLine("| Request | Type | Response | Handler | Validators | Dispatched by | Project |");
        md.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");
        foreach (var r in result.Requests)
        {
            callersByRequest.TryGetValue(r.FullName, out var callers);
            var dispatchedBy = callers is { Count: > 0 } ? string.Join(", ", callers) : "—";
            md.AppendLine($"| {MarkdownEscaping.TableCell(r.Name)} | {MarkdownEscaping.TableCell(r.Kind)} | {MarkdownEscaping.TableCell(r.ResponseType ?? "—")} | " +
                          $"{MarkdownEscaping.TableCell(r.Handlers.Count > 0 ? string.Join(", ", r.Handlers.Select(MermaidDiagramText.ShortTypeName)) : "—")} | " +
                          $"{MarkdownEscaping.TableCell(r.Validators.Count > 0 ? string.Join(", ", r.Validators.Select(MermaidDiagramText.ShortTypeName)) : "—")} | " +
                          $"{MarkdownEscaping.TableCell(dispatchedBy)} | " +
                          $"{MarkdownEscaping.TableCell(r.ProjectName ?? "—")} |");
        }
    }

    private static string GroupingNote(CqrsMapSplitMode split)
        => split == CqrsMapSplitMode.Feature
            ? "_Requests are grouped by feature. Handler and validator nodes are shown " +
              "where linked and may be declared in a different project._"
            : "_Requests are grouped by declaring project. Handler and validator nodes are shown " +
              "where linked and may be declared in a different project._";

    private static string SplitNote(CqrsMapSplitMode split) => split switch
    {
        CqrsMapSplitMode.Feature =>
            "Diagrams are split by feature; every request appears exactly once.",
        CqrsMapSplitMode.Unsplit =>
            "Diagrams are grouped by project and only split further when a map exceeds GitHub's " +
            "rendering limits; every request appears exactly once.",
        _ =>
            "Diagrams are split by project and balanced so each stays within GitHub's " +
            "rendering limits; every request appears exactly once.",
    };

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
