# BRIEF — F4: Public schema + the Noctua release (CqrsLens foundations) — APPROVED AS WRITTEN 2026-11-16

Status: APPROVED as written by Abdullah 2026-11-16 ~17:42 EET (D1–D8 settled as written). Written 2026-11-16 by a
research subagent, following the F3 brief pattern, from the charter the
foundations settle, the F3 verification record, the rename plan, and the
live repo at the F4 base. Anything this brief proposes that the
foundations, F0, or the rename plan do not decide is marked **DECISION**
below — approval settles it; the driver does not decide it alone.
Nothing here authorizes a launch, a commit, a push, a tag, a publish, a
repo rename, or a deprecation. Those are separate calls after
verification, in the sequence the release checklist in CP3 states.

Driver: Antigravity CLI on box #4. Model: `gemini-pro-orchestrator-g1-pro`
(Argon as planner, Hydrogen as implementer) — Abdullah's instruction
2026-11-16, used for the F2 perf-fix pass and F3. Antigravity version:
update before launching and record the version in CP1 (1.5.9 was latest
at the F3 launch, 2026-11-16).
Fresh session for this brief. Repo: /data/workspace/cqrslens, NEW branch
`foundations/f4-noctua`, based on `foundations/f3-docs` @ `18ca9c6`
("Preserve the F1-F3 working notes alongside the code", parent `9141a83`
— the F3 code commit, 43 files, verified on origin; suite 789/789 on the
verification machine). Verify HEAD is `18ca9c6` and the working tree is
clean before creating the branch. `18ca9c6` adds only working-note files
(22 files, +10,686/−0, no code); CP1 proves the F3 binary at this base
reproduces the F3 verification numbers exactly.
Corpus: nopCommerce pinned `release-4.90.0` @ `e7a52b91`, read-only
(see Corpus section).

Read `F0_METAMODEL_v2.1.md` AND `CqrsLens_Modeling_Foundations.md`
(R0–R4) in the repo root in full before writing any code — both are
committed in the tree at `18ca9c6`; if either is absent, STOP and
report it rather than proceeding from memory. F0 v2.1 is the approved
architectural baseline; the foundations' R2 is the F4 charter ("the
model as contract: disciplined now, published at the rename") and the
rename plan (`CqrsLens_To_Noctua_Rename_Plan.md`, in the goal files,
placed in the repo root at launch if it is not already there) is the
rename charter — revised against the real released codebase at F4, per
Abdullah's 2026-11-02 ruling that it is finalized only when the real
codebase and real outputs exist. This brief is the work order for stage
F4 of the foundations build order. If this brief and F0, the
foundations, or the rename plan disagree, STOP and record the
disagreement in the checkpoint — do not resolve it yourself.

## Mission

Publish the model as a contract, and ship the wide tool under the wide
name. Two products, one stage, severable (see On blockers):

1. **Public schema.** The JSON model output becomes a frozen, versioned
   public contract: a machine-readable schema plus a human-readable
   stability document stating field-level promises — what
   `schemaVersion` means, what counts as breaking, how consumers pin
   the contract, and which sections are stable, legacy, or conditional.
   This is the decision F0 §10 deferred to F4 ("the public schema's
   field-level stability promises, decided at F4, after one full
   widening cycle") and foundations R2 ("published later, with the
   Noctua release, after one full widening cycle has taught us what
   the model actually needs to be"). Agents and other tools building
   on the model is the point of having one; the schema is what lets
   them.
2. **The Noctua release.** The rename lands here, with the wide tool
   and the published contract — per Abdullah's sequencing rule
   (2026-11-10): the name ships WITH the widening, never ahead of it.
   The rename covers the surfaces the rename plan names, verified
   against the real F4 tree (see R-RENAME), keeps every compat promise
   the plan makes (config fallback, valid baselines, old installs
   working), and prepares the release mechanics (version, CHANGELOG,
   README, release workflow, deprecation text). The driver builds and
   proves the rename on the branch; the driver performs NO release
   action — no tag, no publish, no repo rename, no deprecation (see
   H9 and the CP3 release checklist).

This stage ADDS NO detection. No new relation kinds, no new component
kinds, no new evidence rules, no Tier 2 (messaging, external
integrations, configuration surface). F4 is a contract + rename stage
over the settled model: the model JSON is unchanged apart from the
declared name/version deltas in H3, every output is the same view with
a new name stamp. Nothing here re-litigates F1, F2, or F3: the spine,
identities, dedup keys, evidence record, F2 relations, F3 docs tree,
gate, and intent layer are taken as built.

## Corpus (frozen; read-only)

| Item | Value |
|---|---|
| Repository | `https://github.com/nopSolutions/nopCommerce.git` |
| Location on box #4 (driver) | `/data/workspace/corpora/nopcommerce` |
| Location on the VM (verification) | cloned pinned at verification setup — see Environment; the VM copy under `cqrslens-tests/round2-corpus/nopcommerce` is at the WRONG commit (`51f116ac`) and must NOT be used |
| Tag / commit | `release-4.90.0` / `e7a52b9199f9c7651839e92826df47f668f3c767` |
| Size | 35 `*.csproj`, 133 `*Controller.cs`, 210 `*Service.cs` (99 interfaces), 3 repository files, 3,464 `.cs` files; net9.0; linq2db 5.4.1, no EF Core |

F3 baseline (frozen regression line; verified on the verification
machine, F3 final verification 2026-11-16 ~15:05 EET):

| Metric | Value |
|---|---|
| Relations (nopCommerce, text/json) | **8,764** |
| Endpoints | **1,151** |
| Components | **516** (282 service, 1 repository, 128 controller, 105 validator) |
| Suite | **789/789** (761 existing + 28 new F3 tests; 0 skipped where corpora are present) |
| Byte-identity | **70/70** outputs identical across the verification |
| nopCommerce JSON SHA-256 (text / roslyn, `--generated-at 2026-01-01T00:00:00Z`) | `5bc6d4f9bf9fa33e90724a5126060223a4f774349a0e3fa74dd95cbc42c0125a` / `378a7f42dad80b32d78e9064fd20d27ae0c224cd58fb112d13dcd7ffc5366b8b` |
| Ground-truth reconciliation | **55/55** checks exact (docs tree vs model) |
| Closest perf cell vs F2 | DiffPair-base text/md **+7.60%** median-of-sessions (observation, not a condition); nopCommerce text/json +5.21% |

The six frozen corpora (F3-verified counts, the regression line — any
delta is a defect unless enumerated with its cause): ABP 3,750, modular
790, northwind 191, eshop 121, ardalis 132, jasontaylor 131;
GoldenCorpus fixture 27. (F1/F2 counts are history, not the regression
line; do not use them as targets.)

Corpus handling rules (part of H10): verify `git status` clean in the
corpus before the first scan and after the last run. Never modify the
corpus, never check out another tag, never build it. Text mode scans
source only; no corpus compilation is involved.

## Decisions for Abdullah at approval

These are the points the foundations, F0, and the rename plan leave
open or state as drafts. Each carries a recommended default; approval
of this brief settles them as written, amendment replaces the default.
The driver implements the approved text, not its own preference.

- **D1 — Schema versioning and stability policy (recommended:
  `schemaVersion` stays 1; additive-only evolution; legacy sections
  documented as legacy-but-stable).** The published schema documents
  the contract exactly as F4 ships it. `schemaVersion` stays `1`:
  the F4 JSON is the F3 JSON plus name/version stamps, and every
  existing reader (`--diff`, baselines, MCP consumers, scripts) keeps
  working with zero migration. Evolution from here is additive-only:
  new fields and new sections are optional additions; removing,
  renaming, or redefining a field or section is a breaking change
  that requires a `schemaVersion` bump AND a reader that accepts both
  the old and the new version. The legacy edge sections
  (`flowEdges`, `resolvedEdges`, `serviceCallEdges`,
  `dispatchCallSites`) STAY in the contract, documented as
  legacy-but-stable: F0 §8 says they "remain until the public schema
  ships at F4" — the schema ships by documenting them, not by
  deleting them, because deletion would break `--diff` readers and
  every consumer of the current JSON with no migration path.
  Removing them is a future major's decision, chartered separately.
  If vetoed (bump to 2 and drop the legacy sections), the brief
  gains a migration section: dual-accepting readers, a migration
  note, and a compat test proving old JSON still diffs — and the
  release version decision (D4) is revisited, since the model itself
  would then break.
- **D2 — Schema artifact form (recommended: machine-readable JSON
  Schema + human-readable stability doc, both validated by tests).**
  The schema ships as `noctua.model.schema.json` at the repo root
  (JSON Schema draft 2020-12, the same draft as the existing config
  schema), plus `docs/model-schema.md` stating the field-level
  stability promises in prose: every top-level section and field,
  its type, whether it is always present or conditional (sections
  are omitted when empty — see R-SCHEMA), its stability class
  (stable / legacy / conditional), and what `schemaVersion` governs.
  `additionalProperties: false` at the top level and per section:
  the schema accepts exactly what the tool emits, so a consumer can
  trust it and a drift is caught, not silently tolerated. The schema
  is proven by tests, not by inspection: every JSON the tool emits
  (fixtures + nopCommerce, both modes) validates, and corrupted
  samples (a removed required field, a wrong-typed field, an unknown
  property) are rejected. If vetoed (docs-only, no machine-readable
  schema), the validation tests become doc-accuracy tests only, and
  R2's "agents and tools building on the model" loses its
  machine-checkable footing — say so in the checkpoint.
- **D3 — Rename surfaces (recommended: the rename plan's MUST table,
  verified against the F4 tree; internals untouched; compat kept).**
  Rename exactly the surfaces in the R-RENAME table (package ID,
  command name, banner, report stamps, SARIF name, MCP server name,
  config filename, schema file, action, pins, docs, release
  workflow). LEAVE ALONE, per the rename plan's settled point:
  C# namespaces (`CqrsLens.*`), project and assembly names, the
  solution filename, and test-only environment variables
  (`CQRSLENS_CORPUS_ROOT`, `CQRSLENS_ABP_ROOT`) — internal, invisible
  to users; renaming them is churn risk, and if it ever happens it
  belongs to a later refactor, not the rename release. UNCHANGED by
  design: CLI flags (no flag renamed), MCP tool names
  (`get_architecture_dna`, `check_architecture`,
  `trace_entity_impact` — capability names, not brand names),
  finding and diagnostic codes, baseline format, exit codes, config
  keys, action inputs, and JSON section/field names (only name-stamp
  VALUES change). Compat: Noctua reads `.noctua.json` first, then
  falls back to `.cqrslens.json` FOREVER (the rename plan's settled
  rule — one existence check, permanent insurance for every repo
  that already has the old file); old baselines stay valid; old
  installs keep working. If vetoed on any surface (e.g. keep the
  command name, or rename namespaces too), the R-RENAME table is
  amended line by line and the grep gate (CP3 gate 4) is re-scoped
  to match — the gate must still be total over the approved table.
- **D4 — Release version (recommended: 2.0.0).** The package ID, the
  command name, and the primary config filename all change: every
  install script, CI line, and action pin breaks and must be edited
  by the user. That is a breaking change under SemVer, so the Noctua
  release is 2.0.0, set in `Directory.Build.props` with all pins
  moved together (the pin test fails until they do — by design). The
  model JSON itself stays additive (`schemaVersion` 1, per D1); the
  major bump is for the rename, not the model — the CHANGELOG entry
  says exactly that. If vetoed (1.2.0), the release is framed as a
  minor with a rename notice, and the CHANGELOG must still state the
  breaking install/command changes plainly.
- **D5 — The old NuGet ID (recommended: deprecate, never unlist, no
  dual-publish; Abdullah's click, never automatic).** After the
  Noctua package is verified live on NuGet, mark ALL CqrsLens
  versions deprecated, reason "package renamed", alternate package
  Noctua, custom message with the uninstall/install commands. Never
  unlist: published versions stay installable and functional
  forever. NO dual-publish of the Noctua release under the old ID:
  the rename plan's dual-publish recommendation was written for a
  1.0.0 that has since shipped as CqrsLens (1.0.0 and 1.1.0 are
  live); the old line ends at 1.1.0, and the deprecation banner IS
  the notice for users who update normally. The download-count reset
  on the new page is an accepted cost — NuGet offers no self-serve
  count transfer, and the `support@nuget.org` request is a long shot
  Abdullah may try himself; the driver does not contact NuGet.
  Deprecation is a nuget.org account action by Abdullah, after
  verification — the driver prepares the exact deprecation text in
  the release checklist and performs nothing. If vetoed (publish a
  final CqrsLens notice release, e.g. 1.1.1 carrying only the
  "now Noctua" banner), that release is chartered as a separate
  release step after F4 verification, not driver scope.
- **D6 — Repo rename (recommended: rename `cqrslens` → `noctua`,
  his settings click, sequenced before the release tag).** GitHub
  repo renames redirect web URLs, git remotes, and release links, as
  long as no new repo is created under the old name afterwards
  (never create one). Sequence: F4 branch verified and merged to
  main (his call) → repo renamed (his click) → release tag pushed →
  release workflow publishes → Noctua verified live → old ID
  deprecated (D5). The Action path becomes
  `AbdullahHakima/noctua@…`; old released action versions keep
  working via redirect. The F4 branch updates README/docs references
  to the new path before the rename lands — those references 404
  until the rename happens, which is correct: the branch is not
  released until the rename does. One UNVERIFIED point, flagged for
  the release checklist: whether `raw.githubusercontent.com` URLs
  redirect after a repo rename (this decides whether old `$schema`
  URLs keep resolving for editor completion). The checklist must
  verify it at release time and record the outcome; if they do not
  redirect, the release notes say old `$schema` URLs go stale (an
  advisory editor feature, not a tool behavior — the tool does not
  fetch the schema). If vetoed (keep the repo name, rename only the
  package), the README/docs/action references keep the old path and
  only the package/command/config names change.
- **D7 — Site, comms, and examples (recommended: OUT of the F4
  driver stage).** The Noctua site flip, the Examples page rebuild
  (`Noctua_Examples_Plan.md` — every figure re-measured with the
  release build, extracted programmatically, never hand-typed), and
  any blog/LinkedIn note are separate tracks at Abdullah's approval,
  after the release build exists — the driver touches no site copy
  and writes no public claim beyond the repo's own CHANGELOG/README.
  The site is his; Mariam reviews it only on request. If vetoed (do
  the site flip inside F4), it arrives as a separate brief with its
  own verification, not as driver scope creep.
- **D8 — Scope freeze (recommended: schema + rename only).** No new
  detection, no Tier 2, no Reporting Upgrade backlog items
  (per-feature CQRS map split, pipeline diagram, notifications
  diagram, findings visual — recorded backlog, not F4 scope), no
  Wraps rendering fix (the H7 gap stays documented exactly as F3
  left it), no behavior change beyond the declared H3 deltas. The
  release is the F1–F3 widened scope, under contract, under the new
  name. If vetoed (fold an extra item in), the item arrives as an
  amended brief section with its own gates — it is not absorbed
  silently.

## F4 rules (normative)

### R-SCHEMA — The public schema describes what the tool emits, exactly

The schema is written from the writer code and proven against real
output — never from memory, never aspirational. Inventory first (CP1):
enumerate every top-level property and section the JSON writer emits
(verified at the F4 base: `schemaVersion`, `toolVersion`, `scanner`,
`generatedAt`, `elapsedMilliseconds`, `rootName`,
`discoveredFileCount`, `fileCount`, optional `watermark`,
`conventions`, `summary`, `projects`, `requests`, `handlers`,
`validators`, `behaviors`, `dbContexts`, `dbSets`, `entities`,
`relationships`, `checks`, `diagnostics`, `notifications`,
`notificationHandlers`, `endpoints`, `flowEdges`, `resolvedEdges`,
`serviceCallEdges`, `dispatchCallSites`, `unresolvedDispatches`,
`relations`, `components`, `spineEndpoints`, `unproven` — verify this
list against the code in CP1; it is the starting inventory, not a
promise), then every field within each section, with types and
presence rules. Sections are CONDITIONAL: the writer omits a section
when it is empty (no empty arrays) — the schema marks every section
optional and the stability doc says so, with the one exception the
code proves otherwise. `relations` is conditional on unindexed
relations existing. Every emitted field path must appear in the
schema with the correct type; every field the schema marks required
must appear in every emitted document. A script enumerates field
paths from real outputs (all fixtures + nopCommerce, both modes) and
diffs them against the schema — zero undeclared emitted fields, zero
missing required fields. The stability doc (`docs/model-schema.md`)
states, per section: purpose, stability class (stable / legacy /
conditional), and the evolution promise from D1. The doc and the
schema must agree field for field; a disagreement is a defect.

### R-SCHEMA-CONFIG — The config schema must describe the config file

The existing config schema (`cqrslens.schema.json`, draft 2020-12,
`additionalProperties: false`) does NOT describe the `intent` section
F3 added to `.cqrslens.json` — a valid F3 config with `intent` fails
validation against the published schema. That is a defect F4 fixes:
the renamed config schema (see R-RENAME) gains the full `intent`
section from `F3_INTENT_FORMAT.md` (committed at `18ca9c6`), with the
same strictness the tool's parser enforces. CP1 confirms whether the
tool validates configs against this schema at runtime (believed
editor-only — verify, do not assume); either way, the schema must
accept every config the tool accepts and reject what the tool
rejects, proven by tests over valid and invalid samples.

### R-RENAME — The rename table (verified against the F4 tree)

Rename exactly these surfaces. CP1 re-verifies every row against the
code and records any surface found that is not listed here — an
unlisted user-visible name string is a checkpoint finding, not a
silent addition.

| Surface | Where (verified at the F4 base) | Change |
|---|---|---|
| NuGet package ID | `src/CqrsLens.Cli/CqrsLens.Cli.csproj` `PackageId` | `CqrsLens` → `Noctua` |
| Tool command | same file, `ToolCommandName` | `cqrslens` → `noctua` |
| Console banner | `CliRunner.cs` (`CqrsLens {Version}`) | → `Noctua {Version}` |
| Report stamps | `MarkdownReportWriter.cs`, `MarkdownOverviewWriter.cs`, `HtmlReportGenerator.cs`, `AgentArchitectureMapGenerator.cs` ("Generated by CqrsLens …") | → `Noctua` |
| Diff stamp | `ArchitectureDiffMarkdownRenderer.cs` ("with **CqrsLens …**") | → `Noctua` |
| SARIF tool name | `SarifReportGenerator.cs` (`"name", "CqrsLens"`) | → `Noctua` |
| MCP server name | `McpRequestRouter.cs` `serverInfo` (`"name": "cqrslens"`) | → `"noctua"` (tool names unchanged, per D3) |
| Docs-tree headers | F3 docs page writers (`> Source: generated from the model, CqrsLens …`) | → new name + version (exact strings inventoried in CP1) |
| Config filename | `ConfigurationLoader.FileName` | primary `.noctua.json`; `.cqrslens.json` fallback forever (R-COMPAT) |
| Config schema file | `cqrslens.schema.json` at root | → `noctua.schema.json`, new `$id`, plus the `intent` section (R-SCHEMA-CONFIG) |
| GitHub Action | `action.yml` name/description, `CQRSLENS_*` env vars, install/update commands | → `Noctua` / `NOCTUA_*`; inputs unchanged |
| Version pins | README, `docs/ci.md`, ROADMAP, CHANGELOG, `VersionTests` | regenerate together at the D4 version |
| `$schema` URLs | README, `docs/configuration.md` | new canonical URL under the renamed repo (redirect behavior per D6) |
| Release workflow | `.github/workflows/release.yml` (echo/install lines, package checks) | → `Noctua` |
| Docs copy | `docs/*.md`, README, ROADMAP, CHANGELOG | name + install commands + action path, per D6; claims locked to shipped capability (H12) |

The NuGet package ID `Noctua` was verified available 2026-11-16
(flat-container 404; `CqrsLens` 200). Re-verify at release time — the
check is cheap and the cost of being wrong is total.

### R-COMPAT — Old things keep working

- **Config:** Noctua reads `.noctua.json` first; if absent, reads
  `.cqrslens.json`. If both exist, `.noctua.json` wins and the tool
  says which file it read (in verbose/diagnostic output, not by
  failing). The tool never writes either file, never migrates one to
  the other. A test proves each case, including a `.cqrslens.json`
  carrying an `intent` section.
- **Baselines:** they compare findings, not name stamps. An old
  baseline (written by a CqrsLens release, `schemaVersion` 1) gates a
  Noctua run unchanged — proven by a test that writes a baseline
  with the F3 binary and gates with the F4 binary, plus a test over
  a checked-in 1.1.0-era baseline file. `BaselineStore` keeps
  accepting `schemaVersion` 1.
- **Diffs:** a `--diff` JSON written by the F3 binary reads under the
  F4 binary (`JsonReportDocumentReader` keeps accepting
  `schemaVersion` 1) — proven by a test over a checked-in F3 JSON.
- **Installs:** every existing CqrsLens install, report, baseline,
  and CI pipeline keeps working unchanged. The old package is never
  unlisted (D5).
- **MCP:** tool names, request shapes, and answer shapes unchanged;
  only the `serverInfo` name stamp changes. The MCP stdio probe in
  the regression gate proves it.

### R-RELEASE-DOCS — Release copy states only shipped, verified capability

CHANGELOG gains the D4-version entry: the rename, the schema, the
F1–F3 scope it releases, the breaking install/command changes stated
plainly, the compat promises, and the "formerly CqrsLens" line.
ROADMAP moves the foundations stages to shipped. README leads with
Noctua, carries one "formerly CqrsLens" line, and shows the new
install command, the new action path, and the new config filename
(with the fallback noted). Every number, claim, and sample in the
release copy comes from the F4 verification record — no new claims,
no performance claims beyond the measured gate-8 table, no
capability the release does not ship. The copy is written in
Abdullah's voice: plain developer prose, no process or pacing
language, no meta-talk about how the work was done.

### R-FIXTURES — Fixtures prove the schema, the rename, and compat

Fixtures and tests (additive, except the enumerated name-assertion
updates in H2) must prove: every emitted JSON validates against the
published schema (fixtures + a nopCommerce-shaped sample where the
corpus is absent); the schema rejects corrupted samples (removed
required field, wrong-typed field, unknown property); the config
schema accepts a full config including `intent` and rejects an
unknown key and a mistyped `intent` field; the rename grep gate
(zero user-visible old-name strings in a generated report set —
md/html/json/sarif/agent/docs-tree/console — except the declared
compat list); config fallback in both directions plus both-files
precedence; old-baseline gating; old-diff-JSON reading; MCP probe
identical except the server name; `--strict` / `--diff` behavior
unchanged; no-intent and no-config behavior identical to F3 apart
from name stamps.

## Hard constraints (violating any one fails the stage)

- **H1 — No detection beyond this brief.** No new relation kinds, no
  new component kinds, no new evidence rules, no Tier 2 (messaging,
  external integrations, configuration surface). The remaining
  Reporting Upgrade items (per-feature CQRS map split, pipeline
  diagram, notifications diagram, findings visual) are OUT of F4 —
  recorded backlog, not driver scope. Do not implement them.
- **H2 — Existing tests pass, except enumerated name-assertion
  updates.** All 789 existing tests pass (verification-machine
  terms: the full suite runs where corpora are present — see
  Environment). On box #4 expect 721 pass + 68 skipped
  (external-corpus tests; box #4 does not hold the corpora) —
  record the skip count in every checkpoint. EXCEPTION, and the only
  one: tests that assert the old name, version, or paths
  (`VersionTests` pins, banner/stamp assertions, MCP `serverInfo`
  assertions, config-filename tests, SARIF name tests, action-path
  assertions) are UPDATED as part of the rename — each updated test
  enumerated in the checkpoint with its old → new expectation. No
  other test may be weakened, skipped, deleted, or "fixed". New
  tests are additive and must pass. If a test expectation
  contradicts this brief outside the rename exception, STOP and
  record it.
- **H3 — Model unchanged apart from declared deltas.** The JSON model
  is byte-identical to the F3 binary's output on every fixture and
  corpus (same scan, same `--generated-at`) EXCEPT the declared
  delta whitelist: the `toolVersion` value (the D4 version), and
  name-stamp string values (watermark/default stamps carrying the
  tool name). Relations, components, endpoints, checks, diagnostics,
  sections, field names, and every other value are identical — a
  whitelist violation is a defect. The same whitelist governs
  md/html/agent/summary/sarif/console/docs-tree outputs: name and
  version stamps only. `schemaVersion` stays 1 (per D1). MCP answers
  identical except the `serverInfo` name. `--diff` / `--strict`
  behavior unchanged. Baselines stay valid (R-COMPAT).
- **H4 — Frozen question set.** Unmoved. `FrozenQuestionSet.cs`
  untouched. Any movement must be enumerated in the checkpoint with
  the reason for each.
- **H5 — Release changes ONLY as chartered.** The version bump (D4)
  in `Directory.Build.props` plus all pins moved together;
  CHANGELOG/ROADMAP/README edits ONLY as R-RELEASE-DOCS charters;
  `.csproj` diff ONLY `PackageId`/`ToolCommandName`; no new
  `PackageReference` lines; no other `.csproj` diff. NO new CLI
  surface and NO renamed flags — the F3 flags stay exactly as they
  are. (This reverses F3's H5 deliberately: F4 IS the release-prep
  stage. The charter, not the driver, decides which release files
  move.)
- **H6 — Architecture rules hold.** The 4-project one-way dependency
  chain and one-type-per-file rule stay intact
  (`ArchitectureBoundaryTests` must pass).
- **H7 — Rendering gaps preserved, not fixed.** Do NOT change
  sequence/flow rendering or the Wraps gap F1–F3 left documented.
  The gaps stay documented exactly as F3 left them. No silent
  correction.
- **H8 — Determinism.** Repeated scans AND repeated exports of
  unchanged source with a fixed `--generated-at` produce identical
  output bytes — fixtures and nopCommerce, all formats, both modes,
  every file in the docs tree.
- **H9 — Driver never commits, never pushes, never releases.** Leave
  all work uncommitted in the working tree. The driver performs NO
  release action: no tag, no `dotnet nuget push`, no repo rename, no
  deprecation, no contact with NuGet or GitHub settings. Mariam
  verifies each checkpoint independently (on her own machine — see
  Environment) and commits only after verification passes; release
  steps follow the CP3 checklist, each with its named owner.
- **H10 — Corpus read-only.** See Corpus section. Pinned tag/commit
  only; no re-pinning. The verification machine's nopCommerce clone
  is created pinned and treated read-only from the first scan.
- **H11 — No corpus or class-name special-casing.** Every rule must
  be general: no `if nopCommerce`, no hardcoded type names in
  production code. Tests may name audit types; production code must
  not.
- **H12 — No public claim beyond shipped capability.** README,
  CHANGELOG, ROADMAP, and the stability doc state only what the
  release ships and the verification proves. No site copy, no
  examples copy, no performance claim beyond the measured gate-8
  table (see D7).

## Environment

- **Driver (box #4).** .NET SDK 8.0.427 at `~/.dotnet`. Every dotnet
  command runs with `DOTNET_ROOT=$HOME/.dotnet` and
  `PATH=$HOME/.dotnet:$PATH` exported. Test command:
  `dotnet test CqrsLens.sln -c Release --nologo`. Baseline binary
  for before/after comparison: create a clean worktree with
  `git worktree add /tmp/cqrslens-f3base 18ca9c6`, build it once,
  use it read-only for baseline outputs, delete the worktree when
  done. Never modify it. Corpus:
  `/data/workspace/corpora/nopcommerce`, pinned as above; verify the
  pin (`git rev-parse HEAD` → `e7a52b91…`, `git describe --tags` →
  `release-4.90.0`, `git status` clean) before the first scan and
  after the last run. Box #4 does NOT hold the six frozen corpora:
  the driver's A/B scope is the in-repo fixtures + nopCommerce. The
  6-corpus A/B and the full perf A/B are Mariam's verification on
  her own machine, not the driver's — but a smoke regression on
  fixtures or nopCommerce is a stop-and-report. Both scanner modes
  (text + Roslyn) must produce the same verdicts on fixtures; any
  mode divergence on nopCommerce must be enumerated with its cause;
  divergence attributable to F4 is a CP3 gate failure.
- **Verification (Mariam's own machine — standing direction
  2026-11-16: Mariam leads all F phases, verification on her
  machine, NOT box #3).** Box #3 is out of the F-phase loop (blog
  only). The VM runs the full suite and the full A/B: .NET SDK
  8.0.426 at `~/workspace/tools/dotnet-cqrslens` (NOT on PATH —
  export `DOTNET_ROOT` + `PATH` for every dotnet command; record
  `dotnet --list-sdks` in the verification report). Both binaries
  (F3 baseline worktree at `18ca9c6` and the F4 tree) are built on
  the VM with the same SDK, so byte-identity and perf comparisons
  are same-machine valid. Corpora on the VM:
  `~/workspace/cqrslens-benchmark/corpus/` holds the six frozen
  trees (`northwindtraders`, `eshop`, `modular-monolith`, `abp`,
  `ardalis-cleanarchitecture`, `jasontaylor-cleanarchitecture` —
  byte copies of the box #3 corpora per the corpus manifest, so VM
  numbers are comparable with the box record); the suite reads them
  via `CQRSLENS_CORPUS_ROOT` (default `/data/workspace/cqrslens-corpus`)
  and `CQRSLENS_ABP_ROOT` (default `/data/workspace/abp-corpus/abp`)
  — verification sets both to the VM paths and records the mapping.
  nopCommerce MUST be cloned pinned (`release-4.90.0` @ `e7a52b91`)
  at verification setup and verified clean; the existing VM copy is
  at the wrong commit and must not be used. Full-suite expectation
  on the VM: 789 existing + new tests, 0 failed, 0 skipped — a skip
  on the VM is a finding, not an environment shrug. Known
  pre-existing defect carried over: ABP Roslyn mode exits 5 on both
  binaries with no output (recorded in the F2/F3 verifications) —
  it is the baseline, not a regression.
- **Transfer (driver tree → verification machine).** Git bundle +
  base64-over-SSH, hash-verified at each hop (scp over the proxy
  hangs). Never hand-type base64 into a heredoc — always generate
  with `base64 -w0` from the file and hash-verify (the F2
  commit-message lesson). Snapshots MUST include untracked new
  files: the F3 CP3 snapshot held only the 20 modified files, not
  the 23 new ones, and the tree had to be rebuilt from the live box
  (F3 verification §11.3) — the F4 snapshot pattern includes `git
  diff` output PLUS a tarball of untracked source/test files, both
  hash-recorded.
- **ABP size note.** The ABP tree is ~1.3 GB of source; the VM has
  the disk (136G free at last check) and the RAM headroom for text
  mode. Roslyn mode on ABP is the known exit-5 case; do not burn
  verification time re-proving it — record it once per binary and
  move on.

## CP1 — Reproduce the F3 baseline + schema inventory + rename census

1. **Reproduce the F3 baseline.** Run the F3 baseline binary
   (worktree at `18ca9c6`) on the pinned corpus, text mode, JSON,
   pinned `--generated-at 2026-01-01T00:00:00Z`, and reproduce the
   F3 numbers exactly: 8,764 relations, 1,151 endpoints, 516
   components, JSON SHA-256 `5bc6d4f9…` (text) / `378a7f42…`
   (roslyn), suite 721 pass + 68 skipped (box #4 terms — the 68 are
   the external-corpus tests box #4 cannot run; 789/789 is the
   verification-machine full-corpus count). If it does not
   reproduce, STOP: the base or toolchain differs from the F3
   verification, and nothing else in this brief can be trusted
   until that is resolved. (This also proves `18ca9c6`'s docs-only
   commit touched no code.)
2. **Schema inventory (paper first).** Enumerate every JSON field
   path the writer emits (top-level sections + every nested field),
   with types and presence rules, from the writer code — then
   confirm the inventory against real outputs (all fixtures +
   nopCommerce, both modes) with a field-path enumeration script.
   Write the schema draft (`noctua.model.schema.json`) and the
   stability-doc draft (`docs/model-schema.md`) from the inventory.
   No rename code starts until the inventory exists — foundations
   before code. Record: the full section/field inventory, which
   sections are conditional, the `relations` conditional, and the
   config-schema `intent` gap (R-SCHEMA-CONFIG) with the runtime-
   validation answer.
3. **Rename census.** Grep census of every user-visible `CqrsLens` /
   `cqrslens` string in the tree (source strings, docs, workflows,
   action, schema files) with a disposition per hit: rename (with
   the R-RENAME row), keep (internal/compat, with the reason), or
   unlisted finding (a surface the brief missed — record it, do not
   silently rename it). The census is the checklist CP2 works
   through and CP3 gate 4 verifies against.
4. **Schema validation harness.** Tests proving emitted JSON
   validates against the draft schema, and that corrupted samples
   are rejected (R-FIXTURES). The harness runs in the suite from
   CP1 on; the schema is not "done" until the harness is green on
   every fixture in both modes.

Suite green (789 existing + new, box #4 terms). Write
`CHECKPOINT_F4_CP1.md` in the repo root: baseline-reproduction
proof, the schema inventory + drafts, the rename census with
dispositions, files changed (`git status --short` +
`git diff --stat`), test result, decisions, open issues.

## CP2 — Schema proven + rename implemented + compat proven

Implement the schema as final (R-SCHEMA), the rename per the census
(R-RENAME), and the compat rules (R-COMPAT), with the release docs
(R-RELEASE-DOCS) drafted. Document the exact delta whitelist (H3)
with the fields and the reason for each, and the exact compat
behaviors (config precedence, baseline/diff acceptance) in the
checkpoint.

- **Tests for every rule in R-FIXTURES**, including all schema
  validation / rejection / config-schema / rename-grep / compat /
  MCP-probe cases. Each test names the rule it proves. Every
  name-assertion test update enumerated old → new (H2).
- **nopCommerce demonstration.** Full report set + docs tree for the
  pinned corpus from the F4 binary; the JSON diffed against the F3
  baseline binary field by field, with every differing byte shown to
  be inside the H3 whitelist; the gate clean on a fresh export; a
  scratch `.noctua.json` with intent and a scratch `.cqrslens.json`
  with intent both read correctly, with precedence demonstrated.
  Scratch files live in `/tmp` (ephemeral); the checkpoint is the
  durable record. Do NOT add suite tests depending on the box #4-only
  corpus path.
- **Per-corpus rename smoke.** Report sets for the in-repo fixtures;
  record the byte-diff summary per format vs the F3 binary, with
  every difference attributed to the whitelist or recorded as a
  defect. Any non-whitelist byte change is a stop-and-report.

Suite green. Write `CHECKPOINT_F4_CP2.md`: the delta whitelist, the
compat behaviors, the nopCommerce demonstration commands + outputs,
the rename smoke table, files changed, test result, decisions, open
issues.

## CP3 — Gates + release checklist

Prove each gate with commands and outputs recorded in the checkpoint.
Gates 1–6 run on box #4; gate 7's six-corpus leg and gate 8's full
A/B are Mariam's verification on her machine — the driver evidences
the fixture + nopCommerce legs and records everything needed to
re-run them. The release checklist is a CP3 deliverable, not an
afterthought.

1. **Legacy parity.** Full suite green; frozen question set exact
   (H4); JSON model identical to the F3 baseline binary on fixtures
   + nopCommerce except the H3 whitelist — not "equivalent",
   identical outside the whitelist, with the whitelist diff shown.
2. **Determinism.** Two scans + two exports with fixed
   `--generated-at` produce identical bytes on all fixtures and
   nopCommerce, all formats, both modes — including every file in
   the docs tree and every standalone SVG.
3. **Schema correctness.** Every emitted JSON (fixtures +
   nopCommerce, both modes) validates against the published schema;
   corrupted samples are rejected as specified; the field-path
   enumeration shows zero undeclared emitted fields and zero missing
   required fields; the stability doc agrees with the schema field
   for field; the config schema accepts a full config including
   `intent` and rejects unknown keys and mistyped intent.
4. **Rename correctness.** The grep gate: zero user-visible
   `CqrsLens` / `cqrslens` strings in a generated report set
   (md/html/json/sarif/agent/docs-tree/console) except the declared
   compat list (the "formerly CqrsLens" line, the config-fallback
   mention, and any other entry the checkpoint names); every
   R-RENAME row verified renamed in the built tool's actual output
   (not just in source); the UNCHANGED list verified unchanged
   (flags, MCP tool names, finding codes, exit codes, namespaces).
5. **Compat.** Old `.cqrslens.json` (with and without `intent`) read;
   both-files precedence correct; old baseline gates a new run;
   old `--diff` JSON reads; MCP probe identical except the server
   name; no-config and no-intent behavior identical to F3 apart
   from name stamps.
6. **Ground-truth reconciliation.** Reports + docs tree for
   nopCommerce reconcile against the F3-verified model: 516
   components (282/1/128/105), 8,764 relations, 1,151 endpoints,
   requests/handlers/notifications/behaviors/DbContexts 0/0/0/0/0 —
   every number traceable to the model JSON, with the
   reconciliation commands recorded verbatim; schema field-level
   reconciliation per gate 3.
7. **Regression.** Fixtures + nopCommerce: every output identical
   to the F3 baseline binary except the H3 whitelist. MCP stdio
   probe: identical except the server name. `--diff` / `--strict`
   outputs identical in behavior. H7 preserved (sequence/flow
   rendering untouched). Zero new warnings (normalized, deduped
   warning-set comparison vs the F3 tree; `CqrsLens.Core` stays 0
   warnings — the project keeps its name, per D3).
8. **Performance.** Wall-clock `dotnet <dll> <corpus> --format <fmt>`
   including process startup, 1 warmup + 3 timed runs, median, same
   machine/session, pinned `--generated-at`. Bound: **no measured
   scan cell above +10%** vs the F3 baseline binary (text/json,
   text/md, roslyn/json — fixtures + nopCommerce). A single-session
   breach triggers a second session (5-run median); judgment is on
   median-of-sessions, and small-corpus session swing (±5pp, the
   F1-perf lesson) is noise unless it reproduces across sessions
   AND a same-session comparison confirms a regression attributable
   to F4. In-process profiling is never a substitute for wall-clock
   medians. The driver leg runs on box #4; the verification leg
   runs on the VM with both binaries built there — each leg judged
   on its own machine, never mixed.
9. **Scope clean + release readiness.** `git diff` review: only
   chartered files move (the R-RENAME table + schema files + release
   docs + enumerated test updates); no added `<PackageReference>`
   lines; no new or renamed CLI flags (state how this was checked);
   no corpus/class-name special-casing (state how this was checked);
   corpus `git status` still clean; version + pins consistent
   (the pin test green). **Release checklist** (the handoff):
   ordered steps for merge → repo rename (D6, his click) → tag →
   release workflow → NuGet verification (`Noctua` flat-container,
   clean-environment install, `noctua --version`, a real scan) →
   deprecation (D5, his click, exact text prepared) → raw-URL
   redirect check (D6) → comms (D7, separate track) — each step
   with its owner, its exact command or click, and its verification.

Write `CHECKPOINT_F4_CP3.md`: gate-by-gate evidence with the exact
commands run and their outputs, full test result, timing tables,
the complete `git status` / diff stat of the working tree, the
release checklist, open issues. Then STOP for verification.

## Checkpoint format

Every checkpoint file goes in the repo root, named exactly
`CHECKPOINT_F4_CP<n>.md`, and contains: status (done / blocked), files
changed (paste `git status --short` + `git diff --stat`), tests run with
results, gate evidence where applicable, decisions made, open issues,
and the exact commands to reproduce every claim. Do not delete or
overwrite a previous checkpoint file. Write the checkpoint, then
continue to the next CP in the same session, except after CP3, where
you STOP. If the session is interrupted, resume from the last written
checkpoint — re-read it first. Checkpoint and brief files are working
notes: they stay uncommitted in the working tree during the build
(H9) and follow the cleanup-commit pattern afterwards (the `18ca9c6`
precedent — working notes committed alongside the code by Mariam,
after verification, at Abdullah's direction), never as part of the
F4 code commit itself.

## On blockers

If any hard constraint cannot be met, if a test expectation contradicts
this brief outside the H2 rename exception, or if F0, the foundations,
the rename plan, and this brief disagree: STOP, write the checkpoint
with the blocker described precisely (what, where, what you tried),
and do not work around it. Do not weaken a gate. Do not invent an
interpretation of F0, the foundations, or the rename plan. The
products are SEVERABLE: if the rename stalls on a decision or a
blocker, the public schema (CP1 + its tests) can land alone under
the CqrsLens name — the schema content is name-independent except
its `$id`/title, which the checkpoint records as the rename-dependent
residue. The reverse does not hold: the rename without the schema
repeats the mistake R2 warns against (a new name on an unpublished
contract). Everything else blocks.

## What success looks like

Three checkpoint files, a green suite (789 existing + additive new
tests, plus enumerated name-assertion updates), a published model
schema proven against every emitted JSON with a stability doc that
agrees with it field for field, a rename proven by a total grep gate
with every compat promise tested (old configs read, old baselines
gate, old diffs read, old installs untouched), the JSON model
identical to F3 outside the declared name/version whitelist, a
release checklist with a named owner per step, and an uncommitted
working tree on `foundations/f4-noctua` that Mariam can verify on
her own machine — after which the Noctua release can be executed
step by step, with nothing left to decide except the calls that are
Abdullah's alone.

## Appendix: verification record this brief rests on (all checked 2026-11-16)

- F3 base: `9141a83` (43 files, +3776/−41), parent `db3c5f6`; cleanup
  commit `18ca9c6` (22 working-note files, +10,686/−0, tree clean,
  pushed). F4 base `18ca9c6`, verified on box #4 (HEAD + clean
  status + show --stat).
- F3 verification: PASS, all 9 gates — suite 789/789, byte-identity
  70/70, determinism 12/12 + docs trees 24/24, reconciliation 55/55,
  6-corpus A/B zero movement, perf all 18 cells ≤+10%
  median-of-sessions (closest DiffPair-base text/md +7.60%).
  Report: `hidden_files/f3-final-verification.md`.
- Rename surfaces verified in the live tree at the F4 base:
  `PackageId`/`ToolCommandName` in `CqrsLens.Cli.csproj`;
  `ConfigurationLoader.FileName = ".cqrslens.json"`;
  `CurrentSchemaVersion = 1` (JSON + baseline readers, both
  strict); SARIF `"name", "CqrsLens"`; MCP `serverInfo` name
  `"cqrslens"`; banner in `CliRunner.cs`; report stamps in four
  generators + the diff renderer; `action.yml` name `CqrsLens` +
  `CQRSLENS_*` env vars; `VersionTests` pin assertions (README
  `AbdullahHakima/cqrslens@v{version}`, action default version);
  `Directory.Build.props` `<Version>1.1.0</Version>`; release
  workflow tag/version check + `dotnet tool install --global
  CqrsLens`; 4 projects (`CqrsLens.Analysis/Cli/Core/Reporting`).
- Config schema `cqrslens.schema.json` (3,262 bytes, draft 2020-12,
  `additionalProperties: false`) verified MISSING the `intent`
  section — the R-SCHEMA-CONFIG defect.
- JSON writer verified: sections conditional on non-emptiness;
  `relations` conditional on unindexed relations; legacy edge
  sections still emitted (`flowEdges`, `resolvedEdges`,
  `serviceCallEdges`, `dispatchCallSites`).
- NuGet ID `Noctua`: flat-container 404 on 2026-11-16 (available);
  `CqrsLens`: 200.
- VM verification prerequisites verified: .NET SDK 8.0.426 at
  `~/workspace/tools/dotnet-cqrslens` (not on PATH); six frozen
  corpora at `~/workspace/cqrslens-benchmark/corpus/` (manifest:
  byte copies of the box #3 corpora); suite corpus env vars
  `CQRSLENS_CORPUS_ROOT` / `CQRSLENS_ABP_ROOT` in
  `FrozenQuestionSet.cs`; VM nopCommerce copy at the WRONG commit
  (must re-clone pinned).
- UNVERIFIED, flagged for the release checklist / CP1: (1) whether
  `raw.githubusercontent.com` URLs redirect after a repo rename
  (D6); (2) whether the tool validates configs against
  `cqrslens.schema.json` at runtime (believed editor-only — CP1
  confirms); (3) the full `action.yml` input list beyond the inputs
  seen in `VersionTests` and the env-var block (CP1 census
  confirms); (4) whether `support@nuget.org` can transfer download
  counts (long shot, his call).
- NOT resurrected: the fresh-repo 0.0.1 idea (proposed 2026-11-13,
  advised against, never adopted — he shipped 1.1.0 on the existing
  repo). F4 builds on the existing repo and its history.
