using System.Text;

namespace CqrsLens;

/// <summary>Builds the Mermaid diagram sources shared by the Markdown and HTML reports.</summary>
public static class DiagramBuilder
{
    /// <summary>Makes a Mermaid-safe node id from an arbitrary name.</summary>
    public static string MermaidId(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name)
            sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        var id = sb.ToString();
        if (id.Length == 0 || char.IsDigit(id[0])) id = "n_" + id;
        return id;
    }

    public static string? ProjectGraph(AnalysisResult result)
    {
        if (result.Projects.Count == 0) return null;
        var sb = new StringBuilder();
        sb.AppendLine("flowchart TD");
        foreach (var p in result.Projects)
            sb.AppendLine($"    {MermaidId(p.Name)}[\"{p.Name}\"]");
        foreach (var p in result.Projects)
            foreach (var dep in p.ReferenceNames)
                sb.AppendLine($"    {MermaidId(p.Name)} --> {MermaidId(dep)}");
        return sb.ToString().TrimEnd();
    }

    public static string? LayerGraph(AnalysisResult result)
    {
        if (result.Projects.Count == 0) return null;
        var sb = new StringBuilder();
        sb.AppendLine("flowchart TD");
        foreach (var layer in LayerClassifier.OrderLayers(result.Projects.Select(p => p.Layer)))
        {
            sb.AppendLine($"    subgraph {MermaidId("layer_" + layer)}[\"{layer}\"]");
            foreach (var p in result.Projects.Where(p => p.Layer == layer))
                sb.AppendLine($"        {MermaidId(p.Name)}[\"{p.Name}\"]");
            sb.AppendLine("    end");
        }
        foreach (var p in result.Projects)
            foreach (var dep in p.ReferenceNames)
                sb.AppendLine($"    {MermaidId(p.Name)} --> {MermaidId(dep)}");
        return sb.ToString().TrimEnd();
    }

    public static string? CqrsMap(AnalysisResult result)
    {
        if (result.Requests.Count == 0) return null;
        var sb = new StringBuilder();
        sb.AppendLine("flowchart LR");

        // Node ids use the simple type name, as long as it is unique; requests
        // whose name collides across namespaces get a fully-qualified id so the
        // two stay distinct (labels always stay short).
        var nameCounts = result.Requests
            .GroupBy(r => r.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        string NodeId(CqrsRequest r) =>
            nameCounts[r.Name] > 1 ? MermaidId(r.FullName) : MermaidId(r.Name);
        var emittedHandlers = new HashSet<string>(StringComparer.Ordinal);
        var emittedValidators = new HashSet<string>(StringComparer.Ordinal);

        foreach (var r in result.Requests)
        {
            sb.AppendLine($"    {NodeId(r)}[\"{r.Name}\"]");
            foreach (var h in r.Handlers)
                if (emittedHandlers.Add(h))
                    sb.AppendLine($"    {MermaidId(h)}([\"{h}\"])");
            foreach (var v in r.Validators)
                if (emittedValidators.Add(v))
                    sb.AppendLine($"    {MermaidId(v)}{{\"{v}\"}}");
        }
        foreach (var r in result.Requests)
        {
            foreach (var h in r.Handlers)
                sb.AppendLine($"    {NodeId(r)} --> {MermaidId(h)}");
            foreach (var v in r.Validators)
                sb.AppendLine($"    {MermaidId(v)} -.-> {NodeId(r)}");
        }
        return sb.ToString().TrimEnd();
    }
}
