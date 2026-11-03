using System.Text;

namespace CqrsLens;

/// <summary>
/// Deterministic, zero-dependency renderer for the <c>sequenceDiagram</c> subset the
/// request-sequence builder emits (participants, solid/dashed messages, notes, and a
/// <c>%% Request sequence:</c> title comment). Compiles to the same self-contained
/// inline SVG shape as the other CqrsLens diagrams so SVG-mode reports stay zero-JS.
/// </summary>
internal static class MermaidSequenceRenderer
{
    private const double Padding = 16.0;
    private const double BoxHeight = 34.0;
    private const double BoxGap = 24.0;
    private const double MessageHeight = 34.0;
    private const double NoteHeight = 30.0;
    private const double NoteAdvance = 44.0;
    private const double MinBoxWidth = 96.0;

    private sealed record Participant(string Id, string Label);

    private sealed record Message(string From, string To, string Text, bool Dashed);

    private sealed record Note(string From, string? To, string Text);

    private abstract record Row;

    private sealed record MessageRow(Message Message) : Row;

    private sealed record NoteRow(Note Note) : Row;

    private static string E(string? value) => MermaidSvgText.Encode(value);

    private static string F(double value) => MermaidSvgText.Format(value);

    internal static string Render(string[] rawLines)
    {
        var title = "Request sequence";
        var participants = new List<Participant>();
        var participantIds = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<Row>();

        foreach (var raw in rawLines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line == "sequenceDiagram")
                continue;

            if (line.StartsWith("%%", StringComparison.Ordinal))
            {
                const string marker = "Request sequence:";
                var at = line.IndexOf(marker, StringComparison.Ordinal);
                if (at >= 0)
                    title = line[(at + marker.Length)..].Trim();
                continue;
            }

            if (line.StartsWith("participant ", StringComparison.Ordinal))
            {
                var rest = line["participant ".Length..];
                var asAt = rest.IndexOf(" as ", StringComparison.Ordinal);
                if (asAt < 0)
                    continue;
                var id = rest[..asAt].Trim();
                var label = rest[(asAt + 4)..].Trim();
                if (id.Length == 0 || !participantIds.Add(id))
                    continue;
                participants.Add(new Participant(id, label));
                continue;
            }

            if (line.StartsWith("Note over ", StringComparison.Ordinal))
            {
                var rest = line["Note over ".Length..];
                var colon = rest.IndexOf(':');
                if (colon < 0)
                    continue;
                var ids = rest[..colon].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (ids.Length == 0)
                    continue;
                rows.Add(new NoteRow(new Note(ids[0], ids.Length > 1 ? ids[1] : null, rest[(colon + 1)..].Trim())));
                continue;
            }

            if (line.Contains("-->>", StringComparison.Ordinal))
            {
                AddMessage(line, "-->>", dashed: true, rows);
                continue;
            }

            if (line.Contains("->>", StringComparison.Ordinal))
                AddMessage(line, "->>", dashed: false, rows);
        }

        if (participants.Count == 0)
            return Empty(title);

        return Write(title, participants, rows);
    }

    private static void AddMessage(string line, string separator, bool dashed, List<Row> rows)
    {
        var at = line.IndexOf(separator, StringComparison.Ordinal);
        if (at <= 0)
            return;
        var from = line[..at].Trim();
        var rest = line[(at + separator.Length)..];
        var colon = rest.IndexOf(':');
        if (colon < 0)
            return;
        var to = rest[..colon].Trim();
        var text = rest[(colon + 1)..].Trim();
        if (from.Length == 0 || to.Length == 0)
            return;
        rows.Add(new MessageRow(new Message(from, to, text, dashed)));
    }

    private static string Write(string title, List<Participant> participants, List<Row> rows)
    {
        var widths = participants
            .Select(p => Math.Max(MinBoxWidth, Math.Ceiling(p.Label.Length * 7.2) + 24.0))
            .ToList();
        var x = new double[participants.Count];
        var center = new double[participants.Count];
        var cursorX = Padding;
        for (var i = 0; i < participants.Count; i++)
        {
            x[i] = cursorX;
            center[i] = cursorX + widths[i] / 2.0;
            cursorX += widths[i] + BoxGap;
        }

        var canvasWidth = cursorX - BoxGap + Padding;
        var headerTop = Padding;
        var headerBottom = headerTop + BoxHeight;

        var rowY = new double[rows.Count];
        var cursorY = headerBottom + 30.0;
        for (var i = 0; i < rows.Count; i++)
        {
            rowY[i] = cursorY;
            cursorY += rows[i] is NoteRow ? NoteAdvance : MessageHeight;
        }
        var lifelineBottom = cursorY + 10.0;
        var canvasHeight = lifelineBottom + Padding;

        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < participants.Count; i++)
            index[participants[i].Id] = i;

        var svg = new StringBuilder();
        svg.Append("<div class=\"svg-diagram\">");
        svg.Append(
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {F(canvasWidth)} {F(canvasHeight)}\" " +
            $"width=\"{F(canvasWidth)}\" height=\"{F(canvasHeight)}\" role=\"img\">");
        svg.Append($"<title>Request sequence: {E(title)}</title>");
        svg.Append("<defs>");
        svg.Append(
            "<marker id=\"arrow-default\" viewBox=\"0 0 10 10\" refX=\"9\" refY=\"5\" " +
            "markerWidth=\"6\" markerHeight=\"6\" orient=\"auto-start-reverse\">" +
            "<path d=\"M 0 1 L 10 5 L 0 9 z\" fill=\"#475569\"/></marker>");
        svg.Append("</defs>");

        // Lifelines.
        for (var i = 0; i < participants.Count; i++)
        {
            svg.Append(
                $"<line x1=\"{F(center[i])}\" y1=\"{F(headerBottom)}\" x2=\"{F(center[i])}\" y2=\"{F(lifelineBottom)}\" " +
                "stroke=\"#94a3b8\" stroke-width=\"1\" stroke-dasharray=\"4 4\"/>");
        }

        // Rows.
        for (var i = 0; i < rows.Count; i++)
        {
            switch (rows[i])
            {
                case MessageRow(var message):
                    WriteMessage(svg, message, index, center, rowY[i]);
                    break;
                case NoteRow(var note):
                    WriteNote(svg, note, index, center, rowY[i], canvasWidth);
                    break;
            }
        }

        // Participant boxes.
        for (var i = 0; i < participants.Count; i++)
        {
            svg.Append($"<g class=\"seq-participant\" data-id=\"{E(participants[i].Id)}\">");
            svg.Append(
                $"<rect x=\"{F(x[i])}\" y=\"{F(headerTop)}\" width=\"{F(widths[i])}\" height=\"{F(BoxHeight)}\" " +
                "rx=\"6\" fill=\"#e0e7ff\" stroke=\"#4f46e5\" stroke-width=\"1.5\"/>");
            svg.Append(
                $"<text x=\"{F(center[i])}\" y=\"{F(headerTop + BoxHeight / 2.0 + 4.5)}\" text-anchor=\"middle\" " +
                "fill=\"#1e293b\" font-family=\"system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif\" " +
                $"font-size=\"13\" font-weight=\"600\">{E(participants[i].Label)}</text>");
            svg.Append("</g>");
        }

        svg.Append("</svg></div>");
        return svg.ToString();
    }

    private static void WriteMessage(
        StringBuilder svg,
        Message message,
        IReadOnlyDictionary<string, int> index,
        double[] center,
        double y)
    {
        if (!index.TryGetValue(message.From, out var from) || !index.TryGetValue(message.To, out var to))
            return;

        var x1 = center[from];
        var x2 = center[to];
        var dash = message.Dashed ? " stroke-dasharray=\"5 3\"" : "";
        svg.Append($"<g class=\"seq-message\" data-from=\"{E(message.From)}\" data-to=\"{E(message.To)}\">");
        svg.Append(
            $"<line x1=\"{F(x1)}\" y1=\"{F(y)}\" x2=\"{F(x2)}\" y2=\"{F(y)}\" " +
            $"stroke=\"#475569\" stroke-width=\"1.5\"{dash} marker-end=\"url(#arrow-default)\"/>");
        if (message.Text.Length > 0)
        {
            svg.Append(
                $"<text x=\"{F((x1 + x2) / 2.0)}\" y=\"{F(y - 6)}\" text-anchor=\"middle\" fill=\"#334155\" " +
                "font-family=\"system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif\" " +
                $"font-size=\"11\">{E(message.Text)}</text>");
        }
        svg.Append("</g>");
    }

    private static void WriteNote(
        StringBuilder svg,
        Note note,
        IReadOnlyDictionary<string, int> index,
        double[] center,
        double y,
        double canvasWidth)
    {
        if (!index.TryGetValue(note.From, out var from))
            return;
        var left = center[from];
        var right = note.To is not null && index.TryGetValue(note.To, out var to) ? center[to] : left;
        var x0 = Math.Max(0.0, Math.Min(left, right) - 80.0);
        var x1 = Math.Min(canvasWidth, Math.Max(left, right) + 80.0);
        svg.Append("<g class=\"seq-note\">");
        svg.Append(
            $"<rect x=\"{F(x0)}\" y=\"{F(y)}\" width=\"{F(x1 - x0)}\" height=\"{F(NoteHeight)}\" rx=\"4\" " +
            "fill=\"#fef9c3\" stroke=\"#ca8a04\" stroke-width=\"1\"/>");
        svg.Append(
            $"<text x=\"{F((x0 + x1) / 2.0)}\" y=\"{F(y + NoteHeight / 2.0 + 4)}\" text-anchor=\"middle\" " +
            "fill=\"#713f12\" font-family=\"system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif\" " +
            $"font-size=\"11\">{E(note.Text)}</text>");
        svg.Append("</g>");
    }

    private static string Empty(string title)
    {
        var svg = new StringBuilder();
        svg.Append("<div class=\"svg-diagram\">");
        svg.Append(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 200 60\" width=\"200\" height=\"60\" role=\"img\">");
        svg.Append($"<title>Request sequence: {E(title)}</title>");
        svg.Append("</svg></div>");
        return svg.ToString();
    }
}
