using System.Text;

namespace CqrsLens;

/// <summary>ANSI 24-bit TrueColor primitives and horizontal gradient rendering.</summary>
internal static class AnsiColor
{
    internal static string Colorize(string text, byte r, byte g, byte b, bool bold = false)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return bold
            ? $"\x1b[1;38;2;{r};{g};{b}m{text}\x1b[0m"
            : $"\x1b[38;2;{r};{g};{b}m{text}\x1b[0m";
    }

    internal static void AppendGradientLine(
        StringBuilder sb,
        string line,
        (byte R, byte G, byte B)[] stops,
        double offsetBias = 0.0)
    {
        if (string.IsNullOrEmpty(line))
        {
            sb.AppendLine();
            return;
        }

        var len = Math.Max(1, line.Length - 1);
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == ' ')
            {
                sb.Append(' ');
                continue;
            }

            var t = Math.Clamp((double)i / len * 0.75 + offsetBias, 0.0, 1.0);
            var (r, g, b) = InterpolateStops(stops, t);
            sb.Append($"\x1b[38;2;{r};{g};{b}m{ch}");
        }

        sb.AppendLine("\x1b[0m");
    }

    private static (byte R, byte G, byte B) InterpolateStops(
        (byte R, byte G, byte B)[] stops,
        double t)
    {
        if (stops.Length == 0) return (255, 255, 255);
        if (stops.Length == 1 || t <= 0.0) return stops[0];
        if (t >= 1.0) return stops[^1];

        var scaled = t * (stops.Length - 1);
        var idx = Math.Min((int)Math.Floor(scaled), stops.Length - 2);
        var localT = scaled - idx;

        var a = stops[idx];
        var b = stops[idx + 1];
        var r = (byte)Math.Round(a.R + (b.R - a.R) * localT);
        var g = (byte)Math.Round(a.G + (b.G - a.G) * localT);
        var bl = (byte)Math.Round(a.B + (b.B - a.B) * localT);
        return (r, g, bl);
    }
}
