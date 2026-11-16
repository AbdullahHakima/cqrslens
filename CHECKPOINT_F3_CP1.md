# CHECKPOINT F3 CP1 — F2 Baseline Reproduction, Intent Spec, & Docs Skeleton

- **Status:** `done`
- **Branch:** `foundations/f3-docs` (created from `foundations/f2-controllers` @ `db3c5f63741b6a5c9f86142ca149d1e224b01116`)
- **Antigravity CLI Version:** `1.5.9`
- **Target Corpus:** `/data/workspace/corpora/nopcommerce` @ `release-4.90.0` (`e7a52b9199f9c7651839e92826df47f668f3c767`, `git status --short` clean)

---

## 1. F2 Baseline Reproduction Proof

Clean baseline worktree `/tmp/cqrslens-f2base` @ `db3c5f6` built in `Release` and scanned on pinned `nopCommerce`:

- **Suite at baseline (`db3c5f6`):** `Passed! - Failed: 0, Passed: 693, Skipped: 68, Total: 761` (`68` skipped external-corpus tests absent on box #4).
- **nopCommerce (`text` mode, `json`, `--generated-at 2026-01-01T00:00:00Z`):**
  - **SHA-256:** `5bc6d4f9bf9fa33e90724a5126060223a4f774349a0e3fa74dd95cbc42c0125a`
  - **Relations:** `8,764`
  - **Spine Endpoints (`spineEndpoints`):** `1,151`
  - **Components:** `516` (`service`: `282`, `repository`: `1`, `controller`: `128`, `validator`: `105`)

Reproduction commands:
```bash
git worktree add /tmp/cqrslens-f2base db3c5f6
DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet build /tmp/cqrslens-f2base/CqrsLens.sln -c Release --nologo
DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet /tmp/cqrslens-f2base/src/CqrsLens.Cli/bin/Release/net8.0/cqrslens.dll \
  /data/workspace/corpora/nopcommerce --analysis text --format json --generated-at 2026-01-01T00:00:00Z --no-banner --no-color -o /tmp/f3-base-nop.json
sha256sum /tmp/f3-base-nop.json
```

---

## 2. Intent Format Specification (Paper First)

Written prior to any intent-check code in [F3_INTENT_FORMAT.md](file:///data/workspace/cqrslens/F3_INTENT_FORMAT.md):
- Top-level `"intent"` object in `.cqrslens.json` (strictly parsed with `JsonUnmappedMemberHandling.Disallow`, read-only to the tool).
- Four optional sections: `modules`, `boundaries`, `publicSurface`, `ownership`.
- Exact check set (`C-BOUNDARY`, `C-SURFACE`, plus stale module selector `C-INTENT-STALE`); `ownership` and modules without checkable consequence are never checked and render explicitly as `declared, unverified`.

---

## 3. Docs Tree Layout & Completeness Table

Implemented `--export-docs <dir>` via [DocsTreeBuilder.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Docs/DocsTreeBuilder.cs) and [DocsExporter.cs](file:///data/workspace/cqrslens/src/CqrsLens.Cli/Execution/DocsExporter.cs), plus updated XML documentation comments on [SvgExporter.cs](file:///data/workspace/cqrslens/src/CqrsLens.Cli/Execution/SvgExporter.cs) and [MermaidSvgRenderer.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Mermaid/MermaidSvgRenderer.cs).

| Output Path | Sections | Completeness at CP1 | Notes |
|---|---|---|---|
| `01-context.md` | System Context Summary, Projects & References, External References in Relations, Entry Points (Endpoints), Declared Public Surface (when intent present) | **Complete** | Includes `configured` vs `guessed (inferred)` layer provenance and per-fact endpoint `proven` / `inferred` provenance; `468,247` bytes on nopCommerce |
| `02-building-blocks.md` | Component & Relation Summary, Components by Role, Relations Summary & Rule Breakdown, Building Blocks by Layer, Component Inventory, Unproven Populations, Declared Intent (when intent present) | **Complete** | Sorted by `ComponentId`; multi-role components counted once; ownership and uncheckable modules labelled `declared, unverified`; `148,506` bytes on nopCommerce |
| `03-runtime.md` | Runtime Summary, Per-Flow Sequences (`RequestSequenceBuilder`), Flow & Notification Diagrams, Controller Endpoint Exposures, Component Call & Data Flows | **Complete** | Reuses `RequestSequenceBuilder` untouched (H7); carries `execution order is not guaranteed`; `90,202` bytes on nopCommerce |
| `04-data-model.md` | Data Model Summary, EF Core Data Model (`DataModelView`), Repository & Data-Access Relations | **Complete** | Detects `linq2db` package reference / non-EF variant when `DbContexts = 0`; `236,715` bytes on nopCommerce |
| `05-cross-cutting.md` | Configuration & Conventions (`ConfigurationView`), Pipeline Behaviors (`BehaviorOrdering`), Validation Coverage & Validators, Service Resolution & Unproven Cross-Cutting Observations | **Complete** | Preserves `BehaviorPresentationText.RegisteredOrderNote`; normalizes config path without leaking host directories; `35,935` bytes on nopCommerce |
| `06-findings.md` | Findings Summary, Architecture & Intent Findings (`FindingsClustering`), Intent Verification Findings (when intent present), Analysis Diagnostics | **Skeleton/View Complete** (intent check engine lands in CP2) | Renders all `Checks` and `Diagnostics` deterministically; `C-BOUNDARY` / `C-SURFACE` / `C-INTENT-STALE` checks populated in CP2; `523` bytes on nopCommerce |
| `diagrams/*.svg` | `project-graph.svg`, `layers.svg`, `cqrs-map[-N].svg`, `flow[-N].svg`, `notifications.svg`, `behavior-chain.svg`, `er-diagram[-N].svg`, `sequence-<Name>[-N].svg` | **Complete** | `SvgExporter` reused; standalone `<svg>` bytes verified identical to HTML-embedded SVGs; `2` SVGs (`layers.svg` `16,323` B, `project-graph.svg` `15,289` B) on nopCommerce |

### Verification of Export Determinism & `text == roslyn` Parity
- On `GoldenCorpus` (tested in [F3DocsExportTests.cs](file:///data/workspace/cqrslens/tests/CqrsLens.Tests/F3DocsExportTests.cs)): repeated exports with pinned `--generated-at` and unpinned runs (`unpinned`) are byte-identical; `text` and `roslyn` exports produce identical bytes across all 6 Markdown pages and all standalone SVGs.
- On `nopCommerce`: `text` and `roslyn` exports (`--generated-at 2026-01-01T00:00:00Z`) produced 0 differences under `diff -r` (`01-context.md`..`06-findings.md`, `diagrams/layers.svg`, `diagrams/project-graph.svg`).

---

## 4. Test Results

Command:
```bash
DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet test CqrsLens.sln -c Release --nologo
```
Result:
```text
Passed!  - Failed:     0, Passed:   698, Skipped:    68, Total:   766, Duration: 3 s - CqrsLens.Tests.dll (net8.0)
```
- Existing tests: `693` passed, `68` skipped (external-corpus gated tests absent on box #4), `0` failed.
- Additive CP1 tests ([F3DocsExportTests.cs](file:///data/workspace/cqrslens/tests/CqrsLens.Tests/F3DocsExportTests.cs)): `5/5` passed.
- Compiler warnings: `0` new warnings (`CqrsLens.Core`: `0` warnings).

---

## 5. Files Changed

### `git status --short`
```text
 M src/CqrsLens.Analysis/Analyzer.cs
 M src/CqrsLens.Analysis/Checks/ArchitectureChecker.cs
 M src/CqrsLens.Cli/CommandLine/CliOptions.cs
 M src/CqrsLens.Cli/CommandLine/CliParser.cs
 M src/CqrsLens.Cli/CommandLine/UsageWriter.cs
 M src/CqrsLens.Cli/Execution/ReportOutputPaths.cs
 M src/CqrsLens.Cli/Execution/ReportOutputPathsResolver.cs
 M src/CqrsLens.Cli/Execution/ReportWriter.cs
 M src/CqrsLens.Cli/Execution/ScanCommand.cs
 M src/CqrsLens.Cli/Execution/ScanReporter.cs
 M src/CqrsLens.Cli/Execution/SvgExporter.cs
 M src/CqrsLens.Core/Analysis/AnalysisResult.cs
 M src/CqrsLens.Core/Configuration/AnalysisOptions.cs
 M src/CqrsLens.Reporting/Mermaid/MermaidSvgRenderer.cs
?? BRIEF_F1.md
?? BRIEF_F1_PERF.md
?? BRIEF_F2.md
?? BRIEF_F2_PERF.md
?? BRIEF_F3.md
?? CHECKPOINT_F1_CP1.md
?? CHECKPOINT_F1_CP2.md
?? CHECKPOINT_F1_CP3.md
?? CHECKPOINT_F1_PERF.md
?? CHECKPOINT_F2_CP1.md
?? CHECKPOINT_F2_CP2.md
?? CHECKPOINT_F2_CP3.md
?? CHECKPOINT_F2_PERF.md
?? CqrsLens_Modeling_Foundations.md
?? F0_METAMODEL_v2.1.md
?? F2_CORPUS_AUDIT.md
?? F3_INTENT_FORMAT.md
?? f2-final-verification.md
?? f2perf.patch
?? src/CqrsLens.Cli/Execution/DocsExporter.cs
?? src/CqrsLens.Core/Intent/
?? src/CqrsLens.Reporting/Docs/
?? tests/CqrsLens.Tests/F3DocsExportTests.cs
```

### `git diff --stat`
```text
 src/CqrsLens.Analysis/Analyzer.cs                  |  2 ++
 .../Checks/ArchitectureChecker.cs                  |  3 +++
 src/CqrsLens.Cli/CommandLine/CliOptions.cs         |  2 ++
 src/CqrsLens.Cli/CommandLine/CliParser.cs          | 28 +++++++++++-----------
 src/CqrsLens.Cli/CommandLine/UsageWriter.cs        |  4 ++++
 src/CqrsLens.Cli/Execution/ReportOutputPaths.cs    |  2 +-
 .../Execution/ReportOutputPathsResolver.cs         | 27 +++++++++++++--------
 src/CqrsLens.Cli/Execution/ReportWriter.cs         |  3 ++-
 src/CqrsLens.Cli/Execution/ScanCommand.cs          | 11 +++++++--
 src/CqrsLens.Cli/Execution/ScanReporter.cs         |  5 +++-
 src/CqrsLens.Cli/Execution/SvgExporter.cs          | 13 ++++++----
 src/CqrsLens.Core/Analysis/AnalysisResult.cs       | 18 +++++++++++++-
 src/CqrsLens.Core/Configuration/AnalysisOptions.cs |  3 +++
 .../Mermaid/MermaidSvgRenderer.cs                  |  4 ++--
 14 files changed, 89 insertions(+), 36 deletions(-)
```

---

## 6. Decisions & Open Issues

- **Source line & location precision in exported docs:** Docs pages omit the `scanner` label and render source locations as `file:line` (rather than `file:line:column`) so `text` and `roslyn` scans of the same codebase produce 100% byte-identical docs trees without column-offset discrepancies (e.g., `Entities.cs:6:15` vs `6:21` on `GoldenCorpus`). When `--generated-at` is omitted, the docs header renders `unpinned` so repeated exports remain deterministic with zero wall-clock leakage.
- **No unrequested CWD report:** Running `--export-docs <dir>` without `-o` or `--format` suppresses the default `ARCHITECTURE.md` write into the current working directory.
- **Open for CP2:** Wire `.cqrslens.json` `"intent"` parsing/validation, `IntentChecker` (`C-BOUNDARY`, `C-SURFACE`, `C-INTENT-STALE`), and `--check-docs <dir>` freshness gate + drift attribution.

---

## 7. Exact Reproduction Commands

```bash
DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet test CqrsLens.sln -c Release --nologo
DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet src/CqrsLens.Cli/bin/Release/net8.0/cqrslens.dll \
  /data/workspace/corpora/nopcommerce --analysis text --export-docs /tmp/f3-cp1-nop-text --generated-at 2026-01-01T00:00:00Z --no-banner --no-color
DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet src/CqrsLens.Cli/bin/Release/net8.0/cqrslens.dll \
  /data/workspace/corpora/nopcommerce --analysis roslyn --export-docs /tmp/f3-cp1-nop-roslyn --generated-at 2026-01-01T00:00:00Z --no-banner --no-color
diff -r /tmp/f3-cp1-nop-text /tmp/f3-cp1-nop-roslyn
```
