# BRIEF — F1 perf fix (APPROVED by Abdullah 2026-11-13 11:20 EET: "Run a perf-fix pass first")

Driver: Antigravity CLI on box #4. Model: gemini-4-hydrogen-xhigh.
Repo: /data/workspace/cqrslens, branch `foundations/f1-spine`, based on
origin/main `bd49fc3` (CqrsLens 1.1.0, suite 719/719 on box #3 with the
68 external-corpus tests running). F1 is uncommitted on this branch
(59 modified tracked + 46 untracked source/test files; snapshot sha256
`ff1882d549c3d51c44ab7276bf6db030e00a33f6fe880981bb3a31d2abf44717`).
Driver never commits, never pushes. One checkpoint; STOP for verification
when done.

## Why this brief exists

Full F1 verification returned CONDITIONAL PASS
(`f1-cp3-verification.md`, verified 2026-11-13 ~07:26–08:05 UTC on
box #3 + Mariam's VM). Everything functional passes — suite 719/719,
Core 0 warnings, 6/6 corpora byte-identical in both modes except the
additive JSON `relations` section, all 7 F0 gates evidenced. The single
condition is a real performance regression on the largest corpus:

- **ABP text mode: +16.4% / +23.6% / +24.6%** across three independent
  warmed sessions (base ~5.1–5.6 s vs F1 ~6.4–7.0 s, json format).
  Decomposition (third session): the spine itself costs ~**+14% even in
  md output** (base 5556 ms vs F1 6331 ms) — per-scan spine overhead on
  large corpora — and serializing the **3,624-entry relations section**
  adds ~10 more points in json (F1 json 6997 ms vs F1 md 6331 ms;
  F1 `abp.json` is 5.9 MB vs base 1.2 MB).
- All other real corpora within noise once confirmed (eshop +0.4% on
  re-check; northwind/modular single sessions +7–9%). Roslyn mode
  unaffected everywhere (analysis dominates; ABP Roslyn exit-5 defect
  unchanged on both sides).
- Fixture smoke (`--profile` medians, the brief's Gate 7 metric): PASS
  on the driver's table; independent wall clock 13/14 within +10%. The
  one marginal reading, SampleShop/text +10–14% (~40 ms on a ~320 ms
  scan; driver's `--profile` says +2.6%), is **not** a target of this
  brief — do not chase it at the expense of the ABP targets.

Phase 7 precedent: a perf breach blocked 1.1.0 until fixed. Abdullah
chose the same here over accepting the overhead.

## Objective

Bring the real-corpus A/B within the standing yardstick — no corpus
above **+10%** (hard bound; aim ABP text ≤ +5%) — by optimizing
(a) spine per-scan overhead on large corpora and (b) relations JSON
serialization, **with byte-identical output**. This is a pure
optimization brief: the F0 model shape, extraction definitions,
dedupe/ordering, and every emitted byte are frozen. If a candidate
optimization would change any output, it is out of scope: stop and
report instead of taking it.

## Starting suspects (verify by profiling/reading; do not trust this list)

- `ArchitectureSpine` sorted-view construction and projection caches
  (`SpineProjectedCollection<T>`, `SpineQueries` cached arrays):
  arrays rebuilt/sorted per access instead of per version stamp on
  large relation counts (ABP: 3,624 relations).
- `SpineLinkList` / per-component link projections: repeated full
  relation scans per component instead of one indexed pass.
- Relations JSON writer (`JsonSpineRelation`, `JsonSpineEvidence`,
  `JsonSpineNodeRef`): per-entry string building (node-ref keys,
  evidence locations) — look for repeated formatting of the same
  ref/evidence across occurrences, and for `JsonSerializer` options
  that re-emit predictable structure.
- Identity construction on the scan path (`SymbolId`, `RelationKey`,
  `EvidenceOccurrence` hashing): per-occurrence allocation that
  scales with relation count even when output is md (no relations
  section emitted).

## Hard constraints

- Every emitted file **byte-identical** to the current F1 tree (the
  verified snapshot) on all 6 corpora, text and Roslyn modes, all
  formats (json/md/html/agent/summary/sarif), pinned `--generated-at`.
  The JSON `relations` section must be byte-identical too — this is an
  optimization, not a format change.
- `--diff`, `--strict`, determinism, and MCP stdio outputs unchanged.
- Full suite green (719 + any tests you add). Frozen question set
  untouched. H7 rendering gap stays exactly as-is (documented, not
  fixed).
- No new packages, no public API changes, no config changes, no
  version/docs changes. No corpus / class name special-casing.
- SampleShop/text marginal reading is out of scope unless it worsens.

## Checkpoint deliverable (single CP, then STOP)

CHECKPOINT_F1_PERF.md with:
1. The hotspot(s) found, with evidence (profile numbers or allocation
   counts before/after, and the code location) — separately for the
   spine-overhead part and the relations-serialization part.
2. What changed and why it cannot change output (the invariant each
   change preserves).
3. Warmed A/B medians (1 warmup + ≥3 timed runs, same machine/session)
   for ABP text md and ABP text json before vs after, plus a
   confirmation run on modular and northwind text.
4. The byte-identity proof: hashes of the outputs per corpus/mode,
   before vs after (including the relations section).
5. Suite result.

Mariam re-runs the full verification afterwards (suite, 6-corpus A/B,
gates, perf A/B on box #3) — F1 commits as ONE unit only when the perf
condition clears. If ABP cannot get under +10% without changing
output, say so in the checkpoint with the best numbers achieved;
accepting the overhead then becomes Abdullah's call again, not the
driver's.
