using System.Text;

namespace CqrsLens;

/// <summary>Renders <c>01-context.md</c> (system context: projects, external references, entry points).</summary>
internal static class DocsContextPageWriter
{
    internal static string Write(AnalysisResult result)
    {
        var md = new StringBuilder();
        DocsFormatting.WriteHeader(md, "01 — System Context", result);

        var spine = result.Spine;
        var projects = spine.Projects;
        var projectRefRelations = spine.GetRelations(RelationKind.References);
        var refsByProject = BuildProjectReferenceMap(projectRefRelations);
        var distinctPackages = projects
            .SelectMany(p => p.PackageReferences)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p, StringComparer.Ordinal)
            .ToList();
        var externalNodes = CollectExternalNodes(spine.Relations);
        var endpoints = spine.Endpoints;
        var provenVerbs = endpoints.Count(e => e.VerbStrength == EvidenceStrength.Proven);
        var inferredVerbs = endpoints.Count - provenVerbs;
        var provenRoutes = endpoints.Count(e => e.RouteStrength == EvidenceStrength.Proven);
        var inferredRoutes = endpoints.Count - provenRoutes;

        DocsFormatting.Line(md, "## System Context Summary");
        DocsFormatting.Line(md);
        DocsFormatting.Line(md, "| Metric | Value |");
        DocsFormatting.Line(md, "| --- | ---: |");
        DocsFormatting.Line(md, $"| Projects | {projects.Count} |");
        DocsFormatting.Line(md, $"| Project references (`references`) | {projectRefRelations.Count} |");
        DocsFormatting.Line(md, $"| Distinct package references | {distinctPackages.Count} |");
        DocsFormatting.Line(md, $"| External references in relations | {externalNodes.Count} |");
        DocsFormatting.Line(md, $"| Endpoints (`spineEndpoints`) | {endpoints.Count} |");
        DocsFormatting.Line(md, $"| Endpoint verbs (proven / inferred) | {provenVerbs} / {inferredVerbs} |");
        DocsFormatting.Line(md, $"| Endpoint routes (proven / inferred) | {provenRoutes} / {inferredRoutes} |");
        DocsFormatting.Line(md, $"| Legacy CQRS endpoints | {result.Endpoints.Count} |");
        DocsFormatting.Line(md);

        var projectGraph = DiagramBuilder.ProjectGraph(result);
        if (projectGraph is not null && DiagramBuilder.IsWithinMermaidLimits(projectGraph))
        {
            DocsFormatting.Line(md, "Diagrams: [diagrams/project-graph.svg](diagrams/project-graph.svg)");
            DocsFormatting.Line(md);
        }

        DocsFormatting.Line(md, "## Projects & References");
        DocsFormatting.Line(md);
        if (projects.Count == 0)
        {
            DocsFormatting.Line(md, "_No projects discovered in the model._");
        }
        else
        {
            DocsFormatting.Line(md, "_Layer provenance states whether a project's layer is `configured` or `guessed (inferred)` from naming/folders. PackageReferences come from project files and never by themselves imply an `external` type node._");
            DocsFormatting.Line(md);
            DocsFormatting.Line(md, "| ProjectId | Project | Layer | Layer Provenance | Project References | Package References |");
            DocsFormatting.Line(md, "| --- | --- | --- | --- | --- | --- |");
            foreach (var project in projects)
            {
                var projRefs = refsByProject.TryGetValue(project.Id, out var rList) && rList.Count > 0
                    ? string.Join(", ", rList)
                    : "—";
                var packages = project.PackageReferences.Count > 0
                    ? string.Join(", ", project.PackageReferences
                        .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(p => p, StringComparer.Ordinal))
                    : "—";
                DocsFormatting.Line(
                    md,
                    $"| {MarkdownEscaping.CodeSpan(project.Id.Value)} | " +
                    $"{MarkdownEscaping.TableCell(project.Name)} | " +
                    $"{MarkdownEscaping.TableCell(project.Layer)} | " +
                    $"{MarkdownEscaping.TableCell(DocsFormatting.FormatLayerProvenance(project))} | " +
                    $"{MarkdownEscaping.TableCell(projRefs)} | " +
                    $"{MarkdownEscaping.TableCell(packages)} |");
            }
        }
        DocsFormatting.Line(md);

        DocsFormatting.Line(md, "## External References in Relations");
        DocsFormatting.Line(md);
        if (externalNodes.Count == 0)
        {
            DocsFormatting.Line(md, "_No external type or member references recorded in model relations._");
        }
        else
        {
            DocsFormatting.Line(md, "| Reference Kind | Reference |");
            DocsFormatting.Line(md, "| --- | --- |");
            foreach (var node in externalNodes)
            {
                DocsFormatting.Line(
                    md,
                    $"| {MarkdownEscaping.TableCell(JsonSpineNames.NodeReferenceKindName(node.Kind))} | " +
                    $"{MarkdownEscaping.CodeSpan(node.Name)} |");
            }
        }
        DocsFormatting.Line(md);

        DocsFormatting.Line(md, "## Entry Points (Endpoints)");
        DocsFormatting.Line(md);
        if (endpoints.Count == 0)
        {
            DocsFormatting.Line(md, "_No HTTP entry points discovered in the model._");
        }
        else
        {
            var projectsById = DocsFormatting.BuildProjectsById(spine);
            var controllerByEndpoint = BuildControllerByEndpointMap(spine);
            var targetRequestByEndpointId = BuildTargetRequestMap(result);

            DocsFormatting.Line(md, "_`verb` and `route` provenance distinguish statically declared attributes/mappings (`proven`) from convention-based guesses (`inferred`)._");
            DocsFormatting.Line(md);
            DocsFormatting.Line(md, "| EndpointId | Method | Verb Provenance | Route | Route Provenance | Name | Project | Controller | Target Request | Location |");
            DocsFormatting.Line(md, "| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
            foreach (var ep in endpoints)
            {
                var projectName = projectsById.TryGetValue(ep.Project, out var proj) ? proj.Name : ep.Project.Value;
                var controller = controllerByEndpoint.TryGetValue(ep.Id, out var ctrl) ? ctrl : "—";
                var targetRequest = targetRequestByEndpointId.TryGetValue(ep.Id, out var req) ? req : "—";
                DocsFormatting.Line(
                    md,
                    $"| {MarkdownEscaping.CodeSpan(ep.Id.ToString())} | " +
                    $"{MarkdownEscaping.TableCell(ep.HttpMethod)} | " +
                    $"{MarkdownEscaping.TableCell(JsonSpineNames.EvidenceStrengthName(ep.VerbStrength))} | " +
                    $"{MarkdownEscaping.TableCell(string.IsNullOrEmpty(ep.Route) ? "—" : ep.Route)} | " +
                    $"{MarkdownEscaping.TableCell(JsonSpineNames.EvidenceStrengthName(ep.RouteStrength))} | " +
                    $"{MarkdownEscaping.TableCell(ep.Name)} | " +
                    $"{MarkdownEscaping.TableCell(projectName)} | " +
                    $"{MarkdownEscaping.TableCell(controller)} | " +
                    $"{MarkdownEscaping.TableCell(targetRequest)} | " +
                    $"{DocsFormatting.FormatLocation(ep.Location)} |");
            }
        }
        DocsFormatting.Line(md);

        if (result.Intent?.PublicSurface is not null)
        {
            WriteDeclaredPublicSurface(md, result.Intent.PublicSurface);
        }

        return md.ToString();
    }

    private static void WriteDeclaredPublicSurface(StringBuilder md, IntentPublicSurface surface)
    {
        DocsFormatting.Line(md, "## Declared Public Surface");
        DocsFormatting.Line(md);
        var severity = surface.Severity == CheckSeverity.Error ? "error" : "warning";
        DocsFormatting.Line(md, $"_Checked against the model by `C-SURFACE` at `{severity}` severity._");
        DocsFormatting.Line(md);

        if (surface.Endpoints is not null)
        {
            DocsFormatting.Line(md, $"### Declared Public Endpoints ({surface.Endpoints.Count})");
            DocsFormatting.Line(md);
            if (surface.Endpoints.Count == 0)
            {
                DocsFormatting.Line(md, "_Declared empty endpoint public surface (`[]`)._");
            }
            else
            {
                foreach (var ep in surface.Endpoints.OrderBy(e => e, StringComparer.Ordinal))
                    DocsFormatting.Line(md, $"- {MarkdownEscaping.CodeSpan(ep)}");
            }
            DocsFormatting.Line(md);
        }

        if (surface.Components is not null)
        {
            DocsFormatting.Line(md, $"### Declared Public Components ({surface.Components.Count})");
            DocsFormatting.Line(md);
            if (surface.Components.Count == 0)
            {
                DocsFormatting.Line(md, "_Declared empty component public surface (`[]`)._");
            }
            else
            {
                foreach (var comp in surface.Components.OrderBy(c => c, StringComparer.Ordinal))
                    DocsFormatting.Line(md, $"- {MarkdownEscaping.CodeSpan(comp)}");
            }
            DocsFormatting.Line(md);
        }
    }

    private static Dictionary<ProjectId, List<string>> BuildProjectReferenceMap(IReadOnlyList<SpineRelation> relations)
    {
        var map = new Dictionary<ProjectId, List<string>>();
        foreach (var rel in relations)
        {
            if (rel.From.ProjectId is null)
                continue;

            if (!map.TryGetValue(rel.From.ProjectId, out var list))
                map[rel.From.ProjectId] = list = new List<string>();
            list.Add(rel.To.Name);
        }

        foreach (var list in map.Values)
            list.Sort(StringComparer.Ordinal);
        return map;
    }

    private static List<NodeReference> CollectExternalNodes(IReadOnlyList<SpineRelation> relations)
    {
        var external = new HashSet<NodeReference>();
        foreach (var rel in relations)
        {
            if (rel.From.IsExternal)
                external.Add(rel.From);
            if (rel.To.IsExternal)
                external.Add(rel.To);
            foreach (var via in rel.Via)
            {
                if (via.IsExternal)
                    external.Add(via);
            }
        }

        var sorted = external.ToList();
        sorted.Sort();
        return sorted;
    }

    private static Dictionary<EndpointId, string> BuildControllerByEndpointMap(ArchitectureSpine spine)
    {
        var map = new Dictionary<EndpointId, string>();
        foreach (var rel in spine.GetRelations(RelationKind.Exposes))
        {
            if (rel.To.EndpointId is null)
                continue;
            map.TryAdd(rel.To.EndpointId, rel.From.Name);
        }
        return map;
    }

    private static Dictionary<EndpointId, string> BuildTargetRequestMap(AnalysisResult result)
    {
        var map = new Dictionary<EndpointId, string>();
        foreach (var ep in result.Endpoints)
        {
            if (ep.SpineId is null)
                continue;
            var target = !string.IsNullOrEmpty(ep.TargetRequestFullName)
                ? ep.TargetRequestFullName!
                : ep.TargetRequestName;
            if (!string.IsNullOrEmpty(target))
                map.TryAdd(ep.SpineId, target);
        }
        return map;
    }
}
