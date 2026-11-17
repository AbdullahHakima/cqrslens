# Stage F4 Checkpoint 1 — Model Schema, Config Schema Audit & Rename Census

- **Date:** 2026-11-16
- **Branch:** `foundations/f4-noctua` (created from F3 HEAD `18ca9c6d893c1c3734505af4f1ab45dcbccc8150`)
- **Driver:** Antigravity CLI version `1.5.9` (`agy --version`)
- **Corpus:** `/data/workspace/corpora/nopcommerce` (`release-4.90.0` at `e7a52b9199f9c7651839e92826df47f668f3c767`, clean working tree)

---

## 1. F3 Baseline Reproduction

A read-only F3 worktree was created at `/tmp/cqrslens-f3base` (`18ca9c6d893c1c3734505af4f1ab45dcbccc8150`), built in Release, and executed before any workspace code changes:

- **F3 test suite (`dotnet test CqrsLens.sln -c Release --nologo`):**
  - `Passed! - Failed: 0, Passed: 721, Skipped: 68, Total: 789, Duration: 2 s` (68 skipped external-corpus tests require `CQRSLENS_CORPUS_ROOT`).
- **F3 `nopCommerce` baseline JSON outputs:**
  - `/tmp/f4-base/nop-text.json`: `33,747,841` bytes, SHA-256 `5bc6d4f9bf9fa33e90724a5126060223a4f774349a0e3fa74dd95cbc42c0125a`
  - `/tmp/f4-base/nop-roslyn.json`: `33,747,843` bytes, SHA-256 `378a7f42dad80b32d78e9064fd20d27ae0c224cd58fb112d13dcd7ffc5366b8b`
  - Counts (both modes): `relations = 8764`, `occ = 17422`, `spineEndpoints = 1151`, `components = 516` (`service = 282`, `repository = 1`, `controller = 128`, `validator = 105`).

---

## 2. Deliverable Inventory & Audit (`R-SCHEMA-MODEL`, `R-SCHEMA-CONFIG`)

### 2.1 Public Model Schema (`noctua.model.schema.json`)

- **File:** `noctua.model.schema.json` (`1,028` lines, JSON Schema draft `2020-12`, `$id: https://raw.githubusercontent.com/AbdullahHakima/noctua/main/noctua.model.schema.json`, `additionalProperties: false` on root and every object `$def`).
- **Reconciliation against `BRIEF_F4.md` starting hint:**
  - Inspected `JsonReportDocumentFactory.WriteDirect`, `DirectSpineRelationsConverter`, and `JsonSpineNames` directly and enumerated 340 distinct field paths across 12 fixture/`nopCommerce` scans (`text` + `roslyn`) plus a watermarked `SampleShop` run.
  - **22 required top-level properties** (unconditionally emitted): `schemaVersion`, `toolVersion`, `scanner`, `generatedAt`, `elapsedMilliseconds`, `rootName`, `discoveredFileCount`, `fileCount`, `baselineApplied`, `canRunStrictChecks`, `summary`, `projects`, `requests`, `handlers`, `validators`, `behaviors`, `dbContexts`, `checks`, `diagnostics`, `skippedFiles`, `notifications`, `notificationHandlers`. (Note: `baselineApplied`, `canRunStrictChecks`, and `skippedFiles` were omitted from the brief's draft hint, while `dbSets`, `entities`, and `relationships` are nested inside `dbContexts[]`, not top-level.)
  - **12 conditional top-level properties** (omitted when null or empty): `watermark`, `conventions`, `endpoints`, `flowEdges`, `resolvedEdges`, `serviceCallEdges`, `dispatchCallSites`, `unresolvedDispatches`, `relations` (emitted when `Spine.UnindexedRelations.Count > 0`), `components`, `spineEndpoints`, `unproven`.
  - **43 `$defs`:** `summary`, `location`, `project`, `requestProperty`, `request`, `handler`, `validator`, `behavior`, `dbSet`, `entityProperty`, `entity`, `entityRelationship`, `dbContext`, `checkFinding`, `diagnostic`, `notification`, `notificationHandler`, `endpoint`, `flowEdge`, `resolvedEdge`, `serviceCallEdge`, `dispatchCallSite`, `unresolvedDispatch`, `relationKind`, `nodeReferenceKind`, `evidenceStrength`, `evidenceOrigin`, `resolutionSemantics`, `evidenceRule`, `componentRole`, `unprovenItemKind`, `unestablishedBehaviorReason`, `spineNodeRef`, `spineEvidence`, `spineRelation`, `spineRoleEvidence`, `spineComponent`, `spineEndpoint`, `serviceCandidate`, `ambiguousResolution`, `unestablishedBehavior`, `unprovenDiagnostic`, `unprovenPopulations`.

### 2.2 Stability Documentation (`docs/model-schema.md`)

- **File:** `docs/model-schema.md` (`586` lines).
- Documents the D1 versioning and compatibility contract (`schemaVersion: 1`, additive-only within `v1`, legacy projections preserved as legacy-but-stable), the 22 required and 12 conditional top-level properties, and all 43 `$defs`.
- Verified field-for-field and enum-value parity with `noctua.model.schema.json` by `R_SCHEMA_StabilityDoc_MatchesModelSchemaFieldForField` in `tests/CqrsLens.Tests/F4SchemaAndRenameTests.cs`.

### 2.3 Config Schema Audit (`R-SCHEMA-CONFIG`)

Auditing `cqrslens.schema.json` (`96` lines) against `ConfigurationLoader.cs` and `KnownRules.cs` revealed:
1. `ConfigurationLoader` does **not** load or validate `cqrslens.schema.json` at runtime (`cqrslens.schema.json` is an editor/authoring schema; runtime uses `JsonUnmappedMemberHandling.Disallow` + `ConfigurationValidator`).
2. `cqrslens.schema.json` is missing the entire F3 `intent` section specified in `F3_INTENT_FORMAT.md` (`modules`, `boundaries`, `publicSurface`, `ownership`, selector glob restrictions, severities, and non-empty guards).
3. `cqrslens.schema.json` only lists 9 rule codes under `rules.properties`, missing **5** codes present in `KnownRules.All`: `UnreachableRequest`, `UnpublishedNotification`, `C-BOUNDARY`, `C-SURFACE`, and `C-INTENT-STALE`.
4. CP2 will replace `cqrslens.schema.json` with `noctua.schema.json` fixing both gaps and pointing `$id` to `AbdullahHakima/noctua`.

---

## 3. Repository Rename Census (`R-RENAME` & `R-COMPAT`)

| Category | Target(s) | Decision | Rationale |
|---|---|---|---|
| **Package & version metadata** | `Directory.Build.props` (`<Version>`), `src/CqrsLens.Cli/CqrsLens.Cli.csproj` (`<PackageId>`, `<ToolCommandName>`) | **Rename** in CP2 (`2.0.0`, `Noctua`, `noctua`) | Core package/tool rename (`D4`, `H5`); leaves `<Description>`, URLs, refs, assembly names, and `CqrsLens.sln` untouched per `H5`/`H6`. |
| **Config discovery & compat** | `ConfigurationLoader.cs`, `ConfigurationValidator.cs`, `CqrsLensSettings.cs`, `ConfigurationProvenance.cs`, `ScanCommand.cs`, `ScanReporter.cs`, `McpScanFingerprintCalculator.cs` | **Rename primary + compat fallback** | `.noctua.json` primary; permanent `.cqrslens.json` fallback; stdout notice when both present without altering JSON model or diagnostics (`R-COMPAT`). |
| **CLI user-facing text** | `CliRunner.cs` (`--version`), `UsageWriter.cs` (`Usage: noctua`, `.noctua.json`), `CliParser.cs` (`--config .noctua.json`) | **Rename** | User-visible CLI branding & help (`R-RENAME`). |
| **MCP server metadata** | `McpRequestRouter.cs` (`serverInfo.name = "noctua"`) | **Rename** (`serverInfo` only) | `R-RENAME` / `H3`: MCP tool names and answer prose (`McpToolCatalog`, `McpDiffAnswerGenerator`) stay unchanged. |
| **Report headings & footers** | `MarkdownOverviewWriter.cs`, `MarkdownReportWriter.cs`, `HtmlReportGenerator.cs`, `ArchitectureDiffMarkdownRenderer.cs`, `SummaryCountsWriter.cs`, `AgentArchitectureMapGenerator.cs` (`# Noctua Agent Architecture Map`), `SarifReportGenerator.cs` (`driver.name`/`fullName`/`informationUri`), `DocsFormatting.cs`, `DocsBuildingBlocksPageWriter.cs` | **Rename** | User-facing generated reports, SARIF driver metadata, and docs footers (`R-RENAME`). |
| **Automation & workflows** | `action.yml`, `.github/workflows/release.yml` | **Rename** tool invocations, display names, default version `2.0.0` | `R-RENAME`; preserves `action.yml` input/output parameter names, sticky marker, artifact names, and `category: cqrslens` per rename plan §2.2. |
| **Release & user docs** | `README.md`, `CHANGELOG.md`, `ROADMAP.md`, `docs/{ci,configuration,baseline,architecture,analysis-backends,validation}.md` | **Rename / update** in CP2 | `R-RELEASE-DOCS`; claim discipline enforced (`H12`). |
| **Compatibility / frozen internals (kept)** | `CqrsLens.*` C# namespaces/assemblies, `CqrsLens.sln`, `schemaVersion: 1`, `<!-- cqrslens-architecture-diff -->` (`ArchitectureDiffer.cs`), `"cqrslens/v1"` partialFingerprint (`SarifReportGenerator.cs`), `CQRSLENS_ASCII` compat env var (`ConsoleGlyphs.cs`, `UsageWriter.cs`), internal DOM ID `'cqrslens-diagram-'` (`HtmlReportScripts.cs`), MCP tool names & agent headings in `FrozenQuestionSet.cs` (`McpDiffAnswerGenerator.cs`, `AgentChecksSummaryGenerator.cs`, `AgentDnaIndexGenerator.cs`, `AgentEntityImpactGenerator.cs`, `AgentRequestImpactGenerator.cs`, `AgentArchitectureMapGenerator.cs:34` vertical slice trace) | **Keep** | Preserves compatibility contracts (`H3`, `H4`, `R-COMPAT`) and rename plan §2.2 exclusions. |
| **Unlisted findings recorded (kept unless instructed otherwise)** | `src/CqrsLens.Cli/CqrsLens.Cli.csproj:23,26,27` (`Description`/URLs blocked by `H5`); 9 diagram/docs fallback strings `"the CqrsLens safety limit (45,000 characters / 400 edges)"` (not triggered in fixtures/`nopCommerce`); internal temp dir prefixes (`ScanPreparer.cs:20`, `DocsFreshnessGate.cs:38`); `.github/workflows/ci.yml`, `.github/ISSUE_TEMPLATE/*`, `CONTRIBUTING.md`, `scripts/*`, `benchmarks/CqrsLens.Benchmarks/Program.cs`; historical `docs/sample-output/` & `docs/elhakim-output/` | **Documented (not silently edited)** | Explicit plan compliance: avoid unchartered edits outside the rename plan and `H5` bounds. |
| **Enumerated test updates** | 9 test files (13 assertions): `ArchitectureBoundaryTests`, `VersionTests`, `CliTests`, `V02FeatureTests`, `DeepSliceAndAgentTests`, `ArchitectureDiffTests`, `CiOutputTests`, `McpServerTests`, `F3DocsExportTests` | **Update in CP2** (`H2`) | Expected rebrand assertions per `BRIEF_F4.md`. |

---

## 4. CP1 Test Verification & Working Tree State

- **Command:** `DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet test CqrsLens.sln -c Release --nologo`
- **Result:** `Passed! - Failed: 0, Passed: 746, Skipped: 68, Total: 814, Duration: 2 s - CqrsLens.Tests.dll (net8.0)`
  - `721` existing tests + `25` new schema tests (`tests/CqrsLens.Tests/F4SchemaAndRenameTests.cs`):
    - `R_SCHEMA_AllFixtures_BothModes_ValidateAgainstModelSchema` (12 fixture/mode/watermark cases: `GoldenCorpus`, `Adversarial`, `DiffPair/base`, `DiffPair/head`, `StrictFailure`, `SampleShop` across `Text` and `Roslyn`, plus watermark)
    - `R_SCHEMA_SyntheticFullFeatureModel_ValidatesAgainstModelSchema_AndHasZeroUndeclaredPaths` (all 22 required + all 12 conditional sections)
    - `R_SCHEMA_CorruptedSamples_AreRejected` (10 mutation cases)
    - `R_SCHEMA_StabilityDoc_MatchesModelSchemaFieldForField` (top-level fields, `$defs` object tables, and enum values)
  - Zero new compiler warnings in `F4SchemaAndRenameTests.cs`.
- **`git status --short`:**
  ```
  ?? BRIEF_F4.md
  ?? CqrsLens_To_Noctua_Rename_Plan.md
  ?? docs/model-schema.md
  ?? noctua.model.schema.json
  ?? tests/CqrsLens.Tests/F4SchemaAndRenameTests.cs
  ```
- **`git diff --stat`:** *(empty; only untracked deliverables so far)*

---

## 5. Decisions, Open Issues & Exact Reproduction

- **Decisions:**
  - Implemented an in-process JSON Schema draft `2020-12` validator (`F4SchemaAndRenameTests.JsonSchema202012Validator`) so zero NuGet `<PackageReference>` additions were needed (`H5`).
  - Precedence notice when both `.noctua.json` and `.cqrslens.json` exist will be written to `stdout` via `ScanReporter` driven by `BothConfigFilesPresent`, leaving `AnalysisResult.Diagnostics` and `--strict` / JSON output untouched.
- **Open issues / CP2 next:** Replace `cqrslens.schema.json` with `noctua.schema.json`, apply the `2.0.0` version/rename/compat edits, update release docs and enumerated tests, and run the `nopCommerce` demonstration.
- **Exact reproduction commands:**
  ```bash
  export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
  dotnet test CqrsLens.sln -c Release --nologo
  ```
