using System.Text;

namespace CqrsLens;

/// <summary>Shared Mermaid id, label and safety-limit helpers for diagram builders.</summary>
internal static class MermaidDiagramText
{
    // GitHub's Mermaid configuration uses maxTextSize: 50000 and maxEdges: 500.
    // Leave headroom for the renderer's own preprocessing.
    private const int MaxMermaidTextLength = 45_000;
    private const int MaxMermaidEdges = 400;

    internal const string UnassignedProject = "(unassigned)";

    /// <summary>
    /// Mermaid keywords that cannot be used as a bare node or subgraph id. The
    /// builders additionally role-prefix every id (req_, hdl_, val_, prj_, lyr_),
    /// so this set only protects callers of the public helper.
    /// </summary>
    private static readonly HashSet<string> ReservedMermaidIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "end", "graph", "flowchart", "subgraph", "style", "linkStyle",
        "class", "classDef", "click", "default", "direction",
        "accTitle", "accDescr", "accDescrMultiline", "call", "href",
    };

    /// <summary>
    /// Makes an ASCII-only, Mermaid-safe base id from an arbitrary name. Reserved
    /// words are prefixed with <c>n_</c>; diagram builders add role prefixes.
    /// </summary>
    internal static string MermaidId(string? name)
    {
        var id = SanitizeMermaidId(name);
        return ReservedMermaidIds.Contains(id) ? "n_" + id : id;
    }

    /// <summary>
    /// Escapes text used inside a quoted Mermaid label. GitHub HTML-decodes its
    /// input before Mermaid sees it, so HTML entities such as <c>&amp;quot;</c>
    /// are unsafe. Mermaid's own <c>#...;</c> entities are decoded exactly once.
    /// The label must not be able to terminate its quote or inject a new line.
    /// </summary>
    internal static string MermaidLabel(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        var sb = new StringBuilder(value.Length);
        foreach (var c in value.ReplaceLineEndings(" "))
        {
            switch (c)
            {
                case '&':
                    sb.Append("#38;");
                    break;
                case '"':
                    sb.Append("#quot;");
                    break;
                case '<':
                    sb.Append("#lt;");
                    break;
                case '>':
                    sb.Append("#gt;");
                    break;
                case '|':
                    sb.Append("#124;");
                    break;
                case '#':
                    sb.Append("#35;");
                    break;
                default:
                    sb.Append(char.IsControl(c) ? ' ' : c);
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>Whether a Mermaid source is safely inside GitHub's renderer limits.</summary>
    internal static bool IsWithinMermaidLimits(string source)
    {
        if (source.Length > MaxMermaidTextLength)
            return false;

        var edges = source
            .Split('\n')
            .Count(IsMermaidEdgeLine);
        return edges <= MaxMermaidEdges;
    }

    internal static string CountText(int count, string noun)
        => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    internal static string SanitizeMermaidId(string? name)
    {
        var sb = new StringBuilder(name?.Length ?? 0);
        foreach (var c in name ?? "")
            sb.Append(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_' ? c : '_');
        var id = sb.ToString();
        if (id.Length == 0)
            id = "n";
        if (char.IsDigit(id[0]))
            id = "n_" + id;
        return id;
    }

    private static bool IsMermaidEdgeLine(string line)
        => line.Contains("-->", StringComparison.Ordinal) ||
           line.Contains("-.->", StringComparison.Ordinal) ||
           line.Contains("||--", StringComparison.Ordinal) ||
           line.Contains("|o--", StringComparison.Ordinal) ||
           line.Contains("}o--", StringComparison.Ordinal);
}

/// <summary>Allocates collision-free ids for one diagram, preserving readable bases.</summary>
internal sealed class MermaidIdAllocator
{
    private readonly Dictionary<string, string> _ids = new(StringComparer.Ordinal);
    private readonly HashSet<string> _used = new(StringComparer.Ordinal);

    internal string Allocate(string key, string prefix, string preferredName)
    {
        if (_ids.TryGetValue(key, out var existing))
            return existing;

        var baseId = prefix + "_" + MermaidDiagramText.SanitizeMermaidId(preferredName);
        var id = baseId;
        var suffix = 2;
        while (!_used.Add(id))
            id = $"{baseId}_{suffix++}";

        _ids[key] = id;
        return id;
    }
}
