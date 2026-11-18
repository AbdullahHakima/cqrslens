using System.Text;

namespace CqrsLens;

/// <summary>Writes the unified Architecture Spine section in Markdown reports and shared inventory tables for docs export.</summary>
internal static class MarkdownSpineWriter
{
    internal static void Write(StringBuilder md, AnalysisResult result)
    {
        var view = SpineReportView.Build(result);

        md.AppendLine("## Architecture Spine");
        md.AppendLine();
        md.AppendLine("_Evidence-backed component, endpoint, and relation inventory from the unified architecture spine._");
        md.AppendLine();

        md.AppendLine("| Metric | Count |");
        md.AppendLine("| --- | ---: |");
        md.AppendLine($"| Unique components | {view.ComponentCount} |");
        md.AppendLine($"| Endpoints (`spineEndpoints`) | {view.EndpointCount} |");
        md.AppendLine($"| Relations | {view.RelationCount} |");
        md.AppendLine($"| Relation evidence occurrences | {view.TotalRelationOccurrences} |");
        md.AppendLine($"| Unproven items | {view.UnprovenCount} |");
        md.AppendLine();

        md.AppendLine("### Components by Role");
        md.AppendLine();
        md.AppendLine($"_One declared symbol yields at most one component node with an evidenced role set (unique components: {view.ComponentCount})._");
        md.AppendLine();
        md.AppendLine("| Role | Components | Role Evidence Occurrences |");
        md.AppendLine("| --- | ---: | ---: |");
        foreach (var row in view.Roles)
        {
            var label = row.IsMappingDerivedEndpoint
                ? $"`{row.RoleName}` (mapping-derived)"
                : $"`{row.RoleName}`";
            md.AppendLine($"| {label} | {row.ComponentCount} | {row.EvidenceOccurrences} |");
        }
        md.AppendLine();

        md.AppendLine("### Relations Summary");
        md.AppendLine();
        if (view.RelationsSummary.Count == 0)
        {
            md.AppendLine("_No relations recorded in the model._");
        }
        else
        {
            md.AppendLine("| Relation Kind | Relations | Occurrences |");
            md.AppendLine("| --- | ---: | ---: |");
            foreach (var row in view.RelationsSummary)
                md.AppendLine($"| `{row.KindName}` | {row.RelationCount} | {row.OccurrenceCount} |");
            md.AppendLine($"| **Total** | **{view.RelationCount}** | **{view.TotalRelationOccurrences}** |");
        }
        md.AppendLine();

        WriteEndpointInventoryTable(md, view.Endpoints, lfOnly: false);
        WriteComponentIndexTable(md, view.Components, lfOnly: false);

        md.AppendLine("### Most-Connected Components");
        md.AppendLine();
        if (view.MostConnected.Count == 0)
        {
            md.AppendLine("_No components discovered in the model._");
        }
        else
        {
            md.AppendLine("| Component | In | Out | Total |");
            md.AppendLine("| --- | ---: | ---: | ---: |");
            foreach (var row in view.MostConnected)
            {
                md.AppendLine(
                    $"| {MarkdownEscaping.TableCell(row.Component)} | " +
                    $"{row.Incoming} | {row.Outgoing} | {row.Total} |");
            }
        }
        md.AppendLine();
    }

    internal static void WriteEndpointInventoryTable(
        StringBuilder md,
        IReadOnlyList<SpineReportView.EndpointRow> endpoints,
        bool lfOnly = false)
    {
        AppendLine(md, "### Endpoint Inventory", lfOnly);
        AppendLine(md, lfOnly);
        if (endpoints.Count == 0)
        {
            AppendLine(md, "_No endpoints discovered in the model._", lfOnly);
        }
        else
        {
            AppendLine(md, "| Method | Route | Endpoint | Project | Dispatches to | Inferred | Location |", lfOnly);
            AppendLine(md, "| --- | --- | --- | --- | --- | :---: | --- |", lfOnly);
            foreach (var row in endpoints)
            {
                var route = string.IsNullOrEmpty(row.Route) ? "—" : row.Route;
                AppendLine(
                    md,
                    $"| {MarkdownEscaping.TableCell(row.Method)} | " +
                    $"{MarkdownEscaping.TableCell(route)} | " +
                    $"{MarkdownEscaping.TableCell(row.Endpoint)} | " +
                    $"{MarkdownEscaping.TableCell(row.Project)} | " +
                    $"{MarkdownEscaping.TableCell(row.DispatchesTo)} | " +
                    $"{(row.Inferred ? "yes" : "—")} | " +
                    $"{DocsFormatting.FormatLocation(row.Location)} |",
                    lfOnly);
            }
        }
        AppendLine(md, lfOnly);
    }

    internal static void WriteComponentIndexTable(
        StringBuilder md,
        IReadOnlyList<SpineReportView.ComponentIndexRow> components,
        bool lfOnly = false)
    {
        AppendLine(md, "### Component Index", lfOnly);
        AppendLine(md, lfOnly);
        if (components.Count == 0)
        {
            AppendLine(md, "_No components discovered in the model._", lfOnly);
        }
        else
        {
            AppendLine(md, "| Component | Roles | Project | Location |", lfOnly);
            AppendLine(md, "| --- | --- | --- | --- |", lfOnly);
            foreach (var row in components)
            {
                AppendLine(
                    md,
                    $"| {MarkdownEscaping.TableCell(row.Component)} | " +
                    $"{MarkdownEscaping.TableCell(row.Roles)} | " +
                    $"{MarkdownEscaping.TableCell(row.Project)} | " +
                    $"{DocsFormatting.FormatLocation(row.Location)} |",
                    lfOnly);
            }
        }
        AppendLine(md, lfOnly);
    }

    private static void AppendLine(StringBuilder md, bool lfOnly)
    {
        if (lfOnly)
            DocsFormatting.Line(md);
        else
            md.AppendLine();
    }

    private static void AppendLine(StringBuilder md, string text, bool lfOnly)
    {
        if (lfOnly)
            DocsFormatting.Line(md, text);
        else
            md.AppendLine(text);
    }
}
