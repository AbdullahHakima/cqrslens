# Stage F5 Checkpoint 1 — Architecture Spine Section Proof on SampleShop

- **Date:** 2026-11-17
- **Status:** `DONE`
- **Branch:** `foundations/f5-spine-reports` (created from F4 HEAD `f8902e9e75ac62d936d285b5e42f2be536228f25`)
- **Antigravity CLI Version:** `1.5.9` (`agy --version`)
- **Corpus:** `/data/workspace/corpora/nopcommerce` (`release-4.90.0` at `e7a52b9199f9c7651839e92826df47f668f3c767`, clean working tree)

---

## 1. Preconditions, Branch Setup & F4 Baseline Reproduction

- Verified repository `HEAD` was `f8902e9e75ac62d936d285b5e42f2be536228f25` with a clean working tree (only untracked `BRIEF_F5.md`).
- Created branch `foundations/f5-spine-reports` at `f8902e9e75ac62d936d285b5e42f2be536228f25`.
- Created read-only F4 baseline worktree `/tmp/cqrslens-f4base` at `f8902e9e75ac62d936d285b5e42f2be536228f25`, built in `Release`, and ran the baseline test suite:
  - `Passed! - Failed: 0, Passed: 750, Skipped: 68, Total: 818, Duration: 4 s - CqrsLens.Tests.dll (net8.0)` (68 skipped external-corpus tests absent on Box #4).

---

## 2. Implementation Summary (`src/CqrsLens.Reporting/` Only, `H6`)

| File | Purpose |
|---|---|
| [SpineReportView.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Spine/SpineReportView.cs) | Single shared presentation projection over `AnalysisResult.Spine` computing Summary counts, Components by Role (11 symbol roles + mapping-derived `endpoint`), Relations Summary, sorted Endpoint Inventory (joining `Dispatches` edges), sorted Component Index (`ComponentRole` enum order), and Most-Connected top 25 (`In + Out`, tie-broken by qualified name ascending). |
| [MarkdownSpineWriter.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Markdown/MarkdownSpineWriter.cs) | Renders `## Architecture Spine` and exposes shared `WriteEndpointInventoryTable` / `WriteComponentIndexTable` helpers reused by `DocsBuildingBlocksPageWriter`. Formats source locations as `file:line` for `text == roslyn` parity (`D6`). |
| [HtmlSpineSectionRenderer.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Html/HtmlSpineSectionRenderer.cs) | Renders `<h2>Architecture Spine</h2>` carrying identical data and row order to `MarkdownSpineWriter`. |
| [MarkdownReportWriter.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Markdown/MarkdownReportWriter.cs#L17-L20) | Inserts `MarkdownSpineWriter.Write(md, result)` between `WriteLayers` and `MarkdownCqrsWriter.Write` (`D1`). |
| [HtmlReportGenerator.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Html/HtmlReportGenerator.cs#L86-L93) | Inserts `HtmlSpineSectionRenderer.Append(html, result)` after the `Layers` block and before `AppendCqrs` (`D1`). |
| [DocsBuildingBlocksPageWriter.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Docs/DocsBuildingBlocksPageWriter.cs#L71-L78) | Appends `### Endpoint Inventory` and `### Component Index` immediately after `WriteRelationsSummary` (`D4`). |

---

## 3. D7 Unproven-Populations Confirmation

Verified firsthand across `Core` and `Reporting`:
1. `AnalysisResult.RecordDiagnostic` ([AnalysisResult.cs](file:///data/workspace/cqrslens/src/CqrsLens.Core/Analysis/AnalysisResult.cs#L438-L496)) routes `AmbiguousLink`, `AmbiguousContract`, `UnresolvedContract`, `EfEntityUnresolved`, `EfNavigationUnresolved`, and `EfConfigurationUnresolved` into both `result.Diagnostics` and `Spine.Unproven` (`AmbiguousLinks` / `UnresolvedContracts`).
2. Detailed diagnostic entries for those items are already rendered in `## Analysis Diagnostics` ([MarkdownDiagnosticsWriter.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Markdown/MarkdownDiagnosticsWriter.cs#L23-L64)), `<h2>Analysis Diagnostics</h2>` ([HtmlDiagnosticsSectionRenderer.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Html/HtmlDiagnosticsSectionRenderer.cs#L23-L68)), and `06-findings.md` ([DocsFindingsPageWriter.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Docs/DocsFindingsPageWriter.cs#L166-L201)).
3. Per `D7`, `## Architecture Spine` / `<h2>Architecture Spine</h2>` renders only the `Unproven items` count (`spine.Unproven.TotalCount`) in the summary table and does **not** duplicate those listings.

---

## 4. `SampleShop` Section & Insertion-Only Diff Proof (`D1`, `D2`, `D5`, `H2`)

Measured against `/tmp/cqrslens-f4base` on `samples/SampleShop` (`--generated-at 2026-01-01T00:00:00Z`) in both `text` and `roslyn` modes:

| Mode | Format | F4 vs F5 Result | Details |
|---|---|---|---|
| `text` | `json` | **Byte-identical (`0`-byte diff)** | `58,604` B (`F4`) == `58,604` B (`F5`) |
| `roslyn` | `json` | **Byte-identical (`0`-byte diff)** | `58,606` B (`F4`) == `58,606` B (`F5`) |
| `text` | `md` | **Insertion-only (`0`-byte diff outside Spine)** | `+4,939` B inserted between `## Layers` and `## CQRS Map` |
| `roslyn` | `md` | **Insertion-only (`0`-byte diff outside Spine)** | `+4,939` B inserted between `## Layers` and `## CQRS Map` |
| `text` | `html` (`mermaid`) | **Insertion-only (`0`-byte diff outside Spine)** | `+7,226` B inserted between `<h2>Layers</h2>` and `<h2>CQRS Map</h2>` |
| `text` | `html` (`svg`) | **Insertion-only (`0`-byte diff outside Spine)** | `+7,226` B inserted between `<h2>Layers</h2>` and `<h2>CQRS Map</h2>` |
| `roslyn` | `html` (`mermaid`) | **Insertion-only (`0`-byte diff outside Spine)** | `+7,226` B inserted between `<h2>Layers</h2>` and `<h2>CQRS Map</h2>` |
| `roslyn` | `html` (`svg`) | **Insertion-only (`0`-byte diff outside Spine)** | `+7,226` B inserted between `<h2>Layers</h2>` and `<h2>CQRS Map</h2>` |

- **Section contents verified on `SampleShop`:** `16` unique components, `0` endpoints (`_No endpoints discovered in the model._`), `14` relations (`14` occurrences across `references` 5, `serves` 4, `validates` 2, `relates` 3), `1` unproven item, `16` Component Index rows, `16` Most-Connected rows.
- **MD vs HTML parity on `SampleShop`:** All `5` tables (`row counts = [5, 12, 5, 16, 16]`) matched header-for-header and cell-for-cell in both `text` and `roslyn` modes and both `mermaid` and `svg` renderers.

---

## 5. CP1 Test Suite Verification (`H1`)

1. **Existing 818 tests unmodified:** Ran `dotnet test CqrsLens.sln -c Release --nologo` immediately after Production edits (prior to adding `F5SpineReportTests.cs`):
   - `Passed! - Failed: 0, Passed: 750, Skipped: 68, Total: 818, Duration: 3 s` (`0` existing test files touched).
2. **Added [F5SpineReportTests.cs](file:///data/workspace/cqrslens/tests/CqrsLens.Tests/F5SpineReportTests.cs) (`10` test cases):**
   - `ArchitectureSpine_Placement_IsAfterLayersAndBeforeCqrsMap_InMarkdownAndHtml` (`4` theory cases)
   - `ArchitectureSpine_RendersAllSixSubsections_OrderingTieBreaksCapAndD7NonDuplication`
   - `ArchitectureSpine_Determinism_AndTextRoslynParity_AcrossReportsAndDocs`
   - `DocsExport_BuildingBlocksPage_ExtendsWithInventories_AndCheckDocsPassesAndCatchesDrift`
   - `ArchitectureSpine_EmptySpine_RendersExplicitEmptyStates_InMarkdownAndHtml`
   - `ArchitectureSpine_MarkdownAndHtmlTables_ReconcileWithJsonModel` (`2` theory cases)
3. **Suite run with F5 tests (`DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet test CqrsLens.sln -c Release --nologo`):**
   - `Passed! - Failed: 0, Passed: 760, Skipped: 68, Total: 828, Duration: 2 s - CqrsLens.Tests.dll (net8.0)`
   - Zero new compiler warnings.
