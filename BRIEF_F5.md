# BRIEF F5 — APPROVED AS WRITTEN 2026-11-17 ~07:16 EET

# BRIEF F5 — Surface the architecture spine in human-readable reports

Status: APPROVED AS WRITTEN 2026-11-17 ~07:16 EET. This is the binding brief; the draft is superseded.
file until it is approved as written (then it becomes BRIEF_F5.md, hash-recorded).

## 1. Why

F2 built the architecture spine (evidence-backed components, relations,
endpoints). F4 froze it as a versioned JSON contract. But the Markdown and HTML
reports never render it: the spine appears only as count summaries in the
`--export-docs` building-blocks page, and in full only inside `ARCHITECTURE.json`.

Verified 2026-11-17 on real 2.0.0 output: nopCommerce 4.90 (35 projects,
8,764 relations, 17,422 occurrences, 1,151 spine endpoints, 0 MediatR requests,
0 EF Core DbContexts — it uses Linq2DB) produces a 12 KB Markdown report whose
CQRS sections are all empty and whose API Endpoints section says "No endpoints
discovered", while the JSON carrying the real findings is 33 MB. On any codebase
without MediatR the readable output is nearly empty. The spine is the tool's
general architecture inventory; the human reports must show it.

F5 is rendering only. No detection, no model, no schema, no CLI change.

## 2. Base, branch, roles

- Base: `f8902e9` (tip of `foundations/f4-noctua`, includes the stat-card fix).
- Branch: `foundations/f5-spine-reports`, from `f8902e9`.
- Driver: box #4, Antigravity orchestrator (`gemini-pro-orchestrator-g1-pro`),
  fresh session. Driver never commits or pushes.
- Verification: Mariam's VM (independent), same discipline as F2–F4.
- Release: F5 rides the 2.0.0 release — no version bump. It merges before the
  release tag; the release checklist order (merge → repo rename → tag → NuGet →
  deprecation) is unchanged.

## 3. Decisions (reasoned defaults — Abdullah vetoes by amendment)

- **D1 Placement.** New `## Architecture Spine` section in Markdown and HTML,
  placed after Layers and before the CQRS Map, same position in both formats.
  Rationale: the spine is the general inventory; on non-MediatR codebases it is
  the main content. CQRS-specific sections stay grouped after it.
- **D2 Contents.** MD and HTML carry identical data in identical order:
  1. Summary counts: components, relations, evidence occurrences, endpoints,
     unproven items (same shape as the docs-export summary tables).
  2. Components by Role table (role | components | role evidence occurrences).
  3. Relations Summary table (relation kind | relations | occurrences).
  4. Endpoint inventory, FULL table: Method | Route | Endpoint | Project |
     Dispatches to | Inferred | Location. Sorted by route, then method, then
     project. "Dispatches to" joins `Dispatches` relations and is empty when
     the endpoint dispatches to nothing. Inferred marks verb-or-route inference
     per the endpoint's `VerbStrength`/`RouteStrength`.
  5. Component index, FULL table: Component | Roles | Project | Location.
     Sorted by project, then qualified name. Roles are the component's
     evidenced role set, comma-separated, in `ComponentRole` enum order.
  6. Most-connected components, top 25 by degree (incoming + outgoing
     relations), ties broken by qualified name ascending. Columns: Component |
     In | Out | Total.
  7. NO full relations listing. 8,764 rows is noise; kinds (3) + hubs (6)
     carry the signal.
- **D3 Existing sections unchanged.** The `## API Endpoints` section keeps its
  dispatch-focused shape (Target request / Handler view). The spine inventory
  is the complete surface; the two views are complementary. The inventory's
  "Dispatches to" column is the bridge between them.
- **D4 Docs export.** Extend `02-building-blocks.md` with the same endpoint
  inventory (D2.4) and component index (D2.5) tables, placed after the existing
  summary tables. Pages 01/03/04/05/06 unchanged in shape. The `--check-docs`
  freshness gate must keep passing (content change expected, file set
  unchanged).
- **D5 JSON unchanged.** No schema changes, no new fields, `schemaVersion`
  stays 1. JSON output must be byte-identical to the F4 baseline on every
  corpus. Rendering only.
- **D6 Determinism.** All tables use the fixed sort orders in D2. Output is
  identical across repeated runs and identical in text and Roslyn modes for the
  same input (same rule as F3/F4). `--generated-at` byte-reproducibility is
  preserved.
- **D7 Unproven populations.** Counts only in the spine summary. The driver
  MUST confirm where ambiguous links / unresolved contracts are rendered today
  (Analysis Diagnostics) and MUST NOT duplicate those listings.
- **D8 Caps.** No caps on the inventory tables (full inventory is the point).
  The only cap is most-connected top-25 (D2.6). `Wraps` relations are counted
  exactly as the model reports them (H7 preserved: no relabeling, no fix).

## 4. Hard constraints

- **H1** Existing tests unmodified, except enumerated assertion updates about
  the ADDED section (same exception pattern as F4 H2: the driver lists every
  touched test file, the exact assertion, and why the old assertion cannot
  stand). No assertion may be weakened to mask a behavior change.
- **H2** Additive-only report change: the MD/HTML diff vs the F4 baseline
  shows ONLY the inserted spine section (plus the 02 docs-extension). No other
  section's bytes change.
- **H3** No detection/model changes: nothing under `src/CqrsLens.Core/Spine/`
  or the analysis backends changes. Read the spine, don't reshape it. No new
  `RelationKind` / `ComponentRole` values.
- **H4** No CLI surface changes (no new flags), no config changes, no MCP
  changes, no action.yml changes.
- **H5** No version bump, no CHANGELOG / README / csproj / packages.lock.json
  changes. `ToolInfo.Version` stays 2.0.0.
- **H6** The 4-project one-way chain is preserved. New code lives in
  `CqrsLens.Reporting`, following the existing writer patterns
  (`Markdown*Writer` for MD, `Html*Renderer` for HTML, `Docs*PageWriter` for
  the docs export). No new project, no new `PackageReference`.
- **H7** Rendering gaps preserved: the Wraps over-claim, ABP Roslyn exit 5,
  and every other documented limitation stay exactly as they are — documented,
  not fixed.
- **H8** Determinism in both modes (D6); `--generated-at` reproducibility.
- **H9** Driver never commits or pushes. Corpora read-only. No special-casing
  per corpus, per project name, or per fixture.
- **H10** Frozen regression lines hold: nopCommerce 8,764 relations / 17,422
  occurrences / 1,151 spine endpoints / 516 components (282/1/128/105 split);
  six-corpus counts (ABP 3,750, modular 790, northwind 191, eshop 121,
  ardalis 158, jasontaylor 135 — VM trees); GoldenCorpus fixture 27; suite
  count grows ONLY by added tests (818 + N, 0 failed).

## 5. File map (verified firsthand 2026-11-17)

- Spine model: `src/CqrsLens.Core/Spine/` — `ArchitectureSpine`
  (`Components`, `Relations`, `Endpoints`, `Unproven`), `ComponentNode`
  (Id/Name/FullName/Location/Roles+evidence), `SpineRelation`
  (Kind/From/To/Via/Method/Occurrences), `EndpointNode` (HttpMethod/Route/
  VerbStrength/RouteStrength/Evidence), 12 `ComponentRole` values, 14
  `RelationKind` values. Exposed as `AnalysisResult.Spine`.
- MD composition: `src/CqrsLens.Reporting/Markdown/MarkdownReportWriter.cs`
  `Generate` — Overview, Configuration, ProjectGraph, Layers, CQRS Map,
  Validation Coverage, Flow, Sequences, Endpoints, Requests table,
  Notifications, Behaviors, DbContexts, Diagnostics, Checks, Footer.
  The spine section inserts between `WriteLayers` and the CQRS writer call.
- HTML composition: `src/CqrsLens.Reporting/Html/HtmlReportGenerator.cs`
  `Generate` — same section order (cards, Configuration, Project Graph,
  Layers, CQRS, Validation Coverage, Flow, Sequences, Endpoints, Explorer,
  Notifications, Behaviors, DbContexts, Diagnostics, Checks). Insert after
  the Layers block, before `AppendCqrs`.
- Existing endpoint view: `MarkdownEndpointWriter` (dispatch-focused table,
  D3 — do not touch its shape); HTML equivalent `HtmlEndpointSectionRenderer`.
- Docs export: `src/CqrsLens.Reporting/Docs/DocsBuildingBlocksPageWriter.cs`
  already renders "Components by Role" and "Relations Summary" count tables
  (D4 extends this file only). `DocsTreeBuilder` owns the file set (unchanged).
- JSON: `src/CqrsLens.Reporting/Json/JsonReportDocumentFactory.cs` — MUST NOT
  change (D5).

## 6. Checkpoints

- **CP1 — Section proof on SampleShop.** MD + HTML spine sections render;
  insertion-only diff vs F4 baseline demonstrated; suite green.
- **CP2 — All corpora + docs export.** Six corpora + GoldenCorpus render in
  both modes; docs-export 02 extended; `--check-docs` passes; HTML parity
  shown (same data, same order).
- **CP3 — Full gates.** All G1–G9 evidence assembled (see §7); perf cells run;
  no open issues; handoff with per-gate evidence.

## 7. Verification gates (independent, Mariam's VM)

- **G1 Suite.** Box #4 suite green: 818 + new tests, 0 failed (VM `dotnet test`
  stays blocked by the sandbox loopback redirect; Gate 1 closes on the box #4
  result + claim-level verification of every new test, per the F4 precedent).
- **G2 Determinism.** Repeated runs byte-identical across MD, HTML, JSON, and
  the docs tree, in both modes.
- **G3 JSON identity + schema.** JSON byte-identical to the F4 baseline on all
  six corpora; independent Python `jsonschema` validation against
  `noctua.model.schema.json`, 0 undeclared paths.
- **G4 Insertion-only diff.** MD/HTML vs F4 baseline differ ONLY by the spine
  section (D2). Any other byte change fails the gate.
- **G5 Docs export.** 02 extended per D4; file set unchanged; `--check-docs`
  exit 0 on a fresh export; stale detection still catches drift.
- **G6 Reconciliation.** Spine section counts == JSON counts on all six
  corpora; endpoint inventory rows == `spineEndpoints`; component rows ==
  `components`; most-connected degrees recomputed independently.
- **G7 Six-corpus A/B.** Zero movement in model counts, warnings identical,
  MCP answers identical modulo `serverInfo` (same as F4 G7).
- **G8 Perf.** Report-generation cells ≤ +10% median-of-sessions vs the F4
  baseline (same cell structure as F4 G8; the new rendering cost is what's
  under test). Session-1 contamination rule from F4 applies.
- **G9 Diff chartered.** Changed files limited to Reporting writers/renderers,
  new tests, and enumerated H1 test-assertion updates. csproj, schemas,
  workflows, and docs untouched. 0 new warnings.

## 8. Out of scope (explicit)

Detection of any kind; Linq2DB/EF Core model changes; new relation kinds;
endpoint discovery changes; MCP tools; CLI flags; the Wraps fix; the Reporting
Upgrade backlog; site/comms; anything that changes JSON bytes.
