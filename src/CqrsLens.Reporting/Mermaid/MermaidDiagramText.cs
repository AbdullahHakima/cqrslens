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

    private static readonly HashSet<string> GenericTypeLeafNames = new(StringComparer.Ordinal)
    {
        "Handler", "Validator", "Command", "Query", "Request", "Notification",
        "CommandHandler", "QueryHandler", "RequestHandler", "NotificationHandler",
    };

    /// <summary>
    /// Returns a concise display name for a type. When the leaf segment is generic
    /// (e.g. <c>Handler</c>), includes the enclosing type (<c>CreateCustomerCommand.Handler</c>);
    /// when the leaf is already specific (<c>UpsertCategoryCommandHandler</c>), keeps it.
    /// </summary>
    internal static string ShortTypeName(string? fullName)
    {
        if (string.IsNullOrEmpty(fullName))
            return "";

        var trimmed = fullName.Trim();
        var dot = trimmed.LastIndexOf('.');
        if (dot < 0)
            return trimmed;

        var leaf = trimmed[(dot + 1)..];
        if (!GenericTypeLeafNames.Contains(leaf))
            return leaf;

        var prevDot = trimmed.LastIndexOf('.', dot - 1);
        var enclosing = prevDot < 0 ? trimmed[..dot] : trimmed[(prevDot + 1)..dot];
        return string.IsNullOrEmpty(enclosing) ? leaf : $"{enclosing}.{leaf}";
    }

    /// <summary>
    /// Returns <see cref="ShortTypeName(string?)"/> when unique within <paramref name="allFullNames"/>,
    /// or the minimal disambiguating suffix when multiple types share a leaf/short name.
    /// </summary>
    internal static string ShortTypeName(string? fullName, IEnumerable<string>? allFullNames)
    {
        var baseShort = ShortTypeName(fullName);
        if (string.IsNullOrEmpty(fullName) || allFullNames is null)
            return baseShort;

        var trimmed = fullName.Trim();
        var peerMatches = allFullNames
            .Select(n => n?.Trim() ?? "")
            .Where(n => n.Length > 0 &&
                        !string.Equals(n, trimmed, StringComparison.Ordinal) &&
                        string.Equals(ShortTypeName(n), baseShort, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (peerMatches.Count == 0)
            return baseShort;

        var segments = trimmed.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var baseSegmentCount = baseShort.Contains('.', StringComparison.Ordinal) ? 2 : 1;
        if (segments.Length <= baseSegmentCount)
            return trimmed;

        var peerSegments = peerMatches
            .Select(p => p.Split('.', StringSplitOptions.RemoveEmptyEntries))
            .ToList();

        // Walk right-to-left above the base short name to find the nearest ancestor
        // segment that distinguishes this type from its colliding peers.
        for (var offset = baseSegmentCount + 1; offset <= segments.Length; offset++)
        {
            var candidateSegment = segments[^offset];
            var candidate = $"{candidateSegment}.{baseShort}";
            var collides = peerSegments.Any(ps =>
                ps.Length >= offset &&
                string.Equals(ps[^offset], candidateSegment, StringComparison.Ordinal));
            if (!collides)
                return candidate;
        }

        // Fallback if one path is a strict suffix of another: use increasing suffix length.
        for (var take = baseSegmentCount + 1; take <= segments.Length; take++)
        {
            var suffix = string.Join(".", segments[^take..]);
            var collides = peerSegments.Any(ps =>
                ps.Length >= take &&
                string.Equals(string.Join(".", ps[^take..]), suffix, StringComparison.Ordinal));
            if (!collides)
                return suffix;
        }

        return trimmed;
    }

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
