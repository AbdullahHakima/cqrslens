using System.Text;
using static CqrsLens.MermaidDiagramText;

namespace CqrsLens;

/// <summary>
/// Builds a Mermaid <c>flowchart LR</c> comparing the scanned project reference graph
/// against an evaluated <see cref="ArchitectureProposalEvaluation"/>, highlighting added
/// projects/edges in green, removed edges dashed, and cycles/layer violations in red.
/// </summary>
public static class ProposalDiagramBuilder
{
    public static string? BuildMermaid(
        AnalysisResult current,
        ArchitectureProposalEvaluation evaluation)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(evaluation);

        var projects = new Dictionary<string, (string Layer, bool IsProposed, bool HasViolation)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var project in current.Projects)
            projects.TryAdd(project.Name, (project.Layer, IsProposed: false, HasViolation: false));

        foreach (var proposed in evaluation.ProposedProjects)
            projects[proposed.Name] = (proposed.Layer, IsProposed: true, HasViolation: false);

        if (projects.Count == 0 && evaluation.ProposedReferences.Count == 0)
            return null;

        var violatedNodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var violatedEdges = new HashSet<(string From, string To)>();

        foreach (var finding in evaluation.Findings)
        {
            if (finding.Reference is not null)
            {
                violatedEdges.Add((finding.Reference.From, finding.Reference.To));
                violatedNodes.Add(finding.Reference.From);
                violatedNodes.Add(finding.Reference.To);
            }

            if (finding.DependencyPath is { Count: >= 2 })
            {
                for (var i = 0; i + 1 < finding.DependencyPath.Count; i++)
                {
                    violatedEdges.Add((finding.DependencyPath[i], finding.DependencyPath[i + 1]));
                    violatedNodes.Add(finding.DependencyPath[i]);
                }
                violatedNodes.Add(finding.DependencyPath[^1]);
            }
        }

        // Ensure endpoints of proposed references appear even if unknown/invalid.
        foreach (var reference in evaluation.ProposedReferences)
        {
            projects.TryAdd(reference.From, ("Other", IsProposed: false, HasViolation: false));
            projects.TryAdd(reference.To, ("Other", IsProposed: false, HasViolation: false));
        }

        var ids = new MermaidIdAllocator();
        var nodeIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in projects.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            var info = projects[name];
            var label = info.IsProposed ? $"+ {name} [{info.Layer}]" : $"{name} [{info.Layer}]";
            nodeIds[name] = ids.Allocate(name, "prj", label);
        }

        var sb = new StringBuilder();
        sb.AppendLine("flowchart LR");
        sb.AppendLine("    accTitle: Architecture Proposal Diff");
        sb.AppendLine($"    accDescr: Proposal status {MermaidLabel(evaluation.Status)} across {projects.Count} project(s).");

        foreach (var name in projects.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            var info = projects[name];
            var id = nodeIds[name];
            var label = info.IsProposed ? $"+ {name} [{info.Layer}]" : $"{name} [{info.Layer}]";
            var cls = violatedNodes.Contains(name)
                ? "clsPropViolation"
                : info.IsProposed
                    ? "clsPropAdded"
                    : "clsPropExisting";
            sb.AppendLine($"    {id}[\"{MermaidLabel(label)}\"]:::{cls}");
        }

        var removedPairs = new HashSet<(string From, string To)>();
        var addedPairs = new HashSet<(string From, string To)>();
        foreach (var reference in evaluation.ProposedReferences)
        {
            if (string.Equals(reference.Operation, "remove", StringComparison.OrdinalIgnoreCase))
                removedPairs.Add((reference.From, reference.To));
            else
                addedPairs.Add((reference.From, reference.To));
        }

        var edgeStyles = new List<(int Index, string Style)>();
        var edgeIdx = 0;

        void EmitEdge(string from, string to, string? label, bool dotted, string? strokeColor)
        {
            if (!nodeIds.TryGetValue(from, out var fromId) || !nodeIds.TryGetValue(to, out var toId))
                return;

            var arrow = dotted ? "-.->" : "-->";
            var edgeText = string.IsNullOrEmpty(label)
                ? $"    {fromId} {arrow} {toId}"
                : $"    {fromId} {arrow}|{MermaidLabel(label)}| {toId}";
            sb.AppendLine(edgeText);
            if (!string.IsNullOrEmpty(strokeColor))
                edgeStyles.Add((edgeIdx, $"stroke:{strokeColor},stroke-width:2px"));
            edgeIdx++;
        }

        // 1. Existing edges (or removed existing edges)
        var emittedPairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in current.Projects.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var target in project.ReferenceNames.OrderBy(r => r, StringComparer.OrdinalIgnoreCase))
            {
                var key = $"{project.Name}->{target}";
                if (!emittedPairs.Add(key))
                    continue;

                var isRemoved = removedPairs.Any(p =>
                    string.Equals(p.From, project.Name, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(p.To, target, StringComparison.OrdinalIgnoreCase));
                var isViolated = violatedEdges.Any(p =>
                    string.Equals(p.From, project.Name, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(p.To, target, StringComparison.OrdinalIgnoreCase));

                if (isRemoved)
                    EmitEdge(project.Name, target, "- remove", dotted: true, "#64748b");
                else if (isViolated)
                    EmitEdge(project.Name, target, "violation", dotted: false, "#dc2626");
                else
                    EmitEdge(project.Name, target, null, dotted: false, null);
            }
        }

        // 2. Proposed additions (and removals that did not exist directly)
        foreach (var reference in evaluation.ProposedReferences)
        {
            var key = $"{reference.From}->{reference.To}";
            var isRemove = string.Equals(reference.Operation, "remove", StringComparison.OrdinalIgnoreCase);
            if (isRemove)
            {
                if (!emittedPairs.Contains(key))
                    EmitEdge(reference.From, reference.To, "- remove", dotted: true, "#64748b");
                continue;
            }

            if (emittedPairs.Contains(key))
                continue;

            var isViolated = violatedEdges.Any(p =>
                string.Equals(p.From, reference.From, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.To, reference.To, StringComparison.OrdinalIgnoreCase));
            EmitEdge(
                reference.From,
                reference.To,
                isViolated ? "+ add (violation)" : "+ add",
                dotted: false,
                isViolated ? "#dc2626" : "#16a34a");
        }

        foreach (var (idx, style) in edgeStyles)
            sb.AppendLine($"    linkStyle {idx} {style}");

        sb.AppendLine("    classDef clsPropExisting fill:#f8fafc,stroke:#475569,stroke-width:1.5px,color:#1e293b");
        sb.AppendLine("    classDef clsPropAdded fill:#dcfce7,stroke:#16a34a,stroke-width:2px,color:#14532d");
        sb.AppendLine("    classDef clsPropViolation fill:#fee2e2,stroke:#dc2626,stroke-width:2px,color:#7f1d1d");
        return sb.ToString().TrimEnd();
    }
}
