# BRIEF — F2 perf fix (APPROVED by Abdullah 2026-11-16 ~05:59 EET: "Perf-fix pass first"; model per his 06:02 EET instruction: orchestrator Argon — Argon planner, Hydrogen implementer)

Driver: Antigravity CLI on box #4. Model: gemini-pro-orchestrator-g1-pro (Orchestrator (Argon)).
Repo: /data/workspace/cqrslens, branch `foundations/f2-controllers`,
HEAD `3fcbcba` (F1 spine commit). F2 is uncommitted on this branch
(24 modified tracked files +1388/−202, plus new files in
Resolution/, Roles/, Reporting/Json/ and 2 new test files; final-tree
snapshot sha256
`6c3b7d9a76f8830117987386fcb08d2290a7900327a9eaab159fce3114801bf3`).
Driver never commits, never pushes. One checkpoint; STOP for
verification when done.

Read `f2-final-verification.md` (in the repo root, copied with this
brief) in full before starting — its Gate 8 section is the evidence
this brief acts on. `CHECKPOINT_F2_CP3.md` in the repo root holds the
driver's own gate-8 numbers.

## Why this brief exists

F2 final verification returned CONDITIONAL PASS (independent
verification on box #3, `f2-final-verification.md`). Everything
functional passes — suite 761/761 with the 68 corpus tests running,
nopCommerce relations 3,234 → 8,764 exactly as claimed, ground-truth
reconciliation 100/100 endpoints, determinism 12/12, zero new
warnings. The single perf condition is one marginal cell:

- **DiffPair/base text/json: +9.92% median-of-4-sessions** — 0.08pp
  under the +10% bound; median of the 3 formal protocol sessions
  alone is **+10.60% (over)**. It breached in 2 of 4 sessions
  (+10.60%, +11.92%) and not in the other 2 (+5.04%, +9.24%).
- Cause: a consistent **~+21–33 ms fixed cold-start JIT cost** on
  ~250–350 ms 1-file processes (driver's `--profile`: ~14.5 ms
  internal stage delta), NOT a scaling regression. Every fixture
  carries the same fixed cost: StrictFailure +8.10%,
  GoldenCorpus +7.57%, Adversarial +6.71%, DiffPair/head +6.57%
  (median-of-sessions) — all under the bound, all carrying it.
- nopCommerce is fine and must stay fine: median-of-sessions
  −0.11% (text/json) / −6.23% (text/md) / +1.53% (roslyn/json);
  every measured F2 absolute time under the 3,688 ms ceiling.

F1 precedent: a perf condition blocked the F1 commit until a
perf-fix pass cleared it. Abdullah chose the same here over
accepting the marginal cell.

## Objective

Bring DiffPair/base text/json **robustly** under the +10% bound on
median-of-sessions, with margin (aim ≤ +5% on the marginal cell),
by reducing the fixed cold-start / JIT cost of the F2 resolver
code, **with byte-identical output**. Hard bound, per the F2
brief's Gate 8: no measured cell above +10% vs the F1 baseline
binary (fixtures + nopCommerce; text/json, text/md, roslyn/json).
This is a pure optimization brief: the F0 model shape, extraction
definitions, dedupe/ordering, and every emitted byte are frozen.
If a candidate optimization would change any output, it is out of
scope: stop and report instead of taking it.

## Starting suspects (verify by profiling/reading; do not trust this list)

The cost is FIXED (~25 ms whether the corpus is 1 file or 3,464),
so suspect startup and unconditional work first, not per-occurrence
scaling:

- Eager construction of resolution indexes on every scan even when
  the corpus has nothing to resolve: `ServiceResolutionIndexBuilder`
  / `ServiceResolutionLookup` built unconditionally in the analyzer
  path; `LayerClassifier` / role inventory running even for 1-file
  fixtures with no controllers or services.
- New per-scan passes that run regardless of content:
  `MemberInvocationScanner`, `ComponentCallAndDataResolver`,
  `ConventionalControllerEndpointDiscovery` — check for
  unconditional scans and allocations before any early-out on
  empty inputs.
- Static constructors / type loading: new types in Resolution/,
  Roles/, Reporting/Json/ pulled in on startup (JIT cost) even when
  their features never fire — look for eager static initialization,
  large static tables, compiled regexes.
- Spine projection work running when the format never emits it:
  check whether relations/evidence projection runs for md output
  (no relations section emitted) or before the format is known.
- Identity/evidence allocation on the scan path (`SymbolId`,
  `RelationKey`, `EvidenceOccurrence` hashing) — only if profiling
  shows it contributing to the FIXED cost; do not chase per-occurrence
  scaling, which the nopCommerce numbers rule out.

## Hard constraints

- Every emitted file **byte-identical** to the current F2 tree (the
  verified snapshot) on nopCommerce + all fixtures, text and Roslyn
  modes, all formats (json/md/html/agent/summary/sarif), pinned
  `--generated-at`. The JSON `relations` section must be
  byte-identical too — this is an optimization, not a format change.
  nopCommerce relations stay 8,764. (The 6 frozen corpora do not
  exist on box #4 — that is why the 68 corpus tests skip here;
  Mariam re-verifies the 6-corpus legs on box #3. Your byte-identity
  proof covers fixtures + nopCommerce on this box.)
- `--diff`, `--strict`, determinism, and MCP stdio outputs unchanged.
- Full suite green: 693 passed + 68 skipped = 761 total on box #4
  (the 68 skip for missing corpus paths — they must stay skipped,
  never fail). Frozen question set untouched. H7 rendering gap stays
  exactly as-is (documented, not fixed). schemaVersion stays 1.
- No new packages, no public API changes, no config changes, no
  version/docs changes. No corpus / class name special-casing.
- Conditions 2–3 of the verification (record the 6-corpus
  non-relations deltas; qualify the CP2 interpolated-string claim)
  are Mariam's commit-record items — NOT driver work. Do not edit
  docs or checkpoints to address them.
- nopCommerce must not regress: text/json, text/md, roslyn/json stay
  within +10% vs the F1 baseline binary, aim text ≤ +5% and under
  the 3,688 ms ceiling.

## Measurement protocol (the F2 brief's Gate 8, unchanged)

Wall-clock `dotnet <dll> <corpus> --format <fmt>` including process
startup, 1 warmup + 3 timed runs, median, same machine/session,
pinned `--generated-at 2026-01-01T00:00:00Z`. Baseline binary: build
from a clean worktree at `3fcbcba` — never build the baseline from
the F2 working tree. A single-session breach triggers a second
session (5-run median); judgment is on median-of-sessions, and
small-corpus session swing (±5pp) is noise unless it reproduces
across sessions AND a same-session comparison confirms a regression
attributable to F2. In-process profiling is never a substitute for
wall-clock medians. Record output sizes + relation counts per run.

## Checkpoint deliverable (single CP, then STOP)

CHECKPOINT_F2_PERF.md with:
1. The hotspot(s) found, with evidence (profile numbers or allocation
   counts before/after, and the code location) — separately for the
   cold-start/JIT part and any per-scan part.
2. What changed and why it cannot change output (the invariant each
   change preserves).
3. Warmed A/B medians (protocol above) for DiffPair/base,
   StrictFailure, GoldenCorpus, Adversarial, and DiffPair/head
   text/json before vs after — plus a second 5-run session for any
   cell that breaches +10% in the first session — and confirmation
   runs for nopCommerce text/json, text/md, roslyn/json.
4. The byte-identity proof: hashes of the outputs per corpus/mode,
   before vs after (fixtures + nopCommerce, all formats, including
   the relations section).
5. Suite result, and the full `git status --short` + `git diff --stat`
   of the working tree.

Mariam re-runs the full verification afterwards (suite, 6-corpus A/B,
gates, perf A/B on box #3) — F2 commits as ONE unit only when the
perf condition clears. If DiffPair/base cannot get robustly under
+10% without changing output, say so in the checkpoint with the best
numbers achieved; accepting the overhead then becomes Abdullah's call
again, not the driver's.
