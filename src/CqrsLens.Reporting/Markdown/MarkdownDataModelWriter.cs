using System.Text;

namespace CqrsLens;

/// <summary>
/// Writes the combined, solution-level data view: every DbContext, its entities,
/// mapped DbSets and relationships in one place, plus the contexts with no entities.
/// </summary>
internal static class MarkdownDataModelWriter
{
    internal static void Write(StringBuilder md, DataModelView.Summary view)
    {
        md.AppendLine("### Data model — all DbContexts");
        md.AppendLine();
        md.AppendLine("_Every DbContext in the solution with its entities and relationships in one place; " +
                      "each context's entity set matches its ER diagram below. Contexts with no entities " +
                      "discovered are listed explicitly and never dropped._");
        md.AppendLine();
        md.AppendLine($"**{view.ContextCount} DbContext(s):** {view.ContextsWithEntities} with entities, " +
                      $"{view.ContextsWithoutEntities} with none · {view.EntityCount} entities · " +
                      $"{view.RelationshipCount} relationships.");
        md.AppendLine();

        md.AppendLine("| DbContext | Project | Entities | DbSets | Relationships |");
        md.AppendLine("| --- | --- | ---: | ---: | ---: |");
        foreach (var context in view.Contexts)
            md.AppendLine($"| {MarkdownEscaping.TableCell(context.DisplayName)} | " +
                          $"{MarkdownEscaping.TableCell(context.Project ?? "—")} | " +
                          $"{context.EntityCount} | {context.DbSets.Count} | {context.RelationshipCount} |");
        md.AppendLine();

        var withoutEntities = view.Contexts.Where(c => c.EntityCount == 0).ToList();
        if (withoutEntities.Count > 0)
        {
            md.AppendLine($"#### DbContexts with no entities discovered ({withoutEntities.Count})");
            md.AppendLine();
            md.AppendLine("| DbContext | Project |");
            md.AppendLine("| --- | --- |");
            foreach (var context in withoutEntities)
                md.AppendLine($"| {MarkdownEscaping.TableCell(context.DisplayName)} | " +
                              $"{MarkdownEscaping.TableCell(context.Project ?? "—")} |");
            md.AppendLine();
        }

        foreach (var context in view.Contexts.Where(c => c.EntityCount > 0))
        {
            md.AppendLine($"#### {MarkdownEscaping.MarkdownText(context.DisplayName)} — " +
                          $"{context.EntityCount} entities, {context.RelationshipCount} relationships");
            md.AppendLine();
            md.AppendLine("| Entity | Key | Mapped DbSet |");
            md.AppendLine("| --- | --- | --- |");
            foreach (var entity in context.Entities)
                md.AppendLine($"| {MarkdownEscaping.TableCell(DataModelView.EntityLabel(context, entity))} | " +
                              $"{MarkdownEscaping.TableCell(DataModelView.KeyLabel(entity))} | " +
                              $"{MarkdownEscaping.TableCell(DataModelView.DbSetLabel(context, entity))} |");

            if (context.Relationships.Count > 0)
            {
                md.AppendLine();
                md.AppendLine("| Principal | Dependent | Navigation | FK | Kind |");
                md.AppendLine("| --- | --- | --- | --- | --- |");
                foreach (var relationship in context.Relationships)
                    md.AppendLine($"| {MarkdownEscaping.TableCell(relationship.PrincipalEntity)} | " +
                                  $"{MarkdownEscaping.TableCell(relationship.DependentEntity)} | " +
                                  $"{MarkdownEscaping.TableCell(DataModelView.NavigationLabel(relationship))} | " +
                                  $"{MarkdownEscaping.TableCell(relationship.ForeignKey ?? "—")} | " +
                                  $"{MarkdownEscaping.TableCell(DataModelView.KindLabel(relationship.Kind))} |");
            }
            md.AppendLine();
        }
    }
}
