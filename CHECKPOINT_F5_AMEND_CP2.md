# Stage F5 Amendment Checkpoint 2 — Full Amended Gates (G1–G9) & Handoff

- **Date:** 2026-11-17
- **Status:** `DONE` (all amended gates G1–G9 verified on Box #4; branch `foundations/f5-spine-reports` left uncommitted per `H-A8`; **stopped after CP2** ready for Mariam's VM verification)
- **Branch:** `foundations/f5-spine-reports` (HEAD `f8902e9e75ac62d936d285b5e42f2be536228f25`)
- **Antigravity CLI Version:** `1.5.10` (`agy --version`)
- **Corpus Pin:** `/data/workspace/corpora/nopcommerce` (`release-4.90.0` at `e7a52b9199f9c7651839e92826df47f668f3c767`, clean read-only)
- **Repro Pack:** `/tmp/mediator-repro/` (read-only, untouched; SHA-256 verified)

---

## Summary of Amended Verification Gates (G1–G9)

| Gate | Requirement | Observed Box #4 Result | Status |
|---|---|---|---|
| **G1 Suite** | `828 + 9 = 837` (`769` passed / `68` skipped / `0` failed), `0` existing tests modified (`H-A3`, `H-A9`) | `Passed! - Failed: 0, Passed: 769, Skipped: 68, Total: 837, Duration: 3 s` (`760` existing + `9` new tests in [MediatorPackageDetectionTests.cs](file:///data/workspace/cqrslens/tests/CqrsLens.Tests/MediatorPackageDetectionTests.cs); `0` existing test files modified; `ArchitectureBoundaryTests` `8/8` pass; `MediatorPackageDetectionTests` `9/9` pass) | **PASS** |
| **G2 Determinism** | Repeated runs byte-identical across MD, HTML (`mermaid` & `svg`), JSON, and docs tree in both modes, including Fixture `B` (`H-A7`) | `515` total output files across `32` target/mode pairs (`7` corpora/fixtures + `9` repro-pack fixtures `A`–`G`, `B2`, `B3` × `text` & `roslyn`) verified `0`-byte diff between `run1` and `run2`; `text == roslyn` parity confirmed on Spine & `02-building-blocks.md` (excluding `StrictFailure`) | **PASS** |
| **G3 JSON identity + schema** | JSON byte-identical to F4 baseline everywhere EXCEPT Fixture `B`/`B-roslyn` (`D-A3`); `B` diff documented field-by-field; schema validation `0` errors / `0` undeclared paths | `30 / 32` JSON outputs (`14` corpus/fixture scans + `16` non-`B` repro scans) `0`-byte diff vs F4 (`f8902e9`); Fixture `B` (`text` & `roslyn`) diff documented field-by-field in §3 below; independent recursive Python Draft `2020-12` validator checked all `32` JSONs (`observed_paths = 357`, `declared_paths = 432`, `undeclared = 0`, `0` errors) | **PASS** |
| **G4 Insertion-only diff** | MD/HTML/`02-building-blocks.md` vs F4 differ ONLY by the inserted spine section/inventories on all non-`B` inputs; Fixture `B` CQRS+Spine change documented | Stripping `## Architecture Spine` (MD), `<h2>Architecture Spine</h2>` (HTML `mermaid` & `svg`), and `### Endpoint Inventory` + `### Component Index` (`02-building-blocks.md`) yielded `0`-byte diff vs F4 across all `30` non-`B` pairs (`01`, `03`, `04`, `05`, `06`, `diagrams/*.svg` `0`-byte diff); Fixture `B` CQRS+Spine diff documented in §3.3 | **PASS** |
| **G5 Docs export** | `02-building-blocks.md` extended per F5 `D4`; file set unchanged; `--check-docs` exit `0` fresh and catches drift | Verified across `nopCommerce`, `SampleShop`, `GoldenCorpus`, and `repro-B` in both `text` & `roslyn` modes (`8/8` pairs): fresh unpinned and pinned `--check-docs` exited `0`; mutating `02-building-blocks.md` under `### Component Index` exited `4` (`section changed (Component Index)`) | **PASS** |
| **G6 Reconciliation** | Spine counts, role/relation rows, inventories, and top-25 hubs reconcile independently against JSON across all corpora AND Fixture `B` | Independent Python reconciler verified `100%` match across all `32` pairs (MD, HTML `mermaid`, HTML `svg`, and `02-building-blocks.md`), including `nopCommerce` (`516 / 1,151 / 8,764`), `GoldenCorpus` (`21 / 6 / 27`), `SampleShop` (`16 / 0 / 14`), and Fixture `B` (`5 / 1 / 5`) | **PASS** |
| **G7 Corpus A/B, Warnings & MCP** | Frozen lines hold + new Fixture `B` line; warnings identical; MCP identical; 5 absent external corpora noted | `nopCommerce` `516` (`282/1/128/105`) / `1,151` / `8,764` (`17,422` occ); `GoldenCorpus` `27`; `SampleShop` `14`; Fixture `B` (`2` requests, linked handlers/endpoint, `Mediator` convention); `agent`/`sarif`/`summary`, `DiffPair --diff-output`, `StrictFailure --strict` (`4 == 4`), and 5-request MCP probe (`14,572` B) `100%` byte-identical F4==Amended F5; `--no-incremental` Release warnings `F4 = 26`, `Amended F5 = 26` (`0` new warnings, `Core = 0`); `dotnet restore --locked-mode` exit `0` | **PASS** |
| **G8 Perf** | Report-generation cells `<= +10%` median-of-sessions vs F4 baseline (same 27-cell structure; S1 contamination rule applies) | `25 / 27` cells passed in S1 (`<= +7.59%`); `nopCommerce export-docs` S1 `+11.68%` -> S2 `+3.70%`; `nopCommerce text/html-svg` noisy S1 `+11.96%` & S2 `+13.49%` (during host load `~2.12`) -> S3 `-1.36%`, S4 `+9.41%`, S5 `+4.92%` (`+7.17%` median across post-S1 sessions, `+9.41%` across all 5 sessions, both `<= +10%`) | **PASS** |
| **G9 Diff chartered** | Limited to F5 set (hashes unchanged), `MediatorLibraryDetector.cs` predicate, new test file, and brief/checkpoints; cleanup complete | `/tmp/cqrslens-f4base` and all `/tmp/f5-amend-*` temp dirs removed (`TMP_CLEAN`); all `7` F5 file SHA-256 hashes verified identical; `0`-byte diff in `*.csproj`, schemas, workflows, `Core`, `Cli`, `Reporting`, and other `Analysis` files; `ToolInfo.Version == 2.0.0`; `nopCommerce` clean at `e7a52b91`; `/tmp/mediator-repro/*.json` hashes unchanged | **PASS** |

---

## 1. Gate 1 — Final Post-Cleanup Test Suite & Boundaries

Executed in `/data/workspace/cqrslens` after Gate 9 cleanup:

```bash
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH
dotnet test CqrsLens.sln -c Release --nologo
dotnet test CqrsLens.sln -c Release --no-build --nologo --filter "FullyQualifiedName~ArchitectureBoundaryTests"
dotnet test CqrsLens.sln -c Release --no-build --nologo --filter "FullyQualifiedName~MediatorPackageDetectionTests"
```

Observed results:
- **Full suite:** `Passed! - Failed: 0, Passed: 769, Skipped: 68, Total: 837, Duration: 3 s - CqrsLens.Tests.dll (net8.0)`
  - `750` pre-F5 tests + `10` F5 tests (`F5SpineReportTests.cs`) + `9` new amendment tests (`MediatorPackageDetectionTests.cs`) passing; `0` existing test files modified (`H-A3`).
  - `68` skipped external-corpus tests (`ABP`, `modular-monolith`, `northwind`, `eshop`, `ardalis`, `jasontaylor` are absent on Box #4 and were not cloned or fetched; covered verification-side on Mariam's VM).
- **Architecture boundary tests:** `Passed! - Failed: 0, Passed: 8, Skipped: 0, Total: 8, Duration: 38 ms`
- **New package-detection tests:** `Passed! - Failed: 0, Passed: 9, Skipped: 0, Total: 9, Duration: 696 ms`

---

## 2. Gate 2, Gate 3, Gate 4 & Gate 6 — Cross-Corpus & Repro Pack Results (`32` Pairs)

All `16` targets (`7` local corpora/fixtures + `9` `/tmp/mediator-repro/` fixtures) scanned in both `text` and `roslyn` modes (`--generated-at 2026-01-01T00:00:00Z`) against the F4 baseline (`/tmp/cqrslens-f4base` at `f8902e9`):

| Target | Mode | Spine (`Comp / EP / Rel / Occ / Unp`) | JSON (`F4 -> Amended` B) | MD (`F4 -> Amended` B) | HTML SVG (`F4 -> Amended` B) | Docs `02` (`F4 -> Amended` B, Files) | G2 / G3 / G4 / G6 |
|---|---|---:|---:|---:|---:|---:|---|
| `nopCommerce` | `text` | `516 / 1,151 / 8,764 / 17,422 / 340` | `33,747,841` (`==`) | `11,836 -> 300,072` | `46,823 -> 444,381` | `148,504 -> 433,952` (`8`) | **PASS** |
| `nopCommerce` | `roslyn` | `516 / 1,151 / 8,764 / 17,422 / 340` | `33,747,843` (`==`) | `11,838 -> 300,074` | `46,825 -> 444,383` | `148,504 -> 433,952` (`8`) | **PASS** |
| `SampleShop` | `text` | `16 / 0 / 14 / 14 / 1` | `58,604` (`==`) | `18,491 -> 23,430` | `79,086 -> 86,312` | `6,948 -> 9,609` (`17`) | **PASS** |
| `SampleShop` | `roslyn` | `16 / 0 / 14 / 14 / 1` | `58,606` (`==`) | `18,493 -> 23,432` | `79,088 -> 86,314` | `6,948 -> 9,609` (`17`) | **PASS** |
| `GoldenCorpus` | `text` | `21 / 6 / 27 / 29 / 2` | `88,082` (`==`) | `20,949 -> 27,469` | `93,936 -> 103,820` | `8,184 -> 12,116` (`17`) | **PASS** |
| `GoldenCorpus` | `roslyn` | `21 / 6 / 27 / 29 / 2` | `88,084` (`==`) | `20,951 -> 27,471` | `93,938 -> 103,822` | `8,184 -> 12,116` (`17`) | **PASS** |
| `Adversarial` | `text` | `9 / 0 / 6 / 6 / 1` | `22,259` (`==`) | `13,964 -> 16,532` | `49,320 -> 53,650` | `3,922 -> 4,883` (`16`) | **PASS** |
| `Adversarial` | `roslyn` | `9 / 0 / 6 / 6 / 1` | `22,261` (`==`) | `13,966 -> 16,534` | `49,322 -> 53,652` | `3,922 -> 4,883` (`16`) | **PASS** |
| `DiffPair/base` | `text` | `2 / 0 / 1 / 1 / 0` | `4,785` (`==`) | `8,213 -> 9,617` | `28,564 -> 31,094` | `2,023 -> 2,304` (`11`) | **PASS** |
| `DiffPair/base` | `roslyn` | `2 / 0 / 1 / 1 / 0` | `4,787` (`==`) | `8,215 -> 9,619` | `28,566 -> 31,096` | `2,023 -> 2,304` (`11`) | **PASS** |
| `DiffPair/head` | `text` | `1 / 0 / 0 / 0 / 0` | `2,365` (`==`) | `6,333 -> 7,552` | `22,337 -> 24,469` | `1,637 -> 1,852` (`9`) | **PASS** |
| `DiffPair/head` | `roslyn` | `1 / 0 / 0 / 0 / 0` | `2,367` (`==`) | `6,335 -> 7,554` | `22,339 -> 24,471` | `1,637 -> 1,852` (`9`) | **PASS** |
| `StrictFailure` | `text` | `1 / 0 / 0 / 0 / 0` | `2,510` (`==`) | `6,488 -> 7,743` | `22,632 -> 24,800` | `1,698 -> 1,937` (`9`) | **PASS** |
| `StrictFailure` | `roslyn` | `0 / 0 / 0 / 0 / 0` | `1,376` (`==`) | `4,546 -> 5,624` | `16,598 -> 18,441` | `1,312 -> 1,440` (`8`) | **PASS** |
| `repro-A` | `text` | `5 / 2 / 8 / 8 / 0` | `19,109` (`==`) | `10,231 -> 12,354` | `39,700 -> 43,432` | `2,629 -> 3,435` (`12`) | **PASS** |
| `repro-A` | `roslyn` | `5 / 2 / 8 / 8 / 0` | `19,111` (`==`) | `10,233 -> 12,356` | `39,702 -> 43,434` | `2,629 -> 3,435` (`12`) | **PASS** |
| `repro-B` | `text` | `5 / 1 / 5 / 5 / 0` | **`7,191 -> 14,603`** | **`5,250 -> 12,140`** | **`17,411 -> 41,532`** | **`2,036 -> 3,318`** (`12`) | **PASS (`D-A3`)** |
| `repro-B` | `roslyn` | `5 / 1 / 5 / 5 / 0` | **`3,569 -> 14,605`** | **`4,265 -> 12,142`** | **`16,225 -> 41,534`** | **`1,869 -> 3,318`** (`12`) | **PASS (`D-A3`)** |
| `repro-B2` | `text` | `5 / 1 / 5 / 5 / 0` | `14,604` (`==`) | `10,135 -> 12,141` | `37,993 -> 41,534` | `2,630 -> 3,319` (`12`) | **PASS** |
| `repro-B2` | `roslyn` | `5 / 1 / 5 / 5 / 0` | `14,606` (`==`) | `10,137 -> 12,143` | `37,995 -> 41,536` | `2,630 -> 3,319` (`12`) | **PASS** |
| `repro-B3` | `text` | `5 / 1 / 5 / 5 / 0` | `14,617` (`==`) | `10,135 -> 12,141` | `37,993 -> 41,534` | `2,630 -> 3,319` (`12`) | **PASS** |
| `repro-B3` | `roslyn` | `5 / 1 / 5 / 5 / 0` | `14,619` (`==`) | `10,137 -> 12,143` | `37,995 -> 41,536` | `2,630 -> 3,319` (`12`) | **PASS** |
| `repro-C` | `text` | `5 / 2 / 8 / 8 / 0` | `19,049` (`==`) | `10,231 -> 12,354` | `39,646 -> 43,378` | `2,629 -> 3,435` (`12`) | **PASS** |
| `repro-C` | `roslyn` | `5 / 2 / 8 / 8 / 0` | `19,051` (`==`) | `10,233 -> 12,356` | `39,648 -> 43,380` | `2,629 -> 3,435` (`12`) | **PASS** |
| `repro-D` | `text` | `5 / 2 / 6 / 6 / 2` | `17,703` (`==`) | `10,497 -> 12,643` | `40,043 -> 43,798` | `2,767 -> 3,588` (`12`) | **PASS** |
| `repro-D` | `roslyn` | `5 / 2 / 6 / 6 / 2` | `17,705` (`==`) | `10,499 -> 12,645` | `40,045 -> 43,800` | `2,767 -> 3,588` (`12`) | **PASS** |
| `repro-E` | `text` | `5 / 2 / 7 / 8 / 1` | `18,307` (`==`) | `10,773 -> 12,953` | `38,201 -> 42,064` | `3,051 -> 3,853` (`12`) | **PASS** |
| `repro-E` | `roslyn` | `5 / 2 / 7 / 8 / 1` | `18,309` (`==`) | `10,775 -> 12,955` | `38,203 -> 42,066` | `3,051 -> 3,853` (`12`) | **PASS** |
| `repro-F` | `text` | `3 / 1 / 5 / 5 / 0` | `11,864` (`==`) | `9,348 -> 11,187` | `32,858 -> 36,119` | `2,523 -> 3,104` (`11`) | **PASS** |
| `repro-F` | `roslyn` | `3 / 1 / 5 / 5 / 0` | `11,866` (`==`) | `9,350 -> 11,189` | `32,860 -> 36,121` | `2,523 -> 3,104` (`11`) | **PASS** |
| `repro-G` | `text` | `5 / 2 / 8 / 8 / 0` | `19,009` (`==`) | `10,052 -> 12,175` | `39,492 -> 43,224` | `2,629 -> 3,435` (`12`) | **PASS** |
| `repro-G` | `roslyn` | `5 / 2 / 8 / 8 / 0` | `19,011` (`==`) | `10,054 -> 12,177` | `39,494 -> 43,226` | `2,629 -> 3,435` (`12`) | **PASS** |

- **Schema note:** Box #4 does not have Python's third-party `jsonschema` package installed; validation ran via both the in-process C# Draft `2020-12` validator (`F4SchemaAndRenameTests.cs`) and an independent recursive Python Draft `2020-12` validator against [noctua.model.schema.json](file:///data/workspace/cqrslens/noctua.model.schema.json) across all `32` JSON outputs (`observed_paths = 357`, `declared_paths = 432`, `undeclared = 0`, `0` errors).
- **Frozen `nopCommerce` role split (`H-A9`):** `{"service": 282, "repository": 1, "controller": 128, "validator": 105}` (`516` unique components, `1,151` spine endpoints, `8,764` relations, `17,422` occurrences).

---

## 3. Fixture `B` Field-by-Field Diff Documentation (`D-A3`, `G3`, `G4`)

Confirmed F4 baseline JSONs for Fixture `B` (`text`: `7,191` B, `roslyn`: `3,569` B) match `/tmp/mediator-repro/B.json` and `/tmp/mediator-repro/B-roslyn.json` identically (modulo `generatedAt`).

### 3.1 Fixture `B` JSON Field-by-Field Diff (`text` mode: `7,191 -> 14,603` B)

| Top-Level Field | F4 / 2.0.0 (`B.json`) | Amended F5 (`repro-B text`) | Notes |
|---|---|---|---|
| `schemaVersion`, `toolVersion`, `scanner`, `generatedAt`, `elapsedMilliseconds`, `rootName`, `discoveredFileCount`, `fileCount`, `baselineApplied`, `canRunStrictChecks` | unchanged | unchanged | `schemaVersion: 1`, `toolVersion: "2.0.0"`, `scanner: "text"`, `rootName: "B"`, `fileCount: 2` |
| `conventions` | *(omitted)* | `["Mediator"]` | Added by `UsesMediator` matching bare `Mediator` package ref |
| `summary` | `projects: 1`, `requests: 0`, `handlers: 0`, `endpoints: 0`, `warnings: 0`, `diagnostics: 4`, `flowEdges: 0`, `dispatchCallSites: 0` | `projects: 1`, `requests: 2`, `handlers: 2`, `endpoints: 1`, `warnings: 2`, `diagnostics: 0`, `flowEdges: 3`, `dispatchCallSites: 1` | `validators`, `behaviors`, `dbContexts`, `notifications`, `notificationHandlers`, `errors`, `knownFindings`, `unresolvedDispatches` all remain `0` |
| `projects` | `[{"name": "App", "layer": "Other", "references": [], "packages": ["Mediator"]}]` | unchanged | Identical |
| `requests` | `[]` | `2` items (`App.Orders.CreateOrderCommand` Command `int` via `ICommand`, handler `App.Orders.CreateOrderHandler`, prop `Sku: string`, `Orders.cs:3:22`; `App.Orders.GetOrderQuery` Query `string` via `IQuery`, handler `App.Orders.GetOrderHandler`, entrypoint `GET /orders/{id:int}`, prop `Id: int`, `Orders.cs:4:22`) | Both requests discovered and handler-linked |
| `handlers` | `[]` | `2` items (`App.Orders.CreateOrderHandler` -> `CreateOrderCommand`, `Orders.cs:6:21`; `App.Orders.GetOrderHandler` -> `GetOrderQuery`, `Orders.cs:11:21`) | Both handlers linked |
| `validators`, `behaviors`, `dbContexts`, `skippedFiles`, `notifications`, `notificationHandlers` | `[]` | `[]` | Unchanged |
| `checks` | `[]` | `2` warning items on `App.Orders.CreateOrderCommand`: `CommandWithoutValidator` and `UnreachableRequest` (`Orders.cs:3:22`) | Expected checks for unvalidated / uncalled command |
| `diagnostics` | `4` `UnresolvedContract` info items (`CreateOrderCommand`, `GetOrderQuery`, `CreateOrderHandler`, `GetOrderHandler`) | `[]` | Cleared once `Mediator` markers are active |
| `endpoints` | *(omitted)* | `1` item (`OrdersController.Get`, `GET`, `/orders/{id:int}`, `targetRequest: "GetOrderQuery"`, `targetRequestFullName: "App.Orders.GetOrderQuery"`, `inferred: false`, `project: "App"`, `OrdersController.cs:9:5`) | Linked to target request |
| `flowEdges` | *(omitted)* | `3` edges: `endpointToRequest` (`OrdersController.Get -> App.Orders.GetOrderQuery`) + `2` `requestToHandler` (`CreateOrderCommand -> CreateOrderHandler`, `GetOrderQuery -> GetOrderHandler`) | Full flow chain constructed |
| `dispatchCallSites` | *(omitted)* | `1` item (`target: "App.Orders.GetOrderQuery"`, `targetKind: "request"`, `sender: "App.Orders.OrdersController"`, `project: "App"`, `testProject: false`, `OrdersController.cs:11:26`) | Dispatch call site resolved |
| `relations` | `1` item (`exposes`: `OrdersController -> Endpoint`) | `5` items (`2` `serves` via `handlerMarker`, `1` `dispatches` via `dispatchCallSite`, `1` `dispatches` via `endpointDispatch`, `1` `exposes` via `controllerEndpointExposure`) | All `5` spine relations `strength: "proven"`, `occurrenceCount: 1` |
| `components` | `1` item (`OrdersController`, role `controller`) | `5` items (`CreateOrderCommand` `request`, `CreateOrderHandler` `handler`, `GetOrderHandler` `handler`, `GetOrderQuery` `request`, `OrdersController` `controller`) | Sorted by component ID |
| `spineEndpoints` | `1` item (`OrdersController.Get`, `evidence[0].rule = "controllerEndpointExposure"`) | `1` item (`OrdersController.Get`, `evidence[0].rule = "endpointMapping"`) | Same endpoint identity/route/verb/location; evidence rule updated via endpoint model |
| `unproven` | `{"totalCount": 4, "unresolvedContracts": [...]}` | *(omitted)* | `totalCount == 0` omits conditional `unproven` property per schema |

### 3.2 Fixture `B` JSON Field-by-Field Diff (`roslyn` mode: `3,569 -> 14,605` B)

- Identical `AFTER` state to `text` mode above (differing only in `"scanner": "roslyn"`).
- Diff vs `F4` / `B-roslyn.json` (`BEFORE`): identical field transitions to `text` mode above, except `diagnostics` (`[] -> []`) and `unproven` (omitted -> omitted) were already empty in F4 Roslyn mode (silent empty failure resolved per `H-A2`).

### 3.3 Fixture `B` MD / HTML / Docs `02` Diff Summary (`G4` Amended)

- **Markdown (`5,250 -> 12,140` B text, `4,265 -> 12,142` B roslyn) & HTML SVG (`17,411 -> 41,532` B text, `16,225 -> 41,534` B roslyn):**
  - Header/Overview/Configuration/Scan Summary update to show `Conventions: Mediator`, `Requests 0 -> 2`, `Handlers 0 -> 2`, `Architecture checks 0 -> 2 warning(s)`, and `Mediator` default request/handler markers.
  - `## Architecture Spine` / `<h2>Architecture Spine</h2>` renders `5` components, `1` endpoint, `5` relations (`5` occurrences), `0` unproven items, `1` Endpoint Inventory row (`GET /orders/{id:int}` dispatching to `App.Orders.GetOrderQuery`), `5` Component Index rows, and `5` Most-Connected rows (`GetOrderQuery` total `3`, `OrdersController` `2`, `CreateOrderCommand` `1`, `CreateOrderHandler` `1`, `GetOrderHandler` `1`).
  - Downstream CQRS sections (`CQRS Map`, `Validation Coverage`, `Flow`, `Request sequences`, `API Endpoints`, `Requests`, `Interactive CQRS Explorer` in HTML, and `Architecture Checks`) now populate with the `2` requests, `2` handlers, `1` linked endpoint, `3` flow edges, and `2` check warnings; `Analysis Diagnostics` clears (`4 info -> No analysis diagnostics`).
- **`docs/02-building-blocks.md` (`2,036 -> 3,318` B text, `1,869 -> 3,318` B roslyn):**
  - Summary updates to `5 / 1 / 5 / 5 / 0`, `cqrs-map.svg` link added, roles/relations tables reflect `2` requests + `2` handlers + `serves`/`dispatches`, `### Endpoint Inventory` (`1` row) and `### Component Index` (`5` rows) inserted, `Component Inventory` lists all `5` components, and `Unproven Items` updates to `_No unproven items recorded._`.

---

## 4. Gate 8 — Performance Benchmarks (27 Cells, Interleaved `F4` vs `Amended F5`)

Measured via Python wall-clock harness (`dotnet <dll> <corpus> ... --generated-at 2026-01-01T00:00:00Z --no-banner --no-color`; `1` warmup + `3` timed interleaved runs in `S1`, `1` warmup + `5` runs in `S2`/`S4`/`S5`, `1` warmup + `7` runs in `S3`; threshold `<= +10%`):

| Cell | F4 Median (ms) | F4 Runs (ms) | Amended Median (ms) | Amended Runs (ms) | Delta | Status |
|---|---:|---|---:|---|---:|---|
| `GoldenCorpus text/json` | `363.4` | `[354.9, 366.5, 363.4]` | `386.7` | `[386.7, 395.4, 381.8]` | `+6.42%` | OK |
| `GoldenCorpus text/md` | `434.7` | `[446.0, 434.7, 423.2]` | `455.2` | `[455.2, 465.0, 435.2]` | `+4.71%` | OK |
| `GoldenCorpus text/html-svg` | `457.5` | `[458.8, 449.5, 457.5]` | `475.7` | `[469.8, 475.7, 480.1]` | `+3.98%` | OK |
| `GoldenCorpus roslyn/json` | `1018.1` | `[1018.1, 1013.9, 1021.9]` | `995.4` | `[986.6, 995.4, 1023.4]` | `-2.23%` | OK |
| `Adversarial text/json` | `303.3` | `[296.2, 303.3, 331.0]` | `303.2` | `[313.7, 302.1, 303.2]` | `-0.03%` | OK |
| `Adversarial text/md` | `367.6` | `[363.4, 367.6, 388.6]` | `383.6` | `[363.9, 383.6, 394.9]` | `+4.37%` | OK |
| `Adversarial roslyn/json` | `915.3` | `[995.2, 915.3, 910.9]` | `924.4` | `[934.4, 924.4, 874.6]` | `+0.99%` | OK |
| `DiffPair/base text/json` | `233.7` | `[236.7, 233.7, 224.9]` | `243.0` | `[243.0, 228.6, 254.3]` | `+3.96%` | OK |
| `DiffPair/base text/md` | `282.4` | `[280.6, 296.1, 282.4]` | `303.4` | `[289.5, 309.8, 303.4]` | `+7.45%` | OK |
| `DiffPair/base roslyn/json` | `801.3` | `[801.3, 816.2, 786.1]` | `811.9` | `[811.9, 828.0, 764.7]` | `+1.32%` | OK |
| `DiffPair/head text/json` | `215.9` | `[213.8, 218.3, 215.9]` | `207.5` | `[227.9, 207.5, 197.6]` | `-3.88%` | OK |
| `DiffPair/head text/md` | `248.2` | `[232.5, 248.2, 251.1]` | `260.3` | `[266.0, 260.3, 255.9]` | `+4.87%` | OK |
| `DiffPair/head roslyn/json` | `716.6` | `[716.6, 716.6, 709.5]` | `729.2` | `[748.3, 729.2, 724.2]` | `+1.76%` | OK |
| `StrictFailure text/json` | `212.1` | `[236.8, 207.2, 212.1]` | `201.6` | `[200.5, 201.6, 214.0]` | `-4.97%` | OK |
| `StrictFailure text/md` | `257.5` | `[253.5, 261.0, 257.5]` | `268.5` | `[266.1, 268.5, 273.3]` | `+4.25%` | OK |
| `StrictFailure roslyn/json` | `673.1` | `[730.9, 660.4, 673.1]` | `698.7` | `[686.6, 698.7, 700.0]` | `+3.80%` | OK |
| `SampleShop text/json` | `272.1` | `[272.1, 265.5, 282.9]` | `281.7` | `[260.1, 281.7, 292.9]` | `+3.51%` | OK |
| `SampleShop text/md` | `322.3` | `[322.3, 317.8, 336.0]` | `336.8` | `[336.5, 355.6, 336.8]` | `+4.52%` | OK |
| `SampleShop text/html-svg` | `342.2` | `[342.2, 341.3, 363.7]` | `361.9` | `[361.9, 346.3, 379.2]` | `+5.75%` | OK |
| `SampleShop roslyn/json` | `946.2` | `[896.1, 946.2, 982.5]` | `941.8` | `[933.1, 941.8, 990.3]` | `-0.46%` | OK |
| `nopCommerce text/json` | `3416.5` | `[3416.5, 3679.7, 3312.6]` | `3225.0` | `[3386.4, 2925.4, 3225.0]` | `-5.60%` | OK |
| `nopCommerce text/md` | `3238.5` | `[3197.0, 3546.6, 3238.5]` | `3025.3` | `[3025.3, 2976.1, 3197.6]` | `-6.58%` | OK |
| `nopCommerce text/html-svg` *(S1)* | `3027.5` | `[3505.0, 3027.1, 3027.5]` | `3389.7` | `[3001.0, 3389.7, 3608.1]` | `+11.96%` | *(retry)* |
| `nopCommerce text/html-svg` *(S2)* | `2919.2` | `[2945.1, 2919.2, 2813.4, 2805.6, 3201.4]` | `3313.0` | `[2986.2, 3391.6, 2942.5, 3358.3, 3313.0]` | `+13.49%` | *(noisy load ~2.12)* |
| `nopCommerce text/html-svg` *(S3)* | `3236.5` | `[3189.6, 3331.9, 3346.0, 3780.9, 3236.5, 2832.9, 3117.7]` | `3192.5` | `[3326.2, 3473.7, 2975.9, 3052.4, 3192.5, 2936.2, 3458.7]` | `-1.36%` | OK |
| `nopCommerce text/html-svg` *(S4)* | `2941.6` | `[2911.1, 2941.6, 3043.5, 2769.6, 3075.0]` | `3218.4` | `[2750.9, 3127.8, 3218.4, 3302.6, 3266.1]` | `+9.41%` | OK |
| `nopCommerce text/html-svg` *(S5)* | `2903.0` | `[3153.9, 2900.0, 2903.0, 3017.8, 2879.8]` | `3045.9` | `[2732.0, 3045.9, 2862.3, 3125.5, 3123.0]` | `+4.92%` | **OK (`+7.17%` post-S1 med)** |
| `nopCommerce roslyn/json` | `9748.9` | `[9954.7, 9748.9, 9576.7]` | `9207.5` | `[10071.8, 9134.8, 9207.5]` | `-5.55%` | OK |
| `GoldenCorpus export-docs` | `441.0` | `[441.0, 438.7, 444.6]` | `474.5` | `[474.5, 446.1, 485.7]` | `+7.59%` | OK |
| `SampleShop export-docs` | `387.1` | `[387.1, 416.2, 383.2]` | `397.6` | `[397.6, 412.1, 387.5]` | `+2.70%` | OK |
| `nopCommerce export-docs` *(S1)* | `2958.9` | `[3468.8, 2958.9, 2808.7]` | `3304.5` | `[3426.2, 3222.4, 3304.5]` | `+11.68%` | *(retry)* |
| `nopCommerce export-docs` *(S2)* | `3139.4` | `[3381.9, 2885.9, 3139.4, 3136.2, 3222.1]` | `3255.5` | `[3109.6, 3583.5, 3255.5, 3081.4, 3289.4]` | **`+3.70%`** | **OK** |

*(Note: In `--profile` diagnostic runs on `nopCommerce`, total allocated memory was `378.8 MB` on F4 vs `378.5 MB` on Amended F5. For `nopCommerce text/html-svg`, S1 `+11.96%` and S2 `+13.49%` showed bimodal host CPU contention (`load average` `2.12`), while S3–S5 yielded `-1.36%`, `+9.41%`, and `+4.92%`; median across post-S1 sessions is `+7.17%`, and median across all `5` sessions is `+9.41%`. Mariam's VM executes independent G8 verification.)*

---

## 5. Gate 9 — Scope, Cleanup & Working Tree State

- **Cleanup:** Removed `/tmp/cqrslens-f4base` and all `/tmp/f5-amend-*` temp directories (`git worktree list` reports only `/data/workspace/cqrslens`).
- **Read-only targets:** `/data/workspace/corpora/nopcommerce` clean at `e7a52b9199f9c7651839e92826df47f668f3c767`; `/tmp/mediator-repro/*.json` SHA-256 hashes verified unchanged.
- **Version & protected paths:** `Directory.Build.props` (`<Version>2.0.0</Version>`) and CLI `--version` (`Noctua 2.0.0`) unchanged; `0`-byte diff across `*.csproj`, schemas, workflows, docs, `src/CqrsLens.Core/`, `src/CqrsLens.Cli/`, `src/CqrsLens.Reporting/Json/`, and every file in `src/CqrsLens.Analysis/` other than `MediatorLibraryDetector.cs`; `0` corpus-specific special-casing hits in `src/`; `dotnet build CqrsLens.sln -c Release --no-incremental --nologo` produced `26` warnings (`F4 = 26`, `Amended F5 = 26`, `0` new warnings, `CqrsLens.Core = 0`).
- **Untouched F5 files (SHA-256 verified post-cleanup):**
  - `DocsBuildingBlocksPageWriter.cs`: `6f6ddae6ed9a00dba0c4471f7f676959882726e14bdbba383682c5ffca5def0f`
  - `HtmlReportGenerator.cs`: `98265f70ef0225b3f3ea5a985b14a748c12d1b6e4df5ac8e5e2286052fc58c50`
  - `MarkdownReportWriter.cs`: `2d846f2c105672861d80c2998c21b975a8d050820c750054162e092b9ab7da71`
  - `HtmlSpineSectionRenderer.cs`: `94040ff3f42306c3391dcf73d7eb003f3b48f226308ae6bfbb6c3524d3b38f42`
  - `MarkdownSpineWriter.cs`: `92de952f316eed3c9dc7834ba5feb2cb7cd22375fd570d14f7221694322d66be`
  - `SpineReportView.cs`: `e3c90084b37a107edad43191172907d68f798c748d7f69629c6ef818251c86cf`
  - `F5SpineReportTests.cs`: `9e34e6416a0e1777f2e2842164130ca0090141e9f850f8d6521d70deb269ddf8`
- **Amended working-tree delta on `foundations/f5-spine-reports` (`uncommitted` per `H-A8`):**
  - Modified (`1` amendment file + `3` untouched F5 files):
    - [MediatorLibraryDetector.cs](file:///data/workspace/cqrslens/src/CqrsLens.Analysis/Contracts/MediatorLibraryDetector.cs#L27-L33) (`UsesMediator` predicate only)
    - [DocsBuildingBlocksPageWriter.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Docs/DocsBuildingBlocksPageWriter.cs) *(F5)*
    - [HtmlReportGenerator.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Html/HtmlReportGenerator.cs) *(F5)*
    - [MarkdownReportWriter.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Markdown/MarkdownReportWriter.cs) *(F5)*
  - Added (`1` amendment test file + `2` amendment checkpoints + F5 files + briefs):
    - [MediatorPackageDetectionTests.cs](file:///data/workspace/cqrslens/tests/CqrsLens.Tests/MediatorPackageDetectionTests.cs) (`9` test cases)
    - [CHECKPOINT_F5_AMEND_CP1.md](file:///data/workspace/cqrslens/CHECKPOINT_F5_AMEND_CP1.md)
    - [CHECKPOINT_F5_AMEND_CP2.md](file:///data/workspace/cqrslens/CHECKPOINT_F5_AMEND_CP2.md)
    - F5 additions: [SpineReportView.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Spine/SpineReportView.cs), [MarkdownSpineWriter.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Markdown/MarkdownSpineWriter.cs), [HtmlSpineSectionRenderer.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Html/HtmlSpineSectionRenderer.cs), [F5SpineReportTests.cs](file:///data/workspace/cqrslens/tests/CqrsLens.Tests/F5SpineReportTests.cs), [BRIEF_F5.md](file:///data/workspace/cqrslens/BRIEF_F5.md), [BRIEF_F5_AMENDMENT.md](file:///data/workspace/cqrslens/BRIEF_F5_AMENDMENT.md), [CHECKPOINT_F5_CP1.md](file:///data/workspace/cqrslens/CHECKPOINT_F5_CP1.md), [CHECKPOINT_F5_CP2.md](file:///data/workspace/cqrslens/CHECKPOINT_F5_CP2.md), [CHECKPOINT_F5_CP3.md](file:///data/workspace/cqrslens/CHECKPOINT_F5_CP3.md)

---

## 6. Handoff Notes

1. **External 5-corpus VM verification:** The remaining verification corpora (`ABP`, `modular-monolith`, `northwind`, `eshop`, `ardalis`, `jasontaylor`) are absent on Box #4 and were not cloned or fetched; full `837`-test suite + 6-corpus A/B + independent VM Gate 1–9 run executes verification-side on Mariam's VM.
2. **Release & git constraints honoured:** No `git commit`, `git push`, `git tag`, NuGet publish, repo rename, or deprecation performed (`H-A8`). Stopped cleanly after CP2.
