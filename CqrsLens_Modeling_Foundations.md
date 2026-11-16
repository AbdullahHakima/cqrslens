# Foundations — from CQRS auditor to modeling tool

Written 2026-11-10 (Mariam). Status: RECOMMENDATIONS, made at Abdullah's
explicit delegation ("I don't know the best answers… I need you to do the
recommendations in order to build a solid foundations"). These become the
working defaults for the next charter unless he vetoes or amends them — the
same pattern as the Phase 6/7 decision defaults. Nothing here is built yet.

Sequencing rule already set by Abdullah (2026-11-10): the Noctua rename does
NOT land before the tool actually implements the wide scope. The name ships
with the widening, never ahead of it. This document is the foundation for
that widening.

## The shift, in one paragraph

Today the tool is an auditor: it scans a solution, builds an internal model
(AnalysisResult), renders a document, and discards the model. The proposal:
the model becomes the product — a persistent, evidence-backed representation
of the system (components, relations, data, surface) — and every output
(report, diagrams, docs, MCP answers, diffs) becomes a view over it.
Documentation is then self-maintaining by construction: it is generated from
a model that is built from the code and fails closed, so stale documentation
becomes a checkable state instead of an invisible one.

Grounding (per the standing research standard — textbooks and named
authorities, not blogs): the view structure follows **arc42** (context,
building blocks, runtime, deployment views) and the **C4 model** (Simon
Brown: one model, multiple views at different zoom levels); the
documentation philosophy is **Living Documentation** (Cyrille Martraire:
documentation derived from the code/system itself, kept alive by being
generated and checked, not by discipline); the model-from-code stance
follows Structurizr's premise (model as data, views rendered from it).
Claims below that rest on none of these are labelled *own reasoning*.

## Recommendations

### R0 — The interaction model: tool-maintained, developer-read, intent annotated
The model is **generated and maintained by the tool; developers read and
query it — they never edit it.** A hand-editable model can claim what the
code does not do, which is exactly the failure this tool exists to catch;
every guarantee the tool has (fail-closed evidence, verify_claim's three
verdicts, read-only for agents — decision #6, standing) rests on the model
being true by construction. Human input enters through R4's separate intent
layer, which the tool *checks*, never absorbs silently. (Own reasoning,
built on the Phase 6 guarantee model Abdullah approved 2026-11-01.)

### R1 — First-class citizens: widen in two bounded tiers, spine first
The widening is a spine change before it is a feature list:
**requests/handlers stop being the model's spine and become one component
family among several.** The spine becomes: project → component → relation.

- **Tier 1 (the foundation phase).** Components: any class with an
  *evidence-derived role* — controller, service, repository, handler,
  validator, background worker, DbContext, entity — plus projects/assemblies
  and declared layers. Relations, each admitted only with static evidence
  (a call site, a registration, a reference): project references; DI
  resolution (built, Phase 7); direct calls (built, Phase 7); mediator
  dispatch (built); data access (DbContext/DbSet usage, built); HTTP surface
  (endpoints from controllers, minimal APIs, FastEndpoints — partially
  built). A relation type with no evidence rule does not enter the model.
- **Tier 2 (only after Tier 1 proves out on real corpora).** Messaging
  (publish/consume over MassTransit/Wolverine-style conventions and
  integration events), external integrations (typed HttpClient and gRPC
  client usage), configuration/feature surface.
- **Out, permanently unless re-chartered:** runtime behaviour, anything
  requiring execution, dynamic dispatch beyond evidence. ABP's
  convention-based DI stays out on the same evidence rule as in Phase 7.

Proof discipline (unchanged from Phases 6–7): every tier lands against
hand-audited ground truth on chosen corpora — including at least one
non-CQRS corpus (controller → service → repository) selected and audited
*before* building, plus ABP as the stress case — and the existing frozen
question set must not move except by intended, recorded additions.

### R2 — The model as contract: disciplined now, published at the rename
- **Now:** the JSON output is renamed in intent from "report" to "the model
  file" and engineered as a contract: `schemaVersion` discipline,
  additive-only evolution, a compatibility test proving old baselines and
  old model files still read correctly (the rename plan's baseline-compat
  test, generalized).
- **Published later:** the documented public schema (fields, stability
  promises) ships **with the Noctua release**, after one full widening cycle
  has taught us what the model actually needs to be. Publishing earlier
  freezes mistakes; never publishing wastes the platform value — agents and
  other tools building on the model is the point of having one. MCP remains
  the agent-facing query surface over the same model throughout.

### R3 — Self-documentation: generated on demand by default; freshness checkable
- Default behaviour unchanged: outputs are generated per run; nothing is
  forced into a user's repo.
- The product: a **docs export** that writes an arc42-shaped documentation
  tree (system context; building blocks; runtime views — the per-request
  sequences generalized to per-flow; data model; cross-cutting
  configuration) into a folder the team chooses and may commit.
- The invention is not the docs; it is the **freshness gate**: a strict-mode
  check that regenerates to a temp location and diffs — stale docs exit
  non-zero in CI, reusing the baseline/strict machinery the tool already
  has. Documentation that can *prove* it is current.
- Narrative text (why, not what) is never generated — the tool documents
  structure and behaviour evidence; rationale stays human, supplied via R4.

### R4 — Human intent: a separate layer the tool verifies, never edits
- Humans may declare intent in a sidecar the tool never writes: module
  groupings and boundaries, the intended public surface, ownership. It
  lives in the existing config file (`.cqrslens.json`) or a sibling file.
- The tool **checks code against declared intent** and reports violations
  as findings (e.g. a call crossing a declared boundary). Intent that
  cannot be checked is rendered in docs explicitly labelled *declared,
  unverified* — the same honesty distinction as verify_claim's
  supported / not supported / not determinable.
- This is how a developer "works in" the model without ever falsifying it:
  they steer with intent; the tool answers with evidence.

## Foundation build order (each stage gated like Phases 6–7)

- **F0 — Metamodel paper.** The component/relation taxonomy with the
  evidence rule for every relation, written and approved before code.
  (This document is the draft of it; the charter expands it.)
- **F1 — Spine refactor.** AnalysisResult reorganized around
  components/relations, CQRS as a family. Hard gate: every frozen number
  and the frozen question set hold exactly; report output unchanged except
  by intended additions.
- **F2 — First widening slice.** Controllers, services, repositories as
  components with reference/DI/call/data edges, proven on the pre-audited
  non-CQRS corpus + all existing corpora. Perf gate applies (the Phase 7
  exit-gate lesson: new resolution work is where the cost hides).
- **F3 — Docs export + freshness gate** on the settled model.
- **F4 — Public schema + the Noctua release.** The rename lands here, with
  the wide tool and the published model contract — per Abdullah's
  sequencing rule.

## What does not change

Fail-closed evidence; read-only for agents (#6); the guarantee model
(agent proposes, tool verifies); staged briefs with independent
verification and frozen regression truth; the perf gate; public claims
locked to shipped, verified capability. 1.1.0 (Phase 7's release) is
unaffected by this document — it ships as CqrsLens, on its own track,
whenever the perf leg is settled.
