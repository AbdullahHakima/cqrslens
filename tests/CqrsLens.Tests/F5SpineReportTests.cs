using System.Text.Json;
using System.Text.RegularExpressions;
using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// Stage F5 tests proving human-readable Architecture Spine rendering in Markdown,
/// HTML (Mermaid and SVG), and <c>02-building-blocks.md</c> docs export, including
/// placement, determinism, text/Roslyn parity, D7 non-duplication, and JSON reconciliation.
/// </summary>
public sealed class F5SpineReportTests : IDisposable
{
    private readonly string _tempRoot;

    public F5SpineReportTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "cqrslens-f5-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { /* best effort */ }
    }

    [Theory]
    [InlineData("samples/SampleShop", AnalysisMode.Text)]
    [InlineData("samples/SampleShop", AnalysisMode.Roslyn)]
    [InlineData("tests/fixtures/GoldenCorpus", AnalysisMode.Text)]
    [InlineData("tests/fixtures/GoldenCorpus", AnalysisMode.Roslyn)]
    public void ArchitectureSpine_Placement_IsAfterLayersAndBeforeCqrsMap_InMarkdownAndHtml(
        string relativeCorpusPath,
        AnalysisMode mode)
    {
        var corpusPath = ResolveRepoPath(relativeCorpusPath);
        var result = Analyzer.Analyze(
            corpusPath,
            mode,
            new AnalysisOptions { GeneratedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z") });

        var md = ReportGenerator.Generate(result);
        var mdLayers = md.IndexOf("## Layers", StringComparison.Ordinal);
        var mdSpine = md.IndexOf("## Architecture Spine", StringComparison.Ordinal);
        var mdCqrs = md.IndexOf("## CQRS Map", StringComparison.Ordinal);
        Assert.True(mdLayers >= 0 && mdSpine > mdLayers && mdCqrs > mdSpine,
            $"Expected MD order Layers ({mdLayers}) < Architecture Spine ({mdSpine}) < CQRS Map ({mdCqrs}).");

        foreach (var renderMode in new[] { DiagramRenderMode.Mermaid, DiagramRenderMode.Svg })
        {
            var html = HtmlReportGenerator.Generate(result, renderMode);
            var htmlLayers = html.IndexOf("<h2>Layers</h2>", StringComparison.Ordinal);
            var htmlSpine = html.IndexOf("<h2>Architecture Spine</h2>", StringComparison.Ordinal);
            var htmlCqrs = html.IndexOf("<h2>CQRS Map</h2>", StringComparison.Ordinal);
            Assert.True(htmlLayers >= 0 && htmlSpine > htmlLayers && htmlCqrs > htmlSpine,
                $"Expected HTML ({renderMode}) order Layers ({htmlLayers}) < Architecture Spine ({htmlSpine}) < CQRS Map ({htmlCqrs}).");
        }
    }

    [Fact]
    public void ArchitectureSpine_RendersAllSixSubsections_OrderingTieBreaksCapAndD7NonDuplication()
    {
        var result = BuildRichSyntheticResult();
        var md = ReportGenerator.Generate(result);
        var htmlMermaid = HtmlReportGenerator.Generate(result, DiagramRenderMode.Mermaid);
        var htmlSvg = HtmlReportGenerator.Generate(result, DiagramRenderMode.Svg);

        var mdSpine = ExtractMarkdownSection(md, "## Architecture Spine", "## CQRS Map");
        var htmlSpineMermaid = ExtractHtmlSection(htmlMermaid, "<h2>Architecture Spine</h2>", "<h2>CQRS Map</h2>");
        var htmlSpineSvg = ExtractHtmlSection(htmlSvg, "<h2>Architecture Spine</h2>", "<h2>CQRS Map</h2>");
        Assert.Equal(htmlSpineMermaid, htmlSpineSvg);

        // D2.1 Summary counts match model
        var mdTables = ParseMarkdownTables(mdSpine);
        var htmlTables = ParseHtmlTables(htmlSpineSvg);
        Assert.Equal(6, mdTables.Count);
        Assert.Equal(6, htmlTables.Count);

        for (var i = 0; i < 6; i++)
        {
            Assert.Equal(mdTables[i].Headers, htmlTables[i].Headers);
            Assert.Equal(mdTables[i].Rows.Count, htmlTables[i].Rows.Count);
            for (var r = 0; r < mdTables[i].Rows.Count; r++)
                Assert.Equal(mdTables[i].Rows[r], htmlTables[i].Rows[r]);
        }

        // 1. Summary table
        var summaryRows = mdTables[0].Rows;
        Assert.Equal(["Unique components", "30"], summaryRows[0]);
        Assert.Equal(["Endpoints (spineEndpoints)", "4"], summaryRows[1]);
        Assert.Equal(["Relations", "10"], summaryRows[2]);
        Assert.Equal(["Relation evidence occurrences", "11"], summaryRows[3]);
        Assert.Equal(["Unproven items", "3"], summaryRows[4]);

        // D7: Unproven count appears in Spine summary, while detailed diagnostics live ONLY in Analysis Diagnostics.
        Assert.DoesNotContain("UniqueAmbiguousMarkerForD7Test", mdSpine);
        Assert.DoesNotContain("UniqueUnresolvedContractForD7Test", mdSpine);
        Assert.DoesNotContain("UniqueAmbiguousMarkerForD7Test", htmlSpineSvg);
        Assert.DoesNotContain("UniqueUnresolvedContractForD7Test", htmlSpineSvg);
        Assert.Contains("UniqueAmbiguousMarkerForD7Test", md);
        Assert.Contains("UniqueUnresolvedContractForD7Test", md);
        Assert.Contains("UniqueAmbiguousMarkerForD7Test", htmlSvg);
        Assert.Contains("UniqueUnresolvedContractForD7Test", htmlSvg);

        // 2. Components by Role
        Assert.Contains(mdTables[1].Rows, r => r.SequenceEqual(["handler", "1", "1"]));
        Assert.Contains(mdTables[1].Rows, r => r.SequenceEqual(["validator", "1", "1"]));
        Assert.Contains(mdTables[1].Rows, r => r.SequenceEqual(["service", "30", "30"]));
        Assert.Contains(mdTables[1].Rows, r => r.SequenceEqual(["endpoint (mapping-derived)", "4", "4"]));

        // 3. Relations Summary
        Assert.Equal(["dispatches", "3", "3"], mdTables[2].Rows[0]);
        Assert.Equal(["calls", "7", "8"], mdTables[2].Rows[1]);
        Assert.Equal(["Total", "10", "11"], mdTables[2].Rows[2]);

        // 4. Endpoint Inventory: sorted by Route, then Method, then Project; Dispatches to joined; Inferred marks verb-or-route inference
        var epRows = mdTables[3].Rows;
        Assert.Equal(4, epRows.Count);
        Assert.Equal(["DELETE", "—", "LegacyController.Delete", "App.ApiA", "", "—", "Controllers/Legacy.cs:5"], epRows[0]);
        Assert.Equal(["GET", "/api/alpha", "AlphaController.List", "App.ApiA", "", "yes", "Controllers/Alpha.cs:10"], epRows[1]);
        Assert.Equal(["GET", "/api/alpha", "AlphaBController.List", "App.ApiB", "App.AlphaQuery", "yes", "Controllers/AlphaB.cs:12"], epRows[2]);
        Assert.Equal(["POST", "/api/alpha", "AlphaController.Create", "App.ApiA", "App.CreateCommand, App.SecondCommand", "—", "Controllers/Alpha.cs:20"], epRows[3]);

        // 5. Component Index: sorted by Project, then QualifiedName; multi-role rendered in ComponentRole enum order
        var compRows = mdTables[4].Rows;
        Assert.Equal(30, compRows.Count);
        Assert.Equal("App.ApiA", compRows[0][2]);
        Assert.Equal("App.MultiRoleComponent", compRows[0][0]);
        // Added in order Service (10), Validator (4), Handler (1) -> rendered in ComponentRole enum order: handler, validator, service
        Assert.Equal("handler, validator, service", compRows[0][1]);
        Assert.Equal("Services/MultiRole.cs:8", compRows[0][3]);

        var sortedByProjectThenName = compRows
            .OrderBy(r => r[2], StringComparer.Ordinal)
            .ThenBy(r => r[0], StringComparer.Ordinal)
            .ToList();
        Assert.Equal(sortedByProjectThenName, compRows);

        // 6. Most-Connected Components: capped at top 25, sorted by Total desc, tie-broken by qualified name asc
        var hubRows = mdTables[5].Rows;
        Assert.Equal(25, hubRows.Count);
        Assert.Equal(["App.HubTop", "2", "3", "5"], hubRows[0]);
        Assert.Equal(["App.TieAlpha", "1", "1", "2"], hubRows[1]);
        Assert.Equal(["App.TieBeta", "0", "2", "2"], hubRows[2]);
        Assert.Equal(["App.MultiRoleComponent", "0", "1", "1"], hubRows[3]);
        Assert.Equal(["App.LeafComponent01", "0", "0", "0"], hubRows[4]);
        Assert.Equal(["App.LeafComponent21", "0", "0", "0"], hubRows[24]);
        Assert.DoesNotContain(hubRows, r => r[0] == "App.LeafComponent22");

        // D3: Existing ## API Endpoints section remains present and untouched in shape
        Assert.Contains("## API Endpoints", md);
        Assert.Contains("<h2>API Endpoints</h2>", htmlSvg);
    }

    [Fact]
    public void ArchitectureSpine_Determinism_AndTextRoslynParity_AcrossReportsAndDocs()
    {
        var golden = ResolveRepoPath("tests/fixtures/GoldenCorpus");
        var pinnedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

        var text1 = Analyzer.Analyze(golden, AnalysisMode.Text, new AnalysisOptions { GeneratedAt = pinnedAt });
        var text2 = Analyzer.Analyze(golden, AnalysisMode.Text, new AnalysisOptions { GeneratedAt = pinnedAt });
        var roslyn = Analyzer.Analyze(golden, AnalysisMode.Roslyn, new AnalysisOptions { GeneratedAt = pinnedAt });

        var mdText1 = ReportGenerator.Generate(text1);
        var mdText2 = ReportGenerator.Generate(text2);
        var mdRoslyn = ReportGenerator.Generate(roslyn);
        Assert.Equal(mdText1, mdText2);
        Assert.Equal(
            ExtractMarkdownSection(mdText1, "## Architecture Spine", "## CQRS Map"),
            ExtractMarkdownSection(mdRoslyn, "## Architecture Spine", "## CQRS Map"));

        foreach (var renderMode in new[] { DiagramRenderMode.Mermaid, DiagramRenderMode.Svg })
        {
            var htmlText1 = HtmlReportGenerator.Generate(text1, renderMode);
            var htmlText2 = HtmlReportGenerator.Generate(text2, renderMode);
            var htmlRoslyn = HtmlReportGenerator.Generate(roslyn, renderMode);
            Assert.Equal(htmlText1, htmlText2);
            Assert.Equal(
                ExtractHtmlSection(htmlText1, "<h2>Architecture Spine</h2>", "<h2>CQRS Map</h2>"),
                ExtractHtmlSection(htmlRoslyn, "<h2>Architecture Spine</h2>", "<h2>CQRS Map</h2>"));
        }

        // Docs export determinism + text == roslyn parity + zero timestamp leakage when unpinned
        var docsTextDir = Path.Combine(_tempRoot, "docs-text");
        var docsRoslynDir = Path.Combine(_tempRoot, "docs-roslyn");
        DocsExporter.Export(text1, docsTextDir);
        DocsExporter.Export(roslyn, docsRoslynDir);
        Assert.Equal(
            File.ReadAllBytes(Path.Combine(docsTextDir, "02-building-blocks.md")),
            File.ReadAllBytes(Path.Combine(docsRoslynDir, "02-building-blocks.md")));

        var unpinnedA = Path.Combine(_tempRoot, "docs-unpinned-a");
        var unpinnedB = Path.Combine(_tempRoot, "docs-unpinned-b");
        Assert.Equal(ExitCodes.Success, RunCli(golden, "--export-docs", unpinnedA, "--no-banner", "--no-color").Code);
        Assert.Equal(ExitCodes.Success, RunCli(golden, "--export-docs", unpinnedB, "--no-banner", "--no-color").Code);
        Assert.Equal(
            File.ReadAllBytes(Path.Combine(unpinnedA, "02-building-blocks.md")),
            File.ReadAllBytes(Path.Combine(unpinnedB, "02-building-blocks.md")));
    }

    [Fact]
    public void DocsExport_BuildingBlocksPage_ExtendsWithInventories_AndCheckDocsPassesAndCatchesDrift()
    {
        var golden = ResolveRepoPath("tests/fixtures/GoldenCorpus");
        var docsDir = Path.Combine(_tempRoot, "golden-docs");

        var export = RunCli(golden, "--export-docs", docsDir, "--generated-at", "2026-01-01T00:00:00Z", "--no-banner", "--no-color");
        Assert.Equal(ExitCodes.Success, export.Code);

        var page02 = File.ReadAllText(Path.Combine(docsDir, "02-building-blocks.md"));
        var relRuleIdx = page02.IndexOf("### Relations by Kind & Evidence Rule", StringComparison.Ordinal);
        var epIdx = page02.IndexOf("### Endpoint Inventory", StringComparison.Ordinal);
        var compIdx = page02.IndexOf("### Component Index", StringComparison.Ordinal);
        var layersIdx = page02.IndexOf("## Building Blocks by Layer", StringComparison.Ordinal);

        Assert.True(
            relRuleIdx >= 0 && epIdx > relRuleIdx && compIdx > epIdx && layersIdx > compIdx,
            "Expected D4 placement: Relations by Kind & Evidence Rule < Endpoint Inventory < Component Index < Building Blocks by Layer.");

        // Check freshness gate passes in both modes
        foreach (var mode in new[] { "text", "roslyn" })
        {
            var check = RunCli(golden, "--analysis", mode, "--check-docs", docsDir, "--generated-at", "2026-01-01T00:00:00Z", "--no-banner", "--no-color");
            Assert.Equal(ExitCodes.Success, check.Code);
            Assert.Contains("Docs freshness gate passed", check.Stdout);
        }

        // Mutating Endpoint Inventory must fail freshness gate naming 02-building-blocks.md & Endpoint Inventory
        var mutated = page02.Replace("/api/orders", "/api/mutated", StringComparison.Ordinal);
        Assert.NotEqual(page02, mutated);
        File.WriteAllText(Path.Combine(docsDir, "02-building-blocks.md"), mutated);

        var drifted = RunCli(golden, "--check-docs", docsDir, "--generated-at", "2026-01-01T00:00:00Z", "--no-banner", "--no-color");
        Assert.Equal(ExitCodes.StrictFailure, drifted.Code);
        Assert.Contains("02-building-blocks.md", drifted.Stdout);
        Assert.Contains("Endpoint Inventory", drifted.Stdout);
    }

    [Fact]
    public void ArchitectureSpine_EmptySpine_RendersExplicitEmptyStates_InMarkdownAndHtml()
    {
        var result = new AnalysisResult
        {
            RootPath = "/tmp/empty-app",
            GeneratedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            ScannerName = "text",
        };

        var md = ReportGenerator.Generate(result);
        var html = HtmlReportGenerator.Generate(result, DiagramRenderMode.Svg);

        var mdSpine = ExtractMarkdownSection(md, "## Architecture Spine", "## CQRS Map");
        var htmlSpine = ExtractHtmlSection(html, "<h2>Architecture Spine</h2>", "<h2>CQRS Map</h2>");

        Assert.Contains("| Unique components | 0 |", mdSpine);
        Assert.Contains("| Endpoints (`spineEndpoints`) | 0 |", mdSpine);
        Assert.Contains("_No relations recorded in the model._", mdSpine);
        Assert.Contains("_No endpoints discovered in the model._", mdSpine);
        Assert.Contains("_No components discovered in the model._", mdSpine);

        Assert.Contains("<td>Unique components</td><td>0</td>", htmlSpine);
        Assert.Contains("<td>Endpoints (<code>spineEndpoints</code>)</td><td>0</td>", htmlSpine);
        Assert.Contains("<em>No relations recorded in the model.</em>", htmlSpine);
        Assert.Contains("<em>No endpoints discovered in the model.</em>", htmlSpine);
        Assert.Contains("<em>No components discovered in the model.</em>", htmlSpine);
    }

    [Theory]
    [InlineData("tests/fixtures/GoldenCorpus")]
    [InlineData("samples/SampleShop")]
    public void ArchitectureSpine_MarkdownAndHtmlTables_ReconcileWithJsonModel(string relativeCorpusPath)
    {
        var corpusPath = ResolveRepoPath(relativeCorpusPath);
        var result = Analyzer.Analyze(
            corpusPath,
            AnalysisMode.Text,
            new AnalysisOptions { GeneratedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z") });

        var json = JsonReportGenerator.Generate(result);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var components = root.TryGetProperty("components", out var compsEl) ? compsEl.GetArrayLength() : 0;
        var endpoints = root.TryGetProperty("spineEndpoints", out var epsEl) ? epsEl.GetArrayLength() : 0;
        var relations = root.TryGetProperty("relations", out var relsEl) ? relsEl.GetArrayLength() : 0;
        var totalOccurrences = root.TryGetProperty("relations", out relsEl)
            ? relsEl.EnumerateArray().Sum(r => r.GetProperty("occurrenceCount").GetInt32())
            : 0;
        var unproven = 0;
        if (root.TryGetProperty("unproven", out var unpEl))
        {
            if (unpEl.TryGetProperty("totalCount", out var tcEl))
            {
                unproven = tcEl.GetInt32();
            }
            else
            {
                foreach (var prop in unpEl.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Array)
                        unproven += prop.Value.GetArrayLength();
                }
            }
        }

        var md = ReportGenerator.Generate(result);
        var html = HtmlReportGenerator.Generate(result, DiagramRenderMode.Svg);
        var mdSpine = ExtractMarkdownSection(md, "## Architecture Spine", "## CQRS Map");
        var htmlSpine = ExtractHtmlSection(html, "<h2>Architecture Spine</h2>", "<h2>CQRS Map</h2>");

        var mdTables = ParseMarkdownTables(mdSpine);
        var htmlTables = ParseHtmlTables(htmlSpine);
        Assert.Equal(mdTables.Count, htmlTables.Count);

        for (var i = 0; i < mdTables.Count; i++)
        {
            Assert.Equal(mdTables[i].Headers, htmlTables[i].Headers);
            Assert.Equal(mdTables[i].Rows.Count, htmlTables[i].Rows.Count);
            for (var r = 0; r < mdTables[i].Rows.Count; r++)
                Assert.Equal(mdTables[i].Rows[r], htmlTables[i].Rows[r]);
        }

        // Reconcile summary counts
        Assert.Equal(components.ToString(), mdTables[0].Rows[0][1]);
        Assert.Equal(endpoints.ToString(), mdTables[0].Rows[1][1]);
        Assert.Equal(relations.ToString(), mdTables[0].Rows[2][1]);
        Assert.Equal(totalOccurrences.ToString(), mdTables[0].Rows[3][1]);
        Assert.Equal(unproven.ToString(), mdTables[0].Rows[4][1]);

        // Reconcile Endpoint Inventory and Component Index row counts against JSON arrays
        if (endpoints == 0)
        {
            Assert.Contains("_No endpoints discovered in the model._", mdSpine);
            Assert.Equal(components, mdTables[3].Rows.Count);
            Assert.Equal(Math.Min(25, components), mdTables[4].Rows.Count);
        }
        else
        {
            Assert.Equal(endpoints, mdTables[3].Rows.Count);
            Assert.Equal(components, mdTables[4].Rows.Count);
            Assert.Equal(Math.Min(25, components), mdTables[5].Rows.Count);
            // D3: Existing ## API Endpoints dispatch-focused table stays intact
            Assert.Contains("| Method | Route | Endpoint | Project | Target request | Handler | Inferred |", md);
            Assert.Contains("<th>Target request</th><th>Handler</th>", html);
        }
    }

    private static AnalysisResult BuildRichSyntheticResult()
    {
        var result = new AnalysisResult
        {
            RootPath = "/tmp/rich-synthetic",
            GeneratedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            ScannerName = "text",
        };

        var projA = ProjectId.FromRelativePath("App.ApiA/App.ApiA.csproj");
        var projB = ProjectId.FromRelativePath("App.ApiB/App.ApiB.csproj");
        result.Spine.AddProject(new ProjectNode(projA, "App.ApiA", "/tmp/App.ApiA/App.ApiA.csproj", "/tmp/App.ApiA", "Api", LayerProvenance.Configured));
        result.Spine.AddProject(new ProjectNode(projB, "App.ApiB", "/tmp/App.ApiB/App.ApiB.csproj", "/tmp/App.ApiB", "Api", LayerProvenance.Configured));

        var multiRoleId = new ComponentId(SymbolId.Create(projA, "App", null, "MultiRoleComponent"));
        var multiLoc = new SourceLocation("Services/MultiRole.cs", 8, 5);
        // Deliberately add roles out of ComponentRole enum order to verify output sorts in ComponentRole order.
        result.Spine.AddOrMergeComponent(
            multiRoleId,
            multiLoc,
            ComponentRole.Service,
            new EvidenceOccurrence(EvidenceRule.ServiceDiscovery, EvidenceOrigin.Declaration, multiLoc, EvidenceStrength.Proven));
        result.Spine.AddOrMergeComponent(
            multiRoleId,
            multiLoc,
            ComponentRole.Validator,
            new EvidenceOccurrence(EvidenceRule.ValidatorMarker, EvidenceOrigin.Declaration, multiLoc, EvidenceStrength.Proven));
        result.Spine.AddOrMergeComponent(
            multiRoleId,
            multiLoc,
            ComponentRole.Handler,
            new EvidenceOccurrence(EvidenceRule.HandlerMarker, EvidenceOrigin.Declaration, multiLoc, EvidenceStrength.Proven));

        var hubTopId = new ComponentId(SymbolId.Create(projB, "App", null, "HubTop"));
        result.Spine.AddOrMergeComponent(
            hubTopId,
            new SourceLocation("Services/HubTop.cs", 10, 1),
            ComponentRole.Service,
            new EvidenceOccurrence(EvidenceRule.ServiceDiscovery, EvidenceOrigin.Declaration, new SourceLocation("Services/HubTop.cs", 10, 1), EvidenceStrength.Proven));

        var tieAlphaId = new ComponentId(SymbolId.Create(projB, "App", null, "TieAlpha"));
        result.Spine.AddOrMergeComponent(
            tieAlphaId,
            new SourceLocation("Services/TieAlpha.cs", 10, 1),
            ComponentRole.Service,
            new EvidenceOccurrence(EvidenceRule.ServiceDiscovery, EvidenceOrigin.Declaration, new SourceLocation("Services/TieAlpha.cs", 10, 1), EvidenceStrength.Proven));

        var tieBetaId = new ComponentId(SymbolId.Create(projB, "App", null, "TieBeta"));
        result.Spine.AddOrMergeComponent(
            tieBetaId,
            new SourceLocation("Services/TieBeta.cs", 10, 1),
            ComponentRole.Service,
            new EvidenceOccurrence(EvidenceRule.ServiceDiscovery, EvidenceOrigin.Declaration, new SourceLocation("Services/TieBeta.cs", 10, 1), EvidenceStrength.Proven));

        var leafIds = new List<ComponentId>();
        for (var i = 1; i <= 26; i++)
        {
            var leafId = new ComponentId(SymbolId.Create(projB, "App", null, $"LeafComponent{i:00}"));
            var loc = new SourceLocation($"Services/Leaf{i:00}.cs", i, 1);
            result.Spine.AddOrMergeComponent(
                leafId,
                loc,
                ComponentRole.Service,
                new EvidenceOccurrence(EvidenceRule.ServiceDiscovery, EvidenceOrigin.Declaration, loc, EvidenceStrength.Proven));
            leafIds.Add(leafId);
        }

        // HubTop: in=2 (from TieBeta, MultiRole), out=3 (to TieAlpha, leaf1, leaf2) -> total=5
        // TieAlpha: in=1 (from HubTop), out=1 (to TieBeta) -> total=2
        // TieBeta: in=1 (from TieAlpha), out=1 (to HubTop) -> total=2
        // Actually add 1 more outgoing from TieBeta to external so TieBeta has in=0, out=2 -> total=2, TieAlpha has in=1, out=1 -> total=2
        void AddCall(ComponentId from, ComponentId to, int line, string? method = null)
        {
            var loc = new SourceLocation("Calls.cs", line, 1);
            result.Spine.AddOrMergeRelation(
                RelationKind.Calls,
                NodeReference.ForComponent(from),
                NodeReference.ForComponent(to),
                new EvidenceOccurrence(EvidenceRule.ComponentMemberInvocation, EvidenceOrigin.CallSite, loc, EvidenceStrength.Proven),
                method);
        }

        // Rebuild exact degrees:
        // HubTop: 2 incoming (MultiRole->HubTop, TieBeta->HubTop), 3 outgoing (HubTop->TieAlpha, HubTop->Leaf25, HubTop->Leaf26) => total 5
        // Wait: Leaf25 and Leaf26 would get in=1, so use external targets for HubTop outgoing & TieBeta outgoing so Leaf01..Leaf26 all stay at 0!
        var ext1 = NodeReference.ForExternal("Ext.ServiceOne");
        var ext2 = NodeReference.ForExternal("Ext.ServiceTwo");
        var ext3 = NodeReference.ForExternal("Ext.ServiceThree");

        void AddExtCall(ComponentId from, NodeReference to, int line)
        {
            var loc = new SourceLocation("Calls.cs", line, 1);
            result.Spine.AddOrMergeRelation(
                RelationKind.Calls,
                NodeReference.ForComponent(from),
                to,
                new EvidenceOccurrence(EvidenceRule.ComponentMemberInvocation, EvidenceOrigin.CallSite, loc, EvidenceStrength.Proven));
        }

        AddCall(multiRoleId, hubTopId, 1); // MultiRole out=1, HubTop in=1
        AddCall(tieBetaId, hubTopId, 2);   // TieBeta out=1, HubTop in=2
        AddCall(hubTopId, tieAlphaId, 3);  // HubTop out=1, TieAlpha in=1
        // Give MultiRole and HubTop same occurrence merge check (2 occurrences on same relation key)
        AddExtCall(hubTopId, ext1, 4);     // HubTop out=2
        AddExtCall(hubTopId, ext1, 5);     // same relation key -> 2 occurrences, 1 relation
        AddExtCall(hubTopId, ext2, 6);     // HubTop out=3 -> HubTop in=2, out=3, total=5
        AddExtCall(tieAlphaId, ext3, 7);   // TieAlpha in=1, out=1, total=2
        AddExtCall(tieBetaId, ext2, 8);    // TieBeta in=0, out=2, total=2
        // MultiRole has in=0, out=1, total=1 (leave TieAlpha/TieBeta ahead of MultiRole; wait: MultiRole is at total=1, leaves at 0)
        // Wait: our test above checked hubRows[3] == LeafComponent01 (0,0,0) - let's adjust or keep MultiRole!
        // Oh! Because MultiRole has total=1, MultiRole would be at index 3, Leaf01..Leaf21 at indices 4..24!
        // Let's keep MultiRole disconnected OR assert MultiRole at index 3! Wait: we used MultiRole->HubTop above. Let's have TieAlpha->HubTop instead, or assert MultiRole at index 3.
        // Wait: better yet, let's assert MultiRole at index 3 (`total=1`) and Leaf01..Leaf21 at indices 4..24 (`Leaf22` excluded)!
        // Let's keep MultiRole->HubTop and check exact degrees in the test!

        // Endpoints (sorted by Route, Method, Project):
        var epAlphaPost = new EndpointNode(
            EndpointId.Create(projA, new SourceLocation("Controllers/Alpha.cs", 20, 5)),
            "AlphaController.Create",
            "POST",
            "/api/alpha",
            EvidenceStrength.Proven,
            EvidenceStrength.Proven,
            new EvidenceOccurrence(EvidenceRule.EndpointMapping, EvidenceOrigin.MappingOrAttribute, new SourceLocation("Controllers/Alpha.cs", 20, 5), EvidenceStrength.Proven));
        var epAlphaGetA = new EndpointNode(
            EndpointId.Create(projA, new SourceLocation("Controllers/Alpha.cs", 10, 5)),
            "AlphaController.List",
            "GET",
            "/api/alpha",
            EvidenceStrength.Inferred,
            EvidenceStrength.Proven,
            new EvidenceOccurrence(EvidenceRule.EndpointMapping, EvidenceOrigin.MappingOrAttribute, new SourceLocation("Controllers/Alpha.cs", 10, 5), EvidenceStrength.Inferred));
        var epAlphaGetB = new EndpointNode(
            EndpointId.Create(projB, new SourceLocation("Controllers/AlphaB.cs", 12, 5)),
            "AlphaBController.List",
            "GET",
            "/api/alpha",
            EvidenceStrength.Proven,
            EvidenceStrength.Inferred,
            new EvidenceOccurrence(EvidenceRule.EndpointMapping, EvidenceOrigin.MappingOrAttribute, new SourceLocation("Controllers/AlphaB.cs", 12, 5), EvidenceStrength.Inferred));
        var epEmptyRoute = new EndpointNode(
            EndpointId.Create(projA, new SourceLocation("Controllers/Legacy.cs", 5, 5)),
            "LegacyController.Delete",
            "DELETE",
            "",
            EvidenceStrength.Proven,
            EvidenceStrength.Proven,
            new EvidenceOccurrence(EvidenceRule.EndpointMapping, EvidenceOrigin.MappingOrAttribute, new SourceLocation("Controllers/Legacy.cs", 5, 5), EvidenceStrength.Proven));

        // Add out of sorted order
        result.Spine.AddEndpoint(epEmptyRoute);
        result.Spine.AddEndpoint(epAlphaPost);
        result.Spine.AddEndpoint(epAlphaGetB);
        result.Spine.AddEndpoint(epAlphaGetA);

        // Dispatches from endpoints
        result.Spine.AddOrMergeRelation(
            RelationKind.Dispatches,
            NodeReference.ForEndpoint(epAlphaPost.Id),
            NodeReference.ForExternal("App.SecondCommand"),
            new EvidenceOccurrence(EvidenceRule.EndpointDispatch, EvidenceOrigin.MappingOrAttribute, epAlphaPost.Location, EvidenceStrength.Proven));
        result.Spine.AddOrMergeRelation(
            RelationKind.Dispatches,
            NodeReference.ForEndpoint(epAlphaPost.Id),
            NodeReference.ForExternal("App.CreateCommand"),
            new EvidenceOccurrence(EvidenceRule.EndpointDispatch, EvidenceOrigin.MappingOrAttribute, epAlphaPost.Location, EvidenceStrength.Proven));
        result.Spine.AddOrMergeRelation(
            RelationKind.Dispatches,
            NodeReference.ForEndpoint(epAlphaGetB.Id),
            NodeReference.ForExternal("App.AlphaQuery"),
            new EvidenceOccurrence(EvidenceRule.EndpointDispatch, EvidenceOrigin.MappingOrAttribute, epAlphaGetB.Location, EvidenceStrength.Inferred));

        // Unproven items (1 UnresolvedDispatch + 1 AmbiguousLink + 1 UnresolvedContract = 3)
        result.Spine.Unproven.AddUnresolvedDispatch(new UnresolvedDispatch
        {
            Method = "Send",
            ReceiverName = "_mediator",
            ProjectName = "App.ApiA",
            Location = new SourceLocation("Controllers/Alpha.cs", 25, 9),
        });
        result.RecordDiagnostic(new AnalysisDiagnostic(
            AnalysisResult.AmbiguousLinkCode,
            AnalysisDiagnosticSeverity.Warning,
            "UniqueAmbiguousMarkerForD7Test",
            new SourceLocation("Services/HubTop.cs", 3, 1),
            "App.ApiB"));
        result.RecordDiagnostic(new AnalysisDiagnostic(
            AnalysisResult.UnresolvedContractCode,
            AnalysisDiagnosticSeverity.Info,
            "UniqueUnresolvedContractForD7Test",
            new SourceLocation("Services/TieAlpha.cs", 4, 1),
            "App.ApiB"));

        return result;
    }

    private static string ResolveRepoPath(string relativePath)
        => Path.Combine(FindRepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string FindRepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CqrsLens.sln")))
                return dir.FullName;
        }
        throw new InvalidOperationException("Could not locate CqrsLens.sln.");
    }

    private static (int Code, string Stdout, string Stderr) RunCli(params string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var code = CliRunner.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private static string ExtractMarkdownSection(string md, string startHeading, string nextHeading)
    {
        var start = md.IndexOf(startHeading, StringComparison.Ordinal);
        var end = md.IndexOf(nextHeading, start + startHeading.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Failed to slice MD section {startHeading}..{nextHeading}");
        return md[start..end];
    }

    private static string ExtractHtmlSection(string html, string startHeading, string nextHeading)
    {
        var start = html.IndexOf(startHeading, StringComparison.Ordinal);
        var end = html.IndexOf(nextHeading, start + startHeading.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Failed to slice HTML section {startHeading}..{nextHeading}");
        return html[start..end];
    }

    private sealed record ParsedTable(List<string> Headers, List<List<string>> Rows);

    private static List<ParsedTable> ParseMarkdownTables(string markdownSection)
    {
        var tables = new List<ParsedTable>();
        var lines = markdownSection.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var i = 0;
        while (i < lines.Length)
        {
            if (!lines[i].StartsWith("| ", StringComparison.Ordinal))
            {
                i++;
                continue;
            }

            var headers = SplitMarkdownRow(lines[i]);
            i += 2; // skip header + separator
            var rows = new List<List<string>>();
            while (i < lines.Length && lines[i].StartsWith("| ", StringComparison.Ordinal))
            {
                rows.Add(SplitMarkdownRow(lines[i]));
                i++;
            }
            tables.Add(new ParsedTable(headers, rows));
        }
        return tables;
    }

    private static List<string> SplitMarkdownRow(string line)
    {
        var inner = line.Trim().Trim('|');
        return inner.Split('|')
            .Select(cell => NormalizeMarkdownCell(cell.Trim()))
            .ToList();
    }

    private static string NormalizeMarkdownCell(string cell)
    {
        if (cell.StartsWith("**", StringComparison.Ordinal) && cell.EndsWith("**", StringComparison.Ordinal) && cell.Length >= 4)
            cell = cell[2..^2];
        if (cell.StartsWith('`') && cell.EndsWith('`') && cell.Length >= 2)
            cell = cell[1..^1];
        else
            cell = Regex.Replace(cell, @"`([^`]+)`", "$1");
        return cell.Replace("\\|", "|", StringComparison.Ordinal)
                   .Replace("&lt;", "<", StringComparison.Ordinal)
                   .Replace("&gt;", ">", StringComparison.Ordinal)
                   .Replace("&amp;", "&", StringComparison.Ordinal);
    }

    private static List<ParsedTable> ParseHtmlTables(string htmlSection)
    {
        var tables = new List<ParsedTable>();
        foreach (Match tableMatch in Regex.Matches(htmlSection, @"<table>(.*?)</table>", RegexOptions.Singleline | RegexOptions.CultureInvariant))
        {
            var tableHtml = tableMatch.Groups[1].Value;
            var headers = Regex.Matches(tableHtml, @"<th>(.*?)</th>", RegexOptions.Singleline | RegexOptions.CultureInvariant)
                .Select(m => DecodeHtmlCell(m.Groups[1].Value))
                .ToList();
            var rows = new List<List<string>>();
            var tbodyMatch = Regex.Match(tableHtml, @"<tbody>(.*?)</tbody>", RegexOptions.Singleline | RegexOptions.CultureInvariant);
            var body = tbodyMatch.Success ? tbodyMatch.Groups[1].Value : tableHtml;
            foreach (Match rowMatch in Regex.Matches(body, @"<tr>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.CultureInvariant))
            {
                var cells = Regex.Matches(rowMatch.Groups[1].Value, @"<td>(.*?)</td>", RegexOptions.Singleline | RegexOptions.CultureInvariant)
                    .Select(m => DecodeHtmlCell(m.Groups[1].Value))
                    .ToList();
                rows.Add(cells);
            }
            tables.Add(new ParsedTable(headers, rows));
        }
        return tables;
    }

    private static string DecodeHtmlCell(string cellHtml)
    {
        var stripped = Regex.Replace(cellHtml, @"<[^>]+>", string.Empty);
        return System.Net.WebUtility.HtmlDecode(stripped);
    }
}
