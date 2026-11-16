# CHECKPOINT F3 CP2 — Freshness Gate (`--check-docs`) & Intent Checks (`R-INTENT`)

- **Status:** `done`
- **Branch:** `foundations/f3-docs` (base `db3c5f63741b6a5c9f86142ca149d1e224b01116`, uncommitted)
- **Target Corpus:** `/data/workspace/corpora/nopcommerce` @ `release-4.90.0` (`e7a52b9199f9c7651839e92826df47f668f3c767`, read-only clean) + all 5 in-repo fixtures

---

## 1. Freshness Gate Exit-Code Table & Finding Shapes

### 1.1 `--check-docs <dir>` Exit-Code Table
| Condition | Exit Code | `ExitCodes` Constant | Behaviour |
|---|---:|---|---|
| Docs tree identical byte-for-byte, complete scan, no `--strict` active errors | `0` | `ExitCodes.Success` | Reports `Docs freshness gate passed` and prints any intent findings; reads `<dir>` strictly read-only |
| Docs tree differs (modified / missing / unexpected file) | `4` | `ExitCodes.StrictFailure` | Lists every drifted file and changed model/diagram section(s); never modifies `<dir>` |
| Docs tree fresh, `--strict` set, and `>= 1` active (`!IsKnown`) `Error`-severity finding (architecture or intent) | `4` | `ExitCodes.StrictFailure` | Reports fresh tree + `--strict` error count; exits `4` |
| Docs tree fresh, `--strict` set, only `Warning`-severity findings (or known baseline findings) | `0` | `ExitCodes.Success` | Warnings never fail `--strict` |
| Incomplete/errored scan (`!CanRunStrictChecks`, `SkippedFiles > 0`, or error diagnostic) | `4` | `ExitCodes.StrictFailure` | Fail-closed: states scan failed; never reports fresh |
| Unparsable/invalid `.cqrslens.json` / `intent`, `--check-docs` target is a file, or invalid CLI flags | `1` | `ExitCodes.Usage` | Fail-closed with `error: docs freshness gate failed — scan failed: ...` |
| Target scan path missing | `2` | `ExitCodes.PathNotFound` | Fail-closed with `error: docs freshness gate failed — scan failed: ...` |
| No `.csproj` files found under scan path | `3` | `ExitCodes.NoProjects` | Fail-closed with `error: docs freshness gate failed — scan failed: ...` |
| Unexpected I/O failure during scan or temp export | `5` | `ExitCodes.Unexpected` | Fail-closed with `error: docs freshness gate failed — scan failed: ...` |

### 1.2 Intent Finding Shapes (`CheckFinding`)
All intent checks emit standard `CheckFinding(Severity, Code, Message, Target, Location, ProjectName, IsKnown)` records into `AnalysisResult.Checks` (flowing to `06-findings.md`, gate stdout output, and `--strict` / `--baseline` / `rules`):

| Code | Condition | Default Severity | `Target` | `Location` | `Message` Content |
|---|---|---|---|---|---|
| `C-BOUNDARY` | `RelationKind.Calls` relation with source in `boundary.FromModule` and target in `boundary.ToModule` | Declared `boundary.severity` (default `Warning`) | Source `ComponentId.Value` (or `From.Name`) | First sorted occurrence `SourceLocation` | Names `FromModule`, `ToModule`, caller, callee, and `file:line` evidence sites |
| `C-SURFACE` | Model `EndpointNode` absent from `publicSurface.endpoints`, or declared endpoint absent from model | Declared `publicSurface.severity` (default `Warning`) | `EndpointId` (undeclared) or declared selector (stale) | `EndpointNode.Location` or `null` | States `C-SURFACE` mismatch (undeclared vs absent from model) |
| `C-SURFACE` | Model `ComponentNode` absent from `publicSurface.components`, or declared component absent from model | Declared `publicSurface.severity` (default `Warning`) | `ComponentId.Value` (undeclared) or declared selector (stale) | `ComponentNode.Location` or `null` | States `C-SURFACE` mismatch (undeclared vs absent from model) |
| `C-INTENT-STALE` | Module `projects` or `components` selector matches `0` projects/components in model | `Warning` | `"{ModuleName}:{Selector}"` | `null` | Names the module and missing project/component selector |

Ownership (`intent.ownership`) and modules without boundary consequence are never checked (`0` findings) and appear in `02-building-blocks.md` explicitly labelled `declared, unverified`.

---

## 2. `R-FIXTURES` Coverage (`F3GateAndIntentTests.cs`)

All `13` test methods (`23` test cases across `Text` and `Roslyn` theories and invalid-config theories) in [F3GateAndIntentTests.cs](file:///data/workspace/cqrslens/tests/CqrsLens.Tests/F3GateAndIntentTests.cs) pass, proving every rule in `R-FIXTURES`:
- `R_GATE_CleanTree_PassesExitZero_AndLeavesCommittedFilesReadOnly` (verifies mtimes + SHA-256 hashes before == after `--check-docs`, in both `text` and `roslyn` modes)
- `R_GATE_OneFileMutation_FailsNamingFileAndChangedSection`
- `R_GATE_ModelChangingCodeEdit_FailsNamingChangedSections`
- `R_GATE_FailClosed_ScanErrorsAlwaysExitNonZero` (missing folder, zero `.csproj`, ambiguous/incomplete scan)
- `R_INTENT_C_BOUNDARY_TruePositiveWithFileLine_AndTrueNegatives` (`Text` + `Roslyn`, verifies intra-module and allowed-direction calls emit no findings)
- `R_INTENT_C_SURFACE_DetectsUndeclaredAndAbsentEndpointsAndComponents` (`Text` + `Roslyn`, both directions)
- `R_INTENT_StaleIntent_ModuleNamingMissingProjectOrComponent_ProducesFinding`
- `R_INTENT_UnparsableOrInvalidIntent_FailsClosedAsScanError` (`11` malformed/unknown/wildcard/bad-module/bad-severity cases)
- `R_INTENT_Ownership_RenderedDeclaredUnverified_AndNeverProducesFinding`
- `R_INTENT_NoIntentBehaviour_IsIdenticalToPreF3`
- `R_GATE_StrictInteraction_FailsOnlyWhenStrictAndErrorSeverityIntentFindingExists`

---

## 3. nopCommerce Demonstration (Ephemeral `/tmp` Scratch Files)

Actual modules declared in `/tmp` scratch configs:
- `Presentation`: `{ "projects": ["Nop.Web", "Nop.Web.Framework"] }` (`264` components in `Nop.Web` + `Nop.Web.Framework`)
- `Data`: `{ "projects": ["Nop.Data"] }` (`7` components)

### 3.1 Clean Export + Clean Gate (`EXIT=0`)
```bash
DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet src/CqrsLens.Cli/bin/Release/net8.0/cqrslens.dll \
  /data/workspace/corpora/nopcommerce --export-docs /tmp/f3-nop-docs --generated-at 2026-01-01T00:00:00Z --no-banner --no-color
DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet src/CqrsLens.Cli/bin/Release/net8.0/cqrslens.dll \
  /data/workspace/corpora/nopcommerce --check-docs /tmp/f3-nop-docs --generated-at 2026-01-01T00:00:00Z --no-banner --no-color
```
Output:
```text
Scanned 35 project(s), 0 request(s), 0 handler(s), 105 validator(s), 0 behavior(s), 0 DbContext(s).
Docs freshness gate passed: /tmp/f3-nop-docs matches regenerated output (8 file(s)).
EXIT=0
```

### 3.2 One-File Mutation (`02-building-blocks.md`, `EXIT=4`)
Mutated `| \`repository\` | 1 |` → `| \`repository\` | 999 |` in `/tmp/f3-nop-docs/02-building-blocks.md`, then ran `--check-docs /tmp/f3-nop-docs`:
```text
Scanned 35 project(s), 0 request(s), 0 handler(s), 105 validator(s), 0 behavior(s), 0 DbContext(s).
Docs drift detected in /tmp/f3-nop-docs (1 file(s)):
  - 02-building-blocks.md [modified]: component added/removed (Components by Role)
error: docs freshness gate failed — 1 drifted file(s) in /tmp/f3-nop-docs.
EXIT=4
```

### 3.3 Forbidden Boundary `Presentation -> Data` (`C-BOUNDARY`, non-strict `0`, `--strict` `4`)
Scratch intent (`/tmp/f3-nop-intent-violation.json`): `Presentation` (`Nop.Web`, `Nop.Web.Framework`) must not call `Data` (`Nop.Data`), `severity: "error"`.
Exported `/tmp/f3-nop-docs-viol` under that intent config and ran `--check-docs`:
```bash
DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet src/CqrsLens.Cli/bin/Release/net8.0/cqrslens.dll \
  /data/workspace/corpora/nopcommerce --config /tmp/f3-nop-intent-violation.json --check-docs /tmp/f3-nop-docs-viol \
  --generated-at 2026-01-01T00:00:00Z --no-banner --no-color
```
Output (`non-strict`):
```text
Scanned 35 project(s), 0 request(s), 0 handler(s), 105 validator(s), 0 behavior(s), 0 DbContext(s).
Architecture checks: 1 error(s), 0 warning(s) - see the report.
Intent checks: 1 error(s), 0 warning(s).
  - [error] C-BOUNDARY: Intent boundary violation: module 'Presentation' must not call 'Data' — 'Nop.Web.Framework.WebStoreContext' calls 'Nop.Data.EntityRepository`1.GetAll' at src/Presentation/Nop.Web.Framework/WebStoreContext.cs:86. [src/Presentation/Nop.Web.Framework/WebStoreContext.cs:86]
Docs freshness gate passed: /tmp/f3-nop-docs-viol matches regenerated output (8 file(s)).
EXIT=0
```
With `--strict`:
```text
Scanned 35 project(s), 0 request(s), 0 handler(s), 105 validator(s), 0 behavior(s), 0 DbContext(s).
Architecture checks: 1 error(s), 0 warning(s) - see the report.
Intent checks: 1 error(s), 0 warning(s).
  - [error] C-BOUNDARY: Intent boundary violation: module 'Presentation' must not call 'Data' — 'Nop.Web.Framework.WebStoreContext' calls 'Nop.Data.EntityRepository`1.GetAll' at src/Presentation/Nop.Web.Framework/WebStoreContext.cs:86. [src/Presentation/Nop.Web.Framework/WebStoreContext.cs:86]
Docs tree is fresh (8 file(s)), but --strict failed on 1 active check error(s).
error: --strict - 1 architecture/intent check error(s) found.
EXIT=4
```

### 3.4 Clean Boundary `Data -> Presentation` (`--strict`, `EXIT=0`)
Scratch intent (`/tmp/f3-nop-intent-clean.json`): `Data` must not call `Presentation`, `severity: "error"`:
```text
Scanned 35 project(s), 0 request(s), 0 handler(s), 105 validator(s), 0 behavior(s), 0 DbContext(s).
Intent checks: 0 error(s), 0 warning(s).
Docs freshness gate passed: /tmp/f3-nop-docs-clean matches regenerated output (8 file(s)).
EXIT=0
```

---

## 4. Per-Corpus Export Smoke & Existing-Report Byte Parity

| Corpus | Total Files | Markdown (`.md`) | SVGs (`.svg`) | Total Bytes | Export Wall Time (ms) | Existing Reports vs F2 (`.json`/`.md`/`.html`) |
|---|---:|---:|---:|---:|---:|---|
| `GoldenCorpus` | 17 | 6 | 11 | 81,161 | 506.8 | 3/3 byte-identical (`diffs=0`) |
| `Adversarial` | 16 | 6 | 10 | 35,676 | 477.8 | 3/3 byte-identical (`diffs=0`) |
| `DiffPair/base` | 11 | 6 | 5 | 16,624 | 344.9 | 3/3 byte-identical (`diffs=0`) |
| `DiffPair/head` | 9 | 6 | 3 | 11,253 | 342.5 | 3/3 byte-identical (`diffs=0`) |
| `StrictFailure` | 9 | 6 | 3 | 11,528 | 313.3 | 3/3 byte-identical (`diffs=0`) |
| `nopCommerce` | 8 | 6 | 2 | 1,011,767 | 3,753.4 | 3/3 byte-identical (`diffs=0`) |

All `18 / 18` tested existing report outputs across F2 (`db3c5f6`) and F3 are **100% byte-identical**.

---

## 5. Test Results

Command:
```bash
DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet test CqrsLens.sln -c Release --nologo
```
Result:
```text
Passed!  - Failed:     0, Passed:   721, Skipped:    68, Total:   789, Duration: 3 s - CqrsLens.Tests.dll (net8.0)
```
- Existing tests: `693` passed, `68` skipped, `0` failed.
- Additive F3 tests: `28` passed (`5` in `F3DocsExportTests.cs`, `23` in `F3GateAndIntentTests.cs`).
- Compiler warnings: `0` new warnings (`CqrsLens.Core`: `0` warnings).

---

## 6. Files Changed

### `git status --short`
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

### `git diff --stat`
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

---

## 7. Decisions & Open Issues

- **Heading splitting granularity in drift classifier:** Updated `DocsFreshnessGate.SplitMarkdownSections` to split on both `##` and `###` headings outside fenced code blocks with trailing newline normalization, so appending `## Declared Intent` to `02-building-blocks.md` does not cause false drift attribution on the preceding `## Unproven Populations` section.
- **No open blockers.** Ready for CP3 Gates 1–9 verification.
