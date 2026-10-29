using System.Text;

using static CqrsLens.JsonReportGenerator;

namespace CqrsLens;

/// <summary>Writes the CQRS surface section: request tables, delta diagram and behavior changes.</summary>
internal static class DiffCqrsSurfaceSectionWriter
{
    public static void Write(StringBuilder markdown, ArchitectureDiff diff)
    {
        if (!diff.Requests.HasChanges &&
            !diff.Handlers.HasChanges &&
            !diff.Validators.HasChanges &&
            !diff.Behaviors.HasChanges)
        {
            return;
        }

        markdown.AppendLine("### CQRS Surface Changes");
        markdown.AppendLine();

        var deltaDiagram = DiffCqrsMermaidWriter.Build(diff.Requests);
        if (deltaDiagram is not null && DiagramBuilder.IsWithinMermaidLimits(deltaDiagram))
        {
            markdown.AppendLine("**Delta legend:** green = added request · amber = modified request · dashed red/gray = removed request.");
            markdown.AppendLine();
            markdown.AppendLine("```mermaid");
            markdown.AppendLine(deltaDiagram);
            markdown.AppendLine("```");
            markdown.AppendLine();
        }

        WriteAddedRequests(markdown, diff.Requests.Added);
        WriteModifiedRequests(markdown, diff.Requests.Modified);
        WriteRemovedRequests(markdown, diff.Requests.Removed);
        WriteBehaviorChanges(markdown, diff.Behaviors);
    }

    private static void WriteAddedRequests(StringBuilder markdown, IReadOnlyList<JsonRequest> added)
    {
        if (added.Count == 0)
            return;

        markdown.AppendLine($"#### Added requests ({added.Count})");
        markdown.AppendLine();
        markdown.AppendLine("| Request | Type | Response | Handler | Validators | Project |");
        markdown.AppendLine("| --- | --- | --- | --- | --- | --- |");
        foreach (var request in added)
        {
            var handlers = request.Handlers.Count > 0
                ? string.Join(", ", request.Handlers.Select(CSharpScanner.SimpleName))
                : "—";
            var validators = request.Validators.Count > 0
                ? string.Join(", ", request.Validators.Select(CSharpScanner.SimpleName))
                : "—";
            markdown.AppendLine(
                $"| + {ReportGenerator.TableCell(request.Name)} | {ReportGenerator.TableCell(request.Kind)} | " +
                $"{ReportGenerator.TableCell(request.ResponseType ?? "—")} | {ReportGenerator.TableCell(handlers)} | " +
                $"{ReportGenerator.TableCell(validators)} | {ReportGenerator.TableCell(request.Project ?? "—")} |");
        }

        markdown.AppendLine();
    }

    private static void WriteModifiedRequests(StringBuilder markdown, IReadOnlyList<ModifiedRequestDiff> modified)
    {
        if (modified.Count == 0)
            return;

        markdown.AppendLine($"#### Modified requests ({modified.Count})");
        markdown.AppendLine();
        markdown.AppendLine("| Request | Project | Changes |");
        markdown.AppendLine("| --- | --- | --- |");
        foreach (var request in modified)
        {
            var changes = DiffRequestChangeFormatter.Format(request);
            markdown.AppendLine(
                $"| ~ {ReportGenerator.TableCell(request.Name)} | " +
                $"{ReportGenerator.TableCell(request.Project ?? "—")} | " +
                $"{ReportGenerator.TableCell(changes)} |");
        }

        markdown.AppendLine();
    }

    private static void WriteRemovedRequests(StringBuilder markdown, IReadOnlyList<JsonRequest> removed)
    {
        if (removed.Count == 0)
            return;

        markdown.AppendLine($"#### Removed requests ({removed.Count})");
        markdown.AppendLine();
        markdown.AppendLine("| Request | Type | Response | Project |");
        markdown.AppendLine("| --- | --- | --- | --- |");
        foreach (var request in removed)
        {
            markdown.AppendLine(
                $"| - {ReportGenerator.TableCell(request.Name)} | {ReportGenerator.TableCell(request.Kind)} | " +
                $"{ReportGenerator.TableCell(request.ResponseType ?? "—")} | {ReportGenerator.TableCell(request.Project ?? "—")} |");
        }

        markdown.AppendLine();
    }

    private static void WriteBehaviorChanges(StringBuilder markdown, BehaviorDiffSection behaviors)
    {
        if (!behaviors.HasChanges)
            return;

        markdown.AppendLine("#### Pipeline behaviors");
        markdown.AppendLine();
        foreach (var behavior in behaviors.Added)
        {
            markdown.AppendLine(
                $"- ➕ Added {ReportGenerator.CodeSpan(behavior.Name)}" +
                FormatProject(behavior.Project));
        }

        foreach (var behavior in behaviors.Removed)
        {
            markdown.AppendLine(
                $"- ➖ Removed {ReportGenerator.CodeSpan(behavior.Name)}" +
                FormatProject(behavior.Project));
        }

        markdown.AppendLine();
    }

    private static string FormatProject(string? project)
        => project is null ? "" : $" ({ReportGenerator.InlineText(project)})";
}
