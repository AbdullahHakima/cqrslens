namespace CqrsLens;

/// <summary>Parses Mermaid node syntax into an id, label and shape hints.</summary>
internal static class MermaidNodeSyntax
{
    internal static void ParseIdAndLabel(
        string raw,
        out string id,
        out string label,
        out bool isStadium,
        out string? className)
    {
        isStadium = false;
        className = null;
        var working = raw.Trim();

        var classSep = working.LastIndexOf(":::", StringComparison.Ordinal);
        if (classSep > 0)
        {
            className = working[(classSep + 3)..].Trim();
            working = working[..classSep].Trim();
        }

        var stadiumStart = working.IndexOf("([\"", StringComparison.Ordinal);
        if (stadiumStart > 0 && working.EndsWith("\"])", StringComparison.Ordinal))
        {
            id = working[..stadiumStart].Trim();
            label = working[(stadiumStart + 3)..^3];
            isStadium = true;
            return;
        }

        var rectStart = working.IndexOf("[\"", StringComparison.Ordinal);
        if (rectStart > 0 && working.EndsWith("\"]", StringComparison.Ordinal))
        {
            id = working[..rectStart].Trim();
            label = working[(rectStart + 2)..^2];
            return;
        }

        id = working;
        label = working;
    }
}
