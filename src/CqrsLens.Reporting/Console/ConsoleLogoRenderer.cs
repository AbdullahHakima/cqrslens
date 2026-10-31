using System.Text;

namespace CqrsLens;

/// <summary>Renders the CqrsLens wordmark and its brand gradient.</summary>
internal static class ConsoleLogoRenderer
{
    private static readonly string[] LogoLines =
    {
        @"   ╭──────╮     ██████╗  ██████╗ ██████╗ ███████╗██╗     ███████╗███╗   ██╗███████╗",
        @"  ╱  ╭──╮  ╲   ██╔════╝ ██╔═══██╗██╔══██╗██╔════╝██║     ██╔════╝████╗  ██║██╔════╝",
        @" │  │ ◆ │  │   ██║      ██║   ██║██████╔╝███████╗██║     █████╗  ██╔██╗ ██║███████╗",
        @"  ╲  ╰──╯  ╱   ██║      ██║▄▄ ██║██╔══██╗╚════██║██║     ██╔══╝  ██║╚██╗██║╚════██║",
        @"   ╰──┬───╯    ╚██████╗ ╚██████╔╝██║  ██║███████║███████╗███████╗██║ ╚████║███████║",
        @"      ╰──►      ╚═════╝  ╚══▀▀═╝ ╚═╝  ╚═╝╚══════╝╚══════╝╚══════╝╚═╝  ╚═══╝╚══════╝",
    };

    private static readonly (byte R, byte G, byte B)[] BrandGradient =
    {
        (56, 189, 248),  // Sky cyan (#38bdf8)
        (99, 102, 241),  // Indigo (#6366f1)
        (192, 132, 252), // Violet (#c084fc)
    };

    internal static string Render(string version, bool useColor)
    {
        var sb = new StringBuilder();
        for (var row = 0; row < LogoLines.Length; row++)
        {
            var line = LogoLines[row];
            if (useColor)
            {
                var rowBias = LogoLines.Length <= 1 ? 0.0 : (double)row / (LogoLines.Length - 1) * 0.25;
                AnsiColor.AppendGradientLine(sb, line, BrandGradient, rowBias);
            }
            else
            {
                sb.AppendLine(line);
            }
        }

        var tagline = $"      ── ◆ Static CQRS & Clean Architecture Lens · v{version} ◆ ──";
        if (useColor)
            sb.AppendLine(AnsiColor.Colorize(tagline, 148, 163, 184, bold: true));
        else
            sb.AppendLine(tagline);

        return sb.ToString();
    }
}
