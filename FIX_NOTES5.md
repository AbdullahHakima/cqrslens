# FIX NOTES — Phase 5 (reporting upgrade)

## 1. Scope summary

Phase 5 is a reporting-layer phase: it turns the Phase 4 flow data into diagrams and
cleans up the readability of the existing reports. It adds a flow diagram, a pipeline
behavior chain, a publisher side to the notifications diagram, grouped architecture
checks, a per-item option for the CQRS map split, a standalone SVG export, and a set
of readability fixes (#6–#19 in `READABILITY_AUDIT.md`, including the #17 ER
self-loop label fix). It changes no scanner or model code: `AnalysisResult` fields,
inventory counts, findings and flow edges are untouched, and the JSON report is
content-identical to published 0.8.0 except the `generatedAt` / `elapsedMilliseconds`
lines.

## 2. Per-item audit

### Item A — flow diagram (endpoint → request → handler → DbContext/notification)

- **Built:** `FlowDiagramBuilder` emits a `flowchart LR` from `result.FlowEdges`
  with role-prefixed nodes (`ep_/req_/hdl_/ntf_/dbc_`) and edge labels
  `dispatches / handled by / sends / publishes / uses`; `FlowDiagramPlanner`
  partitions edges into feature-labelled, Mermaid-limit-bounded sections so every
  edge is in exactly one part; `FlowDiagramSection` carries heading/caption. Rendered
  in Markdown and HTML in both render modes; the SVG layout puts the chain in
  horizontal ranks. Also included: endpoint labels always carry `Controller.Action`
  (#9), a flow legend and a DbContext-honest note (#12), and notification-handler
  publish edges join their feature section (#13).
- **Files:** `src/CqrsLens.Reporting/Mermaid/FlowDiagramBuilder.cs`,
  `FlowDiagramPlanner.cs`, `FlowDiagramSection.cs`, `DiagramBuilder.cs`,
  `FlowchartEdge.cs` (renamed from the colliding `FlowEdge`), `FlowchartModel.cs`,
  `FlowchartParser.cs`, `FlowchartLayout.cs`;
  `src/CqrsLens.Reporting/Markdown/MarkdownDiagramWriter.cs`,
  `Markdown/MarkdownReportWriter.cs`; `src/CqrsLens.Reporting/Html/HtmlDiagramSectionRenderer.cs`,
  `Html/HtmlReportGenerator.cs`.
- **Tests:** `Phase5ReportingTests.KnownFlow_RendersFlowDiagramsInMarkdownAndHtml_InBothModes`
  (Text, Roslyn), `FlowDiagramSections_DeduplicatesHandConstructedEdgesAndMatchesRenderedEdgeCount`,
  `FlowDiagramSections_SplitsLargeFeatureWithoutDroppingAnyEdge`,
  `FlowSvgLayout_PlacesChainInHorizontalRanksAndPreservesCqrsMapThreeColumnLayout`,
  `EndpointLabels_AlwaysCarryTheControllerName`.
- **Verification:** rendered flow edges equal JSON `flowEdges` exactly on all three
  corpora (38 / 163 / 8) in MD, mermaid HTML and SVG HTML (`CHECKPOINT2.md`,
  `CHECKPOINT4.md`, `CHECKPOINT5.md`).

### Item B — per-feature CQRS map split as an explicit option

- **Built:** `--cqrs-map-split count|feature|unsplit` and the `cqrsMapSplit` config
  key; `count` is the default and byte-identical to the pre-item-B build; `feature`
  splits by feature; `unsplit` emits one diagram per project and keeps the
  node-bound (`BuildBounded`) safety split for oversized maps.
- **Files:** `src/CqrsLens.Reporting/Mermaid/CqrsMapSplitMode.cs`,
  `CqrsDiagramPlanner.cs`, `CqrsRequestOrdering.cs`, `CqrsDiagramBuilder.cs`,
  `DiagramBuilder.cs`, `Markdown/ReportGenerator.cs`, `Markdown/MarkdownReportWriter.cs`,
  `Markdown/MarkdownCqrsWriter.cs`, `Html/HtmlReportGenerator.cs`,
  `Html/HtmlDiagramSectionRenderer.cs`; `src/CqrsLens.Cli/CommandLine/CliOptions.cs`,
  `CliParser.cs`, `UsageWriter.cs`, `Configuration/CqrsMapSplitValues.cs`,
  `CqrsLensConfiguration.cs`, `CqrsLensSettings.cs`, `ConfigurationValidator.cs`,
  `Execution/PreparedScan.cs`, `ScanPreparer.cs`, `ReportWriter.cs`, `ScanCommand.cs`,
  `SvgExporter.cs`; `README.md`, `docs/configuration.md`, `cqrslens.schema.json`.
- **Tests:** `ItemBCqrsMapSplitTests` — `DefaultMode_IsCount_AndMatchesTheExistingOutput`,
  `UnsplitMode_EmitsOneSectionPerProject_AndKeepsEveryRequest`,
  `UnsplitMode_StillBoundsAProjectWhoseMapWouldBeHuge`,
  `FeatureSplitMode_GroupsByFeatureAndKeepsEveryRequest`,
  `FlagWinsOverConfig_WhenBothSpecifyTheSplitMode`, `NoConfigOrFlag_DefaultsToCount`,
  `Parser_ReadsTheSplitMode_AndRejectsUnknownValues`, `Config_RejectsUnknownSplitMode`.
- **Verification:** default output identical to the pre-item-B build; unsplit
  section count equals distinct request projects (northwind 1, modular 10, eshop 1);
  frozen counts hold in both modes (`ITEM_B.md`).

### Item C — pipeline / behavior chain visual

- **Built:** `BehaviorChainDiagramBuilder` emits `flowchart LR` with
  `Request → Behavior₁ → … → Behaviorₙ → Handler`; the behavior node shape was
  switched from `[["..."]]` to `["..."]` because the zero-JS SVG parser does not
  parse the subroutine shape. Rendered in the Pipeline Behaviors section of MD and
  HTML with a truthful caption: “N behaviors in scan order, not resolved execution
  order.” (the model records scan order, not DI registration order, so registration
  order is never claimed).
- **Files:** `src/CqrsLens.Reporting/Mermaid/BehaviorChainDiagramBuilder.cs`,
  `Markdown/MarkdownBehaviorWriter.cs`, `Html/HtmlBehaviorSectionRenderer.cs`.
- **Tests:** `Phase5ReportingTests.BehaviorChain_RendersInMarkdownAndHtmlPipelineBehaviors`.
- **Verification:** northwind Markdown contains `bhv_RequestPerformanceBehaviour…`
  and the scan-order caption; `northwind.svg.html` contains
  `<title>Pipeline behavior chain</title>` (`CHECKPOINT5.md`).

### Item D — notifications fan-out with the publisher side

- **Built:** the existing handler fan-out was extended (not duplicated) with
  publisher nodes from resolved `DispatchCallSites` (`TargetKind == Notification`):
  `publisher →|publishes| notification →|handled by| handler`. Added a
  notification legend and an HTML `<figure>`/`<figcaption>`; above a 40-notification
  threshold the diagram omits notifications with zero publishers and zero handlers
  and states the omitted count (#7); colliding handler names are disambiguated with
  the minimal module/type prefix (#14).
- **Files:** `src/CqrsLens.Reporting/Mermaid/NotificationDiagramBuilder.cs`,
  `Markdown/MarkdownNotificationWriter.cs`, `Html/HtmlNotificationSectionRenderer.cs`,
  `Mermaid/MermaidNodeSyntax.cs` (parallelogram parsing), `Mermaid/MermaidDiagramText.cs`.
- **Tests:** `Phase5ReportingTests.NotificationSection_RendersHandledByEdgeLabelLegendAndHtmlFigureCaption`,
  `ParallelogramPublisherNodes_ParseAndRenderOnceInSvgWithoutRawSyntax`,
  `NotificationsOmission_CaptionCountMatchesIsolatedOrphansAndDiagramDropsThem`.
  One existing assertion was updated: `NotificationsAndExpandedRulesTests.NotificationsAndDomainEvents_AreDiscoveredAndLinkedInBothModes`
  (`publishes to` → `handled by`, `CHECKPOINT4.md`).
- **Verification:** northwind publisher renders once as `CreateCustomerCommand.Handler`;
  modular renders 7 publishers; the modular caption reads
  “62 notifications with no publisher or handler are omitted from this diagram; all
  86 are listed in the table below.” (`CHECKPOINT5.md`).

### Item E — standalone diagram export

- **Built:** `--export-svg <directory>` writes every report diagram as a standalone
  `.svg` (project graph, layers, CQRS map parts, flow parts, notifications, behavior
  chain, one ER per DbContext). Reuses `DiagramBuilder` + `MermaidSvgRenderer`; the
  `<div class="svg-diagram">` wrapper is stripped and an XML declaration added, so
  each file is a complete document (root `<svg>` with `xmlns` + `viewBox`). Export is
  additive and fails loudly (non-zero exit, per-file reason) if it cannot write.
- **Files:** `src/CqrsLens.Cli/Execution/SvgExporter.cs`,
  `CommandLine/CliOptions.cs`, `CliParser.cs`, `UsageWriter.cs`, `Execution/ScanCommand.cs`.
- **Tests:** `ItemESvgExportTests` — `Export_ProducesExpectedFileSetWithExpectedNames_AndEveryFileParsesAsXml`,
  `Export_DoesNotChangeNormalReportOutputs`, `Export_TwiceProducesIdenticalBytes`,
  `SplitCqrsMap_ExportsOneFilePerPart`, `Export_ToAPathThatIsAFile_FailsWithNonZeroExitCode`,
  `CliParser_ReadsExportSvgDirectory`.
- **Verification:** northwind 13 files / modular 46 files, equal to the
  `<div class="svg-diagram">` count in the same scan’s SVG HTML; all 59 exported
  files parse as XML with root `svg`/`xmlns`/`viewBox`; byte-identical repeat export
  (`ITEM_E.md`).

### Item F — findings-by-feature visual

- **NOT BUILT.** Phase 5 shipped the checkpoint-5 #6 work instead: architecture
  checks are grouped by **rule code** (Markdown subheadings, HTML collapsible
  `<details>` with `(×N)` compression of repeated messages) and the Overview / stat
  cards show total error and warning counts. There is no clustering of findings by
  feature or project and no per-feature counts-per-check matrix. See section 7.

### #17 corrective fix — ER self-loop label clipping

- **Built:** the first #17 pass had the label in the markup but clipped off the top
  of the viewBox (the loop/label draw above the top-row box at `Y−28`/`Y−46` while
  layout started at `Y=24`). Fixed by adding `SelfLoopTopPadding = 48` to
  `ErDiagramLayout` **only when the model has a self-referential relationship**,
  shifting content down and growing the canvas height by the same amount;
  `ErDiagramSvgWriter` now draws relationship lines before entities and label pills
  after them, with the self-loop label placed above the box.
- **Files:** `src/CqrsLens.Reporting/Mermaid/ErDiagramLayout.cs`,
  `Mermaid/ErDiagramSvgWriter.cs`.
- **Tests:** `ErSelfLoopBoundsTests.SelfLoopLabelAndLoop_RenderInsideTheCanvasBounds`,
  `DiagramWithoutSelfLoop_KeepsTheOriginalTopMargin`.
- **Verification:** rendered with headless Chromium from both the standalone ER SVG
  and the report’s embedded SVG copy — label text exactly
  `DirectReports / Manager (0..1..*)`, 519 dark pixels in the label band, 0 dark
  pixels in the strip above it, loop lines painted (`ER_LABEL_FIX.md`).

## 3. Chosen names (public surface added)

| Surface | Default | Notes |
| --- | --- | --- |
| `--export-svg <directory>` | off | Writes standalone `.svg` files. **SVG only, no PNG:** the solution references no in-process SVG rasterizer (no SkiaSharp / ImageSharp / System.Drawing rasterization) and adding one is forbidden, so PNG is not produced. |
| `--cqrs-map-split <count\|feature\|unsplit>` | `count` | CQRS map split mode. Unknown value → usage error, exit `1`. Overrides the config key. |
| `cqrsMapSplit` (`.cqrslens.json`) | absent = `count` | Validated strictly; unknown value → exit `1`. CLI flag wins over config. |

**Default behavior:** with none of the above set, the output equals the
checkpoint-2–5 default (verified byte-for-byte for item B). Relative to published
0.8.0, Phase 5 **deliberately changed** the default report in these places (JSON is
not affected):

- New `## Flow` / `<h2>Flow</h2>` section (item A).
- New pipeline behavior chain rendered in Pipeline Behaviors (item C).
- Notification diagram gained publishers and the `handled by` edge label, a legend
  and an HTML `<figure>` caption; isolated orphans are omitted above 40
  notifications with a stated count (item D / #7 / #8).
- Architecture Checks grouped by rule code; Overview / stat cards show
  error+warning counts (#6).
- Markdown section order is CQRS Map → Flow → Requests with `## Requests` at top
  level; `Source` columns on Notifications and Pipeline Behaviors; behaviors render
  as a table; Flow captions say “connected flows” (#18).
- Analysis Diagnostics empty text is “No analysis diagnostics.” (#19).
- CQRS map orders requests feature-first with feature-boundary chunks (#11);
  Flow endpoint labels carry `Controller.Action` (#9); Flow has a legend and an
  honest DbContext note (#12); layer bands are ordered outer→core (#15); ER
  self-loops fit the canvas (#17); duplicate notification names are disambiguated
  (#14).

## 4. Measured report times

Measured now with the Release binary, text mode, full scan (median of 3 runs each;
`--generated-at` pinned so timing metadata is constant):

| Corpus | Runs (ms) | Median |
| --- | --- | ---: |
| northwindtraders | 633, 650, 629 | **633 ms** |
| modular-monolith (`--markers ICommand,IQuery`) | 756, 726, 771 | **756 ms** |
| eshop (Ordering) | 363, 371, 363 | **363 ms** |

(The modular Markdown + SVG HTML *report generation* alone was measured at 1643 ms
in `CHECKPOINT5.md`; not re-measured here.)

## 5. Frozen table

Re-verified for this document (text mode, Release binary):

| Corpus | Req / Han / Val | Errors / Warnings | flowEdges | Other |
| --- | --- | --- | ---: | --- |
| northwindtraders | 19 / 19 / 4 | 0 / 8 | 38 | notif 1 / notif handlers 1 |
| modular-monolith | 105 / 105 / 5 | 0 / 205 | 163 | notif 86 / notif handlers 30 |
| eshop (Ordering) | 8 / 8 / 4 | 0 / 15 | 8 | `--strict` exit **0** |

- **Test suite:** `Passed! - Failed: 0, Passed: 400, Skipped: 0, Total: 400`
  (measured now).
- **JSON:** content-identical to the published 0.8.0 tool on all three corpora
  after normalizing only `generatedAt` / `elapsedMilliseconds` (measured now,
  compared against 0.8.0 JSON generated by the `cb63252` build).

## 6. Known notes

- A `dotnet build -c Release --no-incremental` in this environment emits warnings
  only in these categories: CS9057 (Roslyn analyzer/compiler version mismatch from
  the installed SDK — the bulk, environment-only), CA1869 (benchmark), CS8604 (×2,
  in the Phase 4 file `DispatchCallSiteScanner.cs`), CS8714 (×2, in the item-E test
  `ItemESvgExportTests.cs`). The product `src/` projects show **no Phase 5
  warnings**. `BRIEF_FIX_NOTES5.md` cites 14 pre-existing warnings; the exact total
  varies with how CS9057 is counted per project/analyzer, so the measured breakdown
  is given here instead of a single figure.
- The #17 fix moved the ER diagram canvas: northwind’s `NorthwindDbContext` ER SVG
  viewBox is now **862×2024** (was 862×1976 — the +48 px self-loop top padding).
- The `unsplit` CQRS map mode still runs `BuildBounded`, so a project whose map
  exceeds the 45,000-character / 400-edge Mermaid limit is split into bounded parts
  rather than emitting one oversized diagram.
- Item D draws publishers from resolved `DispatchCallSites`; it does not currently
  exclude test-project publish sites and does not fall back to
  `FlowEdges`/`HandlerToNotification` when `FlowEdges` are populated without
  `DispatchCallSites` (a hand-built-model unit-test nuance noted in `AUDIT5.md` §3
  and left as-is).

## 7. What is NOT in this phase

- **Item F (findings-by-feature visual): not implemented.** The reports group
  architecture checks by rule code with totals, but there is no per-feature /
  per-project findings clustering or counts-per-check matrix. The item was listed as
  built in `BRIEF_FIX_NOTES5.md`; the code audit (`AUDIT5.md` §3, item F) and the
  current `HtmlDiagnosticsSectionRenderer` show it was not built.
- **PNG export:** intentionally dropped; SVG-only export ships instead (reason in
  section 3).
- **Pipeline behavior execution-order resolution:** explicitly out of scope in the
  original brief; the chain is labelled as scan order.
- **Item D publisher test-project filtering / `FlowEdges` fallback:** noted in
  section 6, not changed.
