namespace CqrsLens;

/// <summary>Escaping helpers for text embedded in Markdown reports.</summary>
internal static class MarkdownEscaping
{
    /// <summary>Collapses user-derived text that must stay on one report line.</summary>
    internal static string InlineText(string? value)
        => (value ?? "")
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ');

    /// <summary>
    /// Escapes text that Markdown must not interpret as HTML. GitHub renderers
    /// treat a raw &lt; or &gt; as markup, which silently truncates generic types
    /// such as List&lt;ProductDto&gt; in table cells.
    /// </summary>
    internal static string MarkdownText(string? value)
        => InlineText(value)
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    /// <summary>Escapes a value used in a Markdown table cell.</summary>
    internal static string TableCell(string? value)
        => MarkdownText(value).Replace("|", "\\|", StringComparison.Ordinal);

    /// <summary>Renders a Markdown code span, widening the fence when needed.</summary>
    internal static string CodeSpan(string? value)
    {
        var text = InlineText(value);
        var fence = text.Contains('`') ? "``" : "`";
        return $"{fence}{text}{fence}";
    }
}
