using System.Text;

namespace CqrsLens;

/// <summary>
/// Deterministic, zero-dependency renderer for the <c>sequenceDiagram</c> subset emitted for
/// CQRS requests and direct operations. Dynamically spaces participants to prevent text overlap,
/// backs every message label with a solid pill, and adds CSS hooks for light/dark themes.
/// </summary>
internal static class MermaidSequenceRenderer
{
    private const double Padding = 24.0;
    private const double BoxHeight = 38.0;
    private const double BoxGap = 32.0;
    private const double MessageHeight = 44.0;
    private const double NoteHeight = 32.0;
    private const double NoteAdvance = 52.0;
    private const double MinBoxWidth = 112.0;
    private const double CharWidth = 7.8;

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
        var titlePrefix = "Request sequence";
        var title = "";
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
                const string reqMarker = "Request sequence:";
                const string opMarker = "Operation sequence:";
                var reqAt = line.IndexOf(reqMarker, StringComparison.Ordinal);
                if (reqAt >= 0)
                {
                    titlePrefix = "Request sequence";
                    title = line[(reqAt + reqMarker.Length)..].Trim();
                }
                else
                {
                    var opAt = line.IndexOf(opMarker, StringComparison.Ordinal);
                    if (opAt >= 0)
                    {
                        titlePrefix = "Operation sequence";
                        title = line[(opAt + opMarker.Length)..].Trim();
                    }
                }
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

        var fullTitle = string.IsNullOrEmpty(title) ? titlePrefix : $"{titlePrefix}: {title}";

        if (participants.Count == 0)
            return Empty(fullTitle);

        return Write(fullTitle, participants, rows);
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

    internal static (IReadOnlyList<string> Lines, double PillWidth, double PillHeight) MeasureMessage(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (Array.Empty<string>(), 0.0, 0.0);

        var trimmed = text.Trim();
        IReadOnlyList<string> lines;
        if (trimmed.Length <= 34)
        {
            lines = new[] { trimmed };
        }
        else
        {
            var split = FindSplitIndex(trimmed, 36);
            var line1 = split > 0 ? trimmed[..split].TrimEnd() : trimmed[..34].TrimEnd();
            var line2 = split > 0 ? trimmed[split..].TrimStart() : trimmed[34..].TrimStart();
            if (line1.Length > 38)
                line1 = line1[..37].TrimEnd() + "…";
            if (line2.Length > 38)
                line2 = line2[..37].TrimEnd() + "…";
            lines = string.IsNullOrEmpty(line2) ? new[] { line1 } : new[] { line1, line2 };
        }

        var maxLen = lines.Max(l => l.Length);
        var width = Math.Max(44.0, Math.Ceiling(maxLen * 6.8 + 16.0));
        var height = lines.Count == 1 ? 18.0 : 32.0;
        return (lines, width, height);
    }

    private static int FindSplitIndex(string text, int maxFirstLine)
    {
        var ideal = Math.Min(maxFirstLine, text.Length / 2 + 2);
        var best = -1;
        var bestDist = int.MaxValue;
        for (var i = 10; i <= Math.Min(maxFirstLine, text.Length - 4); i++)
        {
            if (text[i] is ' ' or ',' or '/' or '-')
            {
                var split = text[i] == ' ' ? i : i + 1;
                var dist = Math.Abs(split - ideal);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = split;
                }
            }
        }
        return best;
    }

    private static string Write(string title, List<Participant> participants, List<Row> rows)
    {
        var widths = participants
            .Select(p => Math.Max(MinBoxWidth, Math.Ceiling(p.Label.Length * CharWidth) + 28.0))
            .ToList();

        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < participants.Count; i++)
            index[participants[i].Id] = i;

        var gapAfter = new double[Math.Max(0, participants.Count - 1)];
        Array.Fill(gapAfter, BoxGap);

        var x = new double[participants.Count];
        var center = new double[participants.Count];
        void RecomputeX()
        {
            var cur = Padding;
            for (var i = 0; i < participants.Count; i++)
            {
                x[i] = cur;
                center[i] = cur + widths[i] / 2.0;
                if (i < gapAfter.Length)
                    cur += widths[i] + gapAfter[i];
            }
        }

        RecomputeX();

        // Expand horizontal participant gaps dynamically so no message pill overflows its span.
        var spanMessages = rows
            .OfType<MessageRow>()
            .Where(mr => index.ContainsKey(mr.Message.From) && index.ContainsKey(mr.Message.To))
            .Select(mr =>
            {
                var a = index[mr.Message.From];
                var b = index[mr.Message.To];
                var (_, pillW, _) = MeasureMessage(mr.Message.Text);
                return (Lo: Math.Min(a, b), Hi: Math.Max(a, b), PillW: pillW);
            })
            .Where(s => s.Lo < s.Hi && s.PillW > 0)
            .OrderBy(s => s.Hi - s.Lo)
            .ToList();

        for (var pass = 0; pass < 2; pass++)
        {
            foreach (var (lo, hi, pillW) in spanMessages)
            {
                var requiredSpan = pillW + 32.0;
                var actualSpan = center[hi] - center[lo];
                if (actualSpan < requiredSpan)
                {
                    var deficitPerGap = (requiredSpan - actualSpan) / (hi - lo);
                    for (var g = lo; g < hi; g++)
                        gapAfter[g] += deficitPerGap;
                    RecomputeX();
                }
            }
        }

        // Compute required right edge accounting for self-messages & notes.
        var maxRight = x[^1] + widths[^1] + Padding;
        foreach (var row in rows)
        {
            if (row is MessageRow(var m) &&
                index.TryGetValue(m.From, out var f) &&
                index.TryGetValue(m.To, out var t) &&
                f == t)
            {
                var (_, pillW, _) = MeasureMessage(m.Text);
                maxRight = Math.Max(maxRight, center[f] + 46.0 + pillW + 12.0 + Padding);
            }
        }
        var canvasWidth = Math.Max(220.0, maxRight);

        var headerTop = Padding;
        var headerBottom = headerTop + BoxHeight;
        var rowY = new double[rows.Count];
        var cursorY = headerBottom + 36.0;
        for (var i = 0; i < rows.Count; i++)
        {
            var extraH = rows[i] is MessageRow(var m) && MeasureMessage(m.Text).Lines.Count > 1 ? 10.0 : 0.0;
            rowY[i] = cursorY;
            cursorY += (rows[i] is NoteRow ? NoteAdvance : MessageHeight) + extraH;
        }

        var showBottomBoxes = rows.Count >= 6;
        var lifelineBottom = cursorY + 12.0;
        var canvasHeight = showBottomBoxes
            ? lifelineBottom + BoxHeight + Padding
            : lifelineBottom + Padding;

        var svg = new StringBuilder();
        svg.Append("<div class=\"svg-diagram\">");
        svg.Append(
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {F(canvasWidth)} {F(canvasHeight)}\" " +
            $"width=\"{F(canvasWidth)}\" height=\"{F(canvasHeight)}\" role=\"img\">");
        svg.Append($"<title>{E(title)}</title>");
        svg.Append("<defs>");
        svg.Append(
            "<marker id=\"arrow-default\" viewBox=\"0 0 10 10\" refX=\"9\" refY=\"5\" " +
            "markerWidth=\"6\" markerHeight=\"6\" orient=\"auto-start-reverse\">" +
            "<path class=\"svg-arrow-default\" d=\"M 0 1 L 10 5 L 0 9 z\" fill=\"#475569\"/></marker>");
        svg.Append("</defs>");

        // Lifelines.
        for (var i = 0; i < participants.Count; i++)
        {
            svg.Append(
                $"<line class=\"svg-seq-lifeline\" x1=\"{F(center[i])}\" y1=\"{F(headerBottom)}\" " +
                $"x2=\"{F(center[i])}\" y2=\"{F(lifelineBottom)}\" " +
                "stroke=\"#94a3b8\" stroke-width=\"1.2\" stroke-dasharray=\"4 4\"/>");
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

        // Top participant boxes (+ bottom repeaters for tall diagrams).
        WriteParticipants(svg, participants, x, widths, center, headerTop);
        if (showBottomBoxes)
            WriteParticipants(svg, participants, x, widths, center, lifelineBottom);

        svg.Append("</svg></div>");
        return svg.ToString();
    }

    private static void WriteParticipants(
        StringBuilder svg,
        List<Participant> participants,
        double[] x,
        List<double> widths,
        double[] center,
        double topY)
    {
        for (var i = 0; i < participants.Count; i++)
        {
            svg.Append($"<g class=\"seq-participant\" data-id=\"{E(participants[i].Id)}\">");
            svg.Append(
                $"<rect class=\"svg-seq-box\" x=\"{F(x[i])}\" y=\"{F(topY)}\" width=\"{F(widths[i])}\" height=\"{F(BoxHeight)}\" " +
                "rx=\"8\" fill=\"#e0e7ff\" stroke=\"#4f46e5\" stroke-width=\"1.5\"/>");
            svg.Append(
                $"<text class=\"svg-seq-text\" x=\"{F(center[i])}\" y=\"{F(topY + BoxHeight / 2.0 + 4.5)}\" text-anchor=\"middle\" " +
                "fill=\"#1e293b\" font-family=\"system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif\" " +
                $"font-size=\"13\" font-weight=\"600\">{E(participants[i].Label)}</text>");
            svg.Append("</g>");
        }
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

        var dash = message.Dashed ? " stroke-dasharray=\"5 3\"" : "";
        svg.Append($"<g class=\"seq-message\" data-from=\"{E(message.From)}\" data-to=\"{E(message.To)}\">");
        if (!string.IsNullOrWhiteSpace(message.Text))
            svg.Append($"<title>{E(message.Text)}</title>");

        var (lines, pillW, pillH) = MeasureMessage(message.Text);

        if (from == to)
        {
            var cx = center[from];
            var loopX = cx + 42.0;
            svg.Append(
                $"<path class=\"svg-seq-msg-line\" d=\"M {F(cx)} {F(y - 10)} L {F(loopX)} {F(y - 10)} " +
                $"L {F(loopX)} {F(y + 10)} L {F(cx + 2)} {F(y + 10)}\" fill=\"none\" " +
                $"stroke=\"#475569\" stroke-width=\"1.5\"{dash} marker-end=\"url(#arrow-default)\"/>");
            if (lines.Count > 0)
            {
                var bgX = loopX + 8.0;
                var bgY = y - pillH / 2.0;
                WriteMessagePill(svg, lines, bgX + pillW / 2.0, bgY, pillW, pillH);
            }
        }
        else
        {
            var x1 = center[from];
            var x2 = center[to];
            svg.Append(
                $"<line class=\"svg-seq-msg-line\" x1=\"{F(x1)}\" y1=\"{F(y)}\" x2=\"{F(x2)}\" y2=\"{F(y)}\" " +
                $"stroke=\"#475569\" stroke-width=\"1.5\"{dash} marker-end=\"url(#arrow-default)\"/>");

            if (lines.Count > 0)
            {
                var midX = (x1 + x2) / 2.0;
                var bgY = y - 6.0 - pillH;
                WriteMessagePill(svg, lines, midX, bgY, pillW, pillH);
            }
        }

        svg.Append("</g>");
    }

    private static void WriteMessagePill(
        StringBuilder svg,
        IReadOnlyList<string> lines,
        double midX,
        double topY,
        double pillW,
        double pillH)
    {
        var rectX = midX - pillW / 2.0;
        svg.Append(
            $"<rect class=\"svg-seq-msg-bg\" x=\"{F(rectX)}\" y=\"{F(topY)}\" " +
            $"width=\"{F(pillW)}\" height=\"{F(pillH)}\" rx=\"4\" " +
            "fill=\"#ffffff\" fill-opacity=\"0.96\" stroke=\"#cbd5e1\" stroke-width=\"1\"/>");

        if (lines.Count == 1)
        {
            svg.Append(
                $"<text class=\"svg-seq-msg-text\" x=\"{F(midX)}\" y=\"{F(topY + pillH / 2.0 + 4.0)}\" text-anchor=\"middle\" " +
                "fill=\"#334155\" font-family=\"system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif\" " +
                $"font-size=\"11\">{E(lines[0])}</text>");
        }
        else
        {
            svg.Append(
                $"<text class=\"svg-seq-msg-text\" x=\"{F(midX)}\" y=\"{F(topY + pillH / 2.0 - 3.5)}\" text-anchor=\"middle\" " +
                "fill=\"#334155\" font-family=\"system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif\" " +
                $"font-size=\"11\">{E(lines[0])}</text>");
            svg.Append(
                $"<text class=\"svg-seq-msg-text\" x=\"{F(midX)}\" y=\"{F(topY + pillH / 2.0 + 10.5)}\" text-anchor=\"middle\" " +
                "fill=\"#334155\" font-family=\"system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif\" " +
                $"font-size=\"11\">{E(lines[1])}</text>");
        }
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
        var spanCenter = (left + right) / 2.0;
        var minW = Math.Max(160.0, Math.Ceiling(note.Text.Length * 6.8 + 24.0));
        var boxW = Math.Max(Math.Abs(right - left) + 120.0, minW);
        var x0 = Math.Clamp(spanCenter - boxW / 2.0, 8.0, Math.Max(8.0, canvasWidth - 8.0 - boxW));
        var x1 = Math.Min(canvasWidth - 8.0, x0 + boxW);
        svg.Append("<g class=\"seq-note\">");
        svg.Append(
            $"<rect class=\"svg-seq-note-box\" x=\"{F(x0)}\" y=\"{F(y)}\" width=\"{F(x1 - x0)}\" height=\"{F(NoteHeight)}\" rx=\"5\" " +
            "fill=\"#fef9c3\" stroke=\"#ca8a04\" stroke-width=\"1\"/>");
        svg.Append(
            $"<text class=\"svg-seq-note-text\" x=\"{F((x0 + x1) / 2.0)}\" y=\"{F(y + NoteHeight / 2.0 + 4.0)}\" text-anchor=\"middle\" " +
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
        svg.Append($"<title>{E(title)}</title>");
        svg.Append("</svg></div>");
        return svg.ToString();
    }
}
