using System.Text;

namespace CqrsLens;

/// <summary>Renders <c>03-runtime.md</c> (per-flow sequences via <see cref="RequestSequenceBuilder"/> and recorded runtime flows).</summary>
internal static class DocsRuntimePageWriter
{
    internal static string Write(AnalysisResult result)
    {
        var md = new StringBuilder();
        DocsFormatting.WriteHeader(md, "03 — Runtime Views", result);

        var spine = result.Spine;
        var sequences = RequestSequenceBuilder.Build(result);
        var sequenceFiles = DocsFormatting.SequenceDiagramFileNames(sequences);
        var flowSections = DiagramBuilder.FlowDiagramSections(result);
        var flowFiles = DocsFormatting.DiagramFamilyFileNames(
            "flow",
            flowSections.Select(s => s.Source));
        var notificationMap = DiagramBuilder.NotificationMap(result);

        var exposesRels = spine.GetRelations(RelationKind.Exposes);
        var callsRels = spine.GetRelations(RelationKind.Calls);
        var callsOccs = callsRels.Sum(r => r.OccurrenceCount);
        var resolvesToRels = spine.GetRelations(RelationKind.ResolvesTo);
        var dispatchesRels = spine.GetRelations(RelationKind.Dispatches);
        var publishesRels = spine.GetRelations(RelationKind.Publishes);
        var touchesDataRels = spine.GetRelations(RelationKind.TouchesData);
        var usesDataRels = spine.GetRelations(RelationKind.UsesData);
        var usesDataResolvedRels = spine.GetRelations(RelationKind.UsesDataResolved);

        DocsFormatting.Line(md, "## Runtime Summary");
        DocsFormatting.Line(md);
        DocsFormatting.Line(md, "| Metric | Count |");
        DocsFormatting.Line(md, "| --- | ---: |");
        DocsFormatting.Line(md, $"| Requests | {result.Requests.Count} |");
        DocsFormatting.Line(md, $"| Handlers | {result.Handlers.Count} |");
        DocsFormatting.Line(md, $"| Notifications | {result.Notifications.Count} |");
        DocsFormatting.Line(md, $"| Notification handlers | {result.NotificationHandlers.Count} |");
        DocsFormatting.Line(md, $"| Per-flow sequences | {sequences.Count} |");
        DocsFormatting.Line(md, $"| Flow diagram sections | {flowSections.Count} |");
        DocsFormatting.Line(md, $"| `exposes` relations | {exposesRels.Count} |");
        DocsFormatting.Line(md, $"| `calls` relations (occurrences) | {callsRels.Count} ({callsOccs}) |");
        DocsFormatting.Line(md, $"| `resolvesTo` relations | {resolvesToRels.Count} |");
        DocsFormatting.Line(md, $"| `dispatches` relations | {dispatchesRels.Count} |");
        DocsFormatting.Line(md, $"| `publishes` relations | {publishesRels.Count} |");
        DocsFormatting.Line(md, $"| `touchesData` relations | {touchesDataRels.Count} |");
        DocsFormatting.Line(md, $"| `usesData` / `usesDataResolved` relations | {usesDataRels.Count} / {usesDataResolvedRels.Count} |");
        DocsFormatting.Line(md, $"| Unresolved dispatches | {spine.Unproven.UnresolvedDispatches.Count} |");
        DocsFormatting.Line(md);

        WriteSequences(md, sequences, sequenceFiles);
        WriteFlowDiagrams(md, flowSections, flowFiles, notificationMap);
        DocsFormatting.Line(md, "## Recorded Runtime Flows");
        DocsFormatting.Line(md);
        WriteControllerExposures(md, spine, exposesRels);
        WriteComponentCallFlows(md, spine);

        return md.ToString();
    }

    private static void WriteSequences(
        StringBuilder md,
        IReadOnlyList<RequestSequence> sequences,
        IReadOnlyList<string> sequenceFiles)
    {
        DocsFormatting.Line(md, "## Per-Flow Sequences");
        DocsFormatting.Line(md);
        if (sequences.Count == 0)
        {
            DocsFormatting.Line(md, "_No request-handler sequences available from the model (0 requests with resolved handlers)._");
            DocsFormatting.Line(md);
            return;
        }

        DocsFormatting.Line(md, "_One sequence diagram per request with a resolved handler, built from existing model data via `RequestSequenceBuilder`. Behavior order is registered order — execution order is not guaranteed._");
        DocsFormatting.Line(md);

        for (var i = 0; i < sequences.Count; i++)
        {
            var seq = sequences[i];
            DocsFormatting.Line(md, $"### {MarkdownEscaping.MarkdownText(seq.RequestName)}");
            DocsFormatting.Line(md);

            var svgName = sequenceFiles[i];
            if (!string.IsNullOrEmpty(svgName))
            {
                DocsFormatting.Line(md, $"Diagram: [diagrams/{svgName}](diagrams/{svgName})");
                DocsFormatting.Line(md);
                DocsFormatting.Line(md, "```mermaid");
                DocsFormatting.Line(md, seq.Source);
                DocsFormatting.Line(md, "```");
            }
            else
            {
                DocsFormatting.Line(md, "_Diagram omitted: the request sequence exceeds the CqrsLens safety limit (45,000 characters / 400 edges)._");
            }

            DocsFormatting.Line(md);
            DocsFormatting.Line(md, $"_{MarkdownEscaping.MarkdownText(seq.Caption)}_");
            DocsFormatting.Line(md);
        }
    }

    private static void WriteFlowDiagrams(
        StringBuilder md,
        IReadOnlyList<FlowDiagramSection> flowSections,
        IReadOnlyList<string> flowFiles,
        string? notificationMap)
    {
        DocsFormatting.Line(md, "## Flow & Notification Diagrams");
        DocsFormatting.Line(md);

        var any = false;
        if (flowSections.Count > 0)
        {
            any = true;
            var fileIndex = 0;
            foreach (var section in flowSections)
            {
                if (DiagramBuilder.IsWithinMermaidLimits(section.Source))
                {
                    var name = flowFiles[fileIndex++];
                    DocsFormatting.Line(md, $"- **{MarkdownEscaping.MarkdownText(section.Heading)}**: [diagrams/{name}](diagrams/{name}) — {MarkdownEscaping.InlineText(section.Caption)}");
                }
                else
                {
                    DocsFormatting.Line(md, $"- **{MarkdownEscaping.MarkdownText(section.Heading)}**: _omitted (exceeds Mermaid safety limit)_");
                }
            }
        }

        if (notificationMap is not null && DiagramBuilder.IsWithinMermaidLimits(notificationMap))
        {
            any = true;
            DocsFormatting.Line(md, "- **Notifications**: [diagrams/notifications.svg](diagrams/notifications.svg)");
        }

        if (!any)
            DocsFormatting.Line(md, "_No flow or notification diagrams available from the model._");

        DocsFormatting.Line(md);
    }

    private static void WriteControllerExposures(
        StringBuilder md,
        ArchitectureSpine spine,
        IReadOnlyList<SpineRelation> exposesRels)
    {
        DocsFormatting.Line(md, "### Controller Endpoint Exposures");
        DocsFormatting.Line(md);
        if (exposesRels.Count == 0)
        {
            DocsFormatting.Line(md, "_No controller endpoint exposures recorded in the model._");
            DocsFormatting.Line(md);
            return;
        }

        var projectsById = DocsFormatting.BuildProjectsById(spine);
        var endpointsById = spine.Endpoints.ToDictionary(e => e.Id);
        var byController = new Dictionary<ComponentId, (int Total, int Proven, int Inferred)>();

        foreach (var rel in exposesRels)
        {
            if (rel.From.ComponentId is null)
                continue;

            byController.TryGetValue(rel.From.ComponentId, out var counts);
            var isProven = rel.To.EndpointId is not null &&
                           endpointsById.TryGetValue(rel.To.EndpointId, out var ep) &&
                           ep.VerbStrength == EvidenceStrength.Proven;
            byController[rel.From.ComponentId] = (
                counts.Total + 1,
                counts.Proven + (isProven ? 1 : 0),
                counts.Inferred + (isProven ? 0 : 1));
        }

        DocsFormatting.Line(md, "| ControllerId | Controller | Project | Exposed Endpoints | Proven Verbs | Inferred Verbs |");
        DocsFormatting.Line(md, "| --- | --- | --- | ---: | ---: | ---: |");
        var totalAll = 0;
        var totalProven = 0;
        var totalInferred = 0;
        foreach (var (compId, counts) in byController.OrderBy(p => p.Key))
        {
            var name = compId.Symbol.QualifiedName;
            var projectName = projectsById.TryGetValue(compId.Project, out var proj) ? proj.Name : compId.Project.Value;
            totalAll += counts.Total;
            totalProven += counts.Proven;
            totalInferred += counts.Inferred;

            DocsFormatting.Line(
                md,
                $"| {MarkdownEscaping.CodeSpan(compId.Value)} | " +
                $"{MarkdownEscaping.TableCell(name)} | " +
                $"{MarkdownEscaping.TableCell(projectName)} | " +
                $"{counts.Total} | {counts.Proven} | {counts.Inferred} |");
        }
        DocsFormatting.Line(md, $"| **Total** | — | — | **{totalAll}** | **{totalProven}** | **{totalInferred}** |");
        DocsFormatting.Line(md);
    }

    private static void WriteComponentCallFlows(StringBuilder md, ArchitectureSpine spine)
    {
        DocsFormatting.Line(md, "### Component Call & Data Flows");
        DocsFormatting.Line(md);

        var projectsById = DocsFormatting.BuildProjectsById(spine);
        var bySource = new Dictionary<ComponentId, FlowCounts>();

        foreach (var rel in spine.Relations)
        {
            if (rel.From.ComponentId is null)
                continue;

            if (rel.Kind is not (RelationKind.Calls or RelationKind.Dispatches or RelationKind.Publishes or
                RelationKind.TouchesData or RelationKind.UsesData or RelationKind.UsesDataResolved))
            {
                continue;
            }

            if (!bySource.TryGetValue(rel.From.ComponentId, out var counts))
                bySource[rel.From.ComponentId] = counts = new FlowCounts();

            switch (rel.Kind)
            {
                case RelationKind.Calls:
                    counts.Calls++;
                    counts.CallOccurrences += rel.OccurrenceCount;
                    break;
                case RelationKind.Dispatches:
                    counts.Dispatches++;
                    break;
                case RelationKind.Publishes:
                    counts.Publishes++;
                    break;
                case RelationKind.TouchesData:
                    counts.TouchesData++;
                    break;
                case RelationKind.UsesData:
                    counts.UsesData++;
                    break;
                case RelationKind.UsesDataResolved:
                    counts.UsesDataResolved++;
                    break;
            }
        }

        if (bySource.Count == 0)
        {
            DocsFormatting.Line(md, "_No component call or data-access flows recorded in the model._");
            DocsFormatting.Line(md);
            return;
        }

        DocsFormatting.Line(md, "_Static outgoing edges per component; a traced static call or data-access edge does not prove runtime execution._");
        DocsFormatting.Line(md);
        DocsFormatting.Line(md, "| ComponentId | Component | Project | Calls (Relations / Occurrences) | Dispatches | Publishes | TouchesData | UsesData / Resolved |");
        DocsFormatting.Line(md, "| --- | --- | --- | ---: | ---: | ---: | ---: | ---: |");
        foreach (var (compId, counts) in bySource.OrderBy(p => p.Key))
        {
            var projectName = projectsById.TryGetValue(compId.Project, out var proj) ? proj.Name : compId.Project.Value;
            DocsFormatting.Line(
                md,
                $"| {MarkdownEscaping.CodeSpan(compId.Value)} | " +
                $"{MarkdownEscaping.TableCell(compId.Symbol.Name)} | " +
                $"{MarkdownEscaping.TableCell(projectName)} | " +
                $"{counts.Calls} / {counts.CallOccurrences} | " +
                $"{counts.Dispatches} | {counts.Publishes} | {counts.TouchesData} | " +
                $"{counts.UsesData} / {counts.UsesDataResolved} |");
        }
        DocsFormatting.Line(md);
    }

    private sealed class FlowCounts
    {
        public int Calls;
        public int CallOccurrences;
        public int Dispatches;
        public int Publishes;
        public int TouchesData;
        public int UsesData;
        public int UsesDataResolved;
    }
}
