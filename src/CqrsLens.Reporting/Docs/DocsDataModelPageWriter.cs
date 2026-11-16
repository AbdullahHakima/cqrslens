using System.Text;

namespace CqrsLens;

/// <summary>Renders <c>04-data-model.md</c> (DbContexts, entities, and repository/data-access relations).</summary>
internal static class DocsDataModelPageWriter
{
    internal static string Write(AnalysisResult result)
    {
        var md = new StringBuilder();
        DocsFormatting.WriteHeader(md, "04 — Data Model", result);

        var spine = result.Spine;
        var view = DataModelView.Build(result);
        var repositories = spine.GetComponentsByRole(ComponentRole.Repository);
        var usesData = spine.GetRelations(RelationKind.UsesData);
        var usesDataResolved = spine.GetRelations(RelationKind.UsesDataResolved);
        var touchesData = spine.GetRelations(RelationKind.TouchesData);
        var touchesDataOccurrences = touchesData.Sum(r => r.OccurrenceCount);
        var relates = spine.GetRelations(RelationKind.Relates);

        DocsFormatting.Line(md, "## Data Model Summary");
        DocsFormatting.Line(md);
        DocsFormatting.Line(md, "| Metric | Count |");
        DocsFormatting.Line(md, "| --- | ---: |");
        DocsFormatting.Line(md, $"| DbContexts | {view.ContextCount} |");
        DocsFormatting.Line(md, $"| Entities | {view.EntityCount} |");
        DocsFormatting.Line(md, $"| DbSets | {view.DbSetCount} |");
        DocsFormatting.Line(md, $"| Entity relationships (`relates`) | {relates.Count} |");
        DocsFormatting.Line(md, $"| `usesData` relations | {usesData.Count} |");
        DocsFormatting.Line(md, $"| `usesDataResolved` relations | {usesDataResolved.Count} |");
        DocsFormatting.Line(md, $"| Repository components | {repositories.Count} |");
        DocsFormatting.Line(md, $"| `touchesData` relations (occurrences) | {touchesData.Count} ({touchesDataOccurrences}) |");
        DocsFormatting.Line(md);

        WriteEfDataModel(md, result, view);
        WriteRepositoryAndDataAccess(md, result, repositories, usesData, usesDataResolved, touchesData);

        return md.ToString();
    }

    private static void WriteEfDataModel(StringBuilder md, AnalysisResult result, DataModelView.Summary view)
    {
        DocsFormatting.Line(md, "## EF Core Data Model");
        DocsFormatting.Line(md);
        if (view.ContextCount == 0)
        {
            DocsFormatting.Line(md, "_No EF Core DbContext classes found in the model._");
            DocsFormatting.Line(md);
            return;
        }

        MarkdownDataModelWriter.Write(md, view);

        var orderedContexts = result.DbContexts
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Namespace, StringComparer.Ordinal)
            .ToList();
        var erNames = DocsFormatting.DiagramFamilyFileNames(
            "er-diagram",
            orderedContexts
                .Select(DiagramBuilder.ErDiagram)
                .Where(d => d is not null)!);
        if (erNames.Count > 0)
        {
            var links = erNames.Select(n => $"[diagrams/{n}](diagrams/{n})");
            DocsFormatting.Line(md, $"ER Diagrams: {string.Join(" · ", links)}");
            DocsFormatting.Line(md);
        }
    }

    private static void WriteRepositoryAndDataAccess(
        StringBuilder md,
        AnalysisResult result,
        IReadOnlyList<ComponentNode> repositories,
        IReadOnlyList<SpineRelation> usesData,
        IReadOnlyList<SpineRelation> usesDataResolved,
        IReadOnlyList<SpineRelation> touchesData)
    {
        var spine = result.Spine;
        var projectsById = DocsFormatting.BuildProjectsById(spine);

        DocsFormatting.Line(md, "## Repository & Data-Access Relations");
        DocsFormatting.Line(md);

        if (result.DbContexts.Count == 0 && (repositories.Count > 0 || touchesData.Count > 0))
        {
            var hasLinq2Db = spine.Projects.Any(p =>
                p.PackageReferences.Any(pkg => string.Equals(pkg, "linq2db", StringComparison.OrdinalIgnoreCase)));
            var variantLabel = hasLinq2Db
                ? "Linq2DB repository variant (`linq2db` package reference present; 0 EF Core DbContexts in the model)"
                : "generic repository / non-EF Core data-access variant (0 EF Core DbContexts in the model)";
            DocsFormatting.Line(md, $"_Data-access variant: {variantLabel}. Traced static access paths do not prove runtime execution._");
            DocsFormatting.Line(md);
        }

        DocsFormatting.Line(md, $"### Repository Components ({repositories.Count})");
        DocsFormatting.Line(md);
        if (repositories.Count == 0)
        {
            DocsFormatting.Line(md, "_No Repository-role components discovered in the model._");
        }
        else
        {
            DocsFormatting.Line(md, "| ComponentId | Name | Project | Location |");
            DocsFormatting.Line(md, "| --- | --- | --- | --- |");
            foreach (var repo in repositories)
            {
                var projName = projectsById.TryGetValue(repo.Project, out var proj) ? proj.Name : repo.Project.Value;
                DocsFormatting.Line(
                    md,
                    $"| {MarkdownEscaping.CodeSpan(repo.Id.Value)} | " +
                    $"{MarkdownEscaping.TableCell(repo.Id.Symbol.QualifiedName)} | " +
                    $"{MarkdownEscaping.TableCell(projName)} | " +
                    $"{DocsFormatting.FormatLocation(repo.Location)} |");
            }
        }
        DocsFormatting.Line(md);

        var dataRelations = usesData
            .Concat(usesDataResolved)
            .Concat(touchesData)
            .OrderBy(r => r.Key)
            .ToList();

        DocsFormatting.Line(md, $"### Data-Access Edges ({dataRelations.Count})");
        DocsFormatting.Line(md);
        if (dataRelations.Count == 0)
        {
            DocsFormatting.Line(md, "_No `usesData`, `usesDataResolved`, or `touchesData` relations recorded in the model._");
        }
        else
        {
            DocsFormatting.Line(md, "_`touchesData` and `usesData`/`usesDataResolved` record statically traced paths; execution at runtime is not proven._");
            DocsFormatting.Line(md);
            DocsFormatting.Line(md, "| Kind | Source Component | Target | Via Path | Rules | Strength | Occurrences | Primary Location |");
            DocsFormatting.Line(md, "| --- | --- | --- | --- | --- | --- | ---: | --- |");
            foreach (var rel in dataRelations)
            {
                var rules = string.Join(", ", rel.Occurrences
                    .Select(o => JsonSpineNames.EvidenceRuleName(o.Rule))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(r => r, StringComparer.Ordinal));
                var primaryLoc = rel.Occurrences.FirstOrDefault(o => o.Location is not null)?.Location;
                DocsFormatting.Line(
                    md,
                    $"| `{JsonSpineNames.RelationKindName(rel.Kind)}` | " +
                    $"{MarkdownEscaping.CodeSpan(rel.From.ComponentId?.Value ?? rel.From.Name)} | " +
                    $"{MarkdownEscaping.TableCell(rel.To.Name)} | " +
                    $"{MarkdownEscaping.TableCell(DocsFormatting.FormatViaPath(rel.Via))} | " +
                    $"{MarkdownEscaping.TableCell(rules)} | " +
                    $"{JsonSpineNames.EvidenceStrengthName(rel.Strength)} | " +
                    $"{rel.OccurrenceCount} | " +
                    $"{DocsFormatting.FormatLocation(primaryLoc)} |");
            }
        }
        DocsFormatting.Line(md);
    }
}
