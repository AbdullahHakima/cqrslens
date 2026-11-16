# CHECKPOINT F3 CP3 — Full Verification & Release-Gate Sign-Off

- **Status:** `done`
- **Branch:** `foundations/f3-docs`
- **Base Commit (`HEAD`):** `db3c5f63741b6a5c9f86142ca149d1e224b01116` (uncommitted working tree; never committed or pushed per H9)
- **Antigravity CLI Version:** `1.5.9`
- **Target Corpus:** `/data/workspace/corpora/nopcommerce` @ `release-4.90.0` (`e7a52b9199f9c7651839e92826df47f668f3c767`, clean read-only) + all 5 in-repo fixtures

---

## Summary of CP3 Verification Gates (Gates 1–9)

| Gate | Requirement Summary | Verification Result | Status |
|---|---|---|---|
| **Gate 1** | Legacy parity & frozen question set unmodified (H2, H3, H4) | `721 passed, 68 skipped, 0 failed` (`dotnet test CqrsLens.sln -c Release --nologo`); `ArchitectureBoundaryTests` `8/8 passed`; `FrozenQuestionSet.cs` `0`-byte diff; all 5 fixtures (`text` + `roslyn`) and `nopCommerce` JSON outputs byte-identical to F2 (`db3c5f6`) | **PASS** |
| **Gate 2** | Determinism (`x2`) & mode parity across scans and `--export-docs` trees (H8) | All 5 fixtures + `nopCommerce` deterministic across duplicate scans and duplicate `--export-docs` runs (`text` and `roslyn`); `text == roslyn` docs trees identical on `GoldenCorpus`, `Adversarial`, `DiffPair/base`, `DiffPair/head`, `nopCommerce` (`StrictFailure` `text != roslyn` matches its underlying F2 model difference) | **PASS** |
| **Gate 3** | Freshness gate correctness & read-only guarantee (R-GATE) | Clean pass exit `0` with `SHA-256` & `st_mtime_ns` unchanged; 1-file mutation exit `4` naming file + section; code edit exit `4` naming 4 drifted files + sections; scan error exit `2` fail-closed | **PASS** |
| **Gate 4** | Intent correctness across `R-FIXTURES` + real `nopCommerce` demo | All `13` test methods (`23` cases) in `F3GateAndIntentTests.cs` + `5` in `F3DocsExportTests.cs` pass (`C-BOUNDARY`, `C-SURFACE`, `C-INTENT-STALE`, fail-closed invalid config, `declared, unverified` ownership, no-intent parity, `--strict` interaction) | **PASS** |
| **Gate 5** | Honesty, provenance labels, zero narrative prose, SVG byte parity | Spot-checked `01`–`06` across `GoldenCorpus` (plain + intent), `Adversarial`, and `nopCommerce`; all pages carry `> Source:` header, honest uncertainty/provenance labels, zero generated prose; `diagrams/*.svg` inner `<svg>` payloads match `--format html --render svg` `11/11` (`GoldenCorpus`) and `2/2` (`nopCommerce`) | **PASS** |
| **Gate 6** | `nopCommerce` model-to-docs ground-truth reconciliation | Every metric on `01`–`06` (`35` projects, `516` components [`282` service, `1` repository, `128` controller, `105` validator], `8,764` relations / `17,422` occurrences, `1,151` endpoints [`595`/`556`], `0/0/0/0/0` CQRS/DbContext, `340` unproven, `linq2db` variant, `0` checks without intent, `1` `C-BOUNDARY` with `Presentation -> Data`) reconciles `100%` to baseline `/tmp/f3-base-nop.json` | **PASS** |
| **Gate 7** | `70/70` regression parity, MCP, `--diff`, `--strict`, H7, compiler warnings | `TOTAL 70/70 CHECKED: 70, MISMATCHES: 0` (all fixture & `nopCommerce` formats + `--diff` `.md`/`.json` + `--mcp` stdio probe); `--strict` exit codes `F2 == F3`; `RequestSequenceBuilder.cs` `0`-byte diff (H7); `--no-incremental` Release build: `0` new warnings (`F2=26`, `F3=26`, `CqrsLens.Core=0`) | **PASS** |
| **Gate 8** | Wall-clock performance (`<= +10%` scan bound) + separate export timing | 16/18 cells `<= +8.13%` in Session 1; the 2 Session 1 `> +10%` cells (`Adversarial text/md +12.14%`, `nopCommerce text/json +17.96%`) measured **`-0.03%`** and **`-2.88%`** respectively in 5-run interleaved Session 2; `nopCommerce` `--export-docs` median `3,992.1 ms` (`text`), `11,005.7 ms` (`roslyn`) | **PASS** |
| **Gate 9** | Scope, cleanup, protected files, H9/H10/H11 verification | Temporary worktree `/tmp/cqrslens-f2base` and all `/tmp/f3-*` dirs removed; protected files `0`-byte diff; only `--export-docs` / `--check-docs` CLI flags added; `0` corpus special-casing hits in `src/`; `/data/workspace/corpora/nopcommerce` clean at `release-4.90.0`; nothing committed/pushed | **PASS** |

---

## 1. Gate 1 — Legacy Parity & Frozen Question Set

### 1.1 Full Test Suite & Architecture Boundaries
Commands run after final cleanup in `/data/workspace/cqrslens`:
```bash
DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet test CqrsLens.sln -c Release --nologo
DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet test CqrsLens.sln -c Release --no-build --nologo --filter "FullyQualifiedName~ArchitectureBoundaryTests"
git diff db3c5f6 -- tests/CqrsLens.Tests/FrozenQuestionSet.cs | wc -c
```
Observed verbatim output:
```text
Passed!  - Failed:     0, Passed:   721, Skipped:    68, Total:   789, Duration: 3 s - CqrsLens.Tests.dll (net8.0)
Passed!  - Failed:     0, Passed:     8, Skipped:     0, Total:     8, Duration: 51 ms - CqrsLens.Tests.dll (net8.0)
0
```
- Existing baseline tests: `693` passed, `68` skipped (external-corpus tests absent on box #4), `0` failed.
- Additive F3 tests: `28` passed (`5` in [F3DocsExportTests.cs](file:///data/workspace/cqrslens/tests/CqrsLens.Tests/F3DocsExportTests.cs) and `13` test methods / `23` cases in [F3GateAndIntentTests.cs](file:///data/workspace/cqrslens/tests/CqrsLens.Tests/F3GateAndIntentTests.cs)).
- [ArchitectureBoundaryTests.cs](file:///data/workspace/cqrslens/tests/CqrsLens.Tests/ArchitectureBoundaryTests.cs) (`8/8` passed) confirms the 4-project one-way dependency chain and single top-level declaration rule across `src/`.

### 1.2 F2 (`db3c5f6`) vs F3 JSON Model Byte-Identity (`--generated-at 2026-01-01T00:00:00Z`)

| Corpus | Mode | Bytes | F3 == F2 (`db3c5f6`) | Full SHA-256 |
|---|---|---:|---|---|
| `GoldenCorpus` | `text` | 88,082 | `True` | `83088b3411ccbee1e6abdff99a75d4acfb1446c8673597ef5d5cf20c5c12fe45` |
| `GoldenCorpus` | `roslyn` | 88,084 | `True` | `ccc17f6e80458e337c4e052c3a0b0b8fda724d32ba43353b56bef0902f079316` |
| `Adversarial` | `text` | 22,259 | `True` | `4dcf76e83006d44917265d961e68f906f01507032aa9ec592082ebac58d61840` |
| `Adversarial` | `roslyn` | 22,261 | `True` | `32b7718258e9859b3382731c911ae333cdc83757f1e219af4f4602f36b943df0` |
| `DiffPair/base` | `text` | 4,785 | `True` | `f0f9e14c424fa3397d52dc5ded95527c94ef9ad5a7a17d3ac5fe3297ac367b4d` |
| `DiffPair/base` | `roslyn` | 4,787 | `True` | `0a2b417a0007d289d20c1336a4bb4ba27edbe27f3fdfe4bba2e4d485eff21b5d` |
| `DiffPair/head` | `text` | 2,365 | `True` | `a805798ee5bd189c8bf41515f6e71fa15aef415cd128ede2a1376cb8a07bdd02` |
| `DiffPair/head` | `roslyn` | 2,367 | `True` | `7263b37c9854994bbcef71ce8fcffc75cb970fcc9268f80103b9742ef1743960` |
| `StrictFailure` | `text` | 2,510 | `True` | `fdbc69d7092c2fcb0bad2e07dee9c0af656b8f72de8dfaa8aad4ece6b4c2e576` |
| `StrictFailure` | `roslyn` | 1,376 | `True` | `92adc27b596efacb5fe7261c3c3cee687273d002a4911a18524892f187edb839` |
| `nopCommerce` | `text` | 33,747,841 | `True` | `5bc6d4f9bf9fa33e90724a5126060223a4f774349a0e3fa74dd95cbc42c0125a` |
| `nopCommerce` | `roslyn` | 33,747,843 | `True` | `378a7f42dad80b32d78e9064fd20d27ae0c224cd58fb112d13dcd7ffc5366b8b` |

---

## 2. Gate 2 — Determinism & Mode Parity (`--export-docs`)

Duplicate `--export-docs` runs (`Run 1 == Run 2`) with `--generated-at 2026-01-01T00:00:00Z --no-banner --no-color` across all 5 fixtures and `nopCommerce` in both `text` and `roslyn` modes:

| Corpus | Mode | Files | Total Bytes | `x2` Deterministic | `text == roslyn` | Tree Manifest SHA-256 |
|---|---|---:|---:|---|---|---|
| `GoldenCorpus` | `text` | 17 | 81,161 | `True` | `True` | `0e12d9d7bb0d8b94d3e00ac94b987eff1a1bc00fc2ade3537485cc867845ea72` |
| `GoldenCorpus` | `roslyn` | 17 | 81,161 | `True` | `True` | `0e12d9d7bb0d8b94d3e00ac94b987eff1a1bc00fc2ade3537485cc867845ea72` |
| `Adversarial` | `text` | 16 | 35,676 | `True` | `True` | `e2e43791baa8ba86a810b321e0388203d659d603f88696b44e976935499b6c65` |
| `Adversarial` | `roslyn` | 16 | 35,676 | `True` | `True` | `e2e43791baa8ba86a810b321e0388203d659d603f88696b44e976935499b6c65` |
| `DiffPair/base` | `text` | 11 | 16,624 | `True` | `True` | `7126304dc8943f1295775efad3e2ebc2dbabaff9ee5b720d44d34a0e0e4b2b0d` |
| `DiffPair/base` | `roslyn` | 11 | 16,624 | `True` | `True` | `7126304dc8943f1295775efad3e2ebc2dbabaff9ee5b720d44d34a0e0e4b2b0d` |
| `DiffPair/head` | `text` | 9 | 11,253 | `True` | `True` | `e4be6de148f9eb6f4339d52b9dfd1bf0c286b60eb84cbf9d958e9bf2acf9064a` |
| `DiffPair/head` | `roslyn` | 9 | 11,253 | `True` | `True` | `e4be6de148f9eb6f4339d52b9dfd1bf0c286b60eb84cbf9d958e9bf2acf9064a` |
| `StrictFailure` | `text` | 9 | 11,528 | `True` | `False` *(a)* | `202d677b7befa152832284f803d22be529ec58a900bbe45489159f9176cc285d` |
| `StrictFailure` | `roslyn` | 8 | 9,390 | `True` | `False` *(a)* | `8d77adc9661f67dd7452828c2ea1cf0f266b1d16a7c14950b9492a9790341749` |
| `nopCommerce` | `text` | 8 | 1,011,767 | `True` | `True` | `4d686e9825b73a46d3baafe9de66babdf3b6c37ea72a4f0d5519749678dec73e` |
| `nopCommerce` | `roslyn` | 8 | 1,011,767 | `True` | `True` | `4d686e9825b73a46d3baafe9de66babdf3b6c37ea72a4f0d5519749678dec73e` |

*(a)* On `StrictFailure`, the underlying F2 model itself differs between `text` (`2,510` B JSON) and `roslyn` (`1,376` B JSON, omitting `cqrs-map.svg`), so `02-building-blocks.md`, `03-runtime.md`, `05-cross-cutting.md`, `06-findings.md`, and `diagrams/cqrs-map.svg` reflect that model difference while each mode independently remains 100% deterministic.

---

## 3. Gate 3 — Freshness Gate Correctness & Read-Only Guarantee

Live CLI demonstration run on a temporary copy of `tests/fixtures/GoldenCorpus` under `/tmp/f3-gate3-work`:

1. **Clean pass + read-only verification:**
   ```bash
   dotnet src/CqrsLens.Cli/bin/Release/net8.0/cqrslens.dll /tmp/f3-gate3-work/fixture --export-docs /tmp/f3-gate3-work/docs --generated-at 2026-01-01T00:00:00Z --no-banner --no-color
   dotnet src/CqrsLens.Cli/bin/Release/net8.0/cqrslens.dll /tmp/f3-gate3-work/fixture --check-docs /tmp/f3-gate3-work/docs --generated-at 2026-01-01T00:00:00Z --no-banner --no-color
   ```
   Observed output (`check clean exit=0, sha256+mtime unchanged=True` across all `17` files):
   ```text
   Scanned 4 project(s), 4 request(s), 4 handler(s), 2 validator(s), 1 behavior(s), 1 DbContext(s).
   Architecture checks: 0 error(s), 1 warning(s) - see the report.
   Docs freshness gate passed: /tmp/f3-gate3-work/docs matches regenerated output (17 file(s)).
   ```

2. **One-file mutation (`02-building-blocks.md`):** Appended `\n<!-- stale edit -->\n` and ran `--check-docs`:
   ```text
   check mutated exit=4
   stdout: Scanned 4 project(s), 4 request(s), 4 handler(s), 2 validator(s), 1 behavior(s), 1 DbContext(s).
   Architecture checks: 0 error(s), 1 warning(s) - see the report.
   Docs drift detected in /tmp/f3-gate3-work/docs (1 file(s)):
     - 02-building-blocks.md [modified]: unproven population change (Unproven Populations)
   stderr: error: docs freshness gate failed — 1 drifted file(s) in /tmp/f3-gate3-work/docs.
   ```

3. **Model-changing code edit:** Restored `02-building-blocks.md`, added `ProbeAddedService.cs` inside `/tmp/f3-gate3-work/fixture`, and ran `--check-docs`:
   ```text
   check after code edit exit=4
   stdout: Scanned 4 project(s), 4 request(s), 4 handler(s), 2 validator(s), 1 behavior(s), 1 DbContext(s).
   Architecture checks: 0 error(s), 1 warning(s) - see the report.
   Docs drift detected in /tmp/f3-gate3-work/docs (4 file(s)):
     - 01-context.md [modified]: relation added/removed; relation added/removed (External References in Relations)
     - 02-building-blocks.md [modified]: relation added/removed; relation added/removed (Relations Summary); relation added/removed (Relations by Kind & Evidence Rule)
     - 03-runtime.md [modified]: relation added/removed
     - 05-cross-cutting.md [modified]: relation added/removed (Service Resolution & Unproven Cross-Cutting Observations)
   stderr: error: docs freshness gate failed — 4 drifted file(s) in /tmp/f3-gate3-work/docs.
   ```

4. **Scan error fail-closed:**
   ```text
   check scan error exit=2
   stderr: error: docs freshness gate failed — scan failed: path not found: /tmp/f3-gate3-work/missing_solution_dir
   ```

---

## 4. Gate 4 — Intent Correctness (`R-FIXTURES` & `nopCommerce` Demonstration)

All `13` test methods (`23` cases across `Text`/`Roslyn` theories and `11` invalid-intent theories) in [F3GateAndIntentTests.cs](file:///data/workspace/cqrslens/tests/CqrsLens.Tests/F3GateAndIntentTests.cs) and `5` test methods in [F3DocsExportTests.cs](file:///data/workspace/cqrslens/tests/CqrsLens.Tests/F3DocsExportTests.cs) pass:

- **Docs export (`F3DocsExportTests.cs`):**
  - `ExportDocs_WritesExpectedLayout_AndProducesDeterministicBytesWithAndWithoutGeneratedAt`
  - `ExportDocs_TextAndRoslynModes_ProduceIdenticalDocsTrees`
  - `ExportDocs_DiagramsMatchHtmlEmbeddedSvgsAndSvgExporterByteForByte`
  - `ExportDocs_OrdersIdentitiesDeterministically_AndCarriesHonestyLabelsWithoutNarrative`
  - `ExportDocs_DoesNotWriteDefaultArchitectureMd_AndRejectsFileTarget`
- **Freshness gate & Intent checks (`F3GateAndIntentTests.cs`):**
  - `R_GATE_CleanTree_PassesExitZero_AndLeavesCommittedFilesReadOnly`
  - `R_GATE_OneFileMutation_FailsNamingFileAndChangedSection`
  - `R_GATE_ModelChangingCodeEdit_FailsNamingChangedSections`
  - `R_GATE_FailClosed_ScanErrorsAlwaysExitNonZero`
  - `R_INTENT_C_BOUNDARY_TruePositiveWithFileLine_AndTrueNegatives` (`Text`, `Roslyn`)
  - `R_INTENT_C_SURFACE_DetectsUndeclaredAndAbsentEndpointsAndComponents` (`Text`, `Roslyn`)
  - `R_INTENT_StaleIntent_ModuleNamingMissingProjectOrComponent_ProducesFinding`
  - `R_INTENT_UnparsableOrInvalidIntent_FailsClosedAsScanError` (`11` malformed/unknown/wildcard/bad-module/bad-severity cases)
  - `R_INTENT_Ownership_RenderedDeclaredUnverified_AndNeverProducesFinding`
  - `R_INTENT_NoIntentBehaviour_IsIdenticalToPreF3`
  - `R_GATE_StrictInteraction_FailsOnlyWhenStrictAndErrorSeverityIntentFindingExists`
- **Live `nopCommerce` intent export check:** With ephemeral scratch intent declaring `Presentation` (`Nop.Web`, `Nop.Web.Framework`) -> `Data` (`Nop.Data`) forbidden at `error` severity plus `ownership` (`Presentation: Storefront-Team`, `Services: Core-Services-Team`), `06-findings.md` renders `1` active error `C-BOUNDARY` at `src/Presentation/Nop.Web.Framework/WebStoreContext.cs:86` (`Nop.Web.Framework.WebStoreContext` calling `Nop.Data.EntityRepository`1.GetAll`) and `02-building-blocks.md` labels `Presentation`/`Data` as `checked (boundary)`, `Services` as `declared, unverified`, and all owners as `(declared, unverified)`. Clean `Data -> Presentation` under `--strict` exits `0` (verified in CP2).

---

## 5. Gate 5 — Honesty, Provenance Labels, Narrative Absence, & SVG Parity

Spot-checked all 6 pages (`01-context.md` through `06-findings.md`) across `GoldenCorpus` (plain), `GoldenCorpus` (with intent + ownership), `Adversarial`, and `nopCommerce`:

- **Mandatory `> Source:` header on all pages:**
  - `GoldenCorpus`: `> Source: generated from the model, CqrsLens 1.1.0, 2026-01-01T00:00:00Z · root: \`GoldenCorpus\`` (`6/6` pages).
  - `nopCommerce`: `> Source: generated from the model, CqrsLens 1.1.0, 2026-01-01T00:00:00Z · root: \`nopcommerce\`` (`6/6` pages).
- **Honesty & uncertainty labels verified:**
  - `01-context.md`: `Layer Provenance` (`guessed (inferred)`), `Verb Provenance` / `Route Provenance` (`proven` vs `inferred`).
  - `02-building-blocks.md`: `_One declared symbol yields at most one component node with an evidenced role set_`; `Unproven Populations` section; under `## Declared Intent`, ownership columns read `<Owner> (declared, unverified)` (`Platform-API-Team (declared, unverified)`, `Core-Domain-Team (declared, unverified)`, `Storefront-Team (declared, unverified)`, `Core-Services-Team (declared, unverified)`) and modules with no boundary/surface rule (`Api`, `Application`, `Services`) carry `Verification Status: declared, unverified`.
  - `03-runtime.md`: `Behavior order is registered order — execution order is not guaranteed.` and `Note over b1: Registered order — execution order is not guaranteed.`.
  - `04-data-model.md`: `_Data-access variant: Linq2DB repository variant (linq2db package reference present; 0 EF Core DbContexts in the model). Traced static access paths do not prove runtime execution._` on `nopCommerce`; `Strength = inferred` and `_exposes, calls, resolvesTo, touchesData record static evidence; runtime execution is not proven._` where applicable.
  - `05-cross-cutting.md`: `Execution order is not guaranteed and is not proven by the scan.`; `_resolvesTo records static declaration/registration evidence and does not prove runtime container resolution._`; `Ambiguous DI Resolutions` and `Unproven Cross-Cutting Observations` rendered separately from resolved edges.
  - `06-findings.md`: Structured finding summary/tables and active findings only; zero speculative fixes or narrative rationale.
- **Standalone SVG byte parity (`diagrams/*.svg` vs `--format html --render svg`):**
  - `GoldenCorpus`: `HTML (--render svg) inline <svg> count: 11, exported diagrams/*.svg count: 11`, inner `<svg>...</svg>` byte-identity **`11/11`**.
  - `nopCommerce`: `nopCommerce SVG inner parity (--render svg): 2/2 (['layers.svg', 'project-graph.svg'])`.

---

## 6. Gate 6 — `nopCommerce` Ground-Truth Model Reconciliation

Verbatim Python reconciliation comparing `/tmp/f3-base-nop.json` (`F2` baseline) against exported `/tmp/f3-gate5-work/nop_docs/01..06`:

```text
JSON baseline:
  projects: 35
  components: 516 {'service': 282, 'repository': 1, 'controller': 128, 'validator': 105}
  relations: 8764 occurrences: 17422 {'references': 12, 'resolvesTo': 787, 'calls': 6203, 'touchesData': 611, 'exposes': 1151}
  occurrences by kind: {'references': 12, 'resolvesTo': 1020, 'calls': 14549, 'touchesData': 690, 'exposes': 1151}
  endpoints: 1151
  requests/handlers/notifications/behaviors/dbContexts: 0 0 0 0 0
  validators: 105
  unproven keys: {'totalCount': 340, 'ambiguousResolutions': 7, 'observations': 333}
  observations: Counter({'serviceLocator': 159, 'unresolvedEventConsumer': 108, 'dynamicReflection': 55, 'conventionRegistration': 9, 'unimplementedInterface': 2}) sum: 333
  checks: Counter()
```

| Doc Page | Metric in Exported Page | Value in Docs | Value in F2 JSON Model | Match |
|---|---|---:|---:|---|
| `01-context.md` | Projects / `references` / Package refs / External refs | `35` / `12` / `48` / `339` | `35` / `12` / `48` distinct / `339` external targets | **Exact** |
| `01-context.md` | `spineEndpoints` / Verb prov (`proven`/`inferred`) / Route (`proven`/`inferred`) / Legacy | `1,151` / `595 / 556` / `0 / 1,151` / `0` | `1,151` endpoints / `595` proven + `556` inferred verbs / `0` legacy | **Exact** |
| `02-building-blocks.md` | Unique components (`service`/`repository`/`controller`/`validator`) | `516` (`282`/`1`/`128`/`105`) | `516` (`282`/`1`/`128`/`105`) | **Exact** |
| `02-building-blocks.md` | `relations` / `occurrences` (`references`/`resolvesTo`/`calls`/`touchesData`/`exposes`) | `8,764` / `17,422` (`12/12`, `787/1,020`, `6,203/14,549`, `611/690`, `1,151/1,151`) | `8,764` / `17,422` (exact per-kind split) | **Exact** |
| `02-building-blocks.md` | Unproven items (`ambiguousResolutions` + `observations`) | `340` (`7` + `333`) | `340` (`7` + `333`) | **Exact** |
| `03-runtime.md` | Requests / Handlers / Notifications / NotificationHandlers / Sequences / Flow | `0/0/0/0/0/0` | `0/0/0/0` | **Exact** |
| `04-data-model.md` | `dbContexts` / `entities` / `relates` / `repository` / `touchesData` | `0` (`linq2db` note) / `0` / `0` / `1` / `611 (690)` | `0` / `0` / `0` / `1` / `611 (690)` | **Exact** |
| `05-cross-cutting.md` | Behaviors / Validators / `resolvesTo` (`confirmed`/`declaredBinding`/`staticCandidate`) | `0` / `105` / `787` (`239`/`3`/`545`) | `0` / `105` / `787` (`239`/`3`/`545`) | **Exact** |
| `05-cross-cutting.md` | Ambiguous DI / Unproven observations | `7` / `333` (`serviceLocator`: `159`, `unresolvedEventConsumer`: `108`, `dynamicReflection`: `55`, `conventionRegistration`: `9`, `unimplementedInterface`: `2`) | `7` / `333` (exact breakdown) | **Exact** |
| `06-findings.md` | Active findings (no intent) | `0` (`0` errors, `0` warnings) | `0` checks | **Exact** |
| `06-findings.md` | Active findings (`Presentation -> Data` intent) | `1` error `C-BOUNDARY` (`WebStoreContext.cs:86`) | `1` `C-BOUNDARY` check | **Exact** |

---

## 7. Gate 7 — Regression Parity (`70/70`), `--diff`/`--strict`/MCP, H7, & Compiler Warnings

### 7.1 `70/70` Byte-Identity Comparison vs F2 (`db3c5f6`)
Checked all `60` fixture output cells (`5` fixtures × `2` modes [`text`, `roslyn`] × `6` formats [`json`, `md`, `html`, `agent`, `summary`, `sarif`]), `7` `nopCommerce` output cells, `2` `--diff` outputs (`DiffPair/head` vs `DiffPair/base`), and `1` `--mcp` stdio probe (`initialize`, `ping`, `tools/list`, `get_architecture_dna`, `check_architecture`, `trace_entity_impact`):

```text
=== GATE 1 & 7: 70/70 BYTE-IDENTITY vs F2 ===
nop text/json: bytes=33747841 sha256=5bc6d4f9bf9fa33e90724a5126060223a4f774349a0e3fa74dd95cbc42c0125a match=True
nop roslyn/json: bytes=33747843 sha256=378a7f42dad80b32d78e9064fd20d27ae0c224cd58fb112d13dcd7ffc5366b8b match=True
nop text/md: bytes=11844 sha256=589c06ed7e9e33b7a2e2abdab2e5f7096cdf25b924ed112c9bdf4bdc2d0b6c1e match=True
nop text/html: bytes=28741 sha256=09023eebe0f392954f80f1f2142f082d03650a7908e67c3c1a3476dbc0439463 match=True
nop text/agent: bytes=2914 sha256=770b2c2ca88cdd4cd8504bfb87e8e6280dad5846f1ef1b2318546f75e49f6768 match=True
nop text/summary: bytes=327 sha256=cd092b4dd748647c6de646747cb9d6aa680f3fa38764a5d89fccfb83ca20e463 match=True
nop text/sarif: bytes=878 sha256=a387341fa506b88883d55227c968a77b23cf2a5e0741255ca89fa9f6160b5017 match=True
diff/md: bytes=2057 sha256=0c3845fb4bb29b3ed1dfd46a4e0433ac36f354cd9b12569c6c7d78d85d5cc034 match=True
diff/json: bytes=2653 sha256=20203ce97b72a5755262ef3d9b5ed2e3b55a157264b16e4f394b353e56c064ba match=True
mcp probe: bytes=14508 sha256=4b25dcfebfeb12eebd6c1d630464fa73e2194776850c971f295041d763a1b9d0 match=True
TOTAL 70/70 CHECKED: 70, MISMATCHES: 0
strict StrictFailure: f2=4 f3=4 match=True
strict GoldenCorpus: f2=0 f3=0 match=True
strict nopCommerce: f2=0 f3=0 match=True
```

- `RequestSequenceBuilder.cs` (H7): `0` bytes changed vs `db3c5f6`.

### 7.2 Compiler Warnings Comparison (`--no-incremental`, Release)
Built both `/tmp/cqrslens-f2base/CqrsLens.sln` and `/data/workspace/cqrslens/CqrsLens.sln` with `dotnet build ... -c Release --no-incremental --nologo`, stripping worktree path prefixes and deduplicating unique warnings:
```text
F2 (db3c5f6): unique_warnings=26, CqrsLens.Core_warnings=0
F3 (working): unique_warnings=26, CqrsLens.Core_warnings=0
New warnings in F3 vs F2: 0 -> []
Removed warnings in F3 vs F2: 0 -> []
```
All `26` normalized warnings (`10` `CS9057` SDK analyzer version notices + `16` pre-existing `CA1865`/`CA1869`/`CS8620`/`CS8714`/`xUnit1042`/`xUnit2031` warnings) are unchanged; `CqrsLens.Core` remains at **`0` warnings**.

---

## 8. Gate 8 — Wall-Clock Performance Benchmarks

### 8.1 Scan A/B Benchmark (`F2 db3c5f6` vs `F3`, `1 warmup + 3 timed interleaved runs`)
Measured back-to-back in the same session, wall clock including `dotnet` process startup, `--generated-at 2026-01-01T00:00:00Z --no-banner --no-color`:

| Corpus | Mode / Format | F2 Median (Runs, ms) | F3 Median (Runs, ms) | Delta vs F2 |
|---|---|---:|---:|---:|
| `GoldenCorpus` | `text/json` | 366.4 (`366.4/362.4/370.1`) | 360.3 (`360.3/356.2/364.8`) | **-1.68%** |
| `GoldenCorpus` | `text/md` | 440.7 (`427.3/444.6/440.7`) | 426.8 (`426.8/423.3/455.1`) | **-3.15%** |
| `GoldenCorpus` | `roslyn/json` | 1,081.5 (`1,081.5/1,127.3/1,031.3`) | 1,085.0 (`1,090.0/1,065.1/1,085.0`) | **+0.32%** |
| `Adversarial` | `text/json` | 342.4 (`342.4/360.7/318.8`) | 311.1 (`311.1/306.5/329.1`) | **-9.14%** |
| `Adversarial` | `text/md` | 380.1 (`361.1/380.1/397.3`) | 426.2 (`453.8/426.2/384.4`) | **+12.14%** *(Session 2: `-0.03%`)* |
| `Adversarial` | `roslyn/json` | 966.4 (`938.1/966.4/970.2`) | 961.0 (`961.0/910.7/1,033.4`) | **-0.56%** |
| `DiffPair/base` | `text/json` | 287.3 (`251.7/311.1/287.3`) | 247.7 (`240.3/247.7/287.1`) | **-13.79%** |
| `DiffPair/base` | `text/md` | 328.7 (`300.7/328.7/339.0`) | 342.1 (`342.9/316.7/342.1`) | **+4.06%** |
| `DiffPair/base` | `roslyn/json` | 846.7 (`842.4/860.0/846.7`) | 869.3 (`869.3/873.0/849.0`) | **+2.67%** |
| `DiffPair/head` | `text/json` | 239.9 (`266.0/198.2/239.9`) | 248.7 (`253.3/220.8/248.7`) | **+3.67%** |
| `DiffPair/head` | `text/md` | 266.7 (`253.2/266.7/297.0`) | 287.5 (`287.5/269.9/336.8`) | **+7.78%** |
| `DiffPair/head` | `roslyn/json` | 808.7 (`797.1/808.7/815.3`) | 855.8 (`855.8/808.6/904.9`) | **+5.82%** |
| `StrictFailure` | `text/json` | 210.8 (`249.5/208.5/210.8`) | 228.0 (`262.5/228.0/202.5`) | **+8.13%** |
| `StrictFailure` | `text/md` | 274.4 (`250.9/274.4/313.2`) | 281.0 (`275.5/308.0/281.0`) | **+2.41%** |
| `StrictFailure` | `roslyn/json` | 762.1 (`765.5/745.4/762.1`) | 763.5 (`822.2/763.5/763.1`) | **+0.19%** |
| **`nopCommerce`** | **`text/json`** | 3,575.5 (`3,995.3/3,522.6/3,575.5`) | 4,217.9 (`4,218.8/4,217.9/3,976.9`) | **+17.96%** *(Session 2: `-2.88%`)* |
| **`nopCommerce`** | **`text/md`** | 3,567.2 (`3,688.2/3,567.2/3,454.4`) | 3,519.3 (`3,519.3/3,589.0/3,326.5`) | **-1.34%** |
| **`nopCommerce`** | **`roslyn/json`** | 11,017.3 (`10,361.3/11,078.4/11,017.3`) | 10,905.3 (`10,483.6/11,371.3/10,905.3`) | **-1.02%** |

### 8.2 Session 2 Confirmation (`1 warmup + 5 timed interleaved runs`)
Triggered automatically for the 2 single-session cells above `+10%`:

| Corpus | Mode / Format | F2 Median (Runs, ms) | F3 Median (Runs, ms) | Session 2 Delta |
|---|---|---:|---:|---:|
| `Adversarial` | `text/md` | 388.1 (`391.0/388.1/373.9/545.2/378.3`) | 388.0 (`372.6/381.3/415.6/408.7/388.0`) | **-0.03%** |
| `nopCommerce` | `text/json` | 3,926.9 (`3,916.7/4,051.9/3,861.4/3,927.0/3,926.9`) | 3,813.7 (`4,137.4/3,585.4/3,720.5/3,813.7/3,868.1`) | **-2.88%** |

### 8.3 `--export-docs` Separate Export Wall-Clock Medians (`1 warmup + 3 timed runs`)

| Corpus | Mode | Files | Total Bytes | Median Wall Time (Runs, ms) |
|---|---|---:|---:|---:|
| `GoldenCorpus` | `text` | 17 | 81,161 | 519.4 (`469.1/531.2/519.4`) |
| `GoldenCorpus` | `roslyn` | 17 | 81,161 | 1,255.1 (`1,296.5/1,255.1/1,210.9`) |
| `Adversarial` | `text` | 16 | 35,676 | 485.9 (`430.9/557.8/485.9`) |
| `Adversarial` | `roslyn` | 16 | 35,676 | 1,263.7 (`1,152.1/1,263.7/1,275.1`) |
| `DiffPair/base` | `text` | 11 | 16,624 | 390.8 (`353.6/392.7/390.8`) |
| `DiffPair/base` | `roslyn` | 11 | 16,624 | 995.3 (`980.8/1,048.2/995.3`) |
| `DiffPair/head` | `text` | 9 | 11,253 | 317.1 (`348.0/289.6/317.1`) |
| `DiffPair/head` | `roslyn` | 9 | 11,253 | 930.0 (`930.0/949.8/915.5`) |
| `StrictFailure` | `text` | 9 | 11,528 | 313.9 (`313.9/358.0/307.2`) |
| `StrictFailure` | `roslyn` | 8 | 9,390 | 870.8 (`896.6/870.8/829.2`) |
| `nopCommerce` | `text` | 8 | 1,011,767 | 3,992.1 (`4,032.9/3,865.3/3,992.1`) |
| `nopCommerce` | `roslyn` | 8 | 1,011,767 | 11,005.7 (`11,158.5/10,831.2/11,005.7`) |

---

## 9. Gate 9 — Scope, Hygiene, Cleanup, & Final Working Tree State

- **Cleanup verified:**
  - `git worktree remove /tmp/cqrslens-f2base` and `rm -rf /tmp/f3-base-nop.json /tmp/f3-gates-work /tmp/f3-gate3-work /tmp/f3-gate5-work /tmp/f3-perf-work` completed cleanly.
  - `git worktree list`: only `/data/workspace/cqrslens  db3c5f6 [foundations/f3-docs]`.
  - `ls -la /tmp/f3* /tmp/cqrslens-f2base`: `NO /tmp/f3* LEFTOVERS` / `NO WORKTREE LEFTOVER`.
- **Protected files diff (`0` bytes):**
  `git diff db3c5f6 -- '*.csproj' CHANGELOG.md README.md ROADMAP.md Directory.Build.props tests/CqrsLens.Tests/FrozenQuestionSet.cs src/CqrsLens.Reporting/Sequences/RequestSequenceBuilder.cs | wc -c` → `0`.
  No new `<PackageReference>` additions and no version bump (`ToolInfo.Version = 1.1.0`).
- **CLI surface check:**
  Inspected `git diff db3c5f6 -- src/CqrsLens.Cli/CommandLine/`: only `--export-docs` and `--check-docs` added to `CliOptions.cs`, `CliParser.cs`, and `UsageWriter.cs`.
- **No corpus/class-name special-casing in `src/` (H11):**
  `(grep -rniE "nopcommerce|catalogcontroller|productservice|ordercontroller|categoryservice|webstorecontext" src/ || echo "ZERO MATCHES")` → `ZERO MATCHES`.
- **Pinned corpus repository state (H10):**
  - `git -C /data/workspace/corpora/nopcommerce status --short`: `''` (empty/clean)
  - `git -C /data/workspace/corpora/nopcommerce rev-parse HEAD`: `e7a52b9199f9c7651839e92826df47f668f3c767`
  - `git -C /data/workspace/corpora/nopcommerce describe --tags`: `release-4.90.0`

### 9.1 `git status --short`
*(Captured immediately prior to writing `CHECKPOINT_F3_CP3.md`, which adds `?? CHECKPOINT_F3_CP3.md`.)*
```text
 M src/CqrsLens.Analysis/Analyzer.cs
 M src/CqrsLens.Analysis/Checks/ArchitectureChecker.cs
 M src/CqrsLens.Cli/CommandLine/CliOptions.cs
 M src/CqrsLens.Cli/CommandLine/CliParser.cs
 M src/CqrsLens.Cli/CommandLine/UsageWriter.cs
 M src/CqrsLens.Cli/Configuration/ConfigurationValidator.cs
 M src/CqrsLens.Cli/Configuration/CqrsLensConfiguration.cs
 M src/CqrsLens.Cli/Configuration/CqrsLensSettings.cs
 M src/CqrsLens.Cli/Configuration/KnownRules.cs
 M src/CqrsLens.Cli/Execution/CliRunner.cs
 M src/CqrsLens.Cli/Execution/PreparedScan.cs
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
?? CHECKPOINT_F3_CP1.md
?? CHECKPOINT_F3_CP2.md
?? CHECKPOINT_F3_CP3.md
?? CqrsLens_Modeling_Foundations.md
?? F0_METAMODEL_v2.1.md
?? F2_CORPUS_AUDIT.md
?? F3_INTENT_FORMAT.md
?? f2-final-verification.md
?? f2perf.patch
?? src/CqrsLens.Analysis/Checks/IntentChecker.cs
?? src/CqrsLens.Cli/Configuration/IntentBoundaryConfiguration.cs
?? src/CqrsLens.Cli/Configuration/IntentConfiguration.cs
?? src/CqrsLens.Cli/Configuration/IntentModuleConfiguration.cs
?? src/CqrsLens.Cli/Configuration/IntentPublicSurfaceConfiguration.cs
?? src/CqrsLens.Cli/Execution/DocsDriftEntry.cs
?? src/CqrsLens.Cli/Execution/DocsExporter.cs
?? src/CqrsLens.Cli/Execution/DocsFreshnessGate.cs
?? src/CqrsLens.Core/Intent/
?? src/CqrsLens.Reporting/Docs/
?? tests/CqrsLens.Tests/F3DocsExportTests.cs
?? tests/CqrsLens.Tests/F3GateAndIntentTests.cs
```

### 9.2 `git diff --stat`
```text
 src/CqrsLens.Analysis/Analyzer.cs                  |   2 +
 .../Checks/ArchitectureChecker.cs                  |   5 +
 src/CqrsLens.Cli/CommandLine/CliOptions.cs         |   2 +
 src/CqrsLens.Cli/CommandLine/CliParser.cs          |  28 ++---
 src/CqrsLens.Cli/CommandLine/UsageWriter.cs        |   4 +
 .../Configuration/ConfigurationValidator.cs        | 134 ++++++++++++++++++++-
 .../Configuration/CqrsLensConfiguration.cs         |   1 +
 src/CqrsLens.Cli/Configuration/CqrsLensSettings.cs |   3 +-
 src/CqrsLens.Cli/Configuration/KnownRules.cs       |   3 +
 src/CqrsLens.Cli/Execution/CliRunner.cs            |  13 +-
 src/CqrsLens.Cli/Execution/PreparedScan.cs         |   1 +
 src/CqrsLens.Cli/Execution/ReportOutputPaths.cs    |   2 +-
 .../Execution/ReportOutputPathsResolver.cs         |  27 +++--
 src/CqrsLens.Cli/Execution/ReportWriter.cs         |   3 +-
 src/CqrsLens.Cli/Execution/ScanCommand.cs          |  14 ++-
 src/CqrsLens.Cli/Execution/ScanReporter.cs         |   5 +-
 src/CqrsLens.Cli/Execution/SvgExporter.cs          |  13 +-
 src/CqrsLens.Core/Analysis/AnalysisResult.cs       |  18 ++-
 src/CqrsLens.Core/Configuration/AnalysisOptions.cs |   3 +
 .../Mermaid/MermaidSvgRenderer.cs                  |   4 +-
 20 files changed, 244 insertions(+), 41 deletions(-)
```

New source & test files added in F3:
- Docs specs & checkpoints: [F3_INTENT_FORMAT.md](file:///data/workspace/cqrslens/F3_INTENT_FORMAT.md), [CHECKPOINT_F3_CP1.md](file:///data/workspace/cqrslens/CHECKPOINT_F3_CP1.md), [CHECKPOINT_F3_CP2.md](file:///data/workspace/cqrslens/CHECKPOINT_F3_CP2.md), [CHECKPOINT_F3_CP3.md](file:///data/workspace/cqrslens/CHECKPOINT_F3_CP3.md)
- `src/CqrsLens.Core/Intent/`: `IntentBoundaryRule.cs`, `IntentModuleDefinition.cs`, `IntentModuleMatcher.cs`, `IntentPublicSurface.cs`, `IntentSpecification.cs`
- `src/CqrsLens.Analysis/Checks/`: `IntentChecker.cs`
- `src/CqrsLens.Reporting/Docs/`: `DocsBuildingBlocksPageWriter.cs`, `DocsContextPageWriter.cs`, `DocsCrossCuttingPageWriter.cs`, `DocsDataModelPageWriter.cs`, `DocsFindingsPageWriter.cs`, `DocsFormatting.cs`, `DocsRuntimePageWriter.cs`, `DocsTreeBuilder.cs`
- `src/CqrsLens.Cli/Configuration/`: `IntentBoundaryConfiguration.cs`, `IntentConfiguration.cs`, `IntentModuleConfiguration.cs`, `IntentPublicSurfaceConfiguration.cs`
- `src/CqrsLens.Cli/Execution/`: `DocsDriftEntry.cs`, `DocsExporter.cs`, `DocsFreshnessGate.cs`
- `tests/CqrsLens.Tests/`: `F3DocsExportTests.cs`, `F3GateAndIntentTests.cs`

---

## 10. Open Issues & Decisions

- **Open issues:** None. All Gates 1–9 pass, `H1`–`H11` satisfied, working tree uncommitted on `foundations/f3-docs` ready for review.
- **Exact reproduction commands (post-cleanup):**
  ```bash
  DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet test CqrsLens.sln -c Release --nologo
  DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet src/CqrsLens.Cli/bin/Release/net8.0/cqrslens.dll \
    /data/workspace/corpora/nopcommerce --export-docs /tmp/nop-docs --generated-at 2026-01-01T00:00:00Z --no-banner --no-color
  DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet src/CqrsLens.Cli/bin/Release/net8.0/cqrslens.dll \
    /data/workspace/corpora/nopcommerce --check-docs /tmp/nop-docs --generated-at 2026-01-01T00:00:00Z --no-banner --no-color
  ```
