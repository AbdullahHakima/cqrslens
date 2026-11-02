using System.Text;
using static CqrsLens.MermaidDiagramText;

namespace CqrsLens;

/// <summary>
/// Builds a Mermaid <c>flowchart LR</c> of the pipeline behavior chain around a
/// request. Behaviors are drawn in the order the model lists them and the diagram
/// is labelled "registered order" — CqrsLens does not resolve execution order.
/// </summary>
internal static class BehaviorChainDiagramBuilder
{
    internal static string? BehaviorChain(AnalysisResult result)
    {
        if (result.Behaviors.Count == 0)
            return null;

        var behaviors = result.Behaviors.ToList();
        var nameCounts = behaviors
            .GroupBy(b => CSharpScanner.SimpleName(b.Name), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var ids = new MermaidIdAllocator();
        var sb = new StringBuilder();
        sb.AppendLine("flowchart LR");
        sb.AppendLine("    accTitle: Pipeline behavior chain");
        sb.AppendLine($"    accDescr: {CountText(behaviors.Count, "behavior")} in scan order, not resolved execution order.");

        sb.AppendLine("    req([\"Request\"]):::clsChainRequest");
        var previous = "req";
        for (var i = 0; i < behaviors.Count; i++)
        {
            var behavior = behaviors[i];
            var simple = CSharpScanner.SimpleName(behavior.Name);
            var label = nameCounts[simple] > 1 ? behavior.Name : simple;
            var id = ids.Allocate("behavior:" + behavior.Name + ":" + i, "bhv", label);
            // Rectangle shape: the zero-JS SVG parser (MermaidNodeSyntax) does not
            // support the subroutine [[...]] shape, so a plain rectangle is used.
            sb.AppendLine($"    {id}[\"{MermaidLabel(label)}\"]:::clsChainBehavior");
            sb.AppendLine($"    {previous} --> {id}");
            previous = id;
        }

        sb.AppendLine($"    {previous} --> hdl([\"Handler\"]):::clsChainHandler");
        sb.AppendLine("    classDef clsChainRequest fill:#dbeafe,stroke:#2563eb,stroke-width:1.5px,color:#1e3a8a");
        sb.AppendLine("    classDef clsChainBehavior fill:#fef3c7,stroke:#d97706,stroke-width:1.5px,color:#78350f");
        sb.AppendLine("    classDef clsChainHandler fill:#f8fafc,stroke:#475569,stroke-width:1.5px,color:#1e293b");

        return sb.ToString().TrimEnd();
    }
}
