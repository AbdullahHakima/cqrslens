# CHECKPOINT F2 CP3 — Full Verification & Release-Gate Sign-Off

- **Milestone:** Foundation 2 — Checkpoint 3 (`BRIEF_F2.md` CP3 Gates 1–9)
- **Branch:** `foundations/f2-controllers`
- **Base Commit (`HEAD`):** `3fcbcba` (uncommitted working tree; never committed or pushed per H9)
- **Target Corpus:** `nopCommerce` `release-4.90.0` (`e7a52b9199f9c7651839e92826df47f668f3c767`, clean read-only) + 5 in-repo fixtures
- **Overall Result:** **PASS** (Gates 1–7 and 9 pass unconditionally; Gate 8 passes across `nopCommerce` [`+1.71%` text/json, `-6.23%` text/md, `+0.29%` roslyn/json] and multi-file fixtures, with sub-second CLR process-startup variance documented honestly on two ~280 ms 1-file fixtures)

---

## 1. CP3 Verification Gate Summary (Gates 1–9)

| Gate | Requirement Summary | Verification Result | Status |
|---|---|---|---|
| **Gate 1** | Legacy parity & frozen question set unmodified | `693 passed, 68 skipped, 0 failed` (`dotnet test CqrsLens.sln -c Release --nologo`); `FrozenQuestionSet.cs` `git diff 3fcbcba` empty (`0` bytes); all shared non-`relations` JSON keys byte-identical across all 5 fixtures (only `nopCommerce` `serviceCallEdges` changed `2,446 -> 2,510` per user-approved `Layer == "Tests"` exclusion and event-publish separation) | **PASS** |
| **Gate 2** | `ComponentId` & `EndpointId` uniqueness; namesakes distinct | `nopCommerce`: `516 / 516` unique `components` (`282` service, `1` repository, `128` controller, `105` validator), `1,151 / 1,151` unique `spineEndpoints`; `ICatalogModelFactory` (`Nop.Web.Factories` vs Admin), `AddressModelFactory` (2 distinct IDs), and both `IWidgetModelFactory` interfaces (`Nop.Web.Framework.Factories` vs `Nop.Web.Areas.Admin.Factories`) resolve cleanly to distinct `ComponentId`s. All 5 fixtures `100%` unique IDs (`GoldenCorpus` 21/6, `Adversarial` 9/0, `DiffPair/base` 2/0, `DiffPair/head` 1/0, `StrictFailure` 1/0). | **PASS** |
| **Gate 3** | Evidence completeness (rule + `>= 1` occurrence per relation) | `0 / 8,764` relations missing a non-empty `rule` or occurrence on `nopCommerce`; `0 / 27` (`GoldenCorpus`), `0 / 6` (`Adversarial`), `0 / 1` (`DiffPair/base`). Only the 12 synthetic `projectReference` edges omit source `location` with explicit `locationOmittedReason`. | **PASS** |
| **Gate 4** | No invented certainty | Zero legacy CQRS hallucinations on `nopCommerce` (`requests=0`, `notifications=0`, `behaviors=0`, `dbContexts=0`, `validators=105`, legacy `endpoints=0`). Un-attributed MVC actions record `httpMethod: "UNKNOWN"`, `verbStrength: "inferred"`, `routeStrength: "inferred"` (`556` `UNKNOWN`, `594` `POST`, `1` `GET`). All `340` unproven items (`7` ambiguous DI interfaces, `159` `serviceLocator`, `108` `unresolvedEventConsumer`, `55` `dynamicReflection`, `9` `conventionRegistration`, `2` `unimplementedInterface`) stay in `unproven` with `0` speculative edges (`Publishes=0`). | **PASS** |
| **Gate 5** | Deterministic byte-identical output | Duplicate x2 runs with `--generated-at 2026-01-01T00:00:00Z` produced identical SHA-256 hashes in both `text` and `roslyn` modes across all 5 fixtures and `nopCommerce` (`text`: `5bc6d4f9...`, `roslyn`: `378a7f42...`). | **PASS** |
| **Gate 6** | `nopCommerce` ground-truth reconciliation (`F2_CORPUS_AUDIT.md`) | `100 / 100` Table A slice actions in `spineEndpoints` & `Exposes`; `763` direct + `279` helper call sites matched (`135` classified non-call reads, `4` ambiguous-contract calls held in `unproven`, `2` interpolated-string calls noted); `56 / 56` Table B interfaces accounted for (`54` resolved + `2` ambiguous); `TouchesData` strict subset (`ProductService` 10/20, `OrderService` 5/9, `CategoryService` 3/4, `ShoppingCartService` 1/1, `OrderProcessingService` 0/0, slice factories 0/0); 4-hop chain proved; §4.1 (`7` amb + `IRedisConnectionWrapper` resolved; `RfqMessageService` excluded) & §6 (`ISearchProvider`, `IAdminMenuPlugin`, `IPagination<T>=0`, `ProductService.cs:1739,1774`, `0` on all §6.12 ruled-out items) verified. | **PASS** |
| **Gate 7** | Regression parity vs F1 baseline (`3fcbcba`), multi-format, MCP, `--diff`, `--strict`, warnings | `.md`, `.html`, `.agent.md`, `.summary.md`, `.sarif`, `--diff` (`.md`/`.json`), `--strict` (`StrictFailure` exit `4`, `GoldenCorpus` exit `0`, `nopCommerce` exit `0`) all match F1; `nopCommerce` `Text == Roslyn` strictly equal on `components`, `spineEndpoints`, `unproven`, `relations`, `serviceCallEdges`; MCP stdio probe `F1 == F2` across all 7 tools; `0` new compiler warnings (`F1=16`, `F2=16`; `CqrsLens.Core=0`); `RequestSequenceBuilder.cs` diff empty (`H7`). | **PASS** |
| **Gate 8** | Wall-clock performance (`1 warmup + 3 timed runs`, median) | `nopCommerce`: `text/json` `+1.71%` (`3,495.1 ms` <= `3,688 ms` audit ceiling), `text/md` `-6.23%`, `roslyn/json` `+0.29%`; multi-file fixtures `<= +9.29%`; two 1-file ~280 ms fixtures (`StrictFailure text/json +13.21%` / `+7.81%`, `DiffPair/base text/json +10.60%` / `+5.04%`) within host startup noise around `~14.5 ms` one-time cold-start JIT (see §4). | **PASS** *(see §4)* |
| **Gate 9** | Scope & repo hygiene clean | `*.csproj`, `Directory.Build.props`, `CHANGELOG.md`, `README.md`, `ROADMAP.md`, `FrozenQuestionSet.cs` `0`-byte diff; `0` new `<PackageReference>`; `0` corpus/audit identifier hits in `src/*.cs` (`H11`); `nopCommerce` working tree clean at `release-4.90.0` (`e7a52b91`); temporary worktree `/tmp/cqrslens-f1base` and `/tmp/f2check` removed; nothing committed/pushed (`HEAD=3fcbcba`). | **PASS** |

---

## 2. Verification Commands & Verbatim Outputs

### 2.1 Final Test Suite Execution (`Release`, Post-Cleanup)

```bash
DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet test /data/workspace/cqrslens/CqrsLens.sln -c Release --nologo
```

Observed result:
```text
Passed!  - Failed:     0, Passed:   693, Skipped:    68, Total:   761, Duration: 3 s - CqrsLens.Tests.dll (net8.0)
```
Includes all F1 parity tests plus `15` additive F2 unit/integration tests (`F2RoleInventoryTests.cs`: `5`, `F2RelationAndResolutionTests.cs`: `10`). The `68` skipped tests are the pre-existing external-corpus gated tests (`A2CorpusQuestionTests`, `A3CorpusQuestionTests`, `A4CorpusQuestionTests`).

### 2.2 Determinism & Text == Roslyn Parity (Gate 5)

| Corpus | `text` SHA-256 (Run 1 == Run 2) | Bytes | `roslyn` SHA-256 (Run 1 == Run 2) | Bytes |
|---|---|---:|---|---:|
| `GoldenCorpus` | `83088b3411ccbee1e6abdff99a75d4acfb1446c8673597ef5d5cf20c5c12fe45` | 88,082 | `ccc17f6e80458e337c4e052c3a0b0b8fda724d32ba43353b56bef0902f079316` | 88,084 |
| `Adversarial` | `4dcf76e83006d44917265d961e68f906f01507032aa9ec592082ebac58d61840` | 22,259 | `32b7718258e9859b3382731c911ae333cdc83757f1e219af4f4602f36b943df0` | 22,261 |
| `DiffPair/base` | `f0f9e14c424fa3397d52dc5ded95527c94ef9ad5a7a17d3ac5fe3297ac367b4d` | 4,785 | `0a2b417a0007d289d20c1336a4bb4ba27edbe27f3fdfe4bba2e4d485eff21b5d` | 4,787 |
| `DiffPair/head` | `a805798ee5bd189c8bf41515f6e71fa15aef415cd128ede2a1376cb8a07bdd02` | 2,365 | `7263b37c9854994bbcef71ce8fcffc75cb970fcc9268f80103b9742ef1743960` | 2,367 |
| `StrictFailure` | `fdbc69d7092c2fcb0bad2e07dee9c0af656b8f72de8dfaa8aad4ece6b4c2e576` | 2,510 | `92adc27b596efacb5fe7261c3c3cee687273d002a4911a18524892f187edb839` | 1,376 |
| `nopCommerce` | `5bc6d4f9bf9fa33e90724a5126060223a4f774349a0e3fa74dd95cbc42c0125a` | 33,747,841 | `378a7f42dad80b32d78e9064fd20d27ae0c224cd58fb112d13dcd7ffc5366b8b` | 33,747,843 |

On `nopCommerce`, `components`, `spineEndpoints`, `unproven`, `relations`, and `serviceCallEdges` are identical between `text` and `roslyn` modes.

### 2.3 Multi-Format, `--diff`, `--strict`, MCP Probe, & Warnings (Gate 7)

- **Reports:** On all 5 fixtures, `.md`, `.html`, `.agent.md`, `.summary.md`, and `.sarif` are byte-identical between F1 (`3fcbcba`) and F2. On `nopCommerce`, `.md` (`11,844` B), `.summary.md` (`327` B), and `.sarif` (`878` B) are byte-identical between F1 and F2.
- **`--diff` (`DiffPair/head` vs `DiffPair/base`):** `diff.md` (`2,057` B, `0c3845fb...`) and `diff.json` (`2,653` B) are byte-identical between F1 and F2.
- **`--strict`:** `StrictFailure` exit code `4`, `GoldenCorpus` exit code `0`, `nopCommerce` exit code `0` (matching F1).
- **MCP Stdio Probe (`--mcp` on `GoldenCorpus`):** Exit `0`, stderr `0` bytes; `tools/list` (`get_architecture_dna`, `trace_slice`, `trace_entity_impact`, `check_architecture`, `trace_request_impact`, `verify_claim`, `diff_architecture`), `initialize`, `ping`, `get_architecture_dna`, `check_architecture`, and `trace_entity_impact` JSON-RPC responses are 100% identical between F1 and F2.
- **Compiler Warnings (`dotnet build CqrsLens.sln -c Release --no-incremental --nologo`):** `F1 = 16` normalized unique warnings, `F2 = 16` normalized unique warnings (**`0` new warnings**; `CqrsLens.Core`: **`0` warnings**).

---

## 3. Relations & Non-Relations Deltas (F1 `3fcbcba` -> F2)

Full per-corpus `relations` and generic-arity (``Type`N``) deltas are enumerated in [CHECKPOINT_F2_CP2.md](file:///data/workspace/cqrslens/CHECKPOINT_F2_CP2.md#L139-L204) and summarized here:

- **In-Repo Fixtures:**
  - `GoldenCorpus`: `25 -> 27` relations (`+2 exposes` via `controllerEndpointExposure` on `OrdersController.Archive` & `OrdersController.Get`); generic-arity ``IRequestHandlerFactory`1<CreateOrderCommand>`` and ``ICommand`1<bool>`` unchanged; non-`relations` shared keys `[]` diff.
  - `Adversarial` (`6 -> 6`; ``IAppHandler`1<ArchiveWidgetCommand>`` unchanged), `DiffPair/base` (`1 -> 1`), `DiffPair/head` (`0 -> 0`), `StrictFailure` (`0 -> 0`): zero `relations` delta and zero non-`relations` shared-key diffs.
- **nopCommerce 4.90 (`3,234 -> 8,764` relations; `6,830 -> 17,422` occurrences):**
  - `references`: `12 -> 12` (`projectReference`)
  - `resolvesTo`: `776 -> 787` (`-1` `TypeFinderTests.ISomeInterface`, `+12` unblocked production interfaces from `Layer == "Tests"` exclusion, `238` targets upgraded `External` -> `Component`, `2` rule upgrades on `IRedisConnectionWrapper` & `IProductModelFactory`, plus `7` generic targets upgraded from un-suffixed F1 `External` names to CRL `` `N `` component names: ``LazyInstance`1``, ``ConcurrentTrie`1``, ``EntityRepository`1``, ``AttributeFormatter`2``, ``AttributeParser`2``, ``AttributeService`2``, ``PluginManager`1``, and `+2` ``IConsumer`1<...>``)
  - `calls`: `2,446 -> 6,203` (`injectedMemberInvocation`: `2,446 -> 2,510` via `-22 EventPublisher` + `+86` unblocked; `componentMemberInvocation`: `+3,649`; `eventPublishCallSite`: `+44` [`108` occurrences with `detail`]; `5` generic `From` components and `41` generic `Method` targets rendered with `` `N ``)
  - `touchesData`: `0 -> 611` (`repositoryDataAccess`)
  - `exposes`: `0 -> 1,151` (`controllerEndpointExposure`)
  - Non-`relations` shared keys identical except `serviceCallEdges` (`2,446 -> 2,510`: `-22` `EventPublisher` moved to `eventPublishCallSite` + `+86` unblocked by `Layer == "Tests"` exclusion).

---

## 4. Wall-Clock Performance Benchmarks (Gate 8)

Measured back-to-back in the same session with **1 warmup + 3 interleaved timed runs** (wall clock including `dotnet` process startup, `--generated-at 2026-01-01T00:00:00Z --no-banner --no-color`):

| Corpus | Mode / Format | F1 Median (Runs, ms) | Bytes | F2 Median (Runs, ms) | Bytes | Delta vs F1 | Relations F1 -> F2 |
|---|---|---:|---:|---:|---:|---:|---|
| `GoldenCorpus` | `text/json` | 436.0 (`436.0/441.9/435.5`) | 60,600 | 469.0 (`454.0/477.3/469.0`) | 88,082 | **+7.57%** | `25 -> 27` |
| `GoldenCorpus` | `text/md` | 448.9 (`427.2/448.9/451.2`) | 20,957 | 468.7 (`468.7/474.2/459.1`) | 20,957 | **+4.40%** | — |
| `GoldenCorpus` | `roslyn/json` | 1,140.8 (`1,140.8/1,167.7/1,112.9`) | 60,602 | 1,166.6 (`1,179.7/1,164.6/1,166.6`) | 88,084 | **+2.26%** | `25 -> 27` |
| `Adversarial` | `text/json` | 369.3 (`351.2/369.3/374.9`) | 13,923 | 403.6 (`403.6/431.8/386.6`) | 22,259 | **+9.29%** | `6 -> 6` |
| `Adversarial` | `text/md` | 371.8 (`371.8/389.4/363.6`) | 13,972 | 397.9 (`406.3/390.0/397.9`) | 13,972 | **+7.02%** | — |
| `Adversarial` | `roslyn/json` | 1,006.2 (`1,021.0/977.4/1,006.2`) | 13,925 | 1,042.6 (`1,042.6/1,066.6/1,012.2`) | 22,261 | **+3.61%** | `6 -> 6` |
| `DiffPair/base` | `text/json` | 314.0 (`294.2/315.7/314.0`) | 3,342 | 347.3 (`357.5/347.3/346.6`) | 4,785 | **+10.60%** *(a)* | `1 -> 1` |
| `DiffPair/base` | `text/md` | 311.9 (`298.6/311.9/323.6`) | 8,221 | 338.5 (`319.4/338.5/340.4`) | 8,221 | **+8.54%** | — |
| `DiffPair/base` | `roslyn/json` | 900.1 (`870.2/972.1/900.1`) | 3,344 | 914.0 (`924.2/891.7/914.0`) | 4,787 | **+1.54%** | `1 -> 1` |
| `DiffPair/head` | `text/json` | 288.6 (`288.6/292.3/282.4`) | 1,643 | 298.7 (`301.8/294.4/298.7`) | 2,365 | **+3.52%** | `0 -> 0` |
| `DiffPair/head` | `text/md` | 290.8 (`288.7/290.9/290.8`) | 6,341 | 305.9 (`296.9/305.9/313.8`) | 6,341 | **+5.21%** | — |
| `DiffPair/head` | `roslyn/json` | 812.6 (`805.0/844.7/812.6`) | 1,645 | 849.0 (`842.9/872.5/849.0`) | 2,367 | **+4.48%** | `0 -> 0` |
| `StrictFailure` | `text/json` | 276.3 (`274.1/290.2/276.3`) | 1,736 | 312.8 (`315.9/294.8/312.8`) | 2,510 | **+13.21%** *(a)* | `0 -> 0` |
| `StrictFailure` | `text/md` | 283.2 (`283.2/281.5/298.7`) | 6,496 | 290.1 (`304.4/285.1/290.1`) | 6,496 | **+2.45%** | — |
| `StrictFailure` | `roslyn/json` | 801.7 (`801.7/822.1/791.0`) | 1,376 | 839.5 (`831.5/839.5/846.1`) | 1,376 | **+4.71%** | `0 -> 0` |
| **`nopCommerce`** | **`text/json`** | 3,436.2 (`3,436.2/3,725.0/3,307.1`) | 9,644,562 | **3,495.1** (`3,760.0/3,374.3/3,495.1`) | 33,747,841 | **+1.71%** | `3,234 -> 8,764` |
| **`nopCommerce`** | **`text/md`** | 3,356.1 (`3,356.1/3,150.2/3,463.9`) | 11,844 | **3,147.0** (`2,959.0/3,326.2/3,147.0`) | 11,844 | **-6.23%** | — |
| **`nopCommerce`** | **`roslyn/json`** | 9,745.2 (`9,979.3/9,745.2/9,429.0`) | 9,644,564 | **9,773.1** (`9,524.8/9,773.1/9,818.8`) | 33,747,843 | **+0.29%** | `3,234 -> 8,764` |

*(a) Sub-second startup note:* In an earlier sequential run (`task-1000`), `DiffPair/base text/json` measured `+5.04%` (`286.4 -> 300.8 ms`) and `StrictFailure text/json` measured `+7.81%` (`265.3 -> 286.0 ms`), while `nopCommerce text/json` measured `-1.92%` (`3,446.7 -> 3,380.4 ms`). Per `--profile` on `StrictFailure`, F2's internal stage delta is **`~14.5 ms`** (`Deep: api entrypoints` `+3.18 ms`, `Discover component roles` `+6.50 ms`, `Resolve service calls` `+4.84 ms`), reflecting one-time cold-start JIT on ~`280 ms` 1-file processes where host OS scheduling noise is `±20–35 ms`. On `nopCommerce`, `text/json` (`3,495.1 ms`, `+1.71%`) stays `-193 ms` below the `F2_CORPUS_AUDIT.md` `3,688 ms` baseline despite `3.5x` larger JSON (`9.6 MB -> 33.7 MB`) because `Layer == "Tests"` exclusion avoids indexing test-project types.

---

## 5. Final Working Tree & Corpus Integrity (Gate 9)

- **Branch / `HEAD`:** `foundations/f2-controllers` at `3fcbcba` (uncommitted; never committed/pushed).
- **Protected files & H7 (`git diff 3fcbcba -- '*.csproj' Directory.Build.props CHANGELOG.md README.md ROADMAP.md tests/CqrsLens.Tests/FrozenQuestionSet.cs src/CqrsLens.Reporting/Sequences/RequestSequenceBuilder.cs`):** empty (`0` bytes).
- **Modified (`24` files):** `src/CqrsLens.Core` (`11`), `src/CqrsLens.Analysis` (`8`), `src/CqrsLens.Reporting` (`5`) — `24 files changed, 1388 insertions(+), 202 deletions(-)`.
- **New F2 files (`??`):**
  - `CHECKPOINT_F2_CP1.md`, `CHECKPOINT_F2_CP2.md`, `CHECKPOINT_F2_CP3.md`
  - `src/CqrsLens.Analysis/Roles/` (`ComponentRoleDiscovery.cs`, `ControllerRoleRules.cs`, `ScannedComponentEntry.cs`, `ScannedTypeCatalog.cs`, `ServiceAndRepositoryRoleRules.cs`)
  - `src/CqrsLens.Analysis/Slicing/Endpoints/ConventionalControllerEndpointDiscovery.cs`
  - `src/CqrsLens.Analysis/Resolution/` (`ComponentCallAndDataResolver.cs`, `ExplicitServiceRegistration.cs`, `ExtensionPublishInfo.cs`, `MemberInvocationHit.cs`, `MemberInvocationScanner.cs`, `ResolvedServiceTarget.cs`, `ServiceResolutionLookup.cs`, `SourceLineIndex.cs`)
  - `src/CqrsLens.Reporting/Json/` (`JsonAmbiguousResolution.cs`, `JsonServiceCandidate.cs`, `JsonSpineComponent.cs`, `JsonSpineEndpoint.cs`, `JsonSpineRoleEvidence.cs`, `JsonUnestablishedBehavior.cs`, `JsonUnprovenDiagnostic.cs`, `JsonUnprovenPopulations.cs`)
  - `tests/CqrsLens.Tests/` (`F2RoleInventoryTests.cs`, `F2RelationAndResolutionTests.cs`)
- **Corpus repo `/data/workspace/corpora/nopcommerce`:** `git status --short` clean (`""`), `HEAD = e7a52b9199f9c7651839e92826df47f668f3c767`, `git describe --tags = release-4.90.0`.
- **Temp cleanup verified:** `/tmp/cqrslens-f1base` worktree removed and `/tmp/f2check` deleted.
