using System.Text.RegularExpressions;
using System.Xml.Linq;
using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// CP1 coverage for F3 docs export (R-DOCS, R-DIAGRAMS): layout, determinism,
/// text == roslyn parity, sorted ordering, zero timestamp leakage, standalone SVGs
/// matching HTML-embedded SVGs, and no unrequested CWD report file.
/// </summary>
public sealed class F3DocsExportTests : IDisposable
{
    private readonly string _root;

    public F3DocsExportTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cqrslens-f3-docs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private static string FindGoldenCorpus()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "fixtures", "GoldenCorpus");
            if (Directory.Exists(candidate))
                return candidate;
        }

        throw new InvalidOperationException("Could not locate tests/fixtures/GoldenCorpus.");
    }

    private static (int Code, string Stdout, string Stderr) RunCli(params string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var code = CliRunner.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private static Dictionary<string, byte[]> SnapshotTree(string docsDir)
    {
        var files = Directory
            .EnumerateFiles(docsDir, "*", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal);
        return files.ToDictionary(
            p => Path.GetRelativePath(docsDir, p).Replace('\\', '/'),
            File.ReadAllBytes,
            StringComparer.Ordinal);
    }

    [Fact]
    public void ExportDocs_WritesExpectedLayout_AndProducesDeterministicBytesWithAndWithoutGeneratedAt()
    {
        var corpus = FindGoldenCorpus();
        var pinnedA = Path.Combine(_root, "pinned-a");
        var pinnedB = Path.Combine(_root, "pinned-b");
        var unpinnedA = Path.Combine(_root, "unpinned-a");
        var unpinnedB = Path.Combine(_root, "unpinned-b");

        var r1 = RunCli(corpus, "--export-docs", pinnedA, "--generated-at", "2026-01-01T00:00:00Z", "--no-banner", "--no-color");
        var r2 = RunCli(corpus, "--export-docs", pinnedB, "--generated-at", "2026-01-01T00:00:00Z", "--no-banner", "--no-color");
        Assert.Equal(ExitCodes.Success, r1.Code);
        Assert.Equal(ExitCodes.Success, r2.Code);

        var snapA = SnapshotTree(pinnedA);
        var snapB = SnapshotTree(pinnedB);
        Assert.Equal(snapA.Keys.OrderBy(k => k, StringComparer.Ordinal), snapB.Keys.OrderBy(k => k, StringComparer.Ordinal));
        foreach (var key in snapA.Keys)
            Assert.Equal(snapA[key], snapB[key]);

        foreach (var expectedPage in new[]
                 {
                     "01-context.md",
                     "02-building-blocks.md",
                     "03-runtime.md",
                     "04-data-model.md",
                     "05-cross-cutting.md",
                     "06-findings.md",
                 })
        {
            Assert.Contains(expectedPage, snapA.Keys);
            var text = File.ReadAllText(Path.Combine(pinnedA, expectedPage));
            Assert.Contains($"> Source: generated from the model, Noctua {ToolInfo.Version}, 2026-01-01T00:00:00Z", text);
        }

        // Unpinned runs omit wall-clock timestamps and stay byte-identical across runs.
        var u1 = RunCli(corpus, "--export-docs", unpinnedA, "--no-banner", "--no-color");
        var u2 = RunCli(corpus, "--export-docs", unpinnedB, "--no-banner", "--no-color");
        Assert.Equal(ExitCodes.Success, u1.Code);
        Assert.Equal(ExitCodes.Success, u2.Code);
        var snapU1 = SnapshotTree(unpinnedA);
        var snapU2 = SnapshotTree(unpinnedB);
        Assert.Equal(snapU1.Keys, snapU2.Keys);
        foreach (var key in snapU1.Keys)
        {
            Assert.Equal(snapU1[key], snapU2[key]);
            if (key.EndsWith(".md", StringComparison.Ordinal))
                Assert.Contains("unpinned", File.ReadAllText(Path.Combine(unpinnedA, key)));
        }
    }

    [Fact]
    public void ExportDocs_TextAndRoslynModes_ProduceIdenticalDocsTrees()
    {
        var corpus = FindGoldenCorpus();
        var textDir = Path.Combine(_root, "docs-text");
        var roslynDir = Path.Combine(_root, "docs-roslyn");

        var textRun = RunCli(corpus, "--analysis", "text", "--export-docs", textDir, "--generated-at", "2026-01-01T00:00:00Z", "--no-banner", "--no-color");
        var roslynRun = RunCli(corpus, "--analysis", "roslyn", "--export-docs", roslynDir, "--generated-at", "2026-01-01T00:00:00Z", "--no-banner", "--no-color");
        Assert.Equal(ExitCodes.Success, textRun.Code);
        Assert.Equal(ExitCodes.Success, roslynRun.Code);

        var textSnap = SnapshotTree(textDir);
        var roslynSnap = SnapshotTree(roslynDir);
        Assert.Equal(textSnap.Keys.OrderBy(k => k, StringComparer.Ordinal), roslynSnap.Keys.OrderBy(k => k, StringComparer.Ordinal));

        foreach (var key in textSnap.Keys)
        {
            Assert.Equal(textSnap[key], roslynSnap[key]);
        }
    }

    [Fact]
    public void ExportDocs_DiagramsMatchHtmlEmbeddedSvgsAndSvgExporterByteForByte()
    {
        var corpus = FindGoldenCorpus();
        var result = Analyzer.Analyze(corpus, AnalysisMode.Text, generatedAt: DateTimeOffset.Parse("2026-01-01T00:00:00Z"));

        var docsDir = Path.Combine(_root, "docs");
        var svgOnlyDir = Path.Combine(_root, "svg-only");
        DocsExporter.Export(result, docsDir);
        SvgExporter.Export(result, svgOnlyDir);

        var docsSvgs = Directory.EnumerateFiles(Path.Combine(docsDir, "diagrams"), "*.svg")
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToDictionary(p => Path.GetFileName(p)!, File.ReadAllBytes, StringComparer.Ordinal);
        var exportSvgs = Directory.EnumerateFiles(svgOnlyDir, "*.svg")
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToDictionary(p => Path.GetFileName(p)!, File.ReadAllBytes, StringComparer.Ordinal);

        Assert.Equal(exportSvgs.Keys, docsSvgs.Keys);
        foreach (var name in docsSvgs.Keys)
        {
            Assert.Equal(exportSvgs[name], docsSvgs[name]);
            var doc = XDocument.Load(Path.Combine(docsDir, "diagrams", name));
            Assert.Equal("svg", doc.Root!.Name.LocalName);
        }

        // Verify every standalone <svg>...</svg> element appears verbatim inside the zero-JS HTML report.
        var html = HtmlReportGenerator.Generate(result, DiagramRenderMode.Svg);
        var htmlSvgs = Regex.Matches(html, @"<svg\b[^>]*>.*?</svg>", RegexOptions.Singleline | RegexOptions.CultureInvariant)
            .Select(m => m.Value)
            .ToList();

        Assert.Equal(docsSvgs.Count, htmlSvgs.Count);
        foreach (var name in docsSvgs.Keys)
        {
            var standalone = File.ReadAllText(Path.Combine(docsDir, "diagrams", name));
            var start = standalone.IndexOf("<svg", StringComparison.Ordinal);
            var end = standalone.LastIndexOf("</svg>", StringComparison.Ordinal) + "</svg>".Length;
            var svgPayload = standalone[start..end];
            Assert.Contains(svgPayload, htmlSvgs);
        }
    }

    [Fact]
    public void ExportDocs_OrdersIdentitiesDeterministically_AndCarriesHonestyLabelsWithoutNarrative()
    {
        var corpus = FindGoldenCorpus();
        var result = Analyzer.Analyze(corpus, AnalysisMode.Text, generatedAt: DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var docsDir = Path.Combine(_root, "ordered-docs");
        DocsExporter.Export(result, docsDir);

        var contextMd = File.ReadAllText(Path.Combine(docsDir, "01-context.md"));
        var buildingBlocksMd = File.ReadAllText(Path.Combine(docsDir, "02-building-blocks.md"));
        var runtimeMd = File.ReadAllText(Path.Combine(docsDir, "03-runtime.md"));
        var crossCuttingMd = File.ReadAllText(Path.Combine(docsDir, "05-cross-cutting.md"));
        var findingsMd = File.ReadAllText(Path.Combine(docsDir, "06-findings.md"));

        // Honesty labels present where model cannot prove runtime claims
        Assert.Contains("guessed (inferred)", contextMd);
        Assert.Contains("guessed (inferred)", buildingBlocksMd);
        Assert.Contains("Execution order is not guaranteed", crossCuttingMd);
        Assert.Contains("execution order is not guaranteed", runtimeMd);
        Assert.DoesNotContain("Declared Intent", buildingBlocksMd);
        Assert.DoesNotContain("Intent Verification Findings", findingsMd);

        // Component inventory rows appear in total sorted ComponentId order
        var inventoryStart = buildingBlocksMd.IndexOf("## Component Inventory", StringComparison.Ordinal);
        Assert.True(inventoryStart >= 0);
        var componentIds = buildingBlocksMd[inventoryStart..]
            .Split('\n')
            .Where(l => l.StartsWith("| `", StringComparison.Ordinal) && l.Contains("::", StringComparison.Ordinal))
            .Select(l => l.Split(" | ")[0][2..].Trim('`', ' '))
            .ToList();
        Assert.Equal(result.Spine.Components.Count, componentIds.Count);
        Assert.Equal(
            componentIds.OrderBy(id => id, StringComparer.Ordinal).ToList(),
            componentIds);
    }

    [Fact]
    public void ExportDocs_DoesNotWriteDefaultArchitectureMd_AndRejectsFileTarget()
    {
        var corpus = FindGoldenCorpus();
        var docsDir = Path.Combine(_root, "out-docs");
        var previousDir = Directory.GetCurrentDirectory();
        var scratchCwd = Path.Combine(_root, "cwd");
        Directory.CreateDirectory(scratchCwd);

        try
        {
            Directory.SetCurrentDirectory(scratchCwd);
            var ok = RunCli(corpus, "--export-docs", docsDir, "--no-banner", "--no-color");
            Assert.Equal(ExitCodes.Success, ok.Code);
            Assert.False(File.Exists(Path.Combine(scratchCwd, "ARCHITECTURE.md")));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDir);
        }

        var filePath = Path.Combine(_root, "not-a-dir");
        File.WriteAllText(filePath, "occupied");
        var fail = RunCli(corpus, "--export-docs", filePath, "--no-banner", "--no-color");
        Assert.Equal(ExitCodes.Usage, fail.Code);
        Assert.Contains("--export-docs path is a file", fail.Stderr);
    }
}
