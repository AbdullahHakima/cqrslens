# Model a feature with CqrsLens

Start with a behavior and its acceptance criteria. CqrsLens supplies the scanned
topology, impact traces, source locations, and repeatable design and TDD checks.
Your MCP client can use that evidence to draft or refine a design, implement it
in the IDE when asked, and run the repository's tests. The team's requirements
and architecture decisions are the source of truth for the proposed behavior.

| Question | Start here | What you can use from the answer |
| --- | --- | --- |
| Where does this feature belong? | `get_architecture_dna`, then `trace_slice`, `trace_request_impact`, or `analyze_change_impact` | Detected `architectureStyles` (Layered / N-Tier, Minimal APIs, gRPC, Event-Driven, Workers, MVC, Blazor, CQRS / Mediator), project and layer names, source paths, affected projects, test references, uncovered hotspots, and static blind spots. |
| Can I seed a slice design from existing code? | `draft_slice_design` or `--draft-slice` | A schema-valid v1 `SliceDesign` seeded from any CQRS request/handler OR non-MediatR operation (`Endpoint`, `Grpc`, `Worker`, `Consumer`, `Service`) — including scanned properties, handler (when CQRS), validator, endpoint/trigger, persistence, dependencies, and published/dispatched links, plus gaps and open questions. |
| Can these projects depend on each other? | `evaluate_architecture_proposal` or `--proposal` | Results from applying proposed projects and reference additions/removals to the scanned graph, including `SelfProjectReference`, `UnknownLayerName`, `TransitiveLayerViolation` (`dependencyPath`), `ConditionalProjectReference` warnings, coupling `metrics`, and `blastRadius`. |
| Does the implementation match the agreed slice? | `verify_slice_design` or `--slice-design` | Per-field status and source evidence for project, `kind`, request/operation, handler (CQRS), validator, endpoint/RPC/worker trigger, properties, publishes, dispatches, persistence (`dbContexts` / `entities`), dependencies, and `forbiddenDependencies`. Pass a directory to `--slice-design` to verify all `*.slice.json` files in one scan. |
| What should fail first? | `plan_slice_tests` | Stated scenarios, payload/operation fields, collaborators with recommended test doubles, active behaviors, discovered test projects and test references, heuristic scenario-to-boundary mappings, and open questions. |
| Where is the slice in the TDD loop? | `verify_tdd_cycle` or `--tdd-check` | Fail-closed phase (`spec_draft`, `red`, `green_incomplete`, `verified`, `drifted`, or `inconclusive`), matching passed/failed tests from optional `.trx` results, slice-scoped architecture checks (including `CircularServiceDependency`), and `nextActions`. |
| Did the implementation match the decision? | Run tests with `--logger trx` and use `verify_tdd_cycle`, `diff_architecture`, `trace_request_impact`, or `verify_claim` | Runtime assertions, the observed project and architecture delta, and evidence for specific static claims (`has handler`, `calls service`, `references project`, `touches entity`, `uses DbContext`, `publishes`, `communicates with`, `has test reference`, etc.). |

## MCP prompts

The MCP server exposes three hardened workflow prompts:

- `model_feature` (`feature`, optional `path`): guides discovery (`get_architecture_dna`, `trace_slice`, `analyze_change_impact`) -> `evaluate_architecture_proposal` -> `draft_slice_design` / `verify_slice_design` -> `plan_slice_tests` -> Red/Green/Refactor with `verify_tdd_cycle`.
- `plan_refactor` (`target`, `goal`, optional `path`): guides `analyze_change_impact` -> `draft_slice_design` -> `evaluate_architecture_proposal` -> `plan_slice_tests` + characterization tests for `uncoveredHotspots` -> refactoring + `verify_tdd_cycle`.
- `review_architecture_change` (`base`, optional `designPath`, optional `path`): guides `diff_architecture` -> `check_architecture` -> `verify_claim` -> `verify_slice_design`.

All prompt `path`, `target`, `base`, and `designPath` arguments reject line breaks and control characters and enforce length limits, and untrusted user text is isolated inside XML delimiter tags (`<feature_request>`, `<refactor_target>`, `<refactor_goal>`, `<review_base>`, `<design_path>`).

## Walk through a SampleShop change

Use `samples/SampleShop` as the MCP workspace. Configure `cqrslens` following the
[README](../README.md#mcp-server). Open the `model_feature` prompt with this
`feature` argument. Pass `path` if the server was started in a different folder
(within the configured workspace root or `--allow-root` directories).

> Reject order lines whose quantity is zero or negative. A line with quantity
> one should still pass when the rest of the command is valid. Put the validator
> tests in a new SampleShop.Application.Tests project.

If your client does not surface MCP prompts, send the feature text directly and
ask the client to follow these steps using its CqrsLens tools.

### Save the design alongside the feature

Start from the [SampleShop design](../samples/SampleShop/create-order.slice.json),
or seed a design directly from an existing CQRS request OR non-MediatR operation
(`Endpoint`, `Grpc`, `Worker`, `Consumer`, `Service`) with `draft_slice_design` /
`--draft-slice`, and use the [version 1 schema](../cqrslens.slice-design.schema.json)
for editor completion. Set the owning project and the exact `request.type` (or
entrypoint/operation name, e.g., `CatalogApi.CreateItem` or
`BasketService.UpdateBasket`), including the namespace and containing type if
present. Use `Command`, `Query`, `Request`, `Endpoint`, `Grpc`, `Worker`,
`Consumer`, or `Service` as shown by the scan. The `responseType` field holds
the type displayed by the selected scanner or `null` for an operation without a
response. For non-CQRS kinds (`Endpoint`, `Grpc`, `Worker`, `Consumer`,
`Service`), leave `handler` as `null` — slice blueprints omit any empty
`Handler` node automatically. For `endpoint.route`, HTTP/Blazor/Razor routes
require a leading `/`, while `GRPC`, `WORKER`, and `CONSUME` triggers accept
service/trigger strings (such as `BasketService.UpdateBasket`,
`OrderWorker.ExecuteAsync`, or `orders.created`).

Optional assertions let you pin the full vertical slice contract without
over-constraining fields you want left open:

- `handler`, `validator`, `endpoint` (`method`, `route`)
- `properties`: array of `{ "name": "...", "type"?: "..." }`
- `publishes`: expected published notifications / domain events
- `dispatches`: expected nested dispatched requests
- `persistence`: `{ "dbContexts"?: [...], "entities"?: [...] }`
- `dependencies`: expected handler constructor/service dependencies
- `forbiddenDependencies`: type names or namespace prefixes (ending with `.`) that the linked handler must not depend on or call

Omitted optional fields make no assertion about that part of the slice, while empty
arrays (e.g. `"publishes": []`) assert that none are observed and fail closed as
`not_determinable` when the scan cannot confirm absence.

Keep the agreed Given/When/Then scenarios in the file. Call `verify_slice_design`
in your MCP client with the JSON as `design`, or run `--slice-design` from the
repo root against either a single file or a directory of `*.slice.json` files:

```bash
cqrslens samples/SampleShop --slice-design samples/SampleShop/create-order.slice.json
cqrslens samples/SampleShop --slice-design samples/SampleShop --strict
cqrslens samples/SampleShop --slice-design samples/SampleShop/create-order.slice.json --format html --render svg -o slice-design.html
```

When `--slice-design` receives a directory, CqrsLens discovers `*.slice.json`
recursively (skipping `bin`, `obj`, and `.git`), scans the solution once, and
outputs `{ "status", "root", "total", "matched", "results": [ { "file", "evaluation" } ] }`
when `--format json` (or default) is selected.

### Visual modeling & TDD outputs

`--proposal`, `--impact`, `--slice-design`, and `--tdd-check` default to JSON on stdout for tool/CI automation, and also accept `--format md` (Markdown with embedded Mermaid diagrams), `--format html` (HTML using `--render mermaid` or offline zero-JS `--render svg` with strict `Content-Security-Policy` headers and zero `<script>` tags), and `-o FILE` / `--output FILE`.

The composite GitHub Action (`action.yml`) also supports `slice-design`, `tdd-check`, and `test-results` inputs, automatically posting formatted Markdown reports with status badges and Mermaid diagrams into `$GITHUB_STEP_SUMMARY`.

| Status | Meaning |
| --- | --- |
| `matched` | The scan supports the declared static expectation. |
| `missing` | The scan found no matching shape or association. This is expected for a new design before implementation. |
| `mismatch` | The scan observed a conflicting shape or association. |
| `not_determinable` | Ambiguity, incomplete evidence, unresolved dispatches, or an inferred association prevents a reliable decision. |

After implementation, pass `--strict` to exit `4` whenever the overall result
is anything other than `matched`. Invalid design files, empty design directories,
or unsupported schema versions exit `1`.

### 1. Find the owning slice and analyze change impact

Call `get_architecture_dna` to inspect detected `architectureStyles` (and layers),
then `trace_slice` / `trace_request_impact` for `CreateOrderCommand` (or a direct
operation such as `CatalogApi.CreateItem`), and `analyze_change_impact` for `Order`
(`targetKind: "entity"`) or `--impact Order --impact-kind entity`:

```bash
cqrslens samples/SampleShop --impact Order --impact-kind entity
cqrslens samples/SampleShop --impact Order --impact-kind entity --format md -o impact.md
cqrslens . --impact "POST /api/catalog/items" --impact-kind entrypoint
cqrslens . --impact OrderStatusChangedToAwaitingValidationIntegrationEvent --impact-kind event
```

`--impact-kind` / `targetKind` supports 9 target kinds: `project`, `entity`,
`dbcontext`, `service`, `notification`, `handler`, `request`, `entrypoint`, and
`event`.

The request and validator are in `SampleShop.Application`. Open the source
location for the validator. Its current rules require a positive customer ID and
a non-empty list. `analyze_change_impact` maps upstream and downstream edges across
projects, requests, handlers, validators, system entrypoints (`MinimalApi`,
`Controller`, `Grpc`, `Worker`, `Consumer`, `SignalR`, `Blazor`, `RazorPage`),
published/consumed integration events and integration flows, services, DbContexts,
entities, and static test references, and surfaces `uncoveredHotspots` (such as
untested requests/entrypoints or unvalidated commands) alongside explicit `blindSpots`
(`SourceFiltersActive`, `IncompleteScan`, `UnresolvedDispatch`,
`ConditionalProjectReference`).

To compare designs that would cross a project boundary, try adding an
`Application → Infrastructure` reference in `evaluate_architecture_proposal`.
SampleShop's existing `Infrastructure → Application` reference closes a cycle.
The finding's `dependencyPath` shows the cycle and its `file` identifies the
declaring project where available. A design that needs infrastructure should
consider an application contract with an infrastructure implementation wired in
the API composition root. This validation change needs only the existing validator.

### 2. Model the test project's dependencies

Pass these arguments to `evaluate_architecture_proposal`, or save them as
`proposal.json` and run `cqrslens samples/SampleShop --proposal proposal.json`
(add `--strict` to exit `4` on `conflicts` or `invalid`, or `--format md|html --render svg -o proposal.html` for visual coupling & cycle diagrams).

```json
{
  "projects": [{ "name": "SampleShop.Application.Tests", "layer": "Tests" }],
  "references": [
    { "from": "SampleShop.Application.Tests", "to": "SampleShop.Application" }
  ]
}
```

Each `from` project declares a reference to `to`. An omitted `operation`
means `add`. Use `"operation": "remove"` to evaluate a replacement or removal.
A proposal can contain only new projects, only reference changes, or both.
Cycle, layer, and transitive layer checks evaluate the combined additions and
removals and compute per-project coupling `metrics` (afferent $C_a$, efferent
$C_e$, instability $I$) and `blastRadius` (`affectedProjects`, affected request,
handler, validator, and endpoint counts, and `sampleRequests`). Existing project
names match case-insensitively. For duplicate names, use the source paths
returned in `candidates`. The evaluation honors configured layer-rule severities
and warns on `UnknownLayerName` and `ConditionalProjectReference` references.
You can also evaluate a greenfield design against an existing directory with no
projects. A CLI exit code of `0` means the tool produced an evaluation. Read its
`status` field to decide whether to change the proposal.

| Status | Action |
| --- | --- |
| `checks_passed` | Review the design against the feature requirements and the evaluation's stated `scope`. |
| `review` | Resolve warnings (such as `UnknownLayerName` or `ConditionalProjectReference`), incomplete cycle evidence, or references already in the desired state. |
| `conflicts` | Revise the proposal using the error findings (`LayerViolation`, `ApplicationLayerViolation`, `TransitiveLayerViolation`, `CircularProjectDependency`) and rerun it. |
| `invalid` | Correct the proposal's project identities, `SelfProjectReference`, or inconsistent reference operations. |

### 3. Plan, run, and verify the TDD cycle

Capture the baseline before writing the first test, and optionally seed a design
from an existing request:

```bash
cqrslens samples/SampleShop --format json -o /tmp/sampleshop-before.json
cqrslens samples/SampleShop --draft-slice CreateOrderCommand
```

Pass the following arguments to `plan_slice_tests`:

```json
{
  "request": "SampleShop.Application.Orders.CreateOrder.CreateOrderCommand",
  "scenarios": [
    {
      "name": "rejects non-positive quantity",
      "given": "A valid customer and a single order line with quantity 0 or -1",
      "when": "CreateOrderCommand is validated",
      "then": "Validation reports an error for the line's Quantity field"
    },
    {
      "name": "accepts the smallest positive quantity",
      "given": "A valid customer and a single order line with quantity 1",
      "when": "CreateOrderCommand is validated",
      "then": "Validation succeeds"
    }
  ]
}
```

The returned plan includes `payload`, `collaborators` (with resolved concrete
implementations and recommended doubles), `activeBehaviors`, discovered
`testProjects` (framework, mocking, and assertion libraries), static
`discoveredTests`, and heuristic `scenarioMappings`. Choose the linked validator
as the first test boundary and add an xUnit test in `SampleShop.Application.Tests`,
which references `SampleShop.Application`.

Run `dotnet test --logger trx --results-directory TestResults` and verify the
TDD state via `verify_tdd_cycle` (passing `design` and `testResultsPath`) or the
CLI:

```bash
cqrslens samples/SampleShop --tdd-check samples/SampleShop/create-order.slice.json --test-results TestResults/run.trx --strict
cqrslens samples/SampleShop --tdd-check samples/SampleShop --test-results TestResults/run.trx --format html --render svg -o tdd-batch.html
```

Passing a directory to `--tdd-check` discovers all `*.slice.json` specs recursively, shares one static scan and parsed `.trx` summary, and emits a batch `TddBatchEvaluation` (with per-phase rollups, strict exit `4` whenever the aggregate phase is not `verified`, and Markdown/HTML summary tables + SVG badges).

| Phase | Meaning |
| --- | --- |
| `inconclusive` | Scan diagnostics, source filters, or `not_determinable` design checks prevent a reliable verdict. |
| `drifted` | The implementation mismatches the saved slice design or has active slice-scoped architecture errors. |
| `red` | One or more runtime tests matching the slice failed in the supplied `.trx`. |
| `spec_draft` | The slice request/design is still missing from source while no slice tests are failing. |
| `green_incomplete` | The design matched, but discovered tests, matching passing `.trx` results, zero slice warnings, or a completely green `.trx` suite are not yet satisfied. |
| `verified` | Design matched, zero slice errors/warnings, static test references discovered, and runtime `.trx` results passed with zero failures (`--strict` exits `0`). |

### 4. Compare the implementation

When the code is ready for review, pass `/tmp/sampleshop-before.json` as the
`base` to `diff_architecture` (adding `--allow-root /tmp` if `/tmp` is outside the
MCP server's startup workspace root, or run the `review_architecture_change`
prompt). Expect the diff to show the test project and its reference to
`SampleShop.Application`. Use the focused test results (`verify_tdd_cycle`),
`verify_claim`, and the static diff to confirm that production contracts and
project boundaries stayed clean.

## Security and path confinement

- **MCP path confinement (`McpPathGuard`)**: Tool `path` arguments, `diff_architecture` `base`, and `verify_tdd_cycle` `testResultsPath` default to the MCP startup workspace root (`options.Path`), blocking path traversal, sibling-prefix bypasses, control characters, and symlink escapes. Pass repeatable `--allow-root DIR` (or `--allow-external-paths` for unrestricted mode) when launching `cqrslens --mcp`.
- **DTD/XXE-safe XML & bounded reads**: `.csproj`, `Directory.Build.*`, and `.trx` parsing disallow DTDs/external entities (`SafeXmlLoader`), and config/report/baseline/proposal/design readers enforce a 32 MB size limit (`BoundedFileReader`).

## Universal non-MediatR modeling examples

CqrsLens works cleanly on repositories with zero MediatR/Wolverine usage (verified on `dotnet/eShop` commit `3c6e531a`, `nopCommerce` commit `0344b617`, and a synthetic zero-MediatR multi-project suite):

- **Minimal API `Endpoint` slice (`dotnet/eShop`, `Catalog.API`)**:
  ```bash
  cqrslens path/to/eShop --draft-slice CatalogApi.CreateItem --format json -o create-item.slice.json
  cqrslens path/to/eShop --slice-design create-item.slice.json --strict --format html -o create-item.html
  ```
  Verified on `dotnet/eShop` (`3c6e531a`): produces `kind: "Endpoint"`, owning project `Catalog.API`, `POST /api/catalog/items`, `CatalogContext`, `CatalogItem`, 7 dependencies, and `handler: null`. `--slice-design --strict` exits `0`, and the standalone HTML blueprint renders 1 SVG with no empty `Handler` node.
- **gRPC `Grpc` slice (`dotnet/eShop`, `Basket.API`)**:
  ```bash
  cqrslens path/to/eShop --draft-slice BasketService.UpdateBasket --format json -o update-basket.slice.json
  cqrslens path/to/eShop --slice-design update-basket.slice.json --strict --format md -o update-basket.md
  ```
  Verified on `dotnet/eShop` (`3c6e531a`): produces `kind: "Grpc"`, owning project `Basket.API`, endpoint `GRPC BasketService.UpdateBasket` (non-`/` trigger strings are accepted for `GRPC`, `WORKER`, and `CONSUME`), `RedisBasketRepository`, and exits `0` under `--slice-design --strict`.
- **Direct entity impact (`dotnet/eShop`, `CatalogItem`)**:
  ```bash
  cqrslens path/to/eShop --impact CatalogItem --impact-kind entity --format html -o impact.html
  ```
  Verified on `dotnet/eShop` (`3c6e531a`): identifies `6` affected projects, `36` impacted items, `26` existing test references, and renders `1` zero-JS SVG diagram.
- **Direct flows, direct sequences & cycle detection without Mediator**:
  - When a codebase has zero CQRS requests/handlers/behaviors (such as `nopCommerce` `0344b617`, `41` projects, `962` system entrypoints, `116` integration flows), Console/Markdown/HTML reports automatically omit empty `0 Requests / 0 Handlers / 0 Behaviors` sections, adapt stat cards, and render **Direct Flows** and **Operation Sequences** (`12` `sequenceDiagram` blocks on `nopCommerce`, zero `participant Mediator` clutter).
  - `ArchitectureChecker` reports **`CircularServiceDependency`** (warning, configurable in `cqrslens.json` or suppressible via `rules`) when two or more non-test application services form a constructor-injected dependency cycle (`0` false positives across all `10` OSS gauntlet repos).
  - `verify_claim` evaluates `touches entity`, `uses DbContext`, `calls service`, `publishes`, and **`communicates with`** (using scanned `IntegrationFlows`) across both CQRS handlers and direct `SystemEntrypoint` operations, failing closed as `not determinable` when static links are absent rather than guessing.

## What the model covers

Project proposals, change impact, and slice verification use source project
references, conditional MSBuild reference metadata, static CQRS/DI/EF flow,
discovered `SystemEntrypoint` and `IntegrationFlows` relationships,
discovered test attributes, and configured or inferred layer names. Review
runtime reflection, dynamic dispatch, and build conditions flagged in `blindSpots`
and `scope`. CqrsLens scans without building the target solution, and the MCP
client runs any requested edits and test commands through its own workspace tools.
