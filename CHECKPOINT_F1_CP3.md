# Checkpoint F1 — CP3: Final Gates Verification, Parity & Handoff

- **Stage:** CqrsLens F1 — Unify on the F0 Spine (`BRIEF_F1.md`, `F0_METAMODEL_v2.1.md`)
- **Checkpoint:** CP3 (`status: done`)
- **Branch:** `foundations/f1-spine` (HEAD remains `bd49fc3`, `release: prepare 1.1.0`; uncommitted per H9)
- **Baseline worktree:** `/tmp/cqrslens-baseline` removed (`git worktree remove --force /tmp/cqrslens-baseline`), `git worktree list` shows only `/data/workspace/cqrslens`.

---

## 1. Acceptance Gates (F0 §8 & `BRIEF_F1.md` Gates 1–7)

| Gate | Requirement Summary | Verification & Result | Status |
|---|---|---|---|
| **Gate 1** | Spine model completeness, layered IDs, bidirectional legacy projection parity | `SpineTypesTests` (12 tests covering `ProjectId`, `SymbolId`, `EndpointId`, roles, `SpineRelation`, provenance, unproven populations) + `SpineF1ParityAndGatesTests.BidirectionalParity_MutationsThroughLegacyListsUpdateSpineAndProjectBack` — all pass | PASS |
| **Gate 2** | Unique IDs across `Projects`, `Components`, `Endpoints`, and `Relations` | `SpineF1ParityAndGatesTests.F0_Gates2To4_HoldForTextAndRoslynScansAcrossEveryInRepoFixture` across all 6 in-repo fixtures (`GoldenCorpus`, `Adversarial`, `DiffPair/base`, `DiffPair/head`, `StrictFailure`, `samples/SampleShop`) x `Text` & `Roslyn` — all pass | PASS |
| **Gate 3** | Structured evidence completeness (`EvidenceRule`, `Location` or `LocationOmittedReason`, inferred endpoints not all `Proven`) | `F0_Gates2To4_HoldForTextAndRoslynScansAcrossEveryInRepoFixture` — all component roles, endpoints, and relations satisfy completeness assertions | PASS |
| **Gate 4** | Unproven/ambiguity separation, `ResolvesTo` candidate-vs-binding, F0 §4.3 `Wraps` applicability, `LayerProvenance` (`Guessed` never `Proven`) | Verified by `F0_Gates2To4_HoldForTextAndRoslynScansAcrossEveryInRepoFixture`, `PromotesValidatesAndTouchesDataRelations_OnGoldenCorpus`, `ResolvesTo_CapturesCandidateVsBindingAndExcludesAmbiguous`, `Wraps_EnforcesClosedOpenGenericAndUnprovenRules_F0Section43`, `LayerProvenance_TracksConfiguredOverridesIndependentlyFromGuessed` — all pass | PASS |
| **Gate 5** | Deterministic byte-identical outputs across repeated scans | `F0_Gate5_Determinism_RepeatedScansProduceByteIdenticalOutputs` across all 6 in-repo fixtures with fixed `GeneratedAt` (`Json`, `Markdown`, `Html`, `Sarif`, `Agent`) — all pass | PASS |
| **Gate 6** | Baseline byte-for-byte parity vs `bd49fc3` (only additive JSON `"relations"` allowed), `H3` & `H4` | A/B comparison (`verify_ab.py`): `OVERALL PARITY: PASS` across all 6 in-repo fixtures x `Text` & `Roslyn` for `md`, `summary`, `sarif`, `console`, `html`, `agent`, `json` non-`"relations"` keys (`GoldenCorpus/text` adds 25 `"relations"` entries with `schemaVersion: 1`), `DiffPair/diff.md`, `DiffPair/diff.json`, `StrictFailure --strict` exit code/console, and `MCP_stdio_GoldenCorpus_and_Diff`. `FrozenQuestion` & `ArchitectureBoundaryTests` pass unmodified (`38 passed, 0 failed` filtered) | PASS |
| **Gate 7** | Zero regressions (`H2`) + performance A/B vs `bd49fc3` | Full solution suite: `651 passed, 68 skipped, 719 total, 0 failed` (`CqrsLens.Tests.dll`, `Release`). CLI `--profile` 3-run median timing comparison (post-warmup, `bd49fc3` vs F1): `OVERALL TIMING: PASS` across all 14 targets | PASS |

---

## 2. Gate 7 CLI `--profile` Timing Comparison (`bd49fc3` vs F1)

Measured via `verify_ab.py` (1 warmup run + 3-run median per target, `Release` build):

| Target / Mode | Baseline Elapsed | F1 Elapsed | Elapsed Delta | Baseline Wall | F1 Wall | Wall Delta |
|---|---:|---:|---:|---:|---:|---:|
| `GoldenCorpus/text` | 222.0 ms | 235.0 ms | +5.9% | 320.0 ms | 358.1 ms | +11.9% |
| `GoldenCorpus/roslyn` | 797.0 ms | 858.0 ms | +7.7% | 903.1 ms | 982.3 ms | +8.8% |
| `SampleShop/text` | 192.0 ms | 197.0 ms | +2.6% | 298.9 ms | 314.3 ms | +5.2% |
| `SampleShop/roslyn` | 831.0 ms | 856.0 ms | +3.0% | 943.2 ms | 1004.6 ms | +6.5% |
| `Adversarial/text` | 202.0 ms | 209.0 ms | +3.5% | 302.9 ms | 325.1 ms | +7.3% |
| `Adversarial/roslyn` | 787.0 ms | 782.0 ms | -0.6% | 897.9 ms | 910.3 ms | +1.4% |
| `DiffPair_base/text` | 155.0 ms | 147.0 ms | -5.2% | 254.1 ms | 273.8 ms | +7.7% |
| `DiffPair_base/roslyn` | 688.0 ms | 696.0 ms | +1.2% | 801.9 ms | 831.7 ms | +3.7% |
| `DiffPair_head/text` | 124.0 ms | 134.0 ms | +8.1% | 216.6 ms | 236.8 ms | +9.4% |
| `DiffPair_head/roslyn` | 623.0 ms | 615.0 ms | -1.3% | 743.7 ms | 746.1 ms | +0.3% |
| `StrictFailure/text` | 119.0 ms | 123.0 ms | +3.4% | 220.1 ms | 233.8 ms | +6.2% |
| `StrictFailure/roslyn` | 606.0 ms | 632.0 ms | +4.3% | 705.3 ms | 765.6 ms | +8.6% |
| `CqrsLens_src/text` | 1310.0 ms | 1437.0 ms | +9.7% | 1420.6 ms | 1565.7 ms | +10.2% |
| `CqrsLens_src/roslyn` | 3450.0 ms | 3409.0 ms | -1.2% | 3564.3 ms | 3546.1 ms | -0.5% |

**Result:** `OVERALL TIMING: PASS`.

---

## 3. Hard Constraints (`H1`–`H9`) & Repository State

- **H1 (No new analyzer capabilities/checks/rules):** Preserved.
- **H2 (Existing tests pass unmodified; new tests additive):** `651 passed, 68 skipped, 719 total, 0 failed` in `Release`.
- **H3 (Byte-identical non-JSON outputs + additive JSON `"relations"` only):** Verified across all 6 in-repo fixtures, `--diff`, `--strict`, and MCP stdio.
- **H4 (`FrozenQuestionSet` intact):** Filtered run passes (`ArchitectureBoundaryTests`, `FrozenQuestion`, `SpineTypesTests`, `SpineF1ParityAndGatesTests`: `38 passed, 0 failed`).
- **H5 (No version, `CHANGELOG.md`, `ROADMAP.md`, `README.md`, CLI flag, or `*.csproj` changes):** `git diff -- '*.csproj' CHANGELOG.md README.md ROADMAP.md` is empty.
- **H6 (4-project dependency chain & `<=1` top-level type per `.cs` file in `src/*`):** Verified by `ArchitectureBoundaryTests`.
- **H7 (`RequestSequenceBuilder` rendering gap):** Preserved untouched and documented at `src/CqrsLens.Reporting/Sequences/RequestSequenceBuilder.cs:92-95`.
- **H8 (Determinism):** Verified with fixed `--generated-at 2026-01-15T12:00:00Z`.
- **H9 (Never commit, never push):** `git log -n 2 --oneline` keeps `bd49fc3` at `HEAD`. Tracked changes remain uncommitted in the working tree (`59 files changed, 1425 insertions(+), 399 deletions(-)`).

---

## 4. Open Issues & Reproduction

- **H7 deferred rendering gap:** `RequestSequenceBuilder` continues rendering `BehaviorOrdering.Flattened(result.Behaviors)` rather than filtering by `RelationKind.Wraps` applicability.
- **External corpus scope:** 68 existing corpus tests were skipped because external corpora reside outside the workspace (`/mnt/...`); full 6-corpus box #3 benchmark verification remains outside this workspace scope per `BRIEF_F1.md` Gate 7.
- **Reproduction commands:**
  ```bash
  dotnet build CqrsLens.sln -c Release --nologo
  dotnet test CqrsLens.sln -c Release --nologo
  dotnet test CqrsLens.sln -c Release --nologo --no-build --filter "FullyQualifiedName~SpineTypesTests|FullyQualifiedName~SpineF1ParityAndGatesTests|FullyQualifiedName~ArchitectureBoundaryTests|FullyQualifiedName~FrozenQuestion"
  git diff -- '*.csproj' CHANGELOG.md README.md ROADMAP.md
  ```
