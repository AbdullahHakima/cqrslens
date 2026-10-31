using System.Text;
using static CqrsLens.AgentReportFormatter;

namespace CqrsLens;

/// <summary>Writes the EF Core data-model summary, optionally filtered to a slice or entity.</summary>
internal static class AgentDataModelWriter
{
    internal static void AppendDataModelSummary(
        StringBuilder sb,
        AnalysisResult result,
        string? filter,
        List<CqrsRequest> matchedRequests,
        bool exactFilter = false)
    {
        var touchedSet = new HashSet<string>(
            matchedRequests.SelectMany(r => r.TouchedEntities),
            StringComparer.OrdinalIgnoreCase);

        bool Matches(string value, string? property = null)
        {
            if (filter is null) return true;
            if (exactFilter)
            {
                return value.Equals(filter, StringComparison.OrdinalIgnoreCase) ||
                       (property is not null && property.Equals(filter, StringComparison.OrdinalIgnoreCase));
            }
            return value.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                   (property is not null && property.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        var anyOutput = false;
        var sectionSb = new StringBuilder();

        foreach (var ctx in result.DbContexts.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            var relevantSets = filter is null
                ? ctx.DbSets
                : ctx.DbSets.Where(s =>
                {
                    var entitySimple = CSharpScanner.SimpleName(s.Entity);
                    var label = $"{s.Property} ({entitySimple})";

                    // Entity impact shows only the requested entity's schema. A slice may touch
                    // several other DbSets, but those are not part of the entity being traced.
                    if (exactFilter)
                        return Matches(entitySimple, s.Property);

                    return touchedSet.Contains(label) || Matches(entitySimple, s.Property);
                }).ToList();

            if (filter is not null && relevantSets.Count == 0)
                continue;

            anyOutput = true;
            sectionSb.AppendLine($"### DbContext: `{ctx.Name}`{FormatLoc(ctx.Location)}");
            if (relevantSets.Count > 0)
            {
                var setList = JoinLimited(relevantSets, 20, s => $"`{s.Property}` → `{s.Entity}`");
                sectionSb.AppendLine($"- **DbSets**: {setList}");
            }

            var relevantEntityNames = new HashSet<string>(
                relevantSets.Select(s => CSharpScanner.SimpleName(s.Entity)),
                StringComparer.OrdinalIgnoreCase);

            AppendEntities(sectionSb, ctx, filter, relevantEntityNames, Matches);
            AppendRelationships(sectionSb, ctx, filter);

            sectionSb.AppendLine();
        }

        if (anyOutput)
        {
            sb.AppendLine("## Data Model (EF Core)");
            sb.AppendLine();
            sb.Append(sectionSb);
        }
    }

    private static void AppendEntities(
        StringBuilder sb,
        DbContextInfo ctx,
        string? filter,
        HashSet<string> relevantEntityNames,
        Func<string, string?, bool> matches)
    {
        foreach (var entity in ctx.Entities.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (filter is not null &&
                !relevantEntityNames.Contains(entity.Name) &&
                !matches(entity.Name, null))
            {
                continue;
            }

            var keys = entity.Properties.Where(p => p.IsKey).Select(p => $"`{p.Name}: {p.Type}` (PK)");
            var fks = entity.Properties.Where(p => p.IsForeignKey && !p.IsKey).Select(p => $"`{p.Name}: {p.Type}` (FK)");
            var scalars = entity.Properties.Where(p => !p.IsKey && !p.IsForeignKey && !p.IsNavigation).Select(p => $"`{p.Name}: {p.Type}`");
            var allProps = keys.Concat(fks).Concat(scalars).ToList();
            var propsText = JoinLimited(allProps, 40, p => p);
            if (allProps.Count == 0) propsText = "_no scalar properties_";
            sb.AppendLine($"- **Entity `{entity.Name}`**{FormatLoc(entity.Location)}: {propsText}");
        }
    }

    private static void AppendRelationships(StringBuilder sb, DbContextInfo ctx, string? filter)
    {
        if (filter is not null || ctx.Relationships.Count == 0)
            return;

        var rels = ctx.Relationships.Select(r =>
        {
            var arrow = r.Kind switch
            {
                EntityRelationshipKind.OneToOne => "1:1",
                EntityRelationshipKind.ManyToMany => "*:*",
                _ => "1:*",
            };
            return $"`{CSharpScanner.SimpleName(r.PrincipalEntity)}` {arrow} `{CSharpScanner.SimpleName(r.DependentEntity)}`";
        });
        sb.AppendLine($"- **Relationships**: {string.Join(", ", rels)}");
    }
}
