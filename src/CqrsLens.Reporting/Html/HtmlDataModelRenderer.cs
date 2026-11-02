using System.Text;
using static CqrsLens.HtmlReportText;

namespace CqrsLens;

/// <summary>
/// Renders the combined, solution-level data view: every DbContext, its entities,
/// mapped DbSets and relationships in one place. Pure HTML/CSS, so it renders in
/// both the interactive and the zero-JS SVG reports. Per-context detail is
/// collapsible so the view stays readable with many contexts (e.g. ABP's 80).
/// </summary>
internal static class HtmlDataModelRenderer
{
    internal static void Append(StringBuilder html, DataModelView.Summary view)
    {
        html.AppendLine("<h3>Data model — all DbContexts</h3>");
        html.AppendLine("<p class=\"note\">Every DbContext in the solution with its entities and relationships " +
                        "in one place; each context's entity set matches its ER diagram below. Contexts with no " +
                        "entities discovered are listed explicitly and never dropped.</p>");
        html.AppendLine($"<p><strong>{view.ContextCount}</strong> DbContext(s): " +
                        $"{view.ContextsWithEntities} with entities, {view.ContextsWithoutEntities} with none · " +
                        $"{view.EntityCount} entities · {view.RelationshipCount} relationships.</p>");

        html.AppendLine("<table><thead><tr><th>DbContext</th><th>Project</th><th>Entities</th>" +
                        "<th>DbSets</th><th>Relationships</th></tr></thead>");
        html.AppendLine("<tbody>");
        foreach (var context in view.Contexts)
            html.AppendLine($"<tr><td>{Encode(context.DisplayName)}</td><td>{Encode(context.Project ?? "—")}</td>" +
                            $"<td>{context.EntityCount}</td><td>{context.DbSets.Count}</td>" +
                            $"<td>{context.RelationshipCount}</td></tr>");
        html.AppendLine("</tbody></table>");

        var withoutEntities = view.Contexts.Where(c => c.EntityCount == 0).ToList();
        if (withoutEntities.Count > 0)
        {
            html.AppendLine($"<h4>DbContexts with no entities discovered ({withoutEntities.Count})</h4>");
            html.AppendLine("<table><thead><tr><th>DbContext</th><th>Project</th></tr></thead><tbody>");
            foreach (var context in withoutEntities)
                html.AppendLine($"<tr><td>{Encode(context.DisplayName)}</td>" +
                                $"<td>{Encode(context.Project ?? "—")}</td></tr>");
            html.AppendLine("</tbody></table>");
        }

        foreach (var context in view.Contexts.Where(c => c.EntityCount > 0))
        {
            html.AppendLine("<details class=\"data-model-context\">");
            html.AppendLine($"<summary>{Encode(context.DisplayName)} — {context.EntityCount} entities, " +
                            $"{context.RelationshipCount} relationships</summary>");
            html.AppendLine("<table><thead><tr><th>Entity</th><th>Key</th><th>Mapped DbSet</th></tr></thead><tbody>");
            foreach (var entity in context.Entities)
                html.AppendLine($"<tr><td>{Encode(DataModelView.EntityLabel(context, entity))}</td>" +
                                $"<td>{Encode(DataModelView.KeyLabel(entity))}</td>" +
                                $"<td>{Encode(DataModelView.DbSetLabel(context, entity))}</td></tr>");
            html.AppendLine("</tbody></table>");

            if (context.Relationships.Count > 0)
            {
                html.AppendLine("<table><thead><tr><th>Principal</th><th>Dependent</th><th>Navigation</th>" +
                                "<th>FK</th><th>Kind</th></tr></thead><tbody>");
                foreach (var relationship in context.Relationships)
                    html.AppendLine($"<tr><td>{Encode(relationship.PrincipalEntity)}</td>" +
                                    $"<td>{Encode(relationship.DependentEntity)}</td>" +
                                    $"<td>{Encode(DataModelView.NavigationLabel(relationship))}</td>" +
                                    $"<td>{Encode(relationship.ForeignKey ?? "—")}</td>" +
                                    $"<td>{Encode(DataModelView.KindLabel(relationship.Kind))}</td></tr>");
                html.AppendLine("</tbody></table>");
            }
            html.AppendLine("</details>");
        }
    }
}
