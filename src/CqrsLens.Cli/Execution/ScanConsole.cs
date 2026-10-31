namespace CqrsLens;

/// <summary>
/// Owns console presentation decisions for a scan: banner/watermark visibility,
/// the interactive progress line and the closing summary card.
/// </summary>
internal sealed class ScanConsole
{
    private readonly CliOptions _options;
    private readonly TextWriter _stdout;
    private readonly bool _useColor;
    private readonly bool _showBanner;
    private readonly bool _isInteractive;
    private int _lastProgressLength;

    public ScanConsole(CliOptions options, TextWriter stdout)
    {
        _options = options;
        _stdout = stdout;
        StreamsSlice = options.SliceFilter is not null && options.Output is null && options.Format is null;
        _useColor = ConsoleArtwork.ShouldUseColor(stdout, options.Color);
        _showBanner = !StreamsSlice &&
                      ConsoleArtwork.ShouldShowBanner(stdout, options.Banner, options.GitHubAnnotations);
        _isInteractive = !StreamsSlice &&
                         ReferenceEquals(stdout, Console.Out) &&
                         !Console.IsOutputRedirected &&
                         !options.GitHubAnnotations;
    }

    /// <summary>True when <c>--slice</c> writes its report to stdout instead of a file.</summary>
    public bool StreamsSlice { get; }

    public void WriteBanner(string? watermark)
    {
        if (_showBanner)
        {
            _stdout.WriteLine(ConsoleArtwork.RenderLogoBanner(ToolInfo.Version, _useColor));
            if (!string.IsNullOrWhiteSpace(watermark))
            {
                _stdout.WriteLine(ConsoleArtwork.RenderWatermarkStamp(watermark, _useColor));
                _stdout.WriteLine();
            }

            return;
        }

        if (!string.IsNullOrWhiteSpace(watermark) &&
            _options.WatermarkSpecified &&
            _options.Banner != false &&
            !_options.GitHubAnnotations)
        {
            _stdout.WriteLine(ConsoleArtwork.RenderWatermarkStamp(watermark, _useColor));
            _stdout.WriteLine();
        }
    }

    public Action<ScanProgress>? CreateProgressCallback()
    {
        if (!_isInteractive)
            return null;

        return progress =>
        {
            var line = ConsoleArtwork.FormatProgressBar(progress, _useColor);
            var padding = Math.Max(0, _lastProgressLength - line.Length);
            _lastProgressLength = line.Length;
            _stdout.Write("\r" + line + new string(' ', padding));
            _stdout.Flush();
        };
    }

    public void ClearProgress()
    {
        if (!_isInteractive || _lastProgressLength == 0)
            return;

        _stdout.Write("\r" + new string(' ', _lastProgressLength) + "\r");
        _stdout.Flush();
    }

    public void WriteSummaryCard(AnalysisResult result, string reportPath)
    {
        if (!_showBanner)
            return;

        _stdout.WriteLine(ConsoleArtwork.RenderSummaryCard(result, reportPath, _useColor));
        _stdout.WriteLine();
    }
}
