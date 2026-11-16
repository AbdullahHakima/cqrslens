# BRIEF — F3: Docs export + freshness gate (CqrsLens foundations) — DRAFT FOR ABDULLAH'S APPROVAL

Status: APPROVED 2026-11-16 ~14:39 EET — Abdullah approved as written ("Approve the F3 brief as written"). Build authorized; driver launch follows the F1/F2 pattern.
Written 2026-11-16 by Mariam, following the F1/F2 brief pattern, from the
charter the foundations already settle. Reviewed and corrected
2026-11-16 ~14:15 EET against the F2 verification reports and the live
repo on box #4: the 6-corpus regression counts are now the F2-verified
values (the draft had carried F1 counts), the gate-6 inventory is the
verified one, and the CLI/type claims were checked against the tree
(`--export-svg`, `--generated-at`, `.cqrslens.json` handling,
`RequestSequenceBuilder`, `FrozenQuestionSet.cs`,
`ArchitectureBoundaryTests`, `CurrentSchemaVersion = 1` — all present;
`CqrsLens_Modeling_Foundations.md` is NOT in the repo root and is
placed there at launch). Anything this brief proposes that
the foundations do not decide is marked **DECISION** below — approval
settles it; the driver does not decide it alone.

Driver: Antigravity CLI on box #4. Model: `gemini-pro-orchestrator-g1-pro`
(Argon as planner, Hydrogen as implementer) — Abdullah's instruction
2026-11-16 ~06:02 EET, used for the F2 perf-fix pass. Antigravity
1.5.9, verified latest 2026-11-16; update again before launching and
record the version in CP1.
Fresh session for this brief. Repo: /data/workspace/cqrslens, NEW branch
`foundations/f3-docs`, based on `foundations/f2-controllers` @ `db3c5f6`
("F2 controllers/services/repositories + perf fix", one unit, 49 files,
verified on origin; suite 761/761). Verify HEAD is `db3c5f6` and the
working tree is clean apart from untracked doc files before creating
the branch.
Corpus: nopCommerce pinned `release-4.90.0` @ `e7a52b91`, read-only at
`/data/workspace/corpora/nopcommerce` (see Corpus section).

Read `F0_METAMODEL_v2.1.md` in the repo root AND
`CqrsLens_Modeling_Foundations.md` (R0–R4, placed in the repo root at
launch — if it is absent, STOP and report it rather than proceeding
from memory) in full before writing any code. F0 v2.1 is the approved
architectural baseline; the foundations' R3 is the F3 charter and R4 is
the intent-layer charter. This brief is the work order for stage F3 of
the foundations build order. If this brief and F0 or the foundations
disagree, STOP and record the disagreement in the checkpoint — do not
resolve it yourself.

## Mission

Turn the settled F1+F2 model into documentation that can prove it is
current. Two products, one stage:

1. **Docs export.** An arc42-shaped documentation tree generated from
   the model into a folder the team chooses and may commit — system
   context, building blocks, runtime views, data model, cross-cutting
   configuration (foundations R3) — plus the report's existing diagrams
   as standalone `.svg` files, so nobody has to scrape the HTML report
   to use a diagram (Reporting Upgrade Notes item 5, Abdullah's
   2026-10-31 request).
2. **Freshness gate.** A strict-mode check that regenerates the docs
   to a temp location and diffs against the committed tree — stale docs
   exit non-zero in CI, reusing the baseline/strict machinery the tool
   already has (foundations R3). Documentation that can *prove* it is
   current.
3. **Intent layer (R4).** Decide the file format and check set F0 §10
   deferred to F3: humans declare module groupings, boundaries,
   intended public surface, and ownership in a sidecar the tool never
   writes; the tool checks code against declared intent and reports
   violations as findings; intent that cannot be checked is rendered in
   the docs explicitly labelled *declared, unverified*.

This stage ADDS NO detection. No new relation kinds, no new component
kinds, no new evidence rules. F3 is a rendering + checking stage over
the settled model: the model JSON is unchanged, every output is a view
over it. Nothing here re-litigates F1 or F2: the spine, identities,
dedup keys, evidence record, and the F2 relations are taken as built.

## Corpus (frozen; read-only)

| Item | Value |
|---|---|
| Repository | `https://github.com/nopSolutions/nopCommerce.git` |
| Location on box #4 | `/data/workspace/corpora/nopcommerce` |
| Tag / commit | `release-4.90.0` / `e7a52b9199f9c7651839e92826df47f668f3c767` |
| Size | 35 `*.csproj`, 133 `*Controller.cs`, 210 `*Service.cs` (99 interfaces), 3 repository files, 3,464 `.cs` files; net9.0, SDK 9.0.100; linq2db 5.4.1, no EF Core |

F2 baseline (frozen regression line; verified on box #3, perf-fix
verification 2026-11-16 ~07:50 EET):

| Metric | Value |
|---|---|
| Relations (nopCommerce, text/json) | **8,764** (was 3,234 at F1) |
| Endpoints | **1,151** (all 100 slice actions covered, no verb ever guessed) |
| Suite | **761/761** |
| Byte-identity | **70/70** outputs identical across the verification |
| nopCommerce text/json vs F1 | **−1.11%** median-of-sessions (F1 line: 3,688 ms) |
| DiffPair/base text/json vs F1 | **−13.44%** median-of-sessions (was +9.92%, the F2 marginal cell) |
| Closest remaining cell | GoldenCorpus text/md +7.98% — passes, noted, not a gate item |

F1 baseline for reference: 3,688 ms median text/json, 3,234 relations,
output sha256 `b2825eb8…` (see BRIEF_F2, Corpus section). F2's
relations counts are the regression line now: any delta from 8,764 on
nopCommerce, or from the F2-verified counts on the six frozen corpora
(ABP 3,750, modular 790, northwind 191, eshop 121, ardalis 132,
jasontaylor 131 — f2perf-verification Check 4; GoldenCorpus fixture
27), is a defect unless enumerated with its cause. F3 must not move a
single relation. (The F1 counts — ABP 3,624, modular 710, northwind
172, eshop 114, ardalis 53, jasontaylor 131, GoldenCorpus 25 — are
history, not the regression line; do not use them as targets.)

Corpus handling rules (part of H10): verify `git status` clean in the
corpus before the first scan and after the last run. Never modify the
corpus, never check out another tag, never build it. Text mode scans
source only; the corpus targets net9.0 while the tool runs on .NET 8
and no corpus compilation is involved.

## Decisions for Abdullah at approval

These are the points the foundations leave open. Each carries a
recommended default; approval of this brief settles them as written,
amendment replaces the default. The driver implements the approved
text, not its own preference.

- **D1 — Docs tree layout (recommended: R3's arc42 shape).**
  Export writes, under the chosen directory:
  `01-context.md` (system context: projects, external references,
  entry points), `02-building-blocks.md` (components by role and
  layer, with evidence counts), `03-runtime.md` (per-flow sequences —
  the per-request sequences generalized to per-flow, reusing
  `RequestSequenceBuilder`; no new detection), `04-data-model.md`
  (DbContexts, entities, data edges — Linq2DB repository variant on
  nopCommerce, labelled as such), `05-cross-cutting.md`
  (configuration surface as found, behaviours in registered order
  with the "execution order not guaranteed" label carried over),
  `06-findings.md` (findings grouped per module/feature, with the
  evidence behind each), plus `diagrams/` holding one standalone
  `.svg` per diagram the report already renders (project dependency
  graph, layers, CQRS map, ER per DbContext, per-flow sequences).
  Every page states its source ("generated from the model, <tool
  version>, <generated-at>") and every claim a page makes that the
  model cannot prove is labelled *declared, unverified* — never
  silently presented as fact.
- **D2 — Gate semantics (recommended: drift fails, intent violations
  are findings).** The gate fails (non-zero exit) when the
  regenerated tree differs from the committed tree, and when the scan
  itself fails — fail closed, never a silent pass. Intent violations
  (see C-INTENT) surface as findings in the gate output and in
  `06-findings.md`; they fail the gate only under `--strict`, reusing
  the existing strict machinery and the severity declared in the
  intent file (default: warning). The gate never rewrites the
  committed docs: only an explicit export writes files.
- **D3 — Intent file format (recommended: inside `.cqrslens.json`).**
  Intent lives in the existing config file under an `intent` section
  (foundations R4's first option — one file, no new filename to
  discover). The tool reads it, never writes it. If the repo has no
  `.cqrslens.json`, the intent section is absent and the tool behaves
  exactly as before: no intent, no intent findings, docs render
  without an intent section.
- **D4 — Export command surface (recommended: two flags, no new
  subcommand).** `--export-docs <dir>` writes the tree;
  `--check-docs <dir>` regenerates to temp and diffs (exit 0
  identical, non-zero with a per-file drift list naming the files
  and the model sections that changed). Both accept the existing
  `--generated-at` pin for determinism. This is the ONLY new CLI
  surface in F3 (see H5).

## F3 rules (normative)

### R-DOCS — Docs export is a view, not a model change

The export renders ONLY what the model already contains, plus
declared intent (labelled as such). No hand-written narrative is
generated: the tool documents structure and behaviour evidence;
rationale stays human, supplied via R4 (foundations R3). Every page
is deterministic: same model + same `--generated-at` → byte-identical
files, in both scanner modes. Ordering is total (sorted by
ComponentId / SymbolId / file path) — no dictionary-order or
timestamp leakage into any file. The existing single-file reports
(`ARCHITECTURE.md` / `.html` / `.json`, console summary, SARIF,
`--slice` traces) are byte-identical except for a recorded pointer
to the docs tree, if any; the JSON model sections are byte-identical,
full stop — F3 adds no model content (see H3).

### R-DIAGRAMS — Standalone diagram files

Each diagram the report already renders is emittable as its own
`.svg` under `diagrams/`, byte-identical to the SVG embedded in the
HTML report for the same scan. Reuse the existing `--export-svg`
machinery (`SvgExporter`): extend it to every diagram the report
renders, including the per-flow sequences it does not yet export, and
correct its overstated doc comment so it describes exactly what
exports — the current comment claims more than the flag does.
No new diagram types in F3: the
remaining Reporting Upgrade items are OUT (see H1) — per-feature
CQRS map split (item 2), pipeline diagram (item 3), notifications /
fan-out diagram (item 4), findings visual (item 6). They stay
recorded backlog for a later brief; the driver does not invent them.
The flow diagram (item 1) is covered only insofar as the per-flow
sequences in `03-runtime.md` render it from existing data.

### R-GATE — Freshness gate

`--check-docs <dir>`: scan, regenerate the full tree to a temp
directory, compare file-by-file (bytes) with `<dir>`, exit 0 if
identical, non-zero otherwise. On drift, the output lists every
differing file and, for each, which model section changed (component
added/removed, relation added/removed, finding added/removed, intent
change) — a bare "docs are stale" with no detail is a defect. On
scan failure the gate exits non-zero and says the scan failed; it
never reports "fresh". The gate reads the committed tree read-only
and writes only to temp, except that `--export-docs` is the explicit
write path. Gate behaviour is identical in text and Roslyn modes
(same model ⇒ same verdict); a mode divergence in the verdict is a
CP3 gate failure.

### R-INTENT — Intent layer: format and check set (F0 §10, R4)

Format (per D3): `.cqrslens.json` → `intent` object with four
optional sections. The tool parses it strictly: an intent file that
fails to parse is a scan error (fail closed), never silently ignored.

- `modules`: named groups, each a set of projects and/or components
  selected by explicit project name or component SymbolId prefix —
  NO glob or pattern matching beyond a stated prefix rule; a module
  naming a project/component that does not exist in the model is a
  finding (stale intent), not a silent skip.
- `boundaries`: ordered pairs of modules with a forbidden direction
  (A must not call B). Checked per C-BOUNDARY below.
- `publicSurface`: the declared public surface — endpoints and/or
  components declared public. Checked per C-SURFACE below.
- `ownership`: owner string per module. NEVER checked (no static
  evidence for ownership exists); rendered in `02-building-blocks.md`
  labelled *declared, unverified*.

Check set (the whole set — no other intent checks in F3):

- **C-BOUNDARY — call crossing a declared boundary.** A `Calls`
  edge whose source component is in module A and target in module B
  where the intent forbids A→B is a finding, with file:line evidence
  from the edge's evidence occurrences. Severity: as declared in the
  intent file for that boundary, default warning.
- **C-SURFACE — public-surface mismatch.** An endpoint/component in
  the model that is not in the declared public surface, OR a declared
  public endpoint/component absent from the model, is a finding with
  the identity (ComponentId / endpoint) named. Severity default
  warning. An absent declared item is a finding about stale intent,
  not about the code.

Uncheckable intent (ownership, and any module grouping with no
checkable consequence) is rendered in the docs labelled *declared,
unverified* — the same honesty distinction as verify_claim's
supported / not supported / not determinable. The tool NEVER writes
the intent file and NEVER edits a module, boundary, or owner. A
developer steers with intent; the tool answers with evidence.

### R-FIXTURES — Fixtures prove the gate and the checks

Fixtures (in-repo, additive tests only) must prove: export
determinism (two exports byte-identical); drift detection (mutate
one generated file → gate fails naming that file; mutate the code so
the model changes → gate fails naming the changed section; clean tree
→ gate passes); fail-closed (scan error → gate non-zero, never a
pass); C-BOUNDARY true positive (a fixture call crossing a declared
boundary is found with file:line) and true negative (a call inside a
module, and a call in an allowed direction, produce no finding);
C-SURFACE both directions (undeclared endpoint found; declared-but-
absent endpoint found); stale intent (module naming a missing
component → finding); unparsable intent → scan error; ownership
rendered *declared, unverified* and never a finding; no-intent
behaviour identical to pre-F3 (no intent section in docs, no intent
findings).

## Hard constraints (violating any one fails the stage)

- **H1 — No detection beyond this brief.** No new relation kinds, no
  new component kinds, no new evidence rules, no Tier 2 (messaging,
  external integrations, configuration surface). The remaining
  Reporting Upgrade items (per-feature CQRS map split, pipeline
  diagram, notifications diagram, findings visual) are OUT of F3 —
  recorded backlog, not driver scope. Do not implement them; the
  deferral is recorded here, not a driver decision.
- **H2 — Existing tests pass unmodified.** All 761 existing tests pass
  (box #3 terms). On box #4 expect 693 pass + 68 skipped (external-
  corpus tests; box #4 does not hold the corpora) — record the skip
  count in every checkpoint. Do not weaken, skip, delete, or "fix" an
  existing test. New tests are additive and must pass. If a test
  expectation contradicts this brief, STOP and record it.
- **H3 — Model unchanged, outputs additive-only.** The JSON model is
  byte-identical to the F2 binary's output on every fixture and corpus
  (same scan, same `--generated-at`). Existing md/html/agent/summary/
  sarif/console outputs are byte-identical except a recorded pointer
  to the docs tree, if any. New content appears only in the docs
  tree (separate files, separate directory) and in the gate's stdout
  report. `schemaVersion` stays 1. MCP answers identical. `--diff` /
  `--strict` behavior unchanged except that `--strict` now also fails
  the gate on error-severity intent findings (per D2).
- **H4 — Frozen question set.** Unmoved. `FrozenQuestionSet.cs`
  untouched. Any movement must be enumerated in the checkpoint with
  the reason for each.
- **H5 — No release changes, CLI surface only as chartered.** No
  version bump, no CHANGELOG/ROADMAP edits, no README edits, no new
  PackageReferences, no `.csproj` diff. New CLI surface is ONLY the
  two flags in D4 (`--export-docs`, `--check-docs`); no other new
  flags, no new subcommand, no renamed flags.
- **H6 — Architecture rules hold.** The 4-project one-way dependency
  chain and one-type-per-file rule stay intact
  (`ArchitectureBoundaryTests` must pass).
- **H7 — Rendering gaps preserved, not fixed.** Do NOT change
  sequence/flow rendering or the Wraps gap F1/F2 left documented.
  The gaps stay documented exactly as F2 left them. No silent
  correction. The docs export renders the existing sequences; it
  does not repair them.
- **H8 — Determinism.** Repeated scans AND repeated exports of
  unchanged source with a fixed `--generated-at` produce identical
  output bytes — fixtures and nopCommerce, all formats, both modes,
  every file in the docs tree.
- **H9 — Driver never commits, never pushes.** Leave all work
  uncommitted in the working tree. Mariam verifies each checkpoint
  independently (including a 6-corpus A/B on box #3) and commits only
  after verification passes.
- **H10 — Corpus read-only.** See Corpus section. Pinned tag/commit
  only; no re-pinning.
- **H11 — No corpus or class-name special-casing.** Every rule must be
  general: no `if nopCommerce`, no hardcoded type names in production
  code. Tests may name audit types; production code must not. A docs
  page that only renders correctly on nopCommerce is not a page.

## Environment

- .NET SDK 8.0.427 at `~/.dotnet`. Every dotnet command runs with
  `DOTNET_ROOT=$HOME/.dotnet` and `PATH=$HOME/.dotnet:$PATH` exported.
- Test command: `dotnet test CqrsLens.sln -c Release --nologo`.
- Baseline binary for before/after comparison: create a clean worktree
  with `git worktree add /tmp/cqrslens-f2base db3c5f6`, build it once,
  use it read-only for baseline outputs, delete the worktree when done.
  Never modify it.
- Corpus: `/data/workspace/corpora/nopcommerce`, pinned as above.
  Verify the pin (`git rev-parse HEAD` → `e7a52b91…`, `git describe
  --tags` → `release-4.90.0`, `git status` clean) before the first scan
  and after the last run.
- Box #4 does NOT hold the six frozen corpora: the driver's A/B scope
  is the in-repo fixtures + nopCommerce. The 6-corpus A/B and the full
  perf A/B are Mariam's verification on box #3, not the driver's — but
  a smoke regression on fixtures or nopCommerce is a stop-and-report.
- Both scanner modes (text + Roslyn) must produce the same docs tree
  and the same gate verdict on fixtures. Any mode divergence on
  nopCommerce must be enumerated with its cause; divergence
  attributable to F3 is a CP3 gate failure.

## CP1 — Reproduce the F2 baseline + docs skeleton

1. **Reproduce the F2 baseline.** Run the F2 baseline binary
   (worktree) on the pinned corpus, text mode, JSON, pinned
   `--generated-at 2026-01-01T00:00:00Z`, and reproduce the F2
   numbers exactly: 8,764 relations, 1,151 endpoints, suite 693 pass
   + 68 skipped (box #4 terms — the 68 are the external-corpus tests
   box #4 cannot run; 761/761 is the box #3 full-corpus count).
   If it does not reproduce, STOP: the corpus or toolchain differs
   from the F2 verification, and nothing else in this brief can be
   trusted until that is resolved.
2. **Intent format spec (paper first).** Write the exact
   `.cqrslens.json` `intent` schema (field names, nesting, ordering,
   severity values, defaults) as a doc in the repo root
   (`F3_INTENT_FORMAT.md`), following D3 and R-INTENT. No code for
   intent checks starts until this file exists — foundations before
   code.
3. **Docs skeleton.** Implement `--export-docs` for the tree layout
   in D1 on fixtures + nopCommerce: all six pages + `diagrams/` with
   the standalone SVGs, deterministic bytes (two exports identical),
   both modes. Content may be partial at CP1 (pages present, sections
   filled from the model); record exactly which sections are complete
   and which are stubs — a stub presented as complete is a defect.
4. **Fixture tests** proving export determinism and the R-DOCS /
   R-DIAGRAMS rules on fixtures (sorted ordering, no timestamp
   leakage, SVGs byte-identical to the HTML-embedded ones, no
   narrative text generated).

Suite green (761 existing + new). Write `CHECKPOINT_F3_CP1.md` in the
repo root: baseline-reproduction proof, the intent-format spec file,
tree layout + completeness table, files changed (`git status --short`
+ `git diff --stat`), test result, decisions, open issues.

## CP2 — Gate + intent checks

Implement `--check-docs` (R-GATE) and the R-INTENT check set
(C-BOUNDARY, C-SURFACE), with findings flowing into `06-findings.md`
and the gate output. Document the exact gate exit-code table and the
finding shapes (field names, severity handling, `--strict`
interaction) in the checkpoint.

- **Fixture tests for every rule in R-FIXTURES**, including all
  drift / fail-closed / boundary / surface / stale-intent /
  unparsable-intent / ownership-labelled cases. Each test names the
  rule it proves.
- **nopCommerce demonstration.** Export the full tree for the pinned
  corpus; run the gate clean (pass) and after a one-file mutation
  (fail, naming the file); declare a real boundary in a scratch
  intent file (e.g. Presentation must not call a data-layer module
  directly — state the actual modules used) and show the gate
  reporting the true violations with file:line, plus a clean intent
  file passing. The scratch intent files live in `/tmp` (ephemeral);
  the checkpoint is the durable record. Do NOT add suite tests
  depending on the box #4-only corpus path.
- **Per-corpus export smoke.** Export trees for the in-repo fixtures;
  record file counts, total bytes, and export wall time per corpus.
  Any existing-report byte change must be enumerated as an intended
  addition with its reason — or it is a defect.

Suite green. Write `CHECKPOINT_F3_CP2.md`: the gate exit-code table
and finding shapes, the nopCommerce demonstration commands +
outputs, the export smoke table, files changed, test result,
decisions, open issues.

## CP3 — Gates

Prove each gate with commands and outputs recorded in the checkpoint.
Gates 1–6 run on box #4; gate 7's six-corpus leg and gate 8's full
A/B are Mariam's verification — the driver evidences the fixture +
nopCommerce legs and records everything needed to re-run them.

1. **Legacy parity.** Full suite green; frozen question set exact
   (H4); JSON model byte-identical to the F2 baseline binary on
   fixtures + nopCommerce (H3) — not "equivalent", identical.
2. **Determinism.** Two scans + two exports with fixed
   `--generated-at` produce identical bytes on all fixtures and
   nopCommerce, all formats, both modes — including every file in
   the docs tree and every standalone SVG.
3. **Gate correctness.** On fixtures: clean tree → exit 0; one-file
   mutation → non-zero naming that file and its changed section;
   model-changing code edit → non-zero naming the changed section;
   scan error → non-zero, never a pass; gate reads the committed
   tree read-only (prove: committed files' mtimes/hashes unchanged
   after a `--check-docs` run).
4. **Intent correctness.** All R-FIXTURES intent cases pass:
   C-BOUNDARY true positive with file:line and true negatives;
   C-SURFACE both directions; stale intent found; unparsable intent
   is a scan error; ownership rendered *declared, unverified*, never
   a finding; no-intent scan identical to pre-F3 behaviour.
5. **Honesty.** No page claims as fact what the model cannot prove:
   every intent-derived statement carries the *declared,
   unverified* label where it applies; no generated narrative text
   (rationale) anywhere in the tree — spot-check each page and record
   the check; diagrams in `diagrams/` are byte-identical to the
   HTML-embedded SVGs for the same scan.
6. **Ground-truth reconciliation.** Docs tree for nopCommerce
   reconciles against the F2-verified model: 516 components (282
   service, 1 repository, 128 controller, 105 validator), 8,764
   relations, 1,151 endpoints, requests/handlers/notifications/
   behaviors/DbContexts 0/0/0/0/0 — every number on every page
   traceable to the model JSON, with the reconciliation commands
   recorded verbatim.
7. **Regression.** Fixtures + nopCommerce: every JSON section
   byte-identical to the F2 baseline binary; md/html/agent/summary/
   sarif identical except the recorded docs-tree pointer, if any.
   MCP stdio probe: identical. `--diff` / `--strict` outputs
   identical in behavior except the D2 strict/intent interaction.
   H7 preserved (sequence/flow rendering untouched). Zero new
   warnings (normalized, deduped warning-set comparison vs the F2
   tree; `CqrsLens.Core` stays 0 warnings).
8. **Performance.** Wall-clock `dotnet <dll> <corpus> --format <fmt>`
   including process startup, 1 warmup + 3 timed runs, median, same
   machine/session, pinned `--generated-at`. Bound: **no measured
   scan cell above +10%** vs the F2 baseline binary (text/json,
   text/md, roslyn/json — fixtures + nopCommerce). Export wall time
   is measured separately per corpus and reported (no invented bound
   — it is a separate command, judged on the numbers). A
   single-session breach triggers a second session (5-run median);
   judgment is on median-of-sessions, and small-corpus session swing
   (±5pp, the F1-perf lesson) is noise unless it reproduces across
   sessions AND a same-session comparison confirms a regression
   attributable to F3. In-process profiling is never a substitute
   for wall-clock medians.
9. **Scope clean.** `git diff -- '*.csproj' CHANGELOG.md README.md
   ROADMAP.md Directory.Build.props
   tests/CqrsLens.Tests/FrozenQuestionSet.cs` is empty; no added
   `<PackageReference>` lines; new CLI surface is exactly the two
   D4 flags (state how this was checked); no corpus/class-name
   special-casing (state how this was checked); corpus `git status`
   still clean; no version bump.

Write `CHECKPOINT_F3_CP3.md`: gate-by-gate evidence with the exact
commands run and their outputs, full test result, timing tables,
the complete `git status` / diff stat of the working tree, open
issues. Then STOP for verification.

## Checkpoint format

Every checkpoint file goes in the repo root, named exactly
`CHECKPOINT_F3_CP<n>.md`, and contains: status (done / blocked), files
changed (paste `git status --short` + `git diff --stat`), tests run with
results, gate evidence where applicable, decisions made, open issues,
and the exact commands to reproduce every claim. Do not delete or
overwrite a previous checkpoint file. Write the checkpoint, then
continue to the next CP in the same session, except after CP3, where
you STOP. If the session is interrupted, resume from the last written
checkpoint — re-read it first.

## On blockers

If any hard constraint cannot be met, if a test expectation contradicts
this brief, or if F0, the foundations, and this brief disagree: STOP,
write the checkpoint with the blocker described precisely (what, where,
what you tried), and do not work around it. Do not weaken a gate. Do
not invent an interpretation of F0 or the foundations. The ONLY
severable item is a docs page whose content cannot be rendered from
the model without new detection — it is rendered as an explicit empty
state ("not available from the model"), recorded in the checkpoint,
never faked; everything else blocks.

## What success looks like

Three checkpoint files, a green suite (761 existing + additive new
tests), an arc42 docs tree generated from the settled model with
standalone SVG diagrams, a freshness gate that passes clean trees,
names drift precisely, and fails closed on scan errors, an intent
layer with its format and check set settled and proven on fixtures
plus a real nopCommerce demonstration, the JSON model byte-identical
to F2 everywhere, and an uncommitted working tree on
`foundations/f3-docs` that Mariam can verify — after which F4 (public
schema + the Noctua release) can be briefed on a model whose docs
already prove themselves current.
