# FIX NOTES — Phase 3 (polish: non-CQRS and framework codebases)

## Summary

Phase 3 fixes the three weaknesses round 2 exposed outside the tool's MediatR
home territory:

- **A — `OrphanValidator` noise on non-CQRS codebases.** Orphan detection now
  requires a request model to exist. On a solution with zero discovered CQRS
  requests, `AbstractValidator<T>` types validate view models / DTOs / domain
  types, so they are not orphaned CQRS validators. nopCommerce (116 → 0),
  ABP (13 → 0) and VirtoCommerce (2 → 0). A renamed/deleted request in a
  MediatR-shaped solution still raises the warning.
- **B — error-level layer findings from name-guessed layers.** Layer
  classification is now only asserted when convention naming actually describes
  the solution. On framework-scale repos the collisions are coincidence
  (`...UI.Navigation`, `...Application.Cms.Core.Targets`), so the reference is
  still reported but at warning severity and `--strict` no longer fails.
  ABP 1 error → 0; OrchardCore 1 error → 0. A conventionally-layered solution
  keeps error severity.
- **C — notifications not inventoried from notification handlers.** A
  notification handler's generic argument is a notification by contract, so a
  handler whose event type carries no marker now inventories that event.
  Ardalis 0 → 7 notifications, with no double counting where a marker already
  declared the event.

Two supporting changes make problem B's acceptance countable without weakening
anything: the `AmbiguousProjectName` warning is narrowed to names that a
layer-checked project actually references (the only blind spot the warning
describes), and explicitly configured `LayerOverrides` are treated as a
trustworthy model.

No count changed on the four original corpora (northwindtraders,
eShop Ordering, eShopOnWeb, modular-monolith) in either mode.

## Baseline

Full suite before any edit, on this machine:

```
dotnet test CqrsLens.sln -c Release
Passed!  - Failed: 0, Passed: 347, Skipped: 0, Total: 347
```

After the changes:

```
Passed!  - Failed: 0, Passed: 359, Skipped: 0, Total: 359
```

347 → 359 is exactly the 12 new Phase 3 test cases in
`tests/CqrsLens.Tests/Phase3PolishTests.cs`; no pre-existing test changed
behaviour.

## Problem A — mechanism

`src/CqrsLens.Analysis/Checks/RequestValidatorChecks.cs`:

```csharp
if (result.Validators.Count == 0) return;
if (result.Requests.Count == 0)  return; // new
```

The rule is about the **premise** of the check, not about project names or
directory lookups: a validator is only an *orphan CQRS* validator relative to a
discovered request model. A solution that exposes no concrete request through
any mediator shape has no such model — its validators target view models / DTOs
/ domain types. This is the exact shape the brief describes (nopCommerce has no
MediatR; ABP has 0 requests). It cannot be a lookup table: the gate is
`result.Requests.Count`, which is derived from scanning.

It is not a deletion: in a MediatR-shaped solution (`Requests.Count > 0`) the
check is unchanged, so a validator whose target request was renamed or deleted
is still flagged (genuine-case test below).

## Problem B — mechanism

Layer labels are always inferred from names. A single convention match is weak
evidence, because layer words occur in module names by coincidence. The fix
splits that uncertainty into two outcomes rather than removing the check.

`src/CqrsLens.Analysis/Projects/LayerModelConfidence.cs` (new):

```csharp
// asserted when convention classification describes the solution:
// at least half of the non-test projects map to a layer naming
// convention (Domain/Application/Infrastructure/Api/Web/UI) rather
// than an ad-hoc folder fallback (framework, modules, templates, src, ...)
return nonTest > 0 && convention * 2 >= nonTest;
```

`src/CqrsLens.Analysis/Checks/ProjectLayerChecks.cs`:

```csharp
var layerModelAsserted =
    result.HasConfiguredLayers || LayerModelConfidence.IsAsserted(result.Projects);
var violationSeverity = layerModelAsserted ? CheckSeverity.Error : CheckSeverity.Warning;
```

- **Asserted model** (e.g. `Shop.Domain` + `Shop.Application` +
  `Shop.Infrastructure`): an outward reference stays an **error** and still
  fails `--strict`. Coverage on northwindtraders is 3/6 non-test projects (50%,
  asserted); modular-monolith is 19/26 (73%).
- **Unasserted model** (ABP: 120/499 = 24%; OrchardCore: a handful among 265,
  ≈1–2%): the
  outward reference is still emitted, but as a **warning**. Nothing is disabled
  — the finding still appears in the report, SARIF and diagrams.
- **Configured model**: when the caller passes `LayerOverrides`, `HasConfiguredLayers`
  makes the model asserted regardless of coverage, so user intent is never
  second-guessed.

Two follow-on changes:

1. `ProjectNameIndex.ReportAmbiguousNames` now emits the warning only when a
   **Domain or Application** project actually references the duplicated name.
   That is precisely the blind spot the message describes ("layer checks skip
   references to it"); ABP's 39 duplicates drop to 3 (the rest are template /
   module names nothing layer-checked references). The original corpora have
   none, so nothing changes there, and the existing duplicate-name regression
   test still sees its warning.
2. `AnalysisResult.HasConfiguredLayers` records whether layers were explicitly
   configured (set in `Analyzer.DiscoverProjects`).

## Problem C — mechanism

`src/CqrsLens.Analysis/Linking/NotificationLinker.cs`, before linking:

```csharp
MaterializeHandlerNotifications(result);
```

For every discovered notification handler whose event name is not already in
`result.Notifications`, a notification is created from the handler's target
(`NotificationName`, `NotificationNamespace ?? handler.Namespace`,
`ProjectName`). A handler's `INotificationHandler<T>` / `IDomainEventHandler<T>`
generic argument is a notification by contract, which is exactly the missing
inventory on custom mediator shapes.

- Matched by **simple name** against existing notifications, so an event that
  already implements `INotification`/`IDomainEvent` is never double counted
  (Ordering 7, modular-monolith 86, northwindtraders 1 all unchanged).
- Materialized notifications link to their handler in the same pass, so they are
  never reported as `OrphanNotification`.
- No new dependencies and no project-name special-casing.

## Acceptance — before / after counts

Scanned with the Release build on this box. `abp` before = the committed
`abp-text.json` baseline; everything else re-measured on this machine.

| Corpus | Mode | Requests | Handlers | Validators | Behaviors | DbContexts | Notifications | Notif. handlers | Errors | Warnings |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| ABP (before) | text | 0 | 0 | 13 | 0 | 80 | 0 | 0 | **1** | **52** |
| ABP (after)  | text | 0 | 0 | 13 | 0 | 80 | 0 | 0 | **0** | **4** |
| northwindtraders | text | 19 → 19 | 19 → 19 | 4 → 4 | 2 → 2 | 2 → 2 | 1 → 1 | 1 → 1 | 0 → 0 | 8 → 8 |
| northwindtraders | roslyn | 19 → 19 | 19 → 19 | 4 → 4 | 2 → 2 | 2 → 2 | 1 → 1 | 1 → 1 | 0 → 0 | 8 → 8 |
| eShop Ordering | text | 8 → 8 | 8 → 8 | 4 → 4 | 3 → 3 | 1 → 1 | 7 → 7 | 7 → 7 | 0 → 0 | 5 → 5 |
| eShop Ordering | roslyn | 8 → 8 | 8 → 8 | 4 → 4 | 3 → 3 | 1 → 1 | 7 → 7 | 7 → 7 | 0 → 0 | 5 → 5 |
| eShopOnWeb | text | 2 → 2 | 2 → 2 | 0 → 0 | 0 → 0 | 2 → 2 | 0 → 0 | 0 → 0 | 0 → 0 | 0 → 0 |
| eShopOnWeb | roslyn | 2 → 2 | 2 → 2 | 0 → 0 | 0 → 0 | 2 → 2 | 0 → 0 | 0 → 0 | 0 → 0 | 0 → 0 |
| modular-monolith | text | 105 → 105 | 105 → 105 | 5 → 5 | 0 → 0 | 4 → 4 | 86 → 86 | 30 → 30 | 0 → 0 | 133 → 133 |
| modular-monolith | roslyn | 105 → 105 | 105 → 105 | 5 → 5 | 0 → 0 | 4 → 4 | 86 → 86 | 30 → 30 | 0 → 0 | 133 → 133 |

modular-monolith scanned with `--markers ICommand,IQuery`; the other box corpora
with default markers. TEXT and ROSLYN shape lists (requests, handlers,
notifications, notification handlers) are identical on all four corpora.

### ABP warning breakdown (52 → 4)

| Code | Before | After | Reason |
| --- | ---: | ---: | --- |
| `OrphanValidator` | 13 | 0 | Problem A: 0 requests, so no request model. |
| `AmbiguousProjectName` | 39 | 3 | Problem B follow-on: only names referenced by a Domain/Application project are layer-check blind spots. ABP's other 36 duplicates are template/module names nothing layer-checked references. |
| `ApplicationLayerViolation` | 1 (error) | 1 (warning) | Problem B: layers are name-guessed coincidence, so the intended reference is no longer an error. |

ABP errors **1 → 0**, warnings **52 → 4**, requests **0**, DbContexts **80**.

### Count changes on the four original corpora

**None.** Every count in the table above is identical before and after, in both
modes. No finding on northwindtraders, eShop Ordering, eShopOnWeb or
modular-monolith changed severity, code or target.

### `--strict` (exit code 4 on any error)

```
ordering text strict=0      ordering roslyn strict=0
northwind text strict=0     northwind roslyn strict=0
modular    text strict=0
abp        text strict=0    (was 4: the one ApplicationLayerViolation error)
```

## Fixture evidence

All in `tests/CqrsLens.Tests/Phase3PolishTests.cs` (12 cases, all passing):

| Test | Reproduces | Asserts |
| --- | --- | --- |
| `NonCqrsSolution_ValidatorsOverNonRequestTypes_AreNotOrphanValidators` (text, roslyn) | nopCommerce shape: 3 validators, 0 requests | `Requests` empty, 3 validators, **no** `OrphanValidator` |
| `MediatRShapedSolution_ValidatorForRenamedRequest_IsStillAnOrphan` (text, roslyn) | genuine case: `KeepOrderCommand` exists, `RenamedCommandValidator` targets a deleted request | **`OrphanValidator` warning** on `RenamedCommandValidator` |
| `LayerModelConfidence_IsAboutConventionCoverage_NotProjectNames` | coverage signal | asserted for all-convention; not asserted for 3/7 convention; Tests excluded from the denominator |
| `NonCleanArchitectureShape_ApplicationToInfrastructure_IsNotAnError` (text, roslyn) | OrchardCore shape: 4 ad-hoc projects + `...Application.Targets` → `...Infrastructure` | **no error-level** `ApplicationLayerViolation`; finding present as a **warning**; `HasErrors` false |
| `ConfiguredLayers_AreNotSecondGuessedAndKeepErrorSeverity` | explicit `LayerOverrides` on the same non-clean shape | violation stays **error** |
| `ConventionallyLayeredSolution_ApplicationToInfrastructure_IsStillAnError` (text, roslyn) | genuine case: `Shop.Domain` + `Shop.Application` → `Shop.Infrastructure` | **error-level** `ApplicationLayerViolation`; `HasErrors` true |
| `NotificationHandler_WithUnmarkedEvent_InventoriesTheEventWithoutDoubleCounting` (text, roslyn) | Ardalis/custom-mediator shape: `ContributorDeletedEvent` with no marker + a marker notification | 2 notifications == 2 handlers, unmarked event `ViaMarker` null and linked, marker event not double counted, no `OrphanNotification` |

The two genuine-case tests are the Phase 3 judge: they prove `OrphanValidator`
and the layer error were not weakened away.

## Performance guard (Phase 1 / Phase 2 not regressed)

northwindtraders full scan, this machine: **text 0.57–0.89 s** (target ≤ 5 s),
**roslyn 1.57–2.41 s** (target ≤ 15 s). Other corpora remain sub-second
(text) / low-seconds (roslyn), unchanged from Phase 2.

## Scope / non-regressions

- No repository or project-name special-casing anywhere; every rule is a
  function of discovered shapes and counts.
- No check was deleted or defaulted off; the layer check still reports (at
  warning severity) when the model is untrusted.
- `--format agent` wording, the layer display-name fallback and the benchmark
  runner are untouched (out of scope).
- ABP in **roslyn** mode crashes (`Syntax node is not within syntax tree`).
  This is pre-existing: the same crash reproduces at base `1a83a05` before any
  Phase 3 edit, and ABP's acceptance is measured in text mode. Not touched here.

## Commits

Local only; never pushed.
