using System.Text;

namespace CqrsLens;

/// <summary>Builds the Mermaid diagram sources shared by the Markdown and HTML reports.</summary>
public static class DiagramBuilder
{
    /// <summary>Makes a readable Mermaid-safe base id from an arbitrary name.</summary>
    public static string MermaidId(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name)
            sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        var id = sb.ToString();
        if (id.Length == 0 || char.IsDigit(id[0])) id = "n_" + id;
        return id;
    }

    /// <summary>
    /// Escapes text used inside a quoted Mermaid label. The label must not be able to
    /// terminate its quote or inject a new line into the diagram source.
    /// </summary>
    public static string MermaidLabel(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            switch (c)
            {
                case '"':
                    sb.Append("&quot;");
                    break;
                case '<':
                    sb.Append("&lt;");
                    break;
                case '>':
                    sb.Append("&gt;");
                    break;
                case '|':
                    sb.Append("&#124;");
                    break;
                case '\r':
                case '\n':
                    sb.Append(' ');
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }

    public static string? ProjectGraph(AnalysisResult result)
    {
        if (result.Projects.Count == 0) return null;
        var ids = new MermaidIdAllocator();
        var sb = new StringBuilder();
        sb.AppendLine("flowchart TD");
        foreach (var p in result.Projects)
        {
            var id = ids.Allocate("project:" + p.Name, p.Name);
            sb.AppendLine($"    {id}[\"{MermaidLabel(p.Name)}\"]");
        }
        foreach (var p in result.Projects)
            foreach (var dep in p.ReferenceNames)
                sb.AppendLine(
                    $"    {ids.Allocate("project:" + p.Name, p.Name)} --> " +
                    $"{ids.Allocate("project:" + dep, dep)}");
        return sb.ToString().TrimEnd();
    }

    public static string? LayerGraph(AnalysisResult result)
    {
        if (result.Projects.Count == 0) return null;
        var ids = new MermaidIdAllocator();
        var sb = new StringBuilder();
        sb.AppendLine("flowchart TD");
        foreach (var layer in LayerClassifier.OrderLayers(result.Projects.Select(p => p.Layer)))
        {
            var layerId = ids.Allocate("layer:" + layer, "layer_" + layer);
            sb.AppendLine($"    subgraph {layerId}[\"{MermaidLabel(layer)}\"]");
            foreach (var p in result.Projects.Where(p => p.Layer == layer))
            {
                var projectId = ids.Allocate("project:" + p.Name, p.Name);
                sb.AppendLine($"        {projectId}[\"{MermaidLabel(p.Name)}\"]");
            }
            sb.AppendLine("    end");
        }
        foreach (var p in result.Projects)
            foreach (var dep in p.ReferenceNames)
                sb.AppendLine(
                    $"    {ids.Allocate("project:" + p.Name, p.Name)} --> " +
                    $"{ids.Allocate("project:" + dep, dep)}");
        return sb.ToString().TrimEnd();
    }

    public static string? CqrsMap(AnalysisResult result)
    {
        if (result.Requests.Count == 0) return null;
        var ids = new MermaidIdAllocator();
        var sb = new StringBuilder();
        sb.AppendLine("flowchart LR");

        // Prefer readable simple names. Same-named requests fall back to their fully
        // qualified name, as before; the allocator still suffixes any sanitized
        // collisions (Foo.Bar vs Foo-Bar) and keeps handler/request ids distinct.
        var nameCounts = result.Requests
            .GroupBy(r => r.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var requestIds = result.Requests
            .GroupBy(r => r.FullName, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => ids.Allocate(
                    "request:" + g.Key,
                    nameCounts[g.First().Name] > 1 ? g.Key : g.First().Name),
                StringComparer.Ordinal);

        var handlerIds = result.Requests
            .SelectMany(r => r.Handlers)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(h => h, StringComparer.Ordinal)
            .ToDictionary(
                h => h,
                h => ids.Allocate("handler:" + h, h),
                StringComparer.Ordinal);

        var validatorIds = result.Requests
            .SelectMany(r => r.Validators)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToDictionary(
                v => v,
                v => ids.Allocate("validator:" + v, v),
                StringComparer.Ordinal);

        var emittedHandlers = new HashSet<string>(StringComparer.Ordinal);
        var emittedValidators = new HashSet<string>(StringComparer.Ordinal);

        foreach (var r in result.Requests)
        {
            sb.AppendLine($"    {requestIds[r.FullName]}[\"{MermaidLabel(r.Name)}\"]");
            foreach (var h in r.Handlers)
                if (emittedHandlers.Add(h))
                    sb.AppendLine(
                        $"    {handlerIds[h]}([\"{MermaidLabel(CSharpScanner.SimpleName(h))}\"])");
            foreach (var v in r.Validators)
                if (emittedValidators.Add(v))
                    sb.AppendLine(
                        $"    {validatorIds[v]}{{\"{MermaidLabel(CSharpScanner.SimpleName(v))}\"}}");
        }
        foreach (var r in result.Requests)
        {
            foreach (var h in r.Handlers)
                sb.AppendLine($"    {requestIds[r.FullName]} --> {handlerIds[h]}");
            foreach (var v in r.Validators)
                sb.AppendLine($"    {validatorIds[v]} -.-> {requestIds[r.FullName]}");
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>Allocates collision-free ids for one diagram, preserving readable bases.</summary>
    private sealed class MermaidIdAllocator
    {
        private readonly Dictionary<string, string> _ids = new(StringComparer.Ordinal);
        private readonly HashSet<string> _used = new(StringComparer.Ordinal);

        public string Allocate(string key, string preferredName)
        {
            if (_ids.TryGetValue(key, out var existing))
                return existing;

            var baseId = MermaidId(preferredName);
            var id = baseId;
            var suffix = 2;
            while (!_used.Add(id))
                id = $"{baseId}_{suffix++}";

            _ids[key] = id;
            return id;
        }
    }
}
