# BRIEF — F1: Spine refactor (CqrsLens foundations)

Driver: Antigravity CLI on box #4. Model: gemini-4-hydrogen-xhigh.
Repo: /data/workspace/cqrslens, branch `foundations/f1-spine`, based on
origin/main `bd49fc3` (CqrsLens 1.1.0, suite 689/689).

Read `F0_METAMODEL_v2.1.md` in the repo root in full before writing any
code. It is the approved architectural baseline (v2.1). This brief is the
work order for stage F1 of its build order. If this brief and the F0 paper
disagree, STOP and record the disagreement in the checkpoint — do not
resolve it yourself.

## Mission

Reorganize `AnalysisResult` around the F0 spine — project → component →
relation — with CQRS as one component family among several. This is a
structural migration, NOT a reimplementation of detection and NOT an
opportunity to add capabilities. Every fact the spine carries must already
be computed today, or be a reorganization of one (Validates, TouchesData —
F0 §4.1).

## Hard constraints (violating any one fails the stage)

- **H1 — No new detection.** No new shape families, no new checks, no new
  evidence rules, no new analyzer capabilities of any kind. The six F0
  acceptance gates (§8) are the contract.
- **H2 — Existing tests pass unmodified.** All 689 existing tests pass.
  Do not weaken, skip, delete, or "fix" an existing test. New tests are
  additive and must pass. If a test expectation contradicts the spine
  reorganization, STOP and record it in the checkpoint.
- **H3 — Output frozen except one intended addition.** Markdown, HTML,
  console, SARIF, summary, and MCP answers are byte-identical on the
  in-repo fixtures. The ONLY permitted output change is the additive JSON
  relations section (F0 §8). Prove byte-identity with before/after diffs
  (CP3) using a fixed `--generated-at` timestamp.
- **H4 — Frozen question set holds exactly.** The `FrozenQuestionSet`
  tests pass unmodified.
- **H5 — No release changes.** No version bump, no CHANGELOG/ROADMAP
  edits, no README edits, no new CLI flags, no new PackageReferences.
- **H6 — Architecture rules hold.** The 4-project one-way dependency
  chain and one-type-per-file rule stay intact
  (`ArchitectureBoundaryTests` must pass).
- **H7 — Wraps rendering gap: document, do not fix.** Do NOT change
  sequence/flow rendering. Record the gap per F0 §4.3 with a code comment
  at the rendering site plus a checkpoint note. No silent correction.
- **H8 — Determinism.** Repeated scans of unchanged source with a fixed
  `--generated-at` produce identical output bytes.
- **H9 — Driver never commits, never pushes.** Leave all work uncommitted
  in the working tree. Mariam verifies each checkpoint independently
  (including a 6-corpus A/B on box #3) and commits only after
  verification passes.

## Environment

- .NET SDK 8.0.427 at `~/.dotnet`. Every dotnet command runs with
  `DOTNET_ROOT=$HOME/.dotnet` and `PATH=$HOME/.dotnet:$PATH` exported.
- Test command: `dotnet test CqrsLens.sln -c Release --nologo`.
- Baseline binary for before/after comparison: create a clean worktree
  with `git worktree add /tmp/cqrslens-baseline bd49fc3`, build it once,
  use it read-only for baseline outputs, delete the worktree when done.
  Never modify it.
- In-repo fixtures for output comparison: the test suite's fixtures
  (GoldenCorpus) plus any sample solution under the repo. Compare ALL
  output formats the CLI can produce for each fixture.

## CP1 — Spine types (additive, no behavior change)

Add the new spine types in `CqrsLens.Core`, following existing conventions
(one type per file, matching folder layout):

- Node kinds (F0 §3): structural nodes, components with evidenced role
  sets (§2.1), typed references (Interface, Method, Member, Type,
  External, Unresolved).
- Identities (F0 §2): ProjectId, SymbolId, ComponentId, EndpointId
  (mapping-level, §2 v2.1). Deterministic construction only.
- Relations (F0 §4): the unified relation type with kind, from/to node
  references, and the deduplication key (kind, from, to, ordered Via
  path, method-if-any — §5.1 v2.1).
- Evidence (F0 §5): the structured evidence record (rule, origin,
  location, ordered Via path, strength) with one-to-many occurrences per
  logical relation; per-fact strength (§5.3).
- The unproven populations (§5.4) as explicit model data, distinct from
  relations.

No consumer changes in CP1. No `AnalysisResult` changes yet: the new types
live alongside the existing model. Add unit tests proving: identity
uniqueness rules, dedup-key behavior (different Via order = different
relation; same key = occurrences of one relation), role-set behavior
(one symbol, many roles, one node), and EndpointId stability for
multi-mapping methods and lambdas (mapping-site based).

Suite green (689 existing + new). Write `CHECKPOINT_F1_CP1.md` in the repo
root: files added, where each F0 concept landed (type names + files),
design decisions taken, test result, open issues.

## CP2 — Migration

- Reorganize `AnalysisResult` internals around the spine. The spine is the
  single source of truth.
- Migrate ALL consumers to read from the spine: Markdown/HTML/JSON
  renderers, checks, MCP tools, diff, baseline, agent reports, console.
- Legacy edge lists (`FlowEdges`, `ResolvedEdges`, `ServiceCallEdges`)
  become projections computed from the spine — kept as properties, never
  stored twice, never diverging.
- Linking lists on shapes (`CqrsRequest.Handlers`,
  `HandlerInfo.Dependencies`, etc.) become derived views over relations,
  computed the same way; keep the properties during F1.
- Promote Validates and TouchesData to relations (F0 §4.1). No new
  detection: both are already computed.
- `ResolvesTo` carries candidate-vs-binding semantics in its evidence
  (F0 §4.4). `Wraps` is modelled under the §4.3 applicability rule; the
  rendering gap is documented per H7, not fixed.
- JSON gains the additive relations section (F0 §8). Document its exact
  shape (field names, nesting, ordering) in the checkpoint. All existing
  JSON sections keep their names and fields byte-for-byte, including the
  `Inferred` field on endpoints.
- Layer assignments gain per-assignment provenance (configured vs
  guessed) as additive data; existing classifications and check results
  do not move.

Suite green. Write `CHECKPOINT_F1_CP2.md`: migration map (every old list
→ its spine home, every consumer → what it reads now), the JSON section
shape, test result, before/after fixture diff summary, open issues.

## CP3 — Gates

Prove each F0 §8 acceptance gate with commands and outputs recorded in
the checkpoint:

1. **Legacy parity.** Full suite green; frozen question set exact.
2. **Identity uniqueness.** Tests + a fixture-scan assertion: no two
   components share a ComponentId, no two endpoints share an EndpointId.
3. **Evidence completeness.** Every relation in the spine on every
   fixture has a valid rule and ≥1 evidence occurrence (assert in tests).
4. **No invented certainty.** Ambiguous resolutions and unresolved
   dispatches never become proven edges (frozen tests + explicit check);
   inferred facts keep their strength label in every view.
5. **Deterministic output.** Two runs with fixed `--generated-at` produce
   identical bytes on all fixtures, all formats.
6. **Bidirectional migration parity.** The legacy edge lists projected
   from the spine equal the pre-migration lists: run the baseline binary
   (worktree) and the migrated binary on every fixture and diff the
   legacy JSON sections — identical except the additive relations
   section.
7. **Timing smoke.** No fixture scan regresses more than 10% vs the
   baseline binary (`--profile`, same machine, 3-run medians). A full
   perf A/B on real corpora is Mariam's verification, not the driver's —
   but a smoke regression here is a stop-and-report.

Write `CHECKPOINT_F1_CP3.md`: gate-by-gate evidence with the exact
commands run and their outputs, full test result, timing table, open
issues, and the complete `git status` / diff stat of the working tree.

## Checkpoint format

Every checkpoint file goes in the repo root, named exactly
`CHECKPOINT_F1_CP<n>.md`, and contains: status (done / blocked), files
changed (paste `git status --short` + `git diff --stat`), tests run with
results, gate evidence where applicable, decisions made, open issues, and
the exact commands to reproduce every claim. Write the checkpoint, then
continue to the next CP in the same session. If the session is
interrupted, resume from the last written checkpoint — re-read it first.

## On blockers

If any hard constraint cannot be met, if a test expectation contradicts
the migration, or if the F0 paper and this brief disagree: STOP, write
the checkpoint with the blocker described precisely (what, where, what
you tried), and do not work around it. Do not weaken a gate. Do not
invent an interpretation of the F0 paper.

## What success looks like

Three checkpoint files, a green suite (689 existing + additive new tests),
byte-identical outputs except the one additive JSON section, all six gates
evidenced, and an uncommitted working tree on `foundations/f1-spine` that
Mariam can verify and commit.
