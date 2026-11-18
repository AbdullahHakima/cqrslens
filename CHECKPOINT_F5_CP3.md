# Stage F5 Checkpoint 3 — Full Verification Gates (G1–G9) & Handoff

- **Date:** 2026-11-17
- **Status:** `DONE` (all G1–G9 gates passed on Box #4; branch `foundations/f5-spine-reports` left uncommitted per `H9`; **stopped after CP3** ready for Mariam's VM 6-corpus verification)
- **Branch:** `foundations/f5-spine-reports` (base F4 HEAD `f8902e9e75ac62d936d285b5e42f2be536228f25`)
- **Antigravity CLI Version:** `1.5.9` (`agy --version`)
- **Corpus Pin:** `/data/workspace/corpora/nopcommerce` (`release-4.90.0` at `e7a52b9199f9c7651839e92826df47f668f3c767`, clean read-only)

---

## Summary of Verification Gates (G1–G9)

| Gate | Requirement | Observed Box #4 Result | Status |
|---|---|---|---|
| **G1 Suite** | `818 + N` tests green, `0` failed, `0` existing tests modified (`H1`) | `Passed! - Failed: 0, Passed: 760, Skipped: 68, Total: 828, Duration: 2 s` (`750` existing + `10` new F5 tests in `F5SpineReportTests.cs`; `68` skipped external-corpus tests absent on Box #4; `ArchitectureBoundaryTests` `8/8` pass) | **PASS** |
| **G2 Determinism** | Repeated runs byte-identical across MD, HTML (`mermaid` & `svg`), JSON, and docs tree in both modes (`D6`, `H8`) | `271` total output files across `14` corpus/mode pairs (`nopCommerce`, `SampleShop`, `GoldenCorpus`, `Adversarial`, `DiffPair/base`, `DiffPair/head`, `StrictFailure` × `text` & `roslyn`) verified `0`-byte diff across `run1` vs `run2` (`json`, `md`, `html-mermaid`, `html-svg`, `agent`, `sarif`, `summary`, `docs/*`); `text == roslyn` parity confirmed on Spine and `02-building-blocks.md` (excluding `StrictFailure` whose underlying model intentionally differs) | **PASS** |
| **G3 JSON identity + schema** | JSON byte-identical to F4 baseline (`D5`); schema validation against `noctua.model.schema.json` with `0` undeclared paths | `14 / 14` JSON files (`7` corpora × `2` modes) `0`-byte diff vs F4 (`f8902e9`); independent Python draft `2020-12` validator validated all `14` JSONs with `observed_paths = 383`, `declared_paths = 490`, `undeclared = 0`, `0` errors | **PASS** |
| **G4 Insertion-only diff** | MD/HTML vs F4 differ ONLY by the inserted spine section (`H2`, `D1`); `02` docs-extension insertion-only (`D4`) | Across all `14` corpus/mode pairs: stripping `## Architecture Spine` from MD, `<h2>Architecture Spine</h2>` from HTML (`mermaid` & `svg`), and `### Endpoint Inventory` + `### Component Index` from `02-building-blocks.md` yields `0`-byte diff vs F4; pages `01`, `03`, `04`, `05`, `06` and `diagrams/*.svg` have `0`-byte diff | **PASS** |
| **G5 Docs export** | `02-building-blocks.md` extended per `D4`; file set unchanged; `--check-docs` passes fresh and catches drift | File sets identical (`8` files on `nopCommerce`, `17` on `SampleShop`/`GoldenCorpus`); `--check-docs` exited `0` (`Docs freshness gate passed`) in both `text` and `roslyn` modes; mutating `02-building-blocks.md` under `### Component Index` exited `4` (`02-building-blocks.md [modified]: section changed (Component Index)`) | **PASS** |
| **G6 Reconciliation** | Spine counts, rows, and top-25 degrees reconcile independently against JSON on MD, HTML, and `02-building-blocks.md` | Independent Python reconciler verified `100%` match across all `14` corpus/mode pairs (both `mermaid` and `svg` HTML, MD, and `02-building-blocks.md`): summary counts, role rows, relation summary rows, Endpoint Inventory rows (`1,151` on `nopCommerce`, `6` on `GoldenCorpus`), Component Index rows (`516` on `nopCommerce`, `21` on `GoldenCorpus`, `16` on `SampleShop`), and top-25 Most-Connected degrees (`In`/`Out`/`Total`) | **PASS** |
| **G7 Corpus A/B, Warnings & MCP** | Zero model count movement, warnings identical, MCP answers identical (`D3`, `H3`–`H7`) | Model counts unchanged (`nopCommerce` `516 / 1,151 / 8,764`, `GoldenCorpus` `21 / 6 / 27`, `SampleShop` `16 / 0 / 14`); `agent`, `sarif`, `summary`, `--diff-output` (`md`/`json`), `--strict` (`4 == 4`), and live 5-request MCP stdio probe (`initialize`, `tools/list`, `get_architecture_dna`, `check_architecture`, `verify_claim`) `100%` byte-identical to F4; `--no-incremental` Release warnings: `F4 = 26`, `F5 = 26`, `0` new warnings (`CqrsLens.Core = 0`) | **PASS** |
| **G8 Perf** | Report-generation cells `<= +10%` median vs F4 baseline | `26 / 27` cells `<= +9.82%` in Session 1 (including `nopCommerce text/md -3.09%`, `nopCommerce text/html-svg +3.44%`, `nopCommerce text/json -2.55%`); the single Session-1 noisy cell (`DiffPair/head text/json +11.10%`) measured **`+0.09%`** (`225.4 ms` F4 vs `225.6 ms` F5) in 5-run interleaved Session 2; `nopCommerce export-docs` also confirmed **`-7.11%`** in 5-run Session 2 | **PASS** |
| **G9 Diff chartered** | Only `src/CqrsLens.Reporting/`, new tests, and checkpoint/brief files touched; cleanup complete (`H4`–`H10`) | Temporary worktree `/tmp/cqrslens-f4base` and all `/tmp/f5-*` dirs removed (`TMP_CLEAN`); `0`-byte diff across `*.csproj`, schemas, workflows, docs, `Core`, `Analysis`, `Cli`, `Reporting/Json`, `FrozenQuestionSet.cs`; `ToolInfo.Version == 2.0.0`; `nopCommerce` clean at `e7a52b91` | **PASS** |

---

## 1. Gate 1 — Final Test Suite & Architecture Boundaries

Executed after final Gate 9 cleanup in `/data/workspace/cqrslens`:
```bash
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
dotnet test CqrsLens.sln -c Release --nologo
dotnet test CqrsLens.sln -c Release --no-build --nologo --filter "FullyQualifiedName~ArchitectureBoundaryTests"
```
Observed results:
- Full suite: `Passed! - Failed: 0, Passed: 760, Skipped: 68, Total: 828, Duration: 2 s - CqrsLens.Tests.dll (net8.0)`
  - `750` existing tests passing (`0` existing test files modified per `H1`).
  - `10` new F5 tests passing in [F5SpineReportTests.cs](file:///data/workspace/cqrslens/tests/CqrsLens.Tests/F5SpineReportTests.cs).
  - `68` external-corpus tests skipped (Box #4 lacks external verification corpora; covered verification-side on Mariam's VM).
- Boundary suite: `Passed! - Failed: 0, Passed: 8, Skipped: 0, Total: 8, Duration: 40 ms` (confirms 4-project one-way dependency chain and single top-level type per file in `src/`).

---

## 2. Gate 2 & Gate 3 & Gate 6 — Determinism, JSON Schema & Independent Reconciliation

### 2.1 Determinism & Schema (`14` Scans, `271` Files)
- **G2 (`run1 == run2`, pinned `--generated-at 2026-01-01T00:00:00Z`):** `100%` byte-identical across:
  - `nopCommerce` (`15` files each in `text` and `roslyn`)
  - `SampleShop` (`24` files each in `text` and `roslyn`)
  - `GoldenCorpus` (`24` files each in `text` and `roslyn`)
  - `Adversarial` (`23` files each in `text` and `roslyn`)
  - `DiffPair/base` (`18` files each in `text` and `roslyn`)
  - `DiffPair/head` (`16` files each in `text` and `roslyn`)
  - `StrictFailure` (`16` files in `text`, `15` in `roslyn`)
- **G3 JSON identity & validation:**
  - `nopCommerce` JSON SHA-256: `text` (`33,747,841` B) `bd18c8817d730e224951e58ef237b6c626b305485cf2c1258d2dc730391396d5`, `roslyn` (`33,747,843` B) `b40f137a09486acf12823494b8b21e332af9b7a3f8262d9815a32367033e6e78` — exact match to F4.
  - Note: Python's third-party `jsonschema` package is not installed on Box #4; validation was performed both via the in-process C# draft `2020-12` validator (`F4SchemaAndRenameTests.cs`) and an independent recursive Python draft `2020-12` validator against `noctua.model.schema.json` across all `14` JSONs (`observed_paths = 383`, `declared_paths = 490`, `undeclared = 0`, `0` errors).

### 2.2 Independent JSON-to-Report Reconciliation (`G6`)
Verified programmatically by parsing `report.json`, independently recomputing all Spine metrics, and asserting exact equality against Markdown (`## Architecture Spine`), HTML (`<h2>Architecture Spine</h2>`, both `mermaid` and `svg`), and `02-building-blocks.md` (`### Endpoint Inventory` & `### Component Index`):
- **`nopCommerce` (`text` & `roslyn`):** `516` components (`105` validator, `128` controller, `282` service, `1` repository), `1,151` endpoints (`1,151` inferred), `8,764` relations / `17,422` occurrences, `340` unproven items, top-25 hubs topped by ``Nop.Data.EntityRepository`1`` (`622` in, `8` out, `630` total).
- **`GoldenCorpus` (`text` & `roslyn`):** `21` components, `6` endpoints, `27` relations, `21` Component Index rows, `6` Endpoint Inventory rows (`Dispatches to` joined).
- **`SampleShop` (`text` & `roslyn`):** `16` components, `0` endpoints, `14` relations, `1` unproven item, `16` Component Index rows.
- **Note on role ordering:** JSON `components[].roles` is sorted alphabetically by `JsonReportDocumentFactory` (e.g., `["entity", "request"]` on `Adversarial`), whereas human reports and `02-building-blocks.md` render `roles` in `ComponentRole` enum order (`"request, entity"`) per `D2.5`.

---

## 3. Gate 4, Gate 5 & Gate 7 — Insertion-Only Diff, Docs Gate, Warnings & MCP

- **G4 Insertion-only:** Stripping `## Architecture Spine` (MD), `<h2>Architecture Spine</h2>` (HTML `mermaid` & `svg`), and `### Endpoint Inventory` + `### Component Index` (`02-building-blocks.md`) yielded `0`-byte diff vs F4 on every tested corpus and mode.
- **G5 Docs gate:** Fresh `--check-docs` exited `0` in `text` and `roslyn` modes (`nopCommerce`, `SampleShop`, `GoldenCorpus`); mutating `02-building-blocks.md` exited `4` naming `02-building-blocks.md` and `Component Index`.
- **G7 Non-report parity & warnings:**
  - `agent`, `sarif`, `summary`, `DiffPair` `--diff-output` (`md` & `json`), and `StrictFailure` `--strict` (`4 == 4`) matched F4 identically.
  - Live MCP stdio probe on `SampleShop` (`initialize`, `tools/list`, `get_architecture_dna`, `check_architecture`, `verify_claim`): `100%` byte-identical between F4 and F5.
  - Normalized compiler/analyzer warnings (`dotnet build CqrsLens.sln -c Release --no-incremental --nologo`): `F4 = 26` (`Core = 0`), `F5 = 26` (`Core = 0`), `0` new warnings.
  - `dotnet restore CqrsLens.sln --locked-mode --nologo`: exited `0`.

---

## 4. Gate 8 — Performance Benchmarks (Idle CPU, Interleaved `F4` vs `F5`)

Measured via Python wall-clock harness (`dotnet <dll> <corpus> ... --generated-at 2026-01-01T00:00:00Z --no-banner --no-color`, `1` warmup + `3` timed interleaved runs in Session 1; `1` warmup + `5` timed interleaved runs in Session 2; threshold: `<= +10%` median):

| Cell | F4 Median (ms) | F4 Runs (ms) | F5 Median (ms) | F5 Runs (ms) | Delta | Status |
|---|---:|---|---:|---|---:|---|
| `GoldenCorpus text/json` | `322.3` | `[322.3, 327.3, 307.3]` | `305.2` | `[300.2, 305.2, 329.5]` | `-5.31%` | OK |
| `GoldenCorpus text/md` | `369.6` | `[361.8, 369.6, 377.3]` | `375.1` | `[367.0, 375.1, 375.7]` | `+1.49%` | OK |
| `GoldenCorpus text/html-svg` | `383.3` | `[383.3, 373.1, 409.7]` | `393.7` | `[413.7, 387.7, 393.7]` | `+2.71%` | OK |
| `GoldenCorpus roslyn/json` | `916.5` | `[984.3, 911.5, 916.5]` | `935.9` | `[905.9, 935.9, 1028.1]` | `+2.12%` | OK |
| `Adversarial text/json` | `279.5` | `[269.5, 279.5, 284.8]` | `291.0` | `[291.5, 291.0, 275.1]` | `+4.11%` | OK |
| `Adversarial text/md` | `351.7` | `[320.2, 351.7, 352.8]` | `348.9` | `[348.9, 337.1, 376.6]` | `-0.80%` | OK |
| `Adversarial roslyn/json` | `859.9` | `[859.9, 893.5, 777.1]` | `874.3` | `[908.0, 874.3, 787.5]` | `+1.67%` | OK |
| `DiffPair/base text/json` | `221.5` | `[212.3, 221.5, 230.4]` | `229.1` | `[227.2, 229.1, 229.7]` | `+3.43%` | OK |
| `DiffPair/base text/md` | `250.1` | `[256.5, 242.4, 250.1]` | `255.2` | `[255.2, 259.9, 252.8]` | `+2.04%` | OK |
| `DiffPair/base roslyn/json` | `710.4` | `[710.4, 709.5, 733.3]` | `729.8` | `[688.6, 729.8, 751.1]` | `+2.73%` | OK |
| `DiffPair/head text/json` *(S1)* | `195.5` | `[215.0, 195.5, 186.5]` | `217.2` | `[217.2, 220.6, 214.5]` | `+11.10%` | *(S2)* |
| `DiffPair/head text/json` *(S2)* | `225.4` | `[219.1, 236.1, 254.1, 217.2, 225.4]` | `225.6` | `[229.0, 219.2, 252.2, 219.6, 225.6]` | **`+0.09%`** | **OK** |
| `DiffPair/head text/md` | `257.0` | `[281.7, 257.0, 244.8]` | `260.5` | `[259.5, 260.5, 262.1]` | `+1.36%` | OK |
| `DiffPair/head roslyn/json` | `698.1` | `[689.6, 698.1, 759.4]` | `723.8` | `[678.6, 723.8, 760.3]` | `+3.68%` | OK |
| `StrictFailure text/json` | `195.2` | `[193.0, 222.6, 195.2]` | `200.6` | `[198.7, 200.6, 202.0]` | `+2.77%` | OK |
| `StrictFailure text/md` | `242.5` | `[239.1, 242.5, 255.3]` | `256.0` | `[246.8, 297.4, 256.0]` | `+5.57%` | OK |
| `StrictFailure roslyn/json` | `685.9` | `[705.6, 685.9, 639.1]` | `651.5` | `[650.8, 720.2, 651.5]` | `-5.02%` | OK |
| `SampleShop text/json` | `267.3` | `[242.1, 269.5, 267.3]` | `261.6` | `[249.1, 262.8, 261.6]` | `-2.13%` | OK |
| `SampleShop text/md` | `334.6` | `[310.9, 334.6, 343.6]` | `345.7` | `[345.0, 345.7, 350.2]` | `+3.32%` | OK |
| `SampleShop text/html-svg` | `343.5` | `[373.9, 343.5, 334.3]` | `360.8` | `[360.8, 374.0, 349.9]` | `+5.04%` | OK |
| `SampleShop roslyn/json` | `889.5` | `[889.5, 909.4, 885.0]` | `891.7` | `[894.1, 891.7, 885.8]` | `+0.25%` | OK |
| `nopCommerce text/json` | `3367.3` | `[3416.2, 3367.3, 3113.9]` | `3281.3` | `[3596.4, 3081.7, 3281.3]` | `-2.55%` | OK |
| `nopCommerce text/md` | `3104.0` | `[3435.3, 3070.9, 3104.0]` | `3008.0` | `[2727.5, 3008.0, 3241.5]` | `-3.09%` | OK |
| `nopCommerce text/html-svg` | `2902.0` | `[2902.0, 2856.2, 2927.4]` | `3001.8` | `[3001.8, 3077.4, 2996.5]` | `+3.44%` | OK |
| `nopCommerce roslyn/json` | `9356.0` | `[9894.5, 8955.6, 9356.0]` | `9422.7` | `[9422.7, 9503.5, 9145.8]` | `+0.71%` | OK |
| `GoldenCorpus export-docs` | `463.2` | `[500.3, 463.2, 424.4]` | `450.7` | `[466.1, 419.9, 450.7]` | `-2.70%` | OK |
| `SampleShop export-docs` | `378.5` | `[354.0, 378.5, 393.1]` | `399.6` | `[376.7, 412.2, 399.6]` | `+5.57%` | OK |
| `nopCommerce export-docs` *(S1)* | `2890.1` | `[2639.3, 2890.1, 2968.9]` | `3173.8` | `[3173.8, 3113.9, 3348.2]` | `+9.82%` | OK |
| `nopCommerce export-docs` *(S2)* | `3514.8` | `[3073.1, 2701.2, 3578.4, 3514.8, 3532.8]` | `3264.8` | `[3264.8, 3321.7, 3143.6, 3340.5, 2704.8]` | **`-7.11%`** | **OK** |

---

## 5. Gate 9 — Scope, Cleanup & Working Tree State

- **Worktree & scratch cleanup:** Removed `/tmp/cqrslens-f4base` and all `/tmp/f5-*` temp directories (`git worktree list` reports only `/data/workspace/cqrslens`).
- **Corpus integrity:** `/data/workspace/corpora/nopcommerce` verified clean (`git status --short` empty) at `e7a52b9199f9c7651839e92826df47f668f3c767`.
- **Version & protected paths:** `Directory.Build.props` (`<Version>2.0.0</Version>`) and `Noctua 2.0.0` CLI `--version` unchanged; `0`-byte diff across `*.csproj`, schemas, workflows, docs, `src/CqrsLens.Core/`, `src/CqrsLens.Analysis/`, `src/CqrsLens.Cli/`, `src/CqrsLens.Reporting/Json/`, and `FrozenQuestionSet.cs`; `0` corpus-specific special-casing hits in `src/`.
- **Changed & untracked files on `foundations/f5-spine-reports` (`uncommitted` per `H9`):**
  - Modified (`3` files, `+5` insertions):
    - [DocsBuildingBlocksPageWriter.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Docs/DocsBuildingBlocksPageWriter.cs)
    - [HtmlReportGenerator.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Html/HtmlReportGenerator.cs)
    - [MarkdownReportWriter.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Markdown/MarkdownReportWriter.cs)
  - Added (`4` source/test files + brief/checkpoints):
    - [SpineReportView.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Spine/SpineReportView.cs)
    - [MarkdownSpineWriter.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Markdown/MarkdownSpineWriter.cs)
    - [HtmlSpineSectionRenderer.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Html/HtmlSpineSectionRenderer.cs)
    - [F5SpineReportTests.cs](file:///data/workspace/cqrslens/tests/CqrsLens.Tests/F5SpineReportTests.cs)
    - [BRIEF_F5.md](file:///data/workspace/cqrslens/BRIEF_F5.md), [CHECKPOINT_F5_CP1.md](file:///data/workspace/cqrslens/CHECKPOINT_F5_CP1.md), [CHECKPOINT_F5_CP2.md](file:///data/workspace/cqrslens/CHECKPOINT_F5_CP2.md), [CHECKPOINT_F5_CP3.md](file:///data/workspace/cqrslens/CHECKPOINT_F5_CP3.md)

---

## 6. Handoff Notes

1. **External 5-corpus VM verification:** The remaining verification corpora (`ABP`, `modular-monolith`, `northwind`, `eshop`, `ardalis`, `jasontaylor`) are absent on Box #4 and were not cloned or fetched; full `828`-test suite + 6-corpus A/B + VM Gate 8 run executes verification-side on Mariam's VM.
2. **Release constraints honoured:** No `git commit`, `git push`, `git tag`, NuGet publish, repo rename, or deprecation performed (`H9`). Stopped cleanly after CP3.
