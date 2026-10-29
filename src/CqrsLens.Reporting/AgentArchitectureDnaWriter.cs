using System.Text;
using static CqrsLens.AgentReportFormatter;
using static CqrsLens.AgentReportLookups;

namespace CqrsLens;

/// <summary>Writes Architectural DNA and EF data-model summaries.</summary>
internal static class AgentArchitectureDnaWriter
{
    internal static void AppendArchitecturalDna(
        StringBuilder sb,
        AnalysisResult result,
        IReadOnlyDictionary<string, HandlerInfo> handlersByFull,
        IReadOnlyDictionary<string, HandlerInfo> handlersBySimple)
    {
        sb.AppendLine("## Architectural DNA & Conventions");
        sb.AppendLine();

        // 1. Layer & Project Topology
        if (result.Projects.Count > 0)
        {
            sb.AppendLine("### Layer & Project Topology");
            foreach (var p in result.Projects.OrderBy(p => p.Layer, StringComparer.Ordinal).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                var refs = p.ReferenceNames.Count > 0
                    ? $" → references {string.Join(", ", p.ReferenceNames.OrderBy(r => r, StringComparer.OrdinalIgnoreCase).Select(r => $"`{r}`"))}"
                    : " (leaf project)";
                sb.AppendLine($"- **`{p.Name}`** [Layer: `{p.Layer}`]{refs}");
            }
            sb.AppendLine();
        }

        // 2. Synthesized CQRS Conventions
        sb.AppendLine("### Codebase Conventions");
        var mediatorLabel = result.Conventions.Count > 0
            ? string.Join(", ", result.Conventions)
            : "not detected (no mediator/dispatch convention found in source)";
        sb.AppendLine($"- **Mediator / Dispatch**: `{mediatorLabel}`");

        if (result.Requests.Count > 0)
        {
            var colocatedCount = 0;
            var linkedCount = 0;
            foreach (var r in result.Requests)
            {
                if (r.Location is null || r.Handlers.Count == 0) continue;
                var h = Resolve(r.Handlers[0], handlersByFull, handlersBySimple);
                if (h?.Location is null) continue;
                linkedCount++;
                if (string.Equals(r.Location.File, h.Location.File, StringComparison.OrdinalIgnoreCase))
                    colocatedCount++;
            }

            if (linkedCount > 0)
            {
                var layoutStyle = colocatedCount * 2 >= linkedCount
                    ? $"Co-located vertical slices ({colocatedCount}/{linkedCount} requests share a file with their handler)"
                    : $"Separate request and handler files ({linkedCount - colocatedCount}/{linkedCount} handlers in dedicated files)";
                sb.AppendLine($"- **Slice File Layout**: {layoutStyle}");
            }

            var reqProjects = result.Requests
                .Where(r => !string.IsNullOrEmpty(r.ProjectName))
                .Select(r => r.ProjectName!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (reqProjects.Count > 0)
                sb.AppendLine($"- **Request Contracts Live In**: {string.Join(", ", reqProjects.Select(p => $"`{p}`"))}");
        }

        if (result.Behaviors.Count > 0)
        {
            var behaviors = string.Join(", ", result.Behaviors
                .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
                .Select(b => $"`{b.Name}`{FormatLoc(b.Location)}"));
            sb.AppendLine($"- **Pipeline Behaviors**: {behaviors}");
        }

        if (result.DbContexts.Count > 0)
        {
            var contexts = string.Join(", ", result.DbContexts
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Select(c => $"`{c.Name}` ({c.DbSets.Count} DbSet(s){FormatLoc(c.Location)})"));
            sb.AppendLine($"- **Data Contexts**: {contexts}");
        }

        sb.AppendLine();
    }

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

            foreach (var entity in ctx.Entities.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (filter is not null &&
                    !relevantEntityNames.Contains(entity.Name) &&
                    !Matches(entity.Name))
                {
                    continue;
                }

                var keys = entity.Properties.Where(p => p.IsKey).Select(p => $"`{p.Name}: {p.Type}` (PK)");
                var fks = entity.Properties.Where(p => p.IsForeignKey && !p.IsKey).Select(p => $"`{p.Name}: {p.Type}` (FK)");
                var scalars = entity.Properties.Where(p => !p.IsKey && !p.IsForeignKey && !p.IsNavigation).Select(p => $"`{p.Name}: {p.Type}`");
                var allProps = keys.Concat(fks).Concat(scalars).ToList();
                var propsText = JoinLimited(allProps, 40, p => p);
                if (allProps.Count == 0) propsText = "_no scalar properties_";
                sectionSb.AppendLine($"- **Entity `{entity.Name}`**{FormatLoc(entity.Location)}: {propsText}");
            }

            if (filter is null && ctx.Relationships.Count > 0)
            {
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
                sectionSb.AppendLine($"- **Relationships**: {string.Join(", ", rels)}");
            }

            sectionSb.AppendLine();
        }

        if (anyOutput)
        {
            sb.AppendLine("## Data Model (EF Core)");
            sb.AppendLine();
            sb.Append(sectionSb);
        }
    }
}