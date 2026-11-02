using System.Text;

namespace CqrsLens;

/// <summary>Writes the endpoint catalog: one row per discovered HTTP endpoint.</summary>
internal static class MarkdownEndpointWriter
{
    internal static void Write(StringBuilder md, AnalysisResult result)
    {
        var view = EndpointCatalogView.Build(result);

        md.AppendLine("## API Endpoints");
        md.AppendLine();
        md.AppendLine("_The HTTP surface and the request each endpoint dispatches to. `inferred` marks a " +
                      "route or verb guessed from naming rather than read from source; an `unresolved` " +
                      "target has no matching request in the model._");
        md.AppendLine();

        if (view.EndpointCount == 0)
        {
            md.AppendLine("_No endpoints discovered._");
            md.AppendLine();
            return;
        }

        var inferred = view.InferredCount > 0 ? $" ({view.InferredCount} inferred)" : "";
        md.AppendLine($"**{view.EndpointCount} endpoint(s)**{inferred} across {view.ProjectCount} project(s).");
        md.AppendLine();
        md.AppendLine("| Method | Route | Endpoint | Project | Target request | Handler | Inferred |");
        md.AppendLine("| --- | --- | --- | --- | --- | --- | :---: |");
        foreach (var row in view.Rows)
        {
            md.AppendLine($"| {MarkdownEscaping.TableCell(row.Method)} | {MarkdownEscaping.TableCell(row.Route)} | " +
                          $"{MarkdownEscaping.TableCell(row.Endpoint)} | {MarkdownEscaping.TableCell(row.Project)} | " +
                          $"{MarkdownEscaping.TableCell(row.Target)} | {MarkdownEscaping.TableCell(row.Handler)} | " +
                          $"{(row.Inferred ? "yes" : "—")} |");
        }
        md.AppendLine();
    }
}
