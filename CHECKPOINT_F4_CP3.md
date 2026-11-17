# Stage F4 Checkpoint 3 — Verification Gates & Release Checklist

- **Date:** 2026-11-16
- **Branch:** `foundations/f4-noctua` (base F3 HEAD `18ca9c6d893c1c3734505af4f1ab45dcbccc8150`)
- **Driver:** Antigravity CLI version `1.5.9` (`agy --version`)
- **Status:** `DONE` (all 9 Gates passed on Box #4; working tree uncommitted on `foundations/f4-noctua` per `H9`; **stopped after CP3** ready for Mariam's 6-corpus VM verification)

---

## 1. Final Test Suite & Working Tree

### 1.1 Final Test Suite Result (after Gate 9 cleanup)

```bash
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
dotnet test CqrsLens.sln -c Release --nologo
```

- **Output:** `Passed!  - Failed:     0, Passed:   750, Skipped:    68, Total:   818, Duration: 3 s - CqrsLens.Tests.dll (net8.0)`
  - `721` existing passing tests (all 9 enumerated name/version/path files updated per `H2`)
  - `29` new F4 schema/rename/compat tests in `tests/CqrsLens.Tests/F4SchemaAndRenameTests.cs`
  - `68` skipped external-corpus tests (`CQRSLENS_CORPUS_ROOT` / `CQRSLENS_ABP_ROOT` absent on Box #4; full `818 / 818` target for Mariam's VM)
- **Restore `--locked-mode`:**
  - `dotnet restore CqrsLens.sln --locked-mode` exited `0` (all 6 projects restored cleanly; `tests/CqrsLens.Tests/packages.lock.json` updated project key `"CqrsLens"` -> `"Noctua"` `[2.0.0, )`).

### 1.2 Protected File & Corpus Integrity

- `FrozenQuestionSet.cs` (`H4`) & `RequestSequenceBuilder.cs` (`H7`):
  ```bash
  git diff HEAD -- tests/CqrsLens.Tests/FrozenQuestionSet.cs src/CqrsLens.Reporting/Sequences/RequestSequenceBuilder.cs | wc -c
  # Output: 0
  ```
- Pinned `nopCommerce` corpus (`H10`):
  - `git -C /data/workspace/corpora/nopcommerce rev-parse HEAD` -> `e7a52b9199f9c7651839e92826df47f668f3c767`
  - `git -C /data/workspace/corpora/nopcommerce describe --tags` -> `release-4.90.0`
  - `git -C /data/workspace/corpora/nopcommerce status --short` -> *(clean, 0 lines)*

### 1.3 `git status --short` & `git diff --stat`

```text
 M .github/workflows/release.yml
 M CHANGELOG.md
 M Directory.Build.props
 M README.md
 M ROADMAP.md
 M action.yml
 D cqrslens.schema.json
 M docs/analysis-backends.md
 M docs/architecture.md
 M docs/baseline.md
 M docs/ci.md
 M docs/configuration.md
 M docs/validation.md
 M src/CqrsLens.Cli/CommandLine/CliParser.cs
 M src/CqrsLens.Cli/CommandLine/UsageWriter.cs
 M src/CqrsLens.Cli/Configuration/ConfigurationLoader.cs
 M src/CqrsLens.Cli/Configuration/ConfigurationValidator.cs
 M src/CqrsLens.Cli/Configuration/CqrsLensSettings.cs
 M src/CqrsLens.Cli/CqrsLens.Cli.csproj
 M src/CqrsLens.Cli/Diff/Markdown/ArchitectureDiffMarkdownRenderer.cs
 M src/CqrsLens.Cli/Execution/CliRunner.cs
 M src/CqrsLens.Cli/Execution/ScanCommand.cs
 M src/CqrsLens.Cli/Execution/ScanReporter.cs
 M src/CqrsLens.Cli/Mcp/McpRequestRouter.cs
 M src/CqrsLens.Cli/Mcp/McpScanFingerprintCalculator.cs
 M src/CqrsLens.Cli/Summary/SummaryCountsWriter.cs
 M src/CqrsLens.Core/Analysis/ConfigurationProvenance.cs
 M src/CqrsLens.Reporting/Agent/AgentArchitectureMapGenerator.cs
 M src/CqrsLens.Reporting/Docs/DocsBuildingBlocksPageWriter.cs
 M src/CqrsLens.Reporting/Docs/DocsFormatting.cs
 M src/CqrsLens.Reporting/Html/HtmlReportGenerator.cs
 M src/CqrsLens.Reporting/Markdown/MarkdownOverviewWriter.cs
 M src/CqrsLens.Reporting/Markdown/MarkdownReportWriter.cs
 M src/CqrsLens.Reporting/Sarif/SarifReportGenerator.cs
 M tests/CqrsLens.Tests/ArchitectureBoundaryTests.cs
 M tests/CqrsLens.Tests/ArchitectureDiffTests.cs
 M tests/CqrsLens.Tests/CiOutputTests.cs
 M tests/CqrsLens.Tests/CliTests.cs
 M tests/CqrsLens.Tests/DeepSliceAndAgentTests.cs
 M tests/CqrsLens.Tests/F3DocsExportTests.cs
 M tests/CqrsLens.Tests/McpServerTests.cs
 M tests/CqrsLens.Tests/V02FeatureTests.cs
 M tests/CqrsLens.Tests/VersionTests.cs
 M tests/CqrsLens.Tests/packages.lock.json
?? BRIEF_F4.md
?? CHECKPOINT_F4_CP1.md
?? CHECKPOINT_F4_CP2.md
?? CHECKPOINT_F4_CP3.md
?? CqrsLens_To_Noctua_Rename_Plan.md
?? docs/model-schema.md
?? noctua.model.schema.json
?? noctua.schema.json
?? tests/CqrsLens.Tests/F4SchemaAndRenameTests.cs
```

```text
 .github/workflows/release.yml                      |   6 +-
 CHANGELOG.md                                       |  42 ++++++-
 Directory.Build.props                              |   2 +-
 README.md                                          | 114 ++++++++++--------
 ROADMAP.md                                         |  18 ++-
 action.yml                                         | 132 ++++++++++-----------
 cqrslens.schema.json                               |  96 ---------------
 docs/analysis-backends.md                          |   2 +-
 docs/architecture.md                               |   6 +-
 docs/baseline.md                                   |  13 +-
 docs/ci.md                                         |  16 +--
 docs/configuration.md                              |  32 +++--
 docs/validation.md                                 |   4 +-
 src/CqrsLens.Cli/CommandLine/CliParser.cs          |   2 +-
 src/CqrsLens.Cli/CommandLine/UsageWriter.cs        |   6 +-
 .../Configuration/ConfigurationLoader.cs           |  27 +++--
 .../Configuration/ConfigurationValidator.cs        |  10 +-
 src/CqrsLens.Cli/Configuration/CqrsLensSettings.cs |   5 +-
 src/CqrsLens.Cli/CqrsLens.Cli.csproj               |   4 +-
 .../Markdown/ArchitectureDiffMarkdownRenderer.cs   |   6 +-
 src/CqrsLens.Cli/Execution/CliRunner.cs            |   2 +-
 src/CqrsLens.Cli/Execution/ScanCommand.cs          |   1 +
 src/CqrsLens.Cli/Execution/ScanReporter.cs         |   6 +
 src/CqrsLens.Cli/Mcp/McpRequestRouter.cs           |   2 +-
 .../Mcp/McpScanFingerprintCalculator.cs            |   2 +-
 src/CqrsLens.Cli/Summary/SummaryCountsWriter.cs    |   2 +-
 .../Analysis/ConfigurationProvenance.cs            |   3 +
 .../Agent/AgentArchitectureMapGenerator.cs         |   4 +-
 .../Docs/DocsBuildingBlocksPageWriter.cs           |   2 +-
 src/CqrsLens.Reporting/Docs/DocsFormatting.cs      |   2 +-
 src/CqrsLens.Reporting/Html/HtmlReportGenerator.cs |   4 +-
 .../Markdown/MarkdownOverviewWriter.cs             |   2 +-
 .../Markdown/MarkdownReportWriter.cs               |   6 +-
 .../Sarif/SarifReportGenerator.cs                  |   6 +-
 tests/CqrsLens.Tests/ArchitectureBoundaryTests.cs  |   4 +-
 tests/CqrsLens.Tests/ArchitectureDiffTests.cs      |   4 +-
 tests/CqrsLens.Tests/CiOutputTests.cs              |   4 +-
 tests/CqrsLens.Tests/CliTests.cs                   |   8 +-
 tests/CqrsLens.Tests/DeepSliceAndAgentTests.cs     |   2 +-
 tests/CqrsLens.Tests/F3DocsExportTests.cs          |   2 +-
 tests/CqrsLens.Tests/McpServerTests.cs             |   2 +-
 tests/CqrsLens.Tests/V02FeatureTests.cs            |   4 +-
 tests/CqrsLens.Tests/VersionTests.cs               |   4 +-
 tests/CqrsLens.Tests/packages.lock.json            |  22 ++--
 44 files changed, 330 insertions(+), 313 deletions(-)
```

---

## 2. CP3 Gate Evidence (Gates 1–9)

### Gate 1 — Legacy Parity (`PASS`)
- Test suite: `750` passed, `68` skipped (external-corpus tests absent on Box #4), `0` failed.
- `FrozenQuestionSet.cs` `0`-byte diff.
- Model JSON F3 (`18ca9c6`) vs F4 (`2.0.0`) with `--generated-at 2026-01-01T00:00:00Z` is byte-identical except `"toolVersion": "1.1.0"` -> `"2.0.0"` on all 14 emitted JSONs:
  - `GoldenCorpus` `text` (`88,082` bytes) & `roslyn` (`88,084` bytes)
  - `Adversarial` `text` (`22,259` bytes) & `roslyn` (`22,261` bytes)
  - `DiffPair/base` `text` (`4,785` bytes) & `roslyn` (`4,787` bytes)
  - `DiffPair/head` `text` (`2,365` bytes) & `roslyn` (`2,367` bytes)
  - `StrictFailure` `text` (`2,510` bytes) & `roslyn` (`1,376` bytes)
  - `SampleShop` `text` (`58,604` bytes) & `roslyn` (`58,606` bytes)
  - `nopCommerce` `text` (`33,747,841` bytes, SHA-256 `bd18c8817d730e224951e58ef237b6c626b305485cf2c1258d2dc730391396d5`) & `roslyn` (`33,747,843` bytes, SHA-256 `b40f137a09486acf12823494b8b21e332af9b7a3f8262d9815a32367033e6e78`)

### Gate 2 — Determinism (`PASS`)
Two full consecutive runs (`run1` vs `run2`, pinned `--generated-at 2026-01-01T00:00:00Z`) covering `--format json` (`text` & `roslyn`), `md`, `--format html --render svg`, `--format agent`, `--sarif`, `--summary`, `--export-docs`, and `--export-svg`:
- `GoldenCorpus`: `35` files byte-identical
- `Adversarial`: `33` files byte-identical
- `DiffPair/base`: `23` files byte-identical
- `DiffPair/head`: `19` files byte-identical
- `StrictFailure`: `19` files byte-identical
- `SampleShop`: `35` files byte-identical
- `nopCommerce`: `17` files byte-identical
- **Total:** `181` files verified `0`-byte diff across repeated runs (`GATE 2 TOTAL: 181 files verified 0-byte diff`).

### Gate 3 — Schema Correctness (`PASS`)
- Field-path enumeration across all `14` emitted JSONs (`5` fixtures + `SampleShop` + `nopCommerce` across `text` and `roslyn`):
  - `Observed distinct instance paths across 14 scans: 384, Declared schema paths: 491, Undeclared: 0`
  - Missing required top-level fields: `0`
- `F4SchemaAndRenameTests.cs` (`all passing`):
  - `R_SCHEMA_AllFixtures_BothModes_ValidateAgainstModelSchema`: `12/12` pass
  - `R_SCHEMA_SyntheticFullFeatureModel_ValidatesAgainstModelSchema_AndHasZeroUndeclaredPaths`: passes (validates all 22 required + 12 conditional sections)
  - `R_SCHEMA_CorruptedSamples_AreRejected`: `10/10` pass (rejects missing/wrong `schemaVersion`, wrong type, unknown properties at root/summary/component, invalid enums, empty conditional `relations`, missing `summary` field)
  - `R_SCHEMA_StabilityDoc_MatchesModelSchemaFieldForField`: passes (top-level 34 properties + 43 `$defs` tables and enums match `docs/model-schema.md`)
  - `R_SCHEMA_CONFIG_NoctuaSchema_CoversAllKnownRules_AndValidatesIntent`: passes (all 14 `KnownRules.All` codes, full `intent` accepted, invalid glob `*`, empty module/publicSurface, invalid boundary severity, and unknown keys rejected)

### Gate 4 — Rename Correctness (`PASS`)
- **Output grep gate:** Checked `91` generated non-SVG output files (`md`, `html`, `json`, `agent`, `sarif`, `summary`, `docs/*.md`) across all 7 corpora (`GoldenCorpus`, `Adversarial`, `DiffPair/base`, `DiffPair/head`, `StrictFailure`, `SampleShop`, `nopCommerce`) — `0` unchartered `cqrslens` occurrences outside allowed compat tokens (`<!-- cqrslens-architecture-diff -->`, `"cqrslens/v1"`, `cqrslens-diagram-`, and documented legacy fallback notes).
- **Console `--version` / `--help`:** Verified `Noctua 2.0.0` and `Usage: noctua <path> [options]` with `.noctua.json (or .cqrslens.json)` in `R_RENAME_GeneratedOutputs_UseNoctuaAndPreserveCompat`.
- **UNCHANGED verification (`task-583`):**
  - C# namespaces: `namespace CqrsLens;` untouched (`ArchitectureBoundaryTests` `8/8` pass)
  - CLI flags in `CliParser.cs`: `ZERO changed CLI flags`
  - MCP tool names in `McpToolCatalog.cs`: unchanged (`get_architecture_dna`, `trace_slice`, `trace_entity_impact`, `check_architecture`, `trace_request_impact`, `verify_claim`, `diff_architecture`)
  - Exit codes in `ExitCodes.cs`: `ZERO changed exit codes`

### Gate 5 — Compatibility (`PASS`)
- **`R_COMPAT_ConfigDiscovery_PrecedenceAndFallbackWithIntent`:**
  - `.cqrslens.json` alone with `intent` loaded cleanly and evaluated intent checks (`C-INTENT-STALE`).
  - `.noctua.json` + `.cqrslens.json` together: `.noctua.json` won, CLI printed `Config: .noctua.json takes precedence over .cqrslens.json.`, JSON model stayed clean.
  - Ephemeral `nopCommerce` scratch copy confirmed `.cqrslens.json` fallback and `.noctua.json` precedence + `2,627` `C-BOUNDARY` warnings.
  - `intent: null` vs omitted config emitted byte-identical JSON.
- **`R_COMPAT_Legacy110Baseline_F3DiffJson_AndMcpContract`:**
  - Checked `1.1.0`-era baseline file (`schemaVersion: 1`) gating `StrictFailure` under `--strict` -> exit `0`.
  - Checked F3 `1.1.0` `ARCHITECTURE.json` input to `--diff` (both `.md` and `.json` outputs) -> exit `0`.
- **Live F3 vs F4 MCP stdio probe (`task-581`):**
  - `F3 serverInfo: {'name': 'cqrslens', 'version': '1.1.0'}` -> `F4 serverInfo: {'name': 'noctua', 'version': '2.0.0'}`
  - Responses for `tools/list`, `get_architecture_dna`, `check_architecture`, and `verify_claim`: **100% byte-identical**.

### Gate 6 — `nopCommerce` Ground-Truth Reconciliation (`PASS`)
Verified across `text` JSON, `roslyn` JSON, `ARCH.md`, and exported `docs/` tree:
- `summary`: `projects=35, requests=0, handlers=0, validators=105, behaviors=0, dbContexts=0, notifications=0, notificationHandlers=0, endpoints=0, errors=0, warnings=0`
- `components`: `516` (`service: 282`, `repository: 1`, `controller: 128`, `validator: 105`)
- `spineEndpoints`: `1,151`
- `relations`: `8,764` (`occurrences: 17,422`)
- Docs tree (`8` files): `01-context.md`, `02-building-blocks.md`, `03-runtime.md`, `04-data-model.md`, `05-cross-cutting.md`, `06-findings.md`, `diagrams/layers.svg`, `diagrams/project-graph.svg`; headers show `Noctua 2.0.0`, tables reconcile (`35` projects, `105` validators).

### Gate 7 — Regression & Warnings (`PASS`)
- Multi-format F3 vs F4 comparison across all `7` corpora (`json`, `md`, `html_mermaid`, `html_svg`, `agent`, `sarif`, `summary`, `docs_tree`, plus `DiffPair` `--diff-output` `md` & `json`): `0` non-whitelist differences.
- `--strict` exit code parity on `StrictFailure`: `F3=4 F4=4`.
- `H7` preserved (`RequestSequenceBuilder.cs` `0`-byte diff).
- Normalized compiler/analyzer warnings (`dotnet build CqrsLens.sln -c Release --no-incremental --nologo` vs `/tmp/cqrslens-f3base`):
  - `F3 normalized unique warnings: 26`
  - `F4 normalized unique warnings: 26`
  - `NEW warnings in F4: 0`
  - `CqrsLens.Core warnings: 0`

### Gate 8 — Performance (`PASS` in Session 1)

Measured via `python3` wall-clock harness (`dotnet <dll> <corpus> ... --generated-at 2026-01-01T00:00:00Z --no-banner --no-color`, 1 warmup + 3 interleaved timed runs per cell, median, Session 1 idle Box #4; threshold: no cell `> +10%`):

| Cell | F3 Median (ms) | F3 Runs (ms) | F4 Median (ms) | F4 Runs (ms) | Delta | Status |
|---|---:|---|---:|---|---:|---|
| `GoldenCorpus text/json` | `392.8` | `[392.8, 387.9, 410.0]` | `393.5` | `[393.5, 402.1, 375.0]` | `+0.19%` | OK |
| `GoldenCorpus text/md` | `428.2` | `[455.6, 428.2, 401.3]` | `436.8` | `[456.6, 436.8, 408.9]` | `+2.00%` | OK |
| `GoldenCorpus roslyn/json` | `1030.6` | `[1022.1, 1073.5, 1030.6]` | `1044.5` | `[1044.5, 1040.6, 1080.2]` | `+1.35%` | OK |
| `Adversarial text/json` | `305.6` | `[292.8, 305.6, 338.1]` | `320.2` | `[315.2, 320.2, 331.8]` | `+4.77%` | OK |
| `Adversarial text/md` | `366.1` | `[344.0, 366.1, 380.0]` | `370.7` | `[367.8, 386.1, 370.7]` | `+1.26%` | OK |
| `Adversarial roslyn/json` | `927.3` | `[903.0, 978.0, 927.3]` | `907.0` | `[901.4, 907.0, 938.1]` | `-2.20%` | OK |
| `DiffPair/base text/json` | `236.2` | `[249.0, 236.2, 226.4]` | `253.4` | `[253.4, 253.7, 227.7]` | `+7.29%` | OK |
| `DiffPair/base text/md` | `282.4` | `[287.2, 276.0, 282.4]` | `285.7` | `[284.2, 312.7, 285.7]` | `+1.18%` | OK |
| `DiffPair/base roslyn/json` | `812.6` | `[826.7, 812.6, 809.9]` | `789.9` | `[797.9, 789.9, 764.5]` | `-2.80%` | OK |
| `DiffPair/head text/json` | `201.3` | `[201.3, 188.5, 221.7]` | `202.0` | `[202.0, 184.1, 213.2]` | `+0.36%` | OK |
| `DiffPair/head text/md` | `247.7` | `[247.4, 247.7, 255.0]` | `258.2` | `[247.7, 263.8, 258.2]` | `+4.21%` | OK |
| `DiffPair/head roslyn/json` | `737.3` | `[732.3, 778.1, 737.3]` | `745.5` | `[792.8, 745.5, 716.9]` | `+1.12%` | OK |
| `StrictFailure text/json` | `217.3` | `[193.2, 217.3, 271.2]` | `216.0` | `[210.9, 216.0, 217.3]` | `-0.57%` | OK |
| `StrictFailure text/md` | `248.6` | `[243.0, 248.6, 265.6]` | `254.5` | `[265.0, 254.5, 251.0]` | `+2.35%` | OK |
| `StrictFailure roslyn/json` | `719.4` | `[699.8, 719.4, 751.2]` | `710.0` | `[691.1, 710.0, 722.2]` | `-1.30%` | OK |
| `SampleShop text/json` | `285.6` | `[261.1, 293.1, 285.6]` | `277.5` | `[277.5, 260.7, 297.3]` | `-2.86%` | OK |
| `SampleShop text/md` | `343.3` | `[322.6, 343.3, 343.4]` | `341.2` | `[333.7, 359.5, 341.2]` | `-0.60%` | OK |
| `SampleShop roslyn/json` | `911.7` | `[877.2, 968.0, 911.7]` | `939.1` | `[926.2, 962.3, 939.1]` | `+3.00%` | OK |
| `nopCommerce text/json` | `3713.5` | `[3563.7, 3897.5, 3713.5]` | `3444.5` | `[3444.5, 3476.5, 3223.9]` | `-7.25%` | OK |
| `nopCommerce text/md` | `3471.9` | `[3208.4, 3471.9, 3530.4]` | `3545.2` | `[3616.9, 3545.2, 3251.2]` | `+2.11%` | OK |
| `nopCommerce roslyn/json` | `10916.3` | `[10916.3, 11072.1, 10766.6]` | `11044.6` | `[10553.3, 11044.6, 11156.8]` | `+1.18%` | OK |
| `GoldenCorpus export-docs` | `528.5` | `[544.1, 523.4, 528.5]` | `512.4` | `[512.4, 500.9, 558.9]` | `-3.05%` | OK |
| `SampleShop export-docs` | `464.7` | `[464.7, 460.8, 513.1]` | `502.7` | `[484.6, 505.3, 502.7]` | `+8.17%` | OK |
| `nopCommerce export-docs` | `3667.5` | `[3667.5, 3625.1, 3688.1]` | `3811.1` | `[3811.1, 3905.5, 3649.2]` | `+3.91%` | OK |

`Session 1 complete: 24 cells, >+10% breaches: 0`

### Gate 9 — Scope & Cleanup (`PASS`)
- Removed `/tmp/cqrslens-f3base` (`git worktree remove /tmp/cqrslens-f3base`) and `/tmp/f4-*`; `git worktree list` reports only `/data/workspace/cqrslens`.
- `git diff HEAD -- '*.csproj'` contains ONLY `<ToolCommandName>noctua</ToolCommandName>` and `<PackageId>Noctua</PackageId>` in `src/CqrsLens.Cli/CqrsLens.Cli.csproj`; zero `<PackageReference>` changes.
- Zero corpus/class-name special-casing in `src/`.
- `VersionTests` pin check passes.

---

## 3. Release Checklist (`Noctua 2.0.0`)

Each step executes strictly in order after **VM 6-corpus + full-A/B verification** passes:

| Step | Owner | Action / Exact Command or Click | Verification |
|---|---|---|---|
| **1. VM Verification & Cleanup** | **Mariam** | Transfer F4 diff + untracked files (`git bundle` + tarball, hash-verified) to VM, export `CQRSLENS_CORPUS_ROOT` and `CQRSLENS_ABP_ROOT`, run full `818` test suite (`0` skips on non-ABP Roslyn known exit-5 baseline), 6-corpus A/B, and VM Gate 8 perf. | `789 + 29` tests pass on VM (`0` failed); 6-corpus byte-identity & perf `<= +10%`. |
| **2. Commit & Merge** | **Abdullah / Mariam** (per Abdullah's direction) | Review uncommitted diff (`foundations/f4-noctua`), make the code commit(s) and follow the `18ca9c6` working-note commit pattern (`BRIEF_F4.md`, `CHECKPOINT_F4_CP*.md`, `CqrsLens_To_Noctua_Rename_Plan.md`), merge `foundations/f4-noctua` to `main`. | `git log -n 3 --stat`, `git status` clean on `main`. |
| **3. Pre-Tag NuGet ID Check** | **Abdullah / Mariam** | Confirm NuGet ID `Noctua` is still unclaimed before pushing tag: `curl -sfI https://api.nuget.org/v3-flatcontainer/noctua/index.json` | HTTP `404` returned for `noctua/index.json`. |
| **4. GitHub Repository Rename (`D6`)** | **Abdullah** (Settings click) | On GitHub (`AbdullahHakima/cqrslens` -> **Settings -> General -> Repository name**): rename `cqrslens` to `noctua`. **Never** create a new repository named `cqrslens`. | Visiting `https://github.com/AbdullahHakima/cqrslens` redirects to `https://github.com/AbdullahHakima/noctua`. |
| **5. Refresh `docs/sample-output/` & `console-run.png` (optional pre-tag)** | **Abdullah / Mariam** | Decide whether to regenerate `docs/sample-output/ARCHITECTURE.md` / screenshot under `Noctua 2.0.0` before tagging, or leave checked-in historical samples as-is. | `dotnet test` remains green (`VersionTests` passes). |
| **6. Tag & Publish Release (`v2.0.0`)** | **Abdullah** | Push tag `v2.0.0` and publish GitHub Release `v2.0.0` so `.github/workflows/release.yml` runs Trusted Publishing: `git tag -a v2.0.0 -m "Noctua 2.0.0" && git push origin main v2.0.0` | `.github/workflows/release.yml` passes: `Build`, `Test`, `Pack`, local tool smoke test, and `Push to NuGet`. |
| **7. NuGet Live Package Verification** | **Abdullah / Mariam** | Wait for NuGet indexing, verify flat container & clean-environment global tool install: `curl -sf https://api.nuget.org/v3-flatcontainer/noctua/index.json \| grep '"2.0.0"'` then in a clean shell/VM: `dotnet tool install --global Noctua --version 2.0.0 && noctua --version && noctua samples/SampleShop -o /tmp/smoke.md` | Flat container lists `2.0.0`; `noctua --version` prints `Noctua 2.0.0`; real scan exits `0`. |
| **8. Old `CqrsLens` NuGet Deprecation (`D5`)** | **Abdullah** (nuget.org click, **ONLY AFTER Step 7 succeeds**) | On `nuget.org/packages/CqrsLens/manage` -> **Deprecation**: select **All versions** (`0.1.0` .. `1.1.0`), reason **"The package has been renamed / legacy"**, alternate package ID: **`Noctua`**, custom message: `"CqrsLens is now Noctua. Run: dotnet tool uninstall -g CqrsLens && dotnet tool install -g Noctua"`. **Never** unlist any version. | `nuget.org/packages/CqrsLens` shows deprecation banner pointing to `Noctua`, and `dotnet tool install -g CqrsLens --version 1.1.0` still succeeds. |
| **9. Raw GitHub URL Redirect Check (`D6`)** | **Abdullah / Mariam** | Test whether existing `$schema` raw URLs redirect after repo rename: `curl -ILs -o /dev/null -w '%{http_code} %{url_effective}\n' https://raw.githubusercontent.com/AbdullahHakima/cqrslens/main/cqrslens.schema.json` (and `.../main/noctua.schema.json`). Also verify old release links (`.../releases/tag/v1.1.0`). | Record whether HTTP redirect resolves to `AbdullahHakima/noctua`; if raw URLs do not redirect, add a short note in the v2.0.0 release notes that old `$schema` editor URLs do not auto-redirect. |
| **10. Separate Comms & Site Flip (`D7`)** | **Abdullah** | Outside F4 repo scope: update landing site `BRAND` (`Noctua`, `2.0.0`, `dotnet tool install -g Noctua`), rebuild Examples page from live `2.0.0` measurements, publish blog/LinkedIn announcement once NuGet + deprecation + redirects are confirmed live. | Live site & announcement match shipped `Noctua 2.0.0` capabilities. |

---

## 4. Open Issues & Notes

1. **Unverified at release time (carried from `BRIEF_F4.md` for Release Checklist Steps 3 & 9):**
   - Live availability of NuGet ID `Noctua` (re-check right before tagging) and `support@nuget.org` download-count transfer inquiry (Abdullah's call).
   - Whether `raw.githubusercontent.com` redirects after GitHub repo rename `cqrslens` -> `noctua` (verified only after Abdullah renames the repo in Step 4).
2. **External 6-corpus VM verification:** On Box #4, `68` corpus tests skipped (`CQRSLENS_CORPUS_ROOT` / `CQRSLENS_ABP_ROOT` not present). Full `818`-test + 6-corpus A/B + VM Gate 8 run executes on Mariam's machine next.
3. **`STOP` constraint honoured:** No `git commit`, `git push`, `git tag`, `dotnet nuget push`, or GitHub/NuGet account action performed (`H9`).
