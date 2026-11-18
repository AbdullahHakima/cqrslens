# BRIEF F5 AMENDMENT — APPROVED AS WRITTEN 2026-11-17 ~12:38 EET

# BRIEF F5 AMENDMENT — Bare-`Mediator` detector fix rides F5

Status: APPROVED AS WRITTEN 2026-11-17 ~12:38 EET. This is the binding brief; the draft is superseded.

## 1. Why

Verified 2026-11-17 on the real 2.0.0 build (diagnosis:
`hidden_files/mediator-failure-diagnosis-2026-11-17.md`, fixtures + outputs in
`hidden_files/mediator-repro-2026-11-17/`): a project referencing the bare
`Mediator` meta-package — the package the library's own docs tell users to
install (`dotnet add package Mediator`) — with no `using Mediator;` in the
scanned files gets a completely empty report (0 requests, 0 endpoints, no
conventions) in BOTH text and Roslyn modes. Roslyn fails fully silently.

Root cause, verified in code: `MediatorLibraryDetector.UsesMediator` matches
only the package refs `Mediator.Abstractions` / `Mediator.SourceGenerator`, a
`<Using Include="Mediator"/>`, or a `using Mediator;` directive. The bare
`Mediator` ref matches none. B vs B2 proves it: adding one `using Mediator;`
line flips the same fixture from 0 requests to fully linked. The detector is
the single choke point: `Analyzer.ScanShapes` calls `Detect` once, and without
`Mediator` in `Conventions`, the `ICommand`/`IQuery` markers are never added,
so the scanner finds nothing. The fix is one predicate. The tool's own
`MediatorConventions` doc comment already promises detection "when the scanned
source references the `Mediator` package or namespace" — the detector is the
only place that breaks the promise.

## 2. Base, branch, roles

- Base: `f8902e9` (tip of `foundations/f4-noctua`). Branch:
  `foundations/f5-spine-reports` — the F5 work is UNCOMMITTED on this branch
  (verified 2026-11-17 ~10:00 EET: HEAD `f8902e9`, 3 modified Reporting files
  + 8 untracked F5 files, driver stopped, no `agy` running).
- Driver: box #4, Antigravity orchestrator
  (`gemini-pro-orchestrator-g1-pro`), FRESH session. Driver never commits or
  pushes. Before touching anything, the driver MUST verify the starting tree
  matches the F5 CP3 state above (HEAD, status lines, and the 7 F5 file hashes
  in the F5 verification report §0). If it does not match, STOP and report —
  do not reconstruct, stash, or reset anything.
- The driver MUST NOT modify any F5 file. The spine rendering is verified;
  any change to it invalidates the F5 verification. Amendment touches exactly
  the files in §4.
- Verification: Mariam's VM (independent), full amended gates (§7) on the
  amended tree. The amended verification SUPERSEDES the F5 verification
  report; the F5 report stands as the record for the pre-amendment tree.
- Release: unchanged — F5 + amendment ride 2.0.0, no version bump.

## 3. Decisions (reasoned defaults — Abdullah vetoes by amendment)

- **D-A1 Predicate.** `UsesMediator` matches a package ref when it equals
  `Mediator` (case-insensitive) OR starts with `Mediator.` (case-insensitive,
  dot required). This subsumes the two existing `Equals` checks, so the
  predicate becomes one rule. Rationale: the dot-prefix covers the library's
  current sub-packages AND future `Mediator.*` packages, mirroring the
  existing Wolverine `StartsWith("WolverineFx")` pattern. The dot is load
  bearing: `MediatorX` must NOT match. The Wolverine predicate, the
  `using Mediator;` regex, and the `<Using>` check are untouched.
- **D-A2 Tests.** New test file (preferred; 0 existing test files modified),
  following the `MediatorConventionTests` harness pattern (temp root,
  `WriteProject`/`WriteFile`, `Analyzer.Analyze`). Five cases, 9 test cases
  total:
  1. Bare `Mediator` + fully-qualified, NO using (fixture B shape, §9),
     Theory text + Roslyn: conventions contain `Mediator`, 2 requests,
     handlers linked, controller endpoints linked with `targetRequest` set.
  2. Bare `Mediator` + `using Mediator;`, Theory text + Roslyn: same
     expectations (using-path regression on the meta-package).
  3. `Mediator.Abstractions` + fully-qualified, NO using (fixture B3 shape),
     Theory text + Roslyn: same expectations (guards the prefix refactor).
  4. Negative, Fact, text mode: package `MediatorX` + fully-qualified
     `Mediator.ICommand` usage, no using: conventions empty, 0 requests.
     Pins the boundary that detection is package-name-based, not usage-based.
  5. Future sub-package rule, Theory text + Roslyn: package
     `Mediator.Example` (hypothetical name, a rule test — not a real package)
     + fully-qualified, no using: conventions contain `Mediator`, 2 requests.
     Pins the D-A1 prefix rule against a future Equals-only narrowing.
- **D-A3 JSON exception (narrow).** D5/G3 are amended ONLY for bare-Mediator
  shapes: JSON stays byte-identical to the F4 baseline on every existing
  corpus, fixture, and the A–G pack EXCEPT fixture B (and B-roslyn), where
  the JSON changes BY DESIGN (0 → 2 requests, endpoints linked, conventions
  set). The driver MUST document B's before/after JSON diff field by field.
  Schema validation (0 errors, 0 undeclared paths) still applies to every
  JSON including B. Any JSON movement anywhere else fails the gate.
- **D-A4 Repro pack.** Mariam transfers the A–G fixture pack to box #4
  (`/tmp/mediator-repro/`, hash-verified) before launch. The driver treats it
  as READ-ONLY and re-scans all of A–G (+B2, B3) in both modes with the
  amended binary, reporting a full table. Expected: everything unchanged
  except B/B-roslyn (fixed); G (MediatR control) unchanged with empty
  conventions.
- **D-A5 Commit shape (intended).** Two commits, pushed together as one unit
  after verification + his accept: (1) F5 spine rendering, (2) detector fix +
  tests. The detector fix stays independently revertible. Final call at
  commit time is his.

## 4. Hard constraints

- **H-A1 Single-file exception to H3.** The ONLY production change is the
  `UsesMediator` predicate in
  `src/CqrsLens.Analysis/Contracts/MediatorLibraryDetector.cs`. Nothing else
  under `src/CqrsLens.Core/`, `src/CqrsLens.Analysis/`, or any analysis
  backend changes. No new `RelationKind` / `ComponentRole` values, no
  convention-semantics change beyond the detection trigger, no change to
  `MediatorConventions.cs`, `Analyzer.cs`, or the Roslyn builder.
- **H-A2 Roslyn guardrail.** If fixture B in Roslyn mode still returns 0
  requests after the predicate fix, STOP and report it as a blocker. Do NOT
  expand scope into the Roslyn builder, stub generation, or any other file
  without a new amendment.
- **H-A3 Existing tests unmodified.** No existing test file is modified and
  no existing assertion changes. New tests live in the new file (D-A2). If
  the driver finds a strong reason to append to `MediatorConventionTests.cs`
  instead, it must list that as an explicit exception and the diff must show
  additions only.
- **H-A4** No CLI/config/MCP/action.yml changes; no version bump, no
  CHANGELOG / README / csproj / packages.lock.json changes.
  `ToolInfo.Version` stays 2.0.0.
- **H-A5** The 4-project one-way chain is preserved. No new project, no new
  `PackageReference`. No F5 file is modified (see §2).
- **H-A6** Wraps over-claim, ABP Roslyn exit 5, and every other documented
  limitation stay exactly as they are.
- **H-A7** Determinism in both modes; `--generated-at` reproducibility.
- **H-A8** Driver never commits or pushes. Corpora read-only. No
  special-casing per corpus, per project name, or per fixture. The repro pack
  is read-only.
- **H-A9 Frozen lines hold, plus one new line.** nopCommerce 8,764 / 17,422 /
  1,151 / 516; six-corpus counts (ABP 3,750, modular 790, northwind 191,
  eshop 121, ardalis 158, jasontaylor 135); GoldenCorpus 27; suite grows ONLY
  by the 9 added tests (828 + 9 = 837 on box #4: 769 passed / 68 skipped /
  0 failed expected). NEW frozen line: fixture B, both modes — 2 requests,
  handlers linked, endpoints linked, `Mediator` convention present.
  (Mariam verified 2026-11-17 that no six-corpus or nopCommerce project
  references the bare `Mediator` package — only ardalis references
  `Mediator.Abstractions`/`Mediator.SourceGenerator`, already detected — so
  zero corpus movement is the expected result, to be PROVEN by the A/B, not
  assumed.)

## 5. File map (verified firsthand 2026-11-17)

- Detector (THE change):
  `src/CqrsLens.Analysis/Contracts/MediatorLibraryDetector.cs:27-33` —
  `UsesMediator` (package refs + `ProjectUsings` + `MediatorUsingRegex`).
  `UsesWolverine` in the same file is NOT to be touched.
- Choke point: `src/CqrsLens.Analysis/Analyzer.cs:184-187` (`ScanShapes`) —
  `Detect` → `EffectiveMarkers`/`EffectiveHandlerMarkers` → `scanner.Scan`.
  `src/CqrsLens.Analysis/Contracts/MediatorConventions.cs` — `Detect`
  delegates to the detector; `EffectiveMarkers` returns the configured set
  unchanged when `Mediator` is absent (this is the empty-report mechanism).
  Neither file changes.
- Test harness pattern:
  `tests/CqrsLens.Tests/MediatorConventionTests.cs` (251 lines, read in full)
  — temp-root `IDisposable` fixture, `WriteProject`/`WriteFile`,
  `Analyzer.Analyze(_root, mode)`; every existing Mediator case uses
  `Mediator.Abstractions` and/or `using Mediator;` (the test gap this
  amendment closes). Do not modify it (H-A3).
- F5 files (DO NOT TOUCH): `SpineReportView.cs`, `MarkdownSpineWriter.cs`,
  `HtmlSpineSectionRenderer.cs`, `F5SpineReportTests.cs`, + the 3 one-line
  insertions in `MarkdownReportWriter.cs`, `HtmlReportGenerator.cs`,
  `DocsBuildingBlocksPageWriter.cs`.

## 6. Checkpoints

- **CP1 — Fix proof.** Predicate changed; 9 new tests green on box #4; fixture
  B fixed in BOTH modes (2 requests, linked endpoints, convention present);
  A–G re-scan table complete; no other file touched.
- **CP2 — Full amended gates.** All G1–G9 evidence assembled (§7); B JSON
  before/after diff documented; no open issues; handoff with per-gate
  evidence. Then STOP — Mariam verifies on her VM, commit decision is his.

## 7. Verification gates (independent, Mariam's VM — FULL re-run, amended)

- **G1 Suite.** Box #4 suite green: 837 total (769 passed / 68 skipped /
  0 failed), 0 existing tests modified. VM `dotnet test` stays blocked
  (sandbox loopback redirect); Gate 1 closes on the box #4 result +
  claim-level verification of every new test, per the F4/F5 precedent.
- **G2 Determinism.** Repeated runs byte-identical across MD, HTML, JSON, and
  the docs tree, in both modes — including fixture B.
- **G3 JSON identity + schema, AMENDED (D-A3).** JSON byte-identical to the
  F4 baseline on all six corpora, nopCommerce, SampleShop, GoldenCorpus,
  in-repo fixtures, and A–G except B/B-roslyn; B's diff documented field by
  field and confined to the detection fix; independent Python `jsonschema`
  validation against `noctua.model.schema.json`, 0 undeclared paths, on every
  JSON including B.
- **G4 Insertion-only diff, AMENDED.** MD/HTML vs F4 differ ONLY by the spine
  section on every corpus/fixture EXCEPT B, where the CQRS content appears
  by design (documented). Any other byte change fails the gate.
- **G5 Docs export.** 02 extended per F5 D4; file set unchanged;
  `--check-docs` exit 0 fresh; stale detection still catches drift.
- **G6 Reconciliation.** Spine counts == JSON counts on all corpora AND
  fixture B; endpoint inventory rows == `spineEndpoints`; component rows ==
  `components`; most-connected degrees recomputed independently.
- **G7 Six-corpus A/B.** Zero movement in model counts, warnings identical,
  MCP answers identical (same as F5 G7). ABP Roslyn exit 5 on both binaries.
- **G8 Perf.** Report-generation cells ≤ +10% median-of-sessions vs the F4
  baseline (same 27-cell structure as F5 G8; session-1 contamination rule
  applies).
- **G9 Diff chartered, AMENDED.** Changed files limited to: the F5 set
  (verified), `MediatorLibraryDetector.cs` (predicate only), the new test
  file, and brief/checkpoint notes. csproj, schemas, workflows, docs, and all
  other Analysis/Core files untouched. 0 new warnings.

## 8. Out of scope (explicit)

Global usings living outside the scan root; central `Directory.Packages.props`
handling; Roslyn builder / stub changes; any other detector (MediatR,
Wolverine); the Wraps fix; the Reporting Upgrade backlog; site/comms;
anything that changes JSON bytes beyond D-A3; anything that touches an F5 file.

## 9. Appendix — fixture B, exact contents (verified from the repro pack)

`App.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="Mediator" Version="3.0.0" />
  </ItemGroup>
</Project>
```

`Orders.cs` (no `using Mediator;` — fully qualified throughout):
```csharp
namespace App.Orders;

public sealed record CreateOrderCommand(string Sku) : Mediator.ICommand<int>;
public sealed record GetOrderQuery(int Id) : Mediator.IQuery<string>;

public sealed class CreateOrderHandler : Mediator.ICommandHandler<CreateOrderCommand, int>
{
    public ValueTask<int> Handle(CreateOrderCommand command, CancellationToken ct) => new(1);
}

public sealed class GetOrderHandler : Mediator.IQueryHandler<GetOrderQuery, string>
{
    public ValueTask<string> Handle(GetOrderQuery query, CancellationToken ct) => new("x");
}
```

`OrdersController.cs`:
```csharp
using Microsoft.AspNetCore.Mvc;

namespace App.Orders;

[ApiController]
[Route("orders")]
public sealed class OrdersController(Mediator.IMediator mediator) : ControllerBase
{
    [HttpGet("{id:int}")]
    public async Task<string> Get(int id, CancellationToken ct)
        => await mediator.Send(new GetOrderQuery(id), ct);
}
```

Pre-fix result (2.0.0 build, both modes): 0 requests, 0 endpoints, no
conventions. Post-fix expectation (both modes): `Mediator` convention present,
2 requests, both handlers linked, controller endpoint linked with
`targetRequest` set.
