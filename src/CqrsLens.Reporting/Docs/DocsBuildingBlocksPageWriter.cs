using System.Text;

namespace CqrsLens;

/// <summary>Renders <c>02-building-blocks.md</c> (components by role/layer, relations, unproven populations, and declared intent).</summary>
internal static class DocsBuildingBlocksPageWriter
{
    private static readonly ComponentRole[] OrderedRoles =
    {
        ComponentRole.Request,
        ComponentRole.Handler,
        ComponentRole.Notification,
        ComponentRole.NotificationHandler,
        ComponentRole.Validator,
        ComponentRole.Behavior,
        ComponentRole.DbContext,
        ComponentRole.Entity,
        ComponentRole.Controller,
        ComponentRole.Service,
        ComponentRole.Repository,
    };

    internal static string Write(AnalysisResult result, CqrsMapSplitMode cqrsMapSplit)
    {
        var md = new StringBuilder();
        DocsFormatting.WriteHeader(md, "02 — Building Blocks", result);

        var spine = result.Spine;
        var components = spine.Components;
        var endpoints = spine.Endpoints;
        var relations = spine.Relations;
        var projectsById = DocsFormatting.BuildProjectsById(spine);

        var totalOccurrences = 0;
        var outgoingCounts = new Dictionary<ComponentId, int>();
        var incomingCounts = new Dictionary<ComponentId, int>();
        foreach (var rel in relations)
        {
            totalOccurrences += rel.OccurrenceCount;
            if (rel.From.ComponentId is not null)
            {
                outgoingCounts.TryGetValue(rel.From.ComponentId, out var c);
                outgoingCounts[rel.From.ComponentId] = c + 1;
            }

            ComponentId? targetComp = rel.To.ComponentId;
            if (targetComp is null && rel.To.SymbolId is not null)
            {
                var candidate = ComponentId.FromSymbol(rel.To.SymbolId);
                if (spine.TryGetComponent(candidate, out _))
                    targetComp = candidate;
            }
            if (targetComp is not null)
            {
                incomingCounts.TryGetValue(targetComp, out var c);
                incomingCounts[targetComp] = c + 1;
            }
        }

        DocsFormatting.Line(md, "## Component & Relation Summary");
        DocsFormatting.Line(md);
        DocsFormatting.Line(md, "| Metric | Count |");
        DocsFormatting.Line(md, "| --- | ---: |");
        DocsFormatting.Line(md, $"| Unique components | {components.Count} |");
        DocsFormatting.Line(md, $"| Endpoints (`spineEndpoints`) | {endpoints.Count} |");
        DocsFormatting.Line(md, $"| Relations | {relations.Count} |");
        DocsFormatting.Line(md, $"| Relation evidence occurrences | {totalOccurrences} |");
        DocsFormatting.Line(md, $"| Unproven items | {spine.Unproven.TotalCount} |");
        DocsFormatting.Line(md);

        WriteDiagramLinks(md, result, cqrsMapSplit);
        WriteRolesTable(md, components, endpoints);
        WriteRelationsSummary(md, relations, totalOccurrences);
        var spineView = SpineReportView.Build(result);
        MarkdownSpineWriter.WriteEndpointInventoryTable(md, spineView.Endpoints, lfOnly: true);
        MarkdownSpineWriter.WriteComponentIndexTable(md, spineView.Components, lfOnly: true);
        WriteLayersTable(md, spine, components, endpoints);
        WriteComponentInventory(md, components, projectsById, outgoingCounts, incomingCounts);
        WriteUnprovenSummary(md, spine.Unproven);

        if (result.Intent is not null)
            WriteDeclaredIntent(md, result.Intent, spine, projectsById);

        return md.ToString();
    }

    private static void WriteDiagramLinks(StringBuilder md, AnalysisResult result, CqrsMapSplitMode cqrsMapSplit)
    {
        var links = new List<string>();
        var layerGraph = DiagramBuilder.LayerGraph(result);
        if (layerGraph is not null && DiagramBuilder.IsWithinMermaidLimits(layerGraph))
            links.Add("[diagrams/layers.svg](diagrams/layers.svg)");

        var cqrsLinks = DocsFormatting.DiagramFamilyFileNames(
            "cqrs-map",
            DiagramBuilder.CqrsMapSections(result, cqrsMapSplit).Select(s => s.Source));
        foreach (var name in cqrsLinks)
            links.Add($"[diagrams/{name}](diagrams/{name})");

        if (links.Count > 0)
        {
            DocsFormatting.Line(md, $"Diagrams: {string.Join(" · ", links)}");
            DocsFormatting.Line(md);
        }
    }

    private static void WriteRolesTable(
        StringBuilder md,
        IReadOnlyList<ComponentNode> components,
        IReadOnlyList<EndpointNode> endpoints)
    {
        DocsFormatting.Line(md, "### Components by Role");
        DocsFormatting.Line(md);
        DocsFormatting.Line(md, $"_One declared symbol yields at most one component node with an evidenced role set (unique components: {components.Count})._");
        DocsFormatting.Line(md);
        DocsFormatting.Line(md, "| Role | Components | Role Evidence Occurrences |");
        DocsFormatting.Line(md, "| --- | ---: | ---: |");

        foreach (var role in OrderedRoles)
        {
            var compCount = 0;
            var evCount = 0;
            foreach (var comp in components)
            {
                if (!comp.HasRole(role))
                    continue;
                compCount++;
                evCount += comp.GetRoleEvidence(role).Count;
            }

            DocsFormatting.Line(
                md,
                $"| `{JsonSpineNames.ComponentRoleName(role)}` | {compCount} | {evCount} |");
        }

        var endpointEv = endpoints.Sum(e => e.Evidence.Count);
        DocsFormatting.Line(md, $"| `endpoint` (mapping-derived) | {endpoints.Count} | {endpointEv} |");
        DocsFormatting.Line(md);
    }

    private static void WriteRelationsSummary(
        StringBuilder md,
        IReadOnlyList<SpineRelation> relations,
        int totalOccurrences)
    {
        DocsFormatting.Line(md, "### Relations Summary");
        DocsFormatting.Line(md);
        if (relations.Count == 0)
        {
            DocsFormatting.Line(md, "_No relations recorded in the model._");
            DocsFormatting.Line(md);
            return;
        }

        DocsFormatting.Line(md, "| Relation Kind | Relations | Occurrences |");
        DocsFormatting.Line(md, "| --- | ---: | ---: |");
        foreach (var group in relations
                     .GroupBy(r => r.Kind)
                     .OrderBy(g => (int)g.Key))
        {
            DocsFormatting.Line(
                md,
                $"| `{JsonSpineNames.RelationKindName(group.Key)}` | {group.Count()} | {group.Sum(r => r.OccurrenceCount)} |");
        }
        DocsFormatting.Line(md, $"| **Total** | **{relations.Count}** | **{totalOccurrences}** |");
        DocsFormatting.Line(md);

        var byKindAndRule = new Dictionary<(RelationKind Kind, EvidenceRule Rule), (int Relations, int Occurrences)>();
        foreach (var rel in relations)
        {
            var seenRules = new HashSet<EvidenceRule>();
            foreach (var occ in rel.Occurrences)
            {
                var key = (rel.Kind, occ.Rule);
                byKindAndRule.TryGetValue(key, out var counts);
                var relInc = seenRules.Add(occ.Rule) ? 1 : 0;
                byKindAndRule[key] = (counts.Relations + relInc, counts.Occurrences + 1);
            }
        }

        DocsFormatting.Line(md, "### Relations by Kind & Evidence Rule");
        DocsFormatting.Line(md);
        DocsFormatting.Line(md, "| Relation Kind | Evidence Rule | Relations | Occurrences |");
        DocsFormatting.Line(md, "| --- | --- | ---: | ---: |");
        foreach (var kv in byKindAndRule
                     .OrderBy(p => (int)p.Key.Kind)
                     .ThenBy(p => (int)p.Key.Rule))
        {
            DocsFormatting.Line(
                md,
                $"| `{JsonSpineNames.RelationKindName(kv.Key.Kind)}` | " +
                $"`{JsonSpineNames.EvidenceRuleName(kv.Key.Rule)}` | " +
                $"{kv.Value.Relations} | {kv.Value.Occurrences} |");
        }
        DocsFormatting.Line(md);
    }

    private static void WriteLayersTable(
        StringBuilder md,
        ArchitectureSpine spine,
        IReadOnlyList<ComponentNode> components,
        IReadOnlyList<EndpointNode> endpoints)
    {
        DocsFormatting.Line(md, "## Building Blocks by Layer");
        DocsFormatting.Line(md);
        if (spine.Projects.Count == 0)
        {
            DocsFormatting.Line(md, "_No projects discovered in the model._");
            DocsFormatting.Line(md);
            return;
        }

        var compByProj = components
            .GroupBy(c => c.Project)
            .ToDictionary(g => g.Key, g => g.Count());
        var epByProj = endpoints
            .GroupBy(e => e.Project)
            .ToDictionary(g => g.Key, g => g.Count());

        DocsFormatting.Line(md, "| Layer | Layer Provenance | ProjectId | Project | Components | Endpoints |");
        DocsFormatting.Line(md, "| --- | --- | --- | --- | ---: | ---: |");
        foreach (var project in spine.Projects
                     .OrderBy(p => p.Layer, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(p => p.Layer, StringComparer.Ordinal)
                     .ThenBy(p => p.Id))
        {
            compByProj.TryGetValue(project.Id, out var cCount);
            epByProj.TryGetValue(project.Id, out var eCount);
            DocsFormatting.Line(
                md,
                $"| {MarkdownEscaping.TableCell(project.Layer)} | " +
                $"{MarkdownEscaping.TableCell(DocsFormatting.FormatLayerProvenance(project))} | " +
                $"{MarkdownEscaping.CodeSpan(project.Id.Value)} | " +
                $"{MarkdownEscaping.TableCell(project.Name)} | " +
                $"{cCount} | {eCount} |");
        }
        DocsFormatting.Line(md);
    }

    private static void WriteComponentInventory(
        StringBuilder md,
        IReadOnlyList<ComponentNode> components,
        IReadOnlyDictionary<ProjectId, ProjectNode> projectsById,
        IReadOnlyDictionary<ComponentId, int> outgoingCounts,
        IReadOnlyDictionary<ComponentId, int> incomingCounts)
    {
        DocsFormatting.Line(md, "## Component Inventory");
        DocsFormatting.Line(md);
        if (components.Count == 0)
        {
            DocsFormatting.Line(md, "_No components discovered in the model._");
            DocsFormatting.Line(md);
            return;
        }

        DocsFormatting.Line(md, "| ComponentId | Name | Project | Layer | Roles | Role Evidence Rules | Role Evidence Occurrences | Outgoing Relations | Incoming Relations | Location |");
        DocsFormatting.Line(md, "| --- | --- | --- | --- | --- | --- | ---: | ---: | ---: | --- |");
        foreach (var comp in components)
        {
            var projectName = projectsById.TryGetValue(comp.Project, out var proj) ? proj.Name : comp.Project.Value;
            var layer = proj?.Layer ?? "—";
            var roles = string.Join(", ", comp.Roles.Select(r => JsonSpineNames.ComponentRoleName(r.Role)));
            var rules = string.Join(", ", comp.Roles
                .SelectMany(r => r.Evidence)
                .Select(e => JsonSpineNames.EvidenceRuleName(e.Rule))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(r => r, StringComparer.Ordinal));
            var occCount = comp.Roles.Sum(r => r.Evidence.Count);
            outgoingCounts.TryGetValue(comp.Id, out var outgoing);
            incomingCounts.TryGetValue(comp.Id, out var incoming);

            DocsFormatting.Line(
                md,
                $"| {MarkdownEscaping.CodeSpan(comp.Id.Value)} | " +
                $"{MarkdownEscaping.TableCell(comp.Name)} | " +
                $"{MarkdownEscaping.TableCell(projectName)} | " +
                $"{MarkdownEscaping.TableCell(layer)} | " +
                $"{MarkdownEscaping.TableCell(roles)} | " +
                $"{MarkdownEscaping.TableCell(rules)} | " +
                $"{occCount} | {outgoing} | {incoming} | " +
                $"{DocsFormatting.FormatLocation(comp.Location)} |");
        }
        DocsFormatting.Line(md);
    }

    private static void WriteUnprovenSummary(StringBuilder md, UnprovenPopulations unproven)
    {
        DocsFormatting.Line(md, "## Unproven Populations");
        DocsFormatting.Line(md);
        if (unproven.TotalCount == 0)
        {
            DocsFormatting.Line(md, "_No unproven items recorded._");
            DocsFormatting.Line(md);
            return;
        }

        DocsFormatting.Line(md, "_Items recorded separately from proven relations so missing edges are never mistaken for proven absence._");
        DocsFormatting.Line(md);
        DocsFormatting.Line(md, "| Population | Count |");
        DocsFormatting.Line(md, "| --- | ---: |");
        if (unproven.AmbiguousResolutions.Count > 0)
            DocsFormatting.Line(md, $"| `ambiguousResolutions` | {unproven.AmbiguousResolutions.Count} |");
        if (unproven.UnresolvedDispatches.Count > 0)
            DocsFormatting.Line(md, $"| `unresolvedDispatches` | {unproven.UnresolvedDispatches.Count} |");
        if (unproven.AmbiguousLinks.Count > 0)
            DocsFormatting.Line(md, $"| `ambiguousLinks` | {unproven.AmbiguousLinks.Count} |");
        if (unproven.UnresolvedContracts.Count > 0)
            DocsFormatting.Line(md, $"| `unresolvedContracts` | {unproven.UnresolvedContracts.Count} |");
        if (unproven.UnestablishedBehaviors.Count > 0)
            DocsFormatting.Line(md, $"| `unestablishedBehaviors` | {unproven.UnestablishedBehaviors.Count} |");

        foreach (var group in unproven.Observations
                     .GroupBy(o => o.Kind)
                     .OrderBy(g => (int)g.Key))
        {
            DocsFormatting.Line(
                md,
                $"| `observations.{JsonSpineNames.UnprovenItemKindName(group.Key)}` | {group.Count()} |");
        }

        DocsFormatting.Line(md, $"| **Total** | **{unproven.TotalCount}** |");
        DocsFormatting.Line(md);
    }

    private static void WriteDeclaredIntent(
        StringBuilder md,
        IntentSpecification intent,
        ArchitectureSpine spine,
        IReadOnlyDictionary<ProjectId, ProjectNode> projectsById)
    {
        DocsFormatting.Line(md, "## Declared Intent");
        DocsFormatting.Line(md);
        DocsFormatting.Line(md, "_Human-declared modules, boundaries, and ownership from `.noctua.json` (or legacy `.cqrslens.json`). Ownership is never checked against static source evidence and is always labelled **declared, unverified**. Modules not referenced by any boundary rule have no checkable boundary consequence and are labelled **declared, unverified**._");
        DocsFormatting.Line(md);

        var checkedModules = new HashSet<string>(StringComparer.Ordinal);
        foreach (var boundary in intent.Boundaries)
        {
            checkedModules.Add(boundary.FromModule);
            checkedModules.Add(boundary.ToModule);
        }

        DocsFormatting.Line(md, "### Declared Modules & Ownership");
        DocsFormatting.Line(md);
        if (intent.Modules.Count == 0)
        {
            DocsFormatting.Line(md, "_No modules declared._");
        }
        else
        {
            DocsFormatting.Line(md, "| Module | Declared Projects | Declared Component Selectors | Matched Projects | Matched Components | Owner | Verification Status |");
            DocsFormatting.Line(md, "| --- | --- | --- | ---: | ---: | --- | --- |");
            foreach (var module in intent.Modules.OrderBy(m => m.Name, StringComparer.Ordinal))
            {
                var matchedProjects = spine.Projects.Count(p =>
                    module.Projects.Any(sel => IntentModuleMatcher.MatchesProjectSelector(sel, p)));
                var matchedComponents = spine.Components.Count(c =>
                    IntentModuleMatcher.ComponentBelongsToModule(c, module, projectsById));

                var declProjects = module.Projects.Count > 0 ? string.Join(", ", module.Projects) : "—";
                var declComponents = module.Components.Count > 0 ? string.Join(", ", module.Components) : "—";
                var ownerText = intent.Ownership.TryGetValue(module.Name, out var owner)
                    ? $"{owner} (declared, unverified)"
                    : "— (declared, unverified)";
                var status = checkedModules.Contains(module.Name)
                    ? "checked (boundary)"
                    : "declared, unverified";

                DocsFormatting.Line(
                    md,
                    $"| {MarkdownEscaping.TableCell(module.Name)} | " +
                    $"{MarkdownEscaping.TableCell(declProjects)} | " +
                    $"{MarkdownEscaping.TableCell(declComponents)} | " +
                    $"{matchedProjects} | {matchedComponents} | " +
                    $"{MarkdownEscaping.TableCell(ownerText)} | " +
                    $"{MarkdownEscaping.TableCell(status)} |");
            }
        }
        DocsFormatting.Line(md);

        DocsFormatting.Line(md, "### Declared Boundaries");
        DocsFormatting.Line(md);
        if (intent.Boundaries.Count == 0)
        {
            DocsFormatting.Line(md, "_No forbidden module boundaries declared._");
        }
        else
        {
            DocsFormatting.Line(md, "| From Module | To Module | Severity |");
            DocsFormatting.Line(md, "| --- | --- | --- |");
            foreach (var boundary in intent.Boundaries
                         .OrderBy(b => b.FromModule, StringComparer.Ordinal)
                         .ThenBy(b => b.ToModule, StringComparer.Ordinal)
                         .ThenByDescending(b => b.Severity))
            {
                var sev = boundary.Severity == CheckSeverity.Error ? "error" : "warning";
                DocsFormatting.Line(
                    md,
                    $"| {MarkdownEscaping.TableCell(boundary.FromModule)} | " +
                    $"{MarkdownEscaping.TableCell(boundary.ToModule)} | " +
                    $"{sev} |");
            }
        }
        DocsFormatting.Line(md);
    }
}
