using System.Text.RegularExpressions;
using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// Console presentation coverage: the banner must degrade cleanly with terminal
/// width, and the summary card must stay a rectangle at every size.
/// </summary>
public sealed class ConsoleArtworkTests
{
    private const string Version = "1.0.0";

    [Theory]
    [InlineData(40)]
    [InlineData(61)]
    [InlineData(80)]
    [InlineData(100)]
    [InlineData(120)]
    [InlineData(200)]
    public void Banner_NeverExceedsTheTerminalWidth(int width)
    {
        var banner = ConsoleArtwork.RenderLogoBanner(Version, useColor: false, width);

        foreach (var line in Lines(banner))
            Assert.True(line.Length <= width, $"'{line}' is {line.Length} wide at width {width}.");
    }

    [Theory]
    [InlineData(40)]
    [InlineData(55)]
    [InlineData(61)]
    [InlineData(87)]
    [InlineData(100)]
    [InlineData(120)]
    public void Banner_WithColor_VisibleWidthStillFitsTheTerminal(int width)
    {
        var banner = ConsoleArtwork.RenderLogoBanner(Version, useColor: true, width);

        foreach (var line in Lines(banner))
        {
            var visible = Ansi().Replace(line, string.Empty).Length;
            Assert.True(visible <= width, $"'{line}' is {visible} wide at width {width}.");
        }
    }

    [Fact]
    public void Banner_WithoutColor_HasNoAnsiEscapes()
    {
        var banner = ConsoleArtwork.RenderLogoBanner(Version, useColor: false, width: 120);

        Assert.DoesNotContain('\u001b', banner);
    }

    [Fact]
    public void Banner_WithColor_EmitsTrueColorSequences()
    {
        var banner = ConsoleArtwork.RenderLogoBanner(Version, useColor: true, width: 120);

        Assert.Contains("\u001b[38;2;", banner, StringComparison.Ordinal);
    }

    [Fact]
    public void Banner_WideTerminal_RendersTheSteppedPixelWordmark()
    {
        var banner = ConsoleArtwork.RenderLogoBanner(Version, useColor: false, width: 120);

        // Half-block packing of the brand face: full, upper and lower cells.
        Assert.Contains('█', banner);
        Assert.Contains('▀', banner);
        Assert.Contains("A lens for the CQRS codebase you already have.", banner, StringComparison.Ordinal);
    }

    [Fact]
    public void Banner_NarrowTerminal_FallsBackToTheTextLockup()
    {
        var banner = ConsoleArtwork.RenderLogoBanner(Version, useColor: false, width: 40);

        Assert.Contains("NOCTUA", banner, StringComparison.Ordinal);
        Assert.DoesNotContain('█', banner);
    }

    [Fact]
    public void Banner_KeepsTheVersionInTheTagline()
    {
        var banner = ConsoleArtwork.RenderLogoBanner(Version, useColor: false, width: 120);

        Assert.Contains($"v{Version}", banner, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(40)]
    [InlineData(60)]
    [InlineData(80)]
    [InlineData(120)]
    public void SummaryCard_StaysRectangularAndFitsTheTerminal(int width)
    {
        var card = ConsoleArtwork.RenderSummaryCard(Result(), "ARCHITECTURE.md", useColor: false, width);
        var lines = Lines(card);

        Assert.True(lines.Length > 4);

        var cardWidth = lines[0].Length;
        Assert.True(cardWidth <= width, $"card is {cardWidth} wide at width {width}.");

        foreach (var line in lines)
            Assert.Equal(cardWidth, line.Length);
    }

    [Fact]
    public void SummaryCard_ReportsPassingStatusWhenThereAreNoChecks()
    {
        var card = ConsoleArtwork.RenderSummaryCard(Result(), "ARCHITECTURE.md", useColor: false, 120);

        Assert.Contains("Passed · no findings", card, StringComparison.Ordinal);
        Assert.Contains("✔", card, StringComparison.Ordinal);
    }

    [Fact]
    public void SummaryCard_WithoutColor_HasNoAnsiEscapes()
    {
        var card = ConsoleArtwork.RenderSummaryCard(Result(), "ARCHITECTURE.md", useColor: false, 120);

        Assert.DoesNotContain('\u001b', card);
    }

    [Theory]
    [InlineData(40)]
    [InlineData(61)]
    [InlineData(80)]
    [InlineData(120)]
    public void AsciiBanner_StaysPureAsciiAndFitsTheTerminal(int width)
    {
        var banner = ConsoleArtwork.RenderLogoBanner(Version, useColor: false, width, ascii: true);

        AssertPureAscii(banner);
        Assert.Contains("* NOCTUA", banner, StringComparison.Ordinal);

        foreach (var line in Lines(banner))
            Assert.True(line.Length <= width, $"'{line}' is {line.Length} wide at width {width}.");
    }

    [Fact]
    public void AsciiBanner_KeepsTheTaglineAndVersionWhenThereIsRoom()
    {
        var banner = ConsoleArtwork.RenderLogoBanner(Version, useColor: false, width: 120, ascii: true);

        Assert.Contains("A lens for the CQRS codebase you already have.", banner, StringComparison.Ordinal);
        Assert.Contains($"- v{Version}", banner, StringComparison.Ordinal);
    }

    [Fact]
    public void AsciiBanner_DropsTheBlockArtEvenOnAWideTerminal()
    {
        var banner = ConsoleArtwork.RenderLogoBanner(Version, useColor: false, width: 200, ascii: true);

        Assert.DoesNotContain('█', banner);
        Assert.DoesNotContain('▀', banner);
        Assert.DoesNotContain('▄', banner);
    }

    [Theory]
    [InlineData(40)]
    [InlineData(60)]
    [InlineData(80)]
    [InlineData(120)]
    public void AsciiCard_StaysRectangularAndPureAscii(int width)
    {
        var card = ConsoleArtwork.RenderSummaryCard(Result(), "ARCHITECTURE.md", useColor: false, width, ascii: true);
        var lines = Lines(card);

        AssertPureAscii(card);

        var cardWidth = lines[0].Length;
        Assert.True(cardWidth <= width, $"card is {cardWidth} wide at width {width}.");

        foreach (var line in lines)
            Assert.Equal(cardWidth, line.Length);
    }

    [Fact]
    public void AsciiProgressBar_UsesSingleWidthCells()
    {
        var progress = new ScanProgress("Analyzing C# shapes", 2, 4, "text");

        var bar = ConsoleArtwork.FormatProgressBar(progress, useColor: false, barWidth: 20, ascii: true);

        AssertPureAscii(bar);
        Assert.Contains("[##########----------]", bar, StringComparison.Ordinal);
    }

    [Fact]
    public void AsciiProgressBar_ReplacesTheDetailSeparator()
    {
        var progress = new ScanProgress("Analyzing C# shapes", 2, 4, "157 file(s) · text");

        var bar = ConsoleArtwork.FormatProgressBar(progress, useColor: false, barWidth: 20, ascii: true);

        AssertPureAscii(bar);
        Assert.Contains("157 file(s) - text", bar, StringComparison.Ordinal);
    }

    [Fact]
    public void UnicodeMode_StillDrawsTheBlockArt()
    {
        var banner = ConsoleArtwork.RenderLogoBanner(Version, useColor: false, width: 120, ascii: false);

        Assert.Contains('█', banner);
    }

    private static AnalysisResult Result()
        => new()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "SampleShop"),
            ScannerName = "text",
            ElapsedMilliseconds = 247,
        };

    /// <summary>
    /// Guards the point of <c>--ascii</c>: a CJK terminal draws these two cells
    /// wide, so any non-ASCII code point would shear the layout apart.
    /// </summary>
    private static void AssertPureAscii(string text)
    {
        var offender = text.FirstOrDefault(ch => ch >= 128);

        Assert.True(
            offender == '\0',
            $"non-ASCII character U+{(int)offender:X4} '{offender}' in:\n{text}");
    }

    private static string[] Lines(string text)
        => text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');

    private static Regex Ansi()
        => new("\u001b\\[[0-9;]*m", RegexOptions.Compiled);
}
