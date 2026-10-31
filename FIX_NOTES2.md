# FIX NOTES — Phase 2 (accuracy only: `MultipleHandlers` false positive)

## Summary

Scanning `dotnet/eShop`'s Ordering service reported **1 error + 5 warnings**. The
5 warnings are genuine (`CommandWithoutValidator`); the error was a false
`MultipleHandlers` on `eShop.Ordering.API.Application.Commands.IdentifiedCommand`,
which alone blocked `--strict` (exit code 4) on Microsoft's own reference codebase.

**Root cause:** the scanner treated the **open-generic** MediatR decorator
`IdentifiedCommand<T, R> : IRequest<R>` as a concrete request, then linked all
**7** `IdentifiedCommandHandler<T, R>` subclasses (one per inner command) to that
single open definition and raised `MultipleHandlers`.

**After the fix, both modes report 0 errors and the request inventory is exactly
the 8 concrete commands** (9 → 8 requests, 15 → 8 handlers). The 5 genuine
warnings, 4 validators, 3 behaviors, 7 notifications and 7 paired notification
handlers are unchanged. No count changed on NorthwindTraders, eShopOnWeb or
modular-monolith, and the Phase 1 performance result holds.

## Root cause (exactly where)

`IdentifiedCommand<T, R>` is the standard idempotency decorator: it declares
`IRequest<R>` with `where T : IRequest<R>`. It is a **type definition**, never
dispatched as-is. Its handler is the abstract `IdentifiedCommandHandler<T, R>`
plus 7 closed subclasses, e.g.

```csharp
public sealed class CancelOrderIdentifiedCommandHandler
    : IdentifiedCommandHandler<CancelOrderCommand, bool> { }
```

MediatR closes the wrapper per inner request
(`IdentifiedCommand<CancelOrderCommand, bool>`), and **each closed instantiation
has exactly one handler**. The model must not collapse all closed instantiations
into one request with N handlers.

Three discovery paths added the open definition as a concrete request:

- `src/CqrsLens.Analysis/Text/ShapeApplier.cs` — direct `IRequest` scan.
- `src/CqrsLens.Analysis/Roslyn/RoslynRequestShapeReader.cs` — Roslyn scan.
- `src/CqrsLens.Analysis/Contracts/MarkerRequestDiscoverer.cs` — marker-based requests (text).

The 7 subclasses were then attached by the normal handler/hierarchy resolution
(`IndirectShapeDiscoverer` in text, `RoslynHandlerShapeReader` in Roslyn),
all resolving to the single name `IdentifiedCommand`.

## The rule implemented (general; no name matching)

> A request declaration with **unbound type parameters** (an open generic) is a
> wrapper shape, not a concrete request.

Concretely:

1. **Request inventory** — every discovery path above now records an open-generic
   request's name and does **not** add it to `AnalysisResult.Requests`. The same
   guard is applied to Wolverine message discovery
   (`WolverineHandlerDiscovery`), where a generic message is likewise not concrete.
2. **Handler inventory** — `RequestLinker` drops handlers whose only request
   target is such a wrapper (`DropNonConcreteHandlerTargets`). A handler keeps its
   place if a concrete request with the same simple name exists, so nothing
   legitimate is removed.
3. **Orphan validators** — `RequestValidatorChecks` does not call a validator an
   orphan when it targets a recorded wrapper shape. `IdentifiedCommandValidator`
   therefore stays a discovered validator (4 validators, unchanged) without
   gaining a spurious `OrphanValidator` warning.

Recorded wrapper names live in a new internal
`AnalysisResult.NonConcreteRequestNames` set (Core internals are visible to
Analysis via `InternalsVisibleTo`). This is deliberately a rule about type
parameters/wrappers, **not** a string match on `IdentifiedCommand`.

Hard constraints respected: `MultipleHandlers` was not disabled or weakened (it
still fires for concrete requests, proven below); the Phase 1 performance code
(`SourceFileScanner` regex) and all reporting/output code are untouched.

## Before / after evidence — eShop Ordering (both modes)

Command: `cqrslens /data/workspace/cqrslens-corpus/eshop/src --analysis <mode> --format json`
(before = `88740e0` baseline; after = this change).

| Mode | Requests | Handlers | Validators | Behaviors | Notifications | Notif. handlers | Errors | Warnings |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| text — before | 9 | 15 | 4 | 3 | 7 | 7 | **1** | 5 |
| text — after  | **8** | **8** | 4 | 3 | 7 | 7 | **0** | 5 |
| roslyn — before | 9 | 15 | 4 | 3 | 7 | 7 | **1** | 5 |
| roslyn — after  | **8** | **8** | 4 | 3 | 7 | 7 | **0** | 5 |

The only finding before was:

```
error MultipleHandlers: Request `eShop.Ordering.API.Application.Commands.IdentifiedCommand`
has 7 handlers (CancelOrderIdentifiedCommandHandler, CreateOrderIdentifiedCommandHandler,
SetAwaitingValidationIdentifiedOrderStatusCommandHandler, SetPaidIdentifiedOrderStatusCommandHandler,
SetStockConfirmedOrderStatusIdentifiedCommandHandler, SetStockRejectedOrderStatusIdentifiedCommandHandler,
ShipOrderIdentifiedCommandHandler); a request should have a single handler.
```

After, the request inventory is exactly the 8 concrete commands (identical in
text and roslyn):

```
CancelOrderCommand, CreateOrderCommand, CreateOrderDraftCommand,
SetAwaitingValidationOrderStatusCommand, SetPaidOrderStatusCommand,
SetStockConfirmedOrderStatusCommand, SetStockRejectedOrderStatusCommand, ShipOrderCommand
```

Remaining warnings (5, all genuine `CommandWithoutValidator`): `CreateOrderDraftCommand`,
`SetAwaitingValidationOrderStatusCommand`, `SetPaidOrderStatusCommand`,
`SetStockConfirmedOrderStatusCommand`, `SetStockRejectedOrderStatusCommand`.
The 7 notification↔handler pairings are byte-for-byte the same as before.

`cqrslens .../eshop/src --analysis {text,roslyn} --strict` now exits **0**
(was 4). TEXT and ROSLYN produce identical request/handler/validator/notification
shape lists.

## `MultipleHandlers` still fires on a genuine case

New regression tests in `tests/CqrsLens.Tests/StrictCheckRegressionTests.cs`
(both `AnalysisMode.Text` and `AnalysisMode.Roslyn`):

- `MultipleHandlers_StillFiresOnConcreteNonGenericRequest` — a concrete, non-generic
  `DuplicatedCommand` with two handlers still yields an **error-severity**
  `MultipleHandlers` finding with both handlers listed. (The pre-existing
  `NotificationsAndExpandedRulesTests.ExpandedArchitectureChecks_...` covers the
  same rule end to end.)
- `OpenGenericCommandWrapper_IsNotInventoriedAndDoesNotRaiseMultipleHandlers` —
  the exact decorator shape (`IdentifiedCommand<T,R>` + abstract
  `IdentifiedCommandHandler<T,R>` + two closed subclasses + a wrapper validator)
  yields one concrete request, no `MultipleHandlers`, and no orphan validator; the
  wrapper validator remains counted.
- The former `GenericRequestAndHandler_LinkByOpenTypeIdentity` test was updated to
  `OpenGenericRequest_IsNotAConcreteRequest`, which now asserts the corrected
  semantics (an open-generic request and its open-generic handler are not concrete
  inventory entries).

## Regression table (re-measured on this machine, before → after)

All values identical before/after; TEXT and ROSLYN agree on every corpus.

| Corpus | Mode | Requests | Handlers | Validators | Notifications | Notif. handlers | Errors | Warnings |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| northwindtraders | text | 19 → 19 | 19 → 19 | 4 → 4 | 1 → 1 | 1 → 1 | 0 → 0 | 8 → 8 |
| northwindtraders | roslyn | 19 → 19 | 19 → 19 | 4 → 4 | 1 → 1 | 1 → 1 | 0 → 0 | 8 → 8 |
| eshoponweb | text | 2 → 2 | 2 → 2 | 0 → 0 | 0 → 0 | 0 → 0 | 0 → 0 | 0 → 0 |
| eshoponweb | roslyn | 2 → 2 | 2 → 2 | 0 → 0 | 0 → 0 | 0 → 0 | 0 → 0 | 0 → 0 |
| modular-monolith (`--markers ICommand,IQuery`) | text | 105 → 105 | 105 → 105 | 5 → 5 | 86 → 86 | 30 → 30 | 0 → 0 | 133 → 133 |
| modular-monolith (`--markers ICommand,IQuery`) | roslyn | 105 → 105 | 105 → 105 | 5 → 5 | 86 → 86 | 30 → 30 | 0 → 0 | 133 → 133 |

## Performance guard (Phase 1 not regressed)

Wall clock, NorthwindTraders full scan, this machine:

| Mode | Before (this session) | After | Phase 1 target |
| --- | ---: | ---: | ---: |
| text | 0.62 s | **0.58 s** | ≤ 5 s |
| roslyn | 1.76 s | **1.74 s** | ≤ 15 s |

Other corpora: eShop text 0.34 → 0.33 s, roslyn 1.23 → 1.25 s; eShopOnWeb text
0.38 → 0.39 s, roslyn 1.52 → 1.40 s; modular text 0.69 → 0.72 s, roslyn
2.28 → 2.32 s.

## `dotnet test`

```
dotnet test CqrsLens.sln -c Release
Passed! - Failed: 0, Passed: 347, Skipped: 0, Total: 347
```

(343 before this change, +4 from the new/parameterised regression tests.)

## Scope

Accuracy only. No special-casing of `IdentifiedCommand`; no change to the
`MultipleHandlers` rule for concrete requests; no change to the Phase 1
performance code, no reporting/output change, no new dependencies.
