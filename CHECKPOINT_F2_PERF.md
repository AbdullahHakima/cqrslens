# CHECKPOINT — F2 Perf Fix

- **Branch:** `foundations/f2-controllers`
- **Base Commit (`HEAD`):** `3fcbcba` (F1 spine commit; uncommitted working tree, never committed or pushed)
- **Driver / Model:** Antigravity CLI on box #4 (Orchestrator Argon planner / Hydrogen implementer)
- **Target Scope:** Pure optimization of F2 cold-start/JIT overhead and per-scan allocations with **100% byte-identical outputs** across all fixtures, `nopCommerce`, analysis modes, report formats, `--diff`, `--strict`, and `--mcp`.

---

## 1. Hotspots Found & Profiling Evidence (Before vs. After)

### 1A. Cold-Start / JIT Overhead (1-File Fixtures)

Cold-process profiling (`dotnet <probe> <corpus>`, single-run cold process against F1 `3fcbcba` vs. F2 pre-opt vs. F2 post-opt) plus warmed CLI `--profile` medians (1 warmup + 3 runs) isolated the fixed **~21–33 ms** F2 overhead into two buckets:

#### 1A-i. JSON Projection & Serialization Cold-Start (`+6–18 ms` over F1; `~69–91 ms` total pre-opt)
Measured in isolated single-process probes on `DiffPair/base`, `StrictFailure`, `DiffPair/head`, `Adversarial`, and `GoldenCorpus`:

| Probe Metric (`text/json`) | `F1 (3fcbcba)` | `F2 Pre-Opt` | `F2 Post-Opt` | Delta (`Post` vs `Pre`) | Delta (`Post` vs `F1`) |
| --- | ---: | ---: | ---: | ---: | ---: |
| `DiffPair/base` `JsonReportGenerator.Generate` | `68.18 ms` | `69.86 ms` | **`15.44 ms`** | **`-54.42 ms`** | **`-52.74 ms`** |
| `DiffPair/head` `JsonReportGenerator.Generate` | `55.52 ms` | `72.36 ms` | **`14.04 ms`** | **`-58.32 ms`** | **`-41.48 ms`** |
| `StrictFailure` `JsonReportGenerator.Generate` | `56.14 ms` | `69.11 ms` | **`15.66 ms`** | **`-53.45 ms`** | **`-40.48 ms`** |
| `Adversarial` `JsonReportGenerator.Generate` | `59.87 ms` | `78.25 ms` | **`18.42 ms`** | **`-59.83 ms`** | **`-41.45 ms`** |
| `GoldenCorpus` `JsonReportGenerator.Generate` | `83.08 ms` | `91.18 ms` | **`26.77 ms`** | **`-64.41 ms`** | **`-56.31 ms`** |

Pre-opt cold-process decomposition inside `JsonReportGenerator.Generate` on `DiffPair/base` (`StrictFailure`):
1. `JsonReportSerializerContext.Default.JsonReportDocument` metadata initialization ([JsonReportSerializerContext.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Json/JsonReportSerializerContext.cs), [JsonReportDocument.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Json/JsonReportDocument.cs)): **`39.89 ms`** (`40.96 ms` on `StrictFailure`, `39.79 ms` on `GoldenCorpus`) — F2 added `Components`, `SpineEndpoints`, and `Unproven` DTO graphs directly to `JsonReportDocument` without direct streaming, pulling 19 DTO/list types into STJ source-gen metadata initialization.
2. `JsonSpineNames..cctor` ([JsonSpineNames.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Json/JsonSpineNames.cs)): **`10.35 ms`** — eager static initialization of 93 `JsonEncodedText.Encode(...)` fields triggered on every JSON report (even `0`-relation fixtures) as soon as `JsonReportDocumentFactory.BuildComponentsField` called `JsonSpineNames.ComponentRoleName`.
3. `JsonReportDocumentFactory.CreateForSerialization` ([JsonReportDocumentFactory.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Json/JsonReportDocumentFactory.cs)) & `SpineQueries` ([SpineQueries.cs](file:///data/workspace/cqrslens/src/CqrsLens.Core/Spine/SpineQueries.cs)): **`20.91 ms`** total (`18.90 ms` on `StrictFailure`), including **`8.65 ms`** inside `SpineQueries` (`FlowEdges` `4.47 ms`, `ResolvedEdges` `1.88 ms`, `ServiceCallEdges` `0.59 ms`, `DispatchCallSites` `1.71 ms`) instantiating LINQ `GroupBy` + `OrderBy` + `ThenBy` enumerables over empty/singleton lists.

#### 1A-ii. Analyzer Resolver & Role-Discovery Cold-Start (`~13–15 ms` internal delta pre-opt)
Measured via cold-process stage timing and warmed CLI `--profile` (1 warmup + 3 timed runs median):

| Stage (`DiffPair/base`, `text/json`) | `F1` (CLI `--profile`) | `F2 Pre-Opt` (cold / CLI) | `F2 Post-Opt` (CLI `--profile` / cold) | Post vs Pre |
| --- | ---: | ---: | ---: | ---: |
| `Resolve services` (`ServiceResolutionIndexBuilder`) | `6.85 ms` (`11.7 KB`) | `7.28 ms` / `6.87 ms` | **`1.16 ms`** (`8.0 KB`) / `1.41 ms` | **`-5.71 ms`** |
| `Discover component roles` (`Roles/` + `ConventionalControllerEndpointDiscovery`) | `0.00 ms` | `7.26 ms` / `6.95 ms` | **`2.01 ms`** (`0.0 KB`) / `1.78 ms` | **`-4.94 ms`** |
| `Resolve service calls` (`ServiceCallEdgeResolver` + `ComponentCallAndDataResolver`) | `3.98 ms` (`8.0 KB`) | `9.86 ms` / `9.76 ms` | **`4.56 ms`** (`8.0 KB`) / `4.11 ms` | **`-5.20 ms`** |

| Stage (`StrictFailure`, `text/json`) | `F1` (CLI `--profile`) | `F2 Pre-Opt` (cold / CLI) | `F2 Post-Opt` (CLI `--profile` / cold) | Post vs Pre |
| --- | ---: | ---: | ---: | ---: |
| `Resolve services` | `5.78 ms` (`8.0 KB`) | `6.65 ms` / `6.25 ms` | **`1.28 ms`** (`7.8 KB`) / `1.08 ms` | **`-4.97 ms`** |
| `Discover component roles` | `0.00 ms` | `7.17 ms` / `6.61 ms` | **`2.01 ms`** (`0.0 KB`) / `1.93 ms` | **`-4.60 ms`** |
| `Resolve service calls` | `2.90 ms` (`0.0 KB`) | `8.52 ms` / `7.65 ms` | **`1.09 ms`** (`0.0 KB`) / `1.09 ms` | **`-6.56 ms`** |

Root causes in the analyzer cold-start path:
1. `ServiceResolutionIndexBuilder.Build` ([ServiceResolutionIndexBuilder.cs](file:///data/workspace/cqrslens/src/CqrsLens.Analysis/Resolution/ServiceResolutionIndexBuilder.cs)): allocated `testProjects`, `classes`, `interfaces`, `classFiles`, `interfaceLocations`, and `entries`, and ran `AddDeclarationCandidates` even when the workspace had `0` interface declarations and `0` DI registrations.
2. `ScannedTypeCatalog` & `CSharpSyntax.HasDeclarationModifier` ([ScannedTypeCatalog.cs](file:///data/workspace/cqrslens/src/CqrsLens.Analysis/Roles/ScannedTypeCatalog.cs), [CSharpSyntax.cs](file:///data/workspace/cqrslens/src/CqrsLens.Analysis/Text/CSharpSyntax.cs)): `HasDeclarationModifier` allocated substrings + `new[] { ';', '{', '}' }` and executed runtime `Regex.IsMatch` for every class declaration; `ScannedTypeCatalog` ran LINQ `.Where().Select()` and eagerly built 7 dictionaries including `_namespaceMapsByPath`.
3. `ControllerRoleRules`, `ServiceAndRepositoryRoleRules`, and `ConventionalControllerEndpointDiscovery` ([ControllerRoleRules.cs](file:///data/workspace/cqrslens/src/CqrsLens.Analysis/Roles/ControllerRoleRules.cs), [ServiceAndRepositoryRoleRules.cs](file:///data/workspace/cqrslens/src/CqrsLens.Analysis/Roles/ServiceAndRepositoryRoleRules.cs), [ConventionalControllerEndpointDiscovery.cs](file:///data/workspace/cqrslens/src/CqrsLens.Analysis/Slicing/Endpoints/ConventionalControllerEndpointDiscovery.cs), [Analyzer.cs](file:///data/workspace/cqrslens/src/CqrsLens.Analysis/Analyzer.cs)): unconditionally JITted and ran `[NonController]` BFS hierarchy walks, controller attribute scans, repository/service resolution loops, and `ConventionalControllerEndpointDiscovery` (plus its 36-string static `HashSet`) on 0-controller, 0-registration fixtures.
4. `ServiceCallEdgeResolver.BuildCore` & `ComponentCallAndDataResolver.Analyze` ([ServiceCallEdgeResolver.cs](file:///data/workspace/cqrslens/src/CqrsLens.Analysis/Resolution/ServiceCallEdgeResolver.cs), [ComponentCallAndDataResolver.cs](file:///data/workspace/cqrslens/src/CqrsLens.Analysis/Resolution/ComponentCallAndDataResolver.cs)): eagerly JITted and allocated `ServiceResolutionLookup` (`17.88 ms` on `DiffPair/base`, `23.47 ms` on `StrictFailure` cold-process), `ExtensionPublishIndex`, `RepositoryEntityMap`, 11 working collections, and `ReceiverTypeMapBuilder.CollectClassLevelDeclarations` even for handlers/components with `0` constructor parameters and no `[FromServices]` (`DiffPair/base` `DoThingQueryHandler`).

### 1B. Per-Scan Overhead (`nopCommerce` 4.90)

Warmed CLI `--profile` (1 warmup + 3 runs median) on `nopCommerce` (`text/json`, 3,464 files, 8,764 relations, 33,747,841 B):
- `Resolve service calls`: `1,189.46 ms` (`F1`, `61.1 MB`) -> **`951.51 ms`** (`F2 post-opt`, `94.8 MB`, **`-237.95 ms`**)
- `Scan shapes`: `492.34 ms` -> **`473.40 ms`** (`-18.94 ms`)
- `Discover component roles`: **`167.09 ms`** (`27.6 MB`)
- Cold-process `nopCommerce` `Analyze + JsonReportGenerator.Generate`: **`3,188.13 ms`** total (`Analyze` `2,942.43 ms`, 33.75 MB JSON direct streaming `245.70 ms`).

---

## 2. Changes Made & Output-Preservation Invariants

No new `.cs` source files, packages, config changes, or test edits were introduced.

1. **Zero-allocation span modifier check** ([CSharpSyntax.cs](file:///data/workspace/cqrslens/src/CqrsLens.Analysis/Text/CSharpSyntax.cs)):
   - Replaced substring slicing, `new[] { ';', '{', '}' }`, and `Regex.IsMatch(segment, $@"\b{modifier}\b")` in `HasDeclarationModifier` with `ReadOnlySpan<char>` scanning (`LastIndexOfAny(';', '{', '}')` + `ContainsWholeWord` checking identifier boundaries `[A-Za-z0-9_]`).
   - *Invariant:* Identifier boundaries (`IsWordChar`) match `\b` around ASCII keywords `"abstract"` and `"static"` identically, returning the exact same boolean for every declaration.

2. **Early-out and `NoInlining` split in DI index builder** ([ServiceResolutionIndexBuilder.cs](file:///data/workspace/cqrslens/src/CqrsLens.Analysis/Resolution/ServiceResolutionIndexBuilder.cs)):
   - Replaced LINQ `.Where().SelectMany().ToList()` with direct counts and loops; parsed `ServiceRegistrationParser.Parse(activeFiles)` first and returned immediately when `interfaceCount == 0 && registrations.Count == 0`, isolating the rest in `[MethodImpl(MethodImplOptions.NoInlining)] BuildCore`.
   - *Invariant:* `AddDeclarationCandidates` only resolves interfaces present in `TypeHierarchy`'s `InterfaceDecls` index (`0` when `interfaceCount == 0`), and `AddRegistrationCandidates` returns immediately when `registrations.Count == 0`. Thus `result.ServiceResolutions` is guaranteed empty whenever `interfaceCount == 0 && registrations.Count == 0`.

3. **Catalog allocation reduction & `NoInlining` role discovery guards** ([ScannedTypeCatalog.cs](file:///data/workspace/cqrslens/src/CqrsLens.Analysis/Roles/ScannedTypeCatalog.cs), [ControllerRoleRules.cs](file:///data/workspace/cqrslens/src/CqrsLens.Analysis/Roles/ControllerRoleRules.cs), [ServiceAndRepositoryRoleRules.cs](file:///data/workspace/cqrslens/src/CqrsLens.Analysis/Roles/ServiceAndRepositoryRoleRules.cs), [ComponentRoleDiscovery.cs](file:///data/workspace/cqrslens/src/CqrsLens.Analysis/Roles/ComponentRoleDiscovery.cs), [ConventionalControllerEndpointDiscovery.cs](file:///data/workspace/cqrslens/src/CqrsLens.Analysis/Slicing/Endpoints/ConventionalControllerEndpointDiscovery.cs), [Analyzer.cs](file:///data/workspace/cqrslens/src/CqrsLens.Analysis/Analyzer.cs)):
   - `ScannedTypeCatalog`: lazy `_testProjects` (null when no `"Tests"` layer exists), lazy `_namespaceMapsByPath`, pre-sized dictionaries, and zero-list fast path in `ResolveByFullName` when `candidates.Count == 1`.
   - `ControllerRoleRules`: added `MayContainControllers` fast substring pre-check (`"Controller"`, `"Route"`, `"[Http"`, `"[AcceptVerbs"`) before `[MethodImpl(NoInlining)] ApplyControllersCore`; checked `DetectControllerEvidence` before `HasNonControllerInHierarchy`; added fast header-span guards in `HasSpecificHeaderAttribute`. Returns discovered controller count.
   - `ServiceAndRepositoryRoleRules`: gated `ApplyRepositories`/`ApplyServiceResolutions` on `result.ServiceResolutions.Count > 0` and `ApplyExplicitRegistrations` on `explicitRegs.Count > 0` (all `NoInlining`).
   - `ComponentRoleDiscovery` & `Analyzer`: tracked `catalog.DiscoveredControllerCount`, skipped `ControllerRoleRules.ApplyExposes` when `controllerCount == 0 || result.Endpoints.Count == 0`, and skipped invoking `ConventionalControllerEndpointDiscovery.Analyze` when `catalog.DiscoveredControllerCount == 0` (plus `NoInlining` split inside `ConventionalControllerEndpointDiscovery.AnalyzeCore`).
   - *Invariant:* Any class satisfying `InheritsAspNetController`, `[ApiController]`/`[Controller]`/`[Route]` header attributes, or `[Http*]`/`[Route]`/`[AcceptVerbs]` action attributes must appear in a source file containing one of those four tokens; `[NonController]` and `DetectControllerEvidence` are pure predicates whose conjunction is commutative; `ConventionalControllerEndpointDiscovery` and `ApplyExposes` iterate only over `ComponentRole.Controller` components.

4. **Resolver fast-outs, lazy tables, and skipping empty-constructor classes** ([ServiceCallEdgeResolver.cs](file:///data/workspace/cqrslens/src/CqrsLens.Analysis/Resolution/ServiceCallEdgeResolver.cs), [ComponentCallAndDataResolver.cs](file:///data/workspace/cqrslens/src/CqrsLens.Analysis/Resolution/ComponentCallAndDataResolver.cs)):
   - `ServiceCallEdgeResolver.BuildCore`: fast-outs when `Handlers.Count == 0 && NotificationHandlers.Count == 0 && ServiceResolutions.Count == 0`; `BuildCoreActive` (`NoInlining`) skips re-sorting when `EnsureSortedByPath` confirms input is already ordered, skips collecting unused `classes`/`interfaces` lists when `hierarchy` is provided, and isolates post-scan Spine emission in `[MethodImpl(NoInlining)] EmitCollectedEdges` (never JITted when `collected.Count == 0`).
   - `ComponentCallAndDataResolver`: removed static array fields from the outer class (`NonEntityNames` and `TokenTables` nested lazy classes); guarded `RecordStartupConventionRegistrations` behind fast substring checks before `NoInlining` `RecordFileConventionRegistrations`; isolated `AnalyzeComponents` and `AnalyzeComponentMembers` (`NoInlining`) so any component with `constructorTypes.Count == 0 && !hasFromServices` skips `ReceiverTypeMapBuilder.CollectClassLevelDeclarations` and `ServiceResolutionLookup`; lazily constructed `ServiceResolutionLookup`, `ExtensionPublishMap`, and `RepoEntityByComponent` only on first actual use; gated `ScanProductionFilesUnproven` per-file regex/token helpers behind cheap `Contains` checks and `PruneAmbiguousResolutions` behind `AmbiguousResolutions.Count > 0`.
   - *Invariant:* `ServiceCallEdgeResolver` `sourceFullNames` only comes from `Handlers`, `NotificationHandlers`, or `ServiceResolutions`; `ComponentCallAndDataResolver` `typeByMember` explicitly rejects every member whose type is not in `constructorTypes` (which only accepts constructor parameters and `[FromServices]` parameters), so a class with neither produces an empty `typeByMember` map and zero lookups/invocations.

5. **LINQ-free `SpineQueries` projections** ([SpineQueries.cs](file:///data/workspace/cqrslens/src/CqrsLens.Core/Spine/SpineQueries.cs)):
   - Added `HasRelation` early-outs (`UsesDataResolved` for `ResolvedEdges`, `Calls` for `ServiceCallEdges`, `Dispatches || Publishes` for `DispatchCallSites`), `Count <= 1` zero-allocation fast paths, and `[MethodImpl(NoInlining)]` dedupe + stable index-tiebroken sorting for `Count > 1` without LINQ `GroupBy`/`OrderBy` or boxed enum `CompareTo`.
   - *Invariant:* First-seen deduplication via `HashSet` plus stable `(Key, OriginalIndex)` sort preserves the exact element selection and total ordering of LINQ `GroupBy(...).Select(g => g.First()).OrderBy(...).ThenBy(...)`.

6. **Direct UTF-8 JSON report streaming & deferred `JsonEncodedText` initialization** ([JsonSpineNames.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Json/JsonSpineNames.cs), [DirectSpineRelationsConverter.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Json/DirectSpineRelationsConverter.cs), [JsonReportDocumentFactory.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Json/JsonReportDocumentFactory.cs), [JsonReportGenerator.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Json/JsonReportGenerator.cs)):
   - Moved `JsonSpineNames`'s 93 `static readonly JsonEncodedText` fields into nested `JsonSpineNames.Encoded` (deferred `.cctor`) and added zero-allocation compile-time UTF-8 `ReadOnlySpan<byte>` helpers (`"..."u8` literals).
   - Updated `DirectSpineRelationsConverter` to write UTF-8 spans directly and exposed `WriteSpineRelations` and `WriteEvidence` internally.
   - Added `JsonReportDocumentFactory.WriteDirect(Utf8JsonWriter, AnalysisResult)` and switched `JsonReportGenerator.Generate` to stream directly through `Utf8JsonWriter` + `PooledByteBufferWriter` (eliminating DTO allocation and `JsonReportSerializerContext.Default` metadata initialization on CLI/report generation while preserving `CreateDocument` and `ReadDocument` unchanged), with `Count <= 1` fast paths and `NoInlining` isolation on optional/empty sections.
   - *Invariant:* `WriteDirect` uses the exact same `Utf8JsonWriter` encoder defaults (`Indented = true`, `JavaScriptEncoder.Default`), identical property declaration order, identical `EmptyToNull` / `null` / empty-collection omission rules, and identical stable sort comparators as `JsonReportDocumentFactory.Create` + `JsonReportSerializerContext`.

---

## 3. Warmed CLI Wall-Clock A/B Medians (Gate 8 Protocol)

Protocol: `dotnet <cli.dll> <corpus> --analysis <mode> --format <fmt> --generated-at 2026-01-01T00:00:00Z --no-banner --no-color -o <file>` including process startup, interleaved F1 (`3fcbcba`) vs. F2, **1 warmup + 3 timed runs** (Session 1) plus a **1 warmup + 5 timed runs** confirmation session (Session 2) on `text/json` fixtures and `nopCommerce`.

### 3A. Before vs. After Optimization Summary (F2 vs. F1 Baseline)

| Cell | Mode / Format | F2 Pre-Opt Delta (same machine) | F2 Post-Opt S1 (3-run) | F2 Post-Opt S2 (5-run) | **Post-Opt Median of Sessions** | Target (`<= +10%`) |
| --- | --- | ---: | ---: | ---: | ---: | --- |
| `DiffPair/base` | `text/json` | `+7.58%` (`296.8 -> 319.3 ms`) | **`-11.88%`** | **`-19.30%`** | **`-15.59%`** | **PASS** (was `+9.92%` marginal) |
| `StrictFailure` | `text/json` | `+4.22%` (`286.7 -> 298.8 ms`) | **`-15.77%`** | **`-22.37%`** | **`-19.07%`** | **PASS** |
| `GoldenCorpus` | `text/json` | `+5.07%` (`388.6 -> 408.3 ms`) | **`-4.15%`** | **`-6.42%`** | **`-5.29%`** | **PASS** |
| `Adversarial` | `text/json` | `+5.88%` (`336.1 -> 355.9 ms`) | **`-7.15%`** | **`-8.29%`** | **`-7.72%`** | **PASS** |
| `DiffPair/head` | `text/json` | `+11.99%` (`266.6 -> 298.6 ms`) | **`-13.72%`** | **`-12.32%`** | **`-13.02%`** | **PASS** |
| `nopCommerce` | `text/json` | `-1.43%` (`3,428.6 -> 3,379.5 ms`) | `+8.95%` | **`-4.55%`** | **`+2.20%`** | **PASS** (`<= +5%` & `< 3,688 ms`) |
| `nopCommerce` | `text/md` | `+2.39%` (`3,322.0 -> 3,401.4 ms`) | **`-7.72%`** | `+1.55%` | **`-3.09%`** | **PASS** |
| `nopCommerce` | `roslyn/json` | `-1.52%` (`9,933.2 -> 9,782.0 ms`) | **`-1.53%`** | `+0.47%` | **`-0.53%`** | **PASS** |

### 3B. Post-Optimization Session 1 Detail (1 Warmup + 3 Timed Runs, Same Session)

| Corpus | Mode / Format | F1 Median (Runs, ms) | Bytes | Rel | F2 Post-Opt Median (Runs, ms) | Bytes | Rel | Delta vs F1 |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `DiffPair/base` | `text/json` | 277.7 (`277.7/269.8/291.7`) | 3,342 | 1 | **244.7** (`220.4/244.7/248.6`) | 4,785 | 1 | **-11.88%** |
| `DiffPair/base` | `text/md` | 281.2 (`281.2/286.7/269.6`) | 8,221 | — | **271.7** (`266.9/294.4/271.7`) | 8,221 | — | **-3.38%** |
| `DiffPair/base` | `roslyn/json` | 757.4 (`772.2/757.4/750.7`) | 3,344 | 1 | **715.9** (`715.9/718.7/697.0`) | 4,787 | 1 | **-5.48%** |
| `StrictFailure` | `text/json` | 246.7 (`246.7/239.0/249.7`) | 1,736 | 0 | **207.8** (`207.8/195.4/213.4`) | 2,510 | 0 | **-15.77%** |
| `StrictFailure` | `text/md` | 244.0 (`252.3/230.3/244.0`) | 6,496 | — | **244.5** (`266.4/232.5/244.5`) | 6,496 | — | **+0.20%** |
| `StrictFailure` | `roslyn/json` | 706.8 (`713.3/704.4/706.8`) | 1,376 | 0 | **652.2** (`686.2/652.2/624.2`) | 1,376 | 0 | **-7.72%** |
| `GoldenCorpus` | `text/json` | 369.5 (`340.6/369.5/380.6`) | 60,600 | 25 | **354.1** (`355.9/354.1/324.0`) | 88,082 | 27 | **-4.15%** |
| `GoldenCorpus` | `text/md` | 377.6 (`351.5/377.6/394.4`) | 20,957 | — | **413.8** (`381.2/439.9/413.8`) | 20,957 | — | **+9.57%** |
| `GoldenCorpus` | `roslyn/json` | 939.0 (`932.8/939.0/953.6`) | 60,602 | 25 | **909.6** (`949.5/909.6/904.7`) | 88,084 | 27 | **-3.13%** |
| `Adversarial` | `text/json` | 300.3 (`300.3/294.2/304.6`) | 13,923 | 6 | **278.8** (`278.8/283.9/266.6`) | 22,259 | 6 | **-7.15%** |
| `Adversarial` | `text/md` | 306.1 (`331.9/293.9/306.1`) | 13,972 | — | **325.0** (`306.0/332.4/325.0`) | 13,972 | — | **+6.16%** |
| `Adversarial` | `roslyn/json` | 905.1 (`919.1/905.1/900.8`) | 13,925 | 6 | **866.2** (`876.8/866.2/865.9`) | 22,261 | 6 | **-4.30%** |
| `DiffPair/head` | `text/json` | 242.3 (`242.3/246.9/237.4`) | 1,643 | 0 | **209.0** (`209.0/209.3/194.9`) | 2,365 | 0 | **-13.72%** |
| `DiffPair/head` | `text/md` | 244.2 (`252.4/244.2/242.8`) | 6,341 | — | **241.0** (`241.0/250.3/231.8`) | 6,341 | — | **-1.29%** |
| `DiffPair/head` | `roslyn/json` | 750.1 (`759.4/730.3/750.1`) | 1,645 | 0 | **722.4** (`749.4/697.0/722.4`) | 2,367 | 0 | **-3.68%** |
| `nopCommerce` | `text/json` | 2,998.3 (`3,240.0/2,998.3/2,959.9`) | 9,644,562 | 3,234 | **3,266.8** (`3,266.8/2,785.3/3,372.6`) | 33,747,841 | 8,764 | **+8.95%** |
| `nopCommerce` | `text/md` | 3,243.9 (`3,310.2/3,243.9/3,029.5`) | 11,844 | — | **2,993.5** (`2,653.7/3,051.8/2,993.5`) | 11,844 | — | **-7.72%** |
| `nopCommerce` | `roslyn/json` | 9,447.8 (`9,473.0/9,447.8/8,961.5`) | 9,644,564 | 3,234 | **9,303.1** (`9,303.1/9,706.7/9,124.2`) | 33,747,843 | 8,764 | **-1.53%** |

### 3C. Post-Optimization Session 2 Detail (1 Warmup + 5 Timed Runs Confirmation)

| Corpus | Mode / Format | F1 Median (Runs, ms) | Bytes | Rel | F2 Post-Opt Median (Runs, ms) | Bytes | Rel | Delta vs F1 |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `DiffPair/base` | `text/json` | 271.5 (`275.3/265.0/271.5/273.5/260.9`) | 3,342 | 1 | **219.1** (`219.1/221.9/210.6/205.4/229.3`) | 4,785 | 1 | **-19.30%** |
| `StrictFailure` | `text/json` | 250.2 (`258.1/252.2/250.2/233.6/248.6`) | 1,736 | 0 | **194.2** (`194.2/189.5/193.2/208.1/204.3`) | 2,510 | 0 | **-22.37%** |
| `GoldenCorpus` | `text/json` | 347.2 (`347.2/331.7/363.9/337.0/380.3`) | 60,600 | 25 | **324.9** (`309.1/324.9/336.1/320.3/337.6`) | 88,082 | 27 | **-6.42%** |
| `Adversarial` | `text/json` | 321.4 (`319.3/329.7/328.1/311.0/321.4`) | 13,923 | 6 | **294.7** (`294.7/287.2/319.3/296.4/274.4`) | 22,259 | 6 | **-8.29%** |
| `DiffPair/head` | `text/json` | 242.9 (`242.5/249.2/241.1/246.1/242.9`) | 1,643 | 0 | **213.0** (`205.1/235.9/213.0/220.8/206.9`) | 2,365 | 0 | **-12.32%** |
| `nopCommerce` | `text/json` | 3,181.0 (`3,236.6/3,079.1/2,924.6/3,206.8/3,181.0`) | 9,644,562 | 3,234 | **3,036.2** (`3,009.5/3,265.8/3,036.2/2,616.0/3,240.0`) | 33,747,841 | 8,764 | **-4.55%** |
| `nopCommerce` | `text/md` | 2,963.6 (`3,214.1/2,815.4/3,076.1/2,963.6/2,944.6`) | 11,844 | — | **3,009.4** (`3,086.2/2,686.6/3,152.6/3,009.4/2,900.7`) | 11,844 | — | **+1.55%** |
| `nopCommerce` | `roslyn/json` | 9,000.5 (`8,419.5/9,000.5/8,843.2/9,117.0/9,508.3`) | 9,644,564 | 3,234 | **9,043.1** (`8,815.9/8,968.1/9,043.1/9,208.4/9,589.3`) | 33,747,843 | 8,764 | **+0.47%** |

Every measured cell is under `+10%` in every session, every `text/json` fixture cell is **4%–22% faster than the F1 baseline**, and `nopCommerce` `text/json` is `3,036.2 ms` / `3,266.8 ms` (`<= 3,688 ms` ceiling; median of sessions `+2.20%`) with all `8,764` relations intact.

---

## 4. Byte-Identity Proof (70 / 70 Outputs + `--strict` + Synthetic Check)

All outputs generated with `--generated-at 2026-01-01T00:00:00Z --no-banner --no-color` and compared byte-for-byte against the pre-optimization F2 snapshot (`6c3b7d9a...`):

### 4A. JSON & Non-JSON Outputs (`70 / 70` SHA-256 Matches, `0` Mismatches)

| Corpus / Command | Mode | Format | Bytes | Identical SHA-256 (Pre-Opt F2 == Post-Opt F2) |
| --- | --- | --- | ---: | --- |
| `GoldenCorpus` | `text` | `json` | 88,082 | `83088b3411ccbee1e6abdff99a75d4acfb1446c8673597ef5d5cf20c5c12fe45` |
| `GoldenCorpus` | `text` | `md` | 20,957 | `c239d04b379e0919f9225aa436910256a58810fcf20e7dd133afafddc2fa7052` |
| `GoldenCorpus` | `text` | `html` | 53,906 | `37e6a2d9df049fc5a28abec7ffce67c1a74a86d39978b931af68d6dfc5b8d13b` |
| `GoldenCorpus` | `text` | `agent` | 5,027 | `81245c7975448d27028253b1542a51a434fd5e017f52947ea4e3e364764c1b23` |
| `GoldenCorpus` | `text` | `summary` | 590 | `2656a0cf0dadf6f49d20d0f833f19cf818a5743dedb9eb7152773c9a823ad19b` |
| `GoldenCorpus` | `text` | `sarif` | 2,250 | `641e4c1568c2008a2b17742100c6e87df9971eaa112b702a58b807cb32d7eaae` |
| `GoldenCorpus` | `roslyn` | `json` | 88,084 | `ccc17f6e80458e337c4e052c3a0b0b8fda724d32ba43353b56bef0902f079316` |
| `GoldenCorpus` | `roslyn` | `md` | 20,959 | `cfdd6947206fc8ef6927682ff401b338c3448886f51b292113c1e12c86e3c32e` |
| `GoldenCorpus` | `roslyn` | `html` | 53,908 | `a38decffb291c25536b1a6df20ca466df0cd15bae16f6145ad787566b9150b41` |
| `GoldenCorpus` | `roslyn` | `agent` | 5,029 | `48b4936f3267c743c5da51ef8c44bb18513170bcd91b625651f05abb0ca301f4` |
| `GoldenCorpus` | `roslyn` | `summary` | 592 | `9046582bfad27e23fa81d20ff1281a55561eb18bd4beb19ee97a81fe47347752` |
| `GoldenCorpus` | `roslyn` | `sarif` | 2,252 | `92cb3b56280b224d2a218a23d3d6a8d65e1fe05514070500145d09ab931e5b19` |
| `Adversarial` | `text` | `json` | 22,259 | `4dcf76e83006d44917265d961e68f906f01507032aa9ec592082ebac58d61840` |
| `Adversarial` | `text` | `md` | 13,972 | `9aa614789f005daf7dde71506321db5f79f42e616e92aec8ef26161f0fedfb1e` |
| `Adversarial` | `text` | `html` | 39,348 | `e714a2217298747fd4bd207a214c7c438fd03da5771c0ac65580d6d184d65990` |
| `Adversarial` | `text` | `agent` | 2,244 | `edec8711e4cbf4f4a5c359cc337edb7381581bc8b3bf91c199c5deb40e21e491` |
| `Adversarial` | `text` | `summary` | 732 | `5cef7c26d97a6a248b236ea8641b7f3a2e06dca6a302768844fc8a9571f722cc` |
| `Adversarial` | `text` | `sarif` | 3,151 | `daaf854bbe7e6b56490d5d487c5ff715a5d6252f09e75a21e64497670d2e8e18` |
| `Adversarial` | `roslyn` | `json` | 22,261 | `32b7718258e9859b3382731c911ae333cdc83757f1e219af4f4602f36b943df0` |
| `Adversarial` | `roslyn` | `md` | 13,974 | `e9fee03255908b00b3f0289414684dc00e66a2d3e0efe9a7dc27f7ac408b329b` |
| `Adversarial` | `roslyn` | `html` | 39,350 | `29678b70b0558acb69bd2dad3f182e79ac7f787036d8a128de9d1834505f761d` |
| `Adversarial` | `roslyn` | `agent` | 2,246 | `face1f0c14c3aa927ecaf5c75d140202da1004a5e95921455fa633d40e54a0fb` |
| `Adversarial` | `roslyn` | `summary` | 734 | `e8c36685cedb26e02ced203c6d65400498531b0f11bd223fea7f109cafe5b9d2` |
| `Adversarial` | `roslyn` | `sarif` | 3,153 | `851f68ca4c5e21a22f355946dbeeff23ee0b29272f03769125ac30ddc083ab29` |
| `DiffPair/base` | `text` | `json` | 4,785 | `f0f9e14c424fa3397d52dc5ded95527c94ef9ad5a7a17d3ac5fe3297ac367b4d` |
| `DiffPair/base` | `text` | `md` | 8,221 | `0d0f9d93892a83a69f4d758f946843f1d123e17a287d01eeb10f681ce644b020` |
| `DiffPair/base` | `text` | `html` | 28,917 | `f90de3b84cccda6358a8326828b6a49bfca66c528862cdfe1c351858c71f0bf4` |
| `DiffPair/base` | `text` | `agent` | 954 | `60991d93d1a6c65b4c027b73f59c5b33c88a9cbb4459854905549291008be3bb` |
| `DiffPair/base` | `text` | `summary` | 558 | `e79917af5c3f192f53613b1eab450718e9651a04680d23949002d2038b7d7602` |
| `DiffPair/base` | `text` | `sarif` | 2,190 | `fc51db4de7439d38384f4d29525c0b15a7b3d8e1f6a8bf36432ab18a04a71424` |
| `DiffPair/base` | `roslyn` | `json` | 4,787 | `0a2b417a0007d289d20c1336a4bb4ba27edbe27f3fdfe4bba2e4d485eff21b5d` |
| `DiffPair/base` | `roslyn` | `md` | 8,223 | `9201b149b784d9cb5d3461daf6ef8041e4514676d21be92c28d9a1b9b240c118` |
| `DiffPair/base` | `roslyn` | `html` | 28,919 | `c01041b32ea5851b6a1bbe24dcb4ea14d27be94c75205e6a3759f721d0c042a1` |
| `DiffPair/base` | `roslyn` | `agent` | 956 | `71748474b52730dca3cc20ab9dbf8921af0bc5a7f43ecf6f2bbb9a16cb477c64` |
| `DiffPair/base` | `roslyn` | `summary` | 560 | `b6ea0cff76eb4e49d1a0778eca1e16697ed1a3010f0a6f6fe57dde56e41d8faa` |
| `DiffPair/base` | `roslyn` | `sarif` | 2,192 | `fa7fee5941b0aec75600b69156646aca1599843a0f472b1b287e52c0aabe8bc2` |
| `DiffPair/head` | `text` | `json` | 2,365 | `a805798ee5bd189c8bf41515f6e71fa15aef415cd128ede2a1376cb8a07bdd02` |
| `DiffPair/head` | `text` | `md` | 6,341 | `be64cc6a7b8475bc0354385e6e3150796f062ab181c9dbbf0aaae29a01576bfc` |
| `DiffPair/head` | `text` | `html` | 26,062 | `e26b28ac18ada2997890dad827068e56ab4e50a924f0ec865fbf1777feb25fb3` |
| `DiffPair/head` | `text` | `agent` | 803 | `fcd6007a09f79950970053d72835725aea343408c8a1fb20fb390743cb3db487` |
| `DiffPair/head` | `text` | `summary` | 522 | `354e622ff3db8051315301be753c19622a770fdb76163ecb0f3202c72a9d39f3` |
| `DiffPair/head` | `text` | `sarif` | 2,117 | `2d89533334e12ba5dcf695a9278ddc4cc1ba22576b2949a4737cf6b2255b6896` |
| `DiffPair/head` | `roslyn` | `json` | 2,367 | `7263b37c9854994bbcef71ce8fcffc75cb970fcc9268f80103b9742ef1743960` |
| `DiffPair/head` | `roslyn` | `md` | 6,343 | `8cd48804e77b9878a4120e3a493ddd50cf104bc79aa8aadc1ba4f1674b1fb32d` |
| `DiffPair/head` | `roslyn` | `html` | 26,064 | `241b28a36148fa6e78747f8c364f362453254fe10dd5db40e6e3b6298e7b1c6c` |
| `DiffPair/head` | `roslyn` | `agent` | 805 | `96c62acca42cbf7454b8d5b2d80cdce0c5f1816e3149aa153c16a4ff63dabea2` |
| `DiffPair/head` | `roslyn` | `summary` | 524 | `30a200cccb5678228ed2078310dfb09544af05b23c3b7813537403fd1940448f` |
| `DiffPair/head` | `roslyn` | `sarif` | 2,119 | `4f07f3ddccfe21656d92cc2583252e8e9dc39da2564cd9a06940c436c6b806e0` |
| `StrictFailure` | `text` | `json` | 2,510 | `fdbc69d7092c2fcb0bad2e07dee9c0af656b8f72de8dfaa8aad4ece6b4c2e576` |
| `StrictFailure` | `text` | `md` | 6,496 | `7ab3be40f33b1683db6f6467ad8d65fb38c9f2b8d929de51f604f39cb7044492` |
| `StrictFailure` | `text` | `html` | 26,362 | `2ffc50a7e3b9dff388ec7d1bfee9b48a2a03cbe0d306c2af5c0151f31068cd7e` |
| `StrictFailure` | `text` | `agent` | 874 | `a858ebe7a8100089bc281521f08c0cf43c191e972d4b3f260e3cc814f10fad34` |
| `StrictFailure` | `text` | `summary` | 536 | `c70141a85e25c8443ce3154bb521f2fe191f98d442906189fe6faabfb2f0b373` |
| `StrictFailure` | `text` | `sarif` | 2,153 | `fe7590fddc31595a39213fd7cd92a0fde41e4b169627554e9d01e4a53d8e0dcf` |
| `StrictFailure` | `roslyn` | `json` | 1,376 | `92adc27b596efacb5fe7261c3c3cee687273d002a4911a18524892f187edb839` |
| `StrictFailure` | `roslyn` | `md` | 4,554 | `2b914472c66743ea795cd9ee8491176f2df78946b6625e5585bc8d25103cd4a0` |
| `StrictFailure` | `roslyn` | `html` | 20,335 | `5ce2dd4c81a51d5ec5444cbc26586c2f8968ceaff6d4ade18867f851cd39c3f7` |
| `StrictFailure` | `roslyn` | `agent` | 534 | `60802f15dfdc224349670b213766be0884fa98b4133369323906ebdd367ec9c2` |
| `StrictFailure` | `roslyn` | `summary` | 533 | `5ffe32d84167bb84e41c950f9fbb129a9e348910ce6f162126a7f53f5213a437` |
| `StrictFailure` | `roslyn` | `sarif` | 1,245 | `163cf066572e0e8e3c9e3804fd20a71cab8e897db587ffe8d3447d6c23aaeefd` |
| **`nopCommerce`** | `text` | `json` (8,764 rels) | **33,747,841** | **`5bc6d4f9bf9fa33e90724a5126060223a4f774349a0e3fa74dd95cbc42c0125a`** |
| **`nopCommerce`** | `roslyn` | `json` (8,764 rels) | **33,747,843** | **`378a7f42dad80b32d78e9064fd20d27ae0c224cd58fb112d13dcd7ffc5366b8b`** |
| `nopCommerce` | `text` | `md` | 11,844 | `589c06ed7e9e33b7a2e2abdab2e5f7096cdf25b924ed112c9bdf4bdc2d0b6c1e` |
| `nopCommerce` | `text` | `html` | 28,741 | `09023eebe0f392954f80f1f2142f082d03650a7908e67c3c1a3476dbc0439463` |
| `nopCommerce` | `text` | `agent` | 2,914 | `770b2c2ca88cdd4cd8504bfb87e8e6280dad5846f1ef1b2318546f75e49f6768` |
| `nopCommerce` | `text` | `summary` | 327 | `cd092b4dd748647c6de646747cb9d6aa680f3fa38764a5d89fccfb83ca20e463` |
| `nopCommerce` | `text` | `sarif` | 878 | `a387341fa506b88883d55227c968a77b23cf2a5e0741255ca89fa9f6160b5017` |
| `--diff` (`head` vs `base`) | `text` | `md` | 2,057 | `0c3845fb4bb29b3ed1dfd46a4e0433ac36f354cd9b12569c6c7d78d85d5cc034` |
| `--diff` (`head` vs `base`) | `text` | `json` | 2,653 | `20203ce97b72a5755262ef3d9b5ed2e3b55a157264b16e4f394b353e56c064ba` |
| `--mcp` (`GoldenCorpus`) | stdio | JSON-RPC (6 calls) | 14,508 | `4b25dcfebfeb12eebd6c1d630464fa73e2194776850c971f295041d763a1b9d0` |

### 4B. `--strict` & Synthetic `WriteDirect` Equivalence
- `--strict` exit codes: `StrictFailure` = `4`, `GoldenCorpus` = `0`, `nopCommerce` = `0`.
- Synthetic `WriteDirect` vs. `JsonSerializer.Serialize(JsonReportGenerator.CreateDocument(r), ReferenceJsonOptions) + "\n"` (covering `DbContexts` with `DbSets`/`Entities`/`Relationships`, multi-role `Components`, `SpineEndpoints`, `Checks` with baseline, and all 6 `Unproven` populations): byte-identical (`11,282 B`).

---

## 5. Suite, Compiler Warnings, & Working Tree Status

- **Test Suite** (`dotnet test /data/workspace/cqrslens/CqrsLens.sln -c Release --no-build --nologo` after final edit):
  ```text
  Passed!  - Failed:     0, Passed:   693, Skipped:    68, Total:   761, Duration: 3 s - CqrsLens.Tests.dll (net8.0)
  ```
  (The `68` skipped tests are the pre-existing external-corpus frozen tests skipped because external corpus paths are absent on box #4.)
- **Compiler Warnings** (`dotnet build /data/workspace/cqrslens/CqrsLens.sln -c Release --no-incremental --nologo`): `0` warnings in `CqrsLens.Core`, `0` new warnings across the solution (`26` pre-existing raw analyzer/test warning lines).
- **Protected Files Diff (`git diff 3fcbcba -- '*.csproj' Directory.Build.props CHANGELOG.md README.md ROADMAP.md tests/CqrsLens.Tests/FrozenQuestionSet.cs src/CqrsLens.Reporting/Sequences/RequestSequenceBuilder.cs tests/CqrsLens.Tests/`):** empty (`0` bytes).
- **Corpus Integrity (`/data/workspace/corpora/nopcommerce`):** clean at `e7a52b9199f9c7651839e92826df47f668f3c767` (`release-4.90.0`).

### `git status --short`
```text
 M src/CqrsLens.Analysis/Analyzer.cs
 M src/CqrsLens.Analysis/Resolution/ServiceCallEdgeResolver.cs
 M src/CqrsLens.Analysis/Resolution/ServiceRegistration.cs
 M src/CqrsLens.Analysis/Resolution/ServiceRegistrationParser.cs
 M src/CqrsLens.Analysis/Resolution/ServiceResolutionIndexBuilder.cs
 M src/CqrsLens.Analysis/Slicing/Endpoints/ControllerEndpointDiscovery.cs
 M src/CqrsLens.Analysis/Slicing/ReceiverTypeMapBuilder.cs
 M src/CqrsLens.Analysis/Text/CSharpSyntax.cs
 M src/CqrsLens.Core/Analysis/AnalysisResult.cs
 M src/CqrsLens.Core/Architecture/EndpointInfo.cs
 M src/CqrsLens.Core/Spine/ArchitectureSpine.cs
 M src/CqrsLens.Core/Spine/ComponentRole.cs
 M src/CqrsLens.Core/Spine/EvidenceOccurrence.cs
 M src/CqrsLens.Core/Spine/EvidenceRule.cs
 M src/CqrsLens.Core/Spine/RelationKind.cs
 M src/CqrsLens.Core/Spine/SpineQueries.cs
 M src/CqrsLens.Core/Spine/SpineRelation.cs
 M src/CqrsLens.Core/Spine/UnprovenItemKind.cs
 M src/CqrsLens.Core/Spine/UnprovenPopulations.cs
 M src/CqrsLens.Reporting/Json/DirectSpineRelationsConverter.cs
 M src/CqrsLens.Reporting/Json/JsonReportDocument.cs
 M src/CqrsLens.Reporting/Json/JsonReportDocumentFactory.cs
 M src/CqrsLens.Reporting/Json/JsonReportGenerator.cs
 M src/CqrsLens.Reporting/Json/JsonSpineEvidence.cs
 M src/CqrsLens.Reporting/Json/JsonSpineNames.cs
?? BRIEF_F1.md
?? BRIEF_F1_PERF.md
?? BRIEF_F2.md
?? BRIEF_F2_PERF.md
?? CHECKPOINT_F1_CP1.md
?? CHECKPOINT_F1_CP2.md
?? CHECKPOINT_F1_CP3.md
?? CHECKPOINT_F1_PERF.md
?? CHECKPOINT_F2_CP1.md
?? CHECKPOINT_F2_CP2.md
?? CHECKPOINT_F2_CP3.md
?? CHECKPOINT_F2_PERF.md
?? F0_METAMODEL_v2.1.md
?? F2_CORPUS_AUDIT.md
?? f2-final-verification.md
?? src/CqrsLens.Analysis/Resolution/ComponentCallAndDataResolver.cs
?? src/CqrsLens.Analysis/Resolution/ExplicitServiceRegistration.cs
?? src/CqrsLens.Analysis/Resolution/ExtensionPublishInfo.cs
?? src/CqrsLens.Analysis/Resolution/MemberInvocationHit.cs
?? src/CqrsLens.Analysis/Resolution/MemberInvocationScanner.cs
?? src/CqrsLens.Analysis/Resolution/ResolvedServiceTarget.cs
?? src/CqrsLens.Analysis/Resolution/ServiceResolutionLookup.cs
?? src/CqrsLens.Analysis/Resolution/SourceLineIndex.cs
?? src/CqrsLens.Analysis/Roles/
?? src/CqrsLens.Analysis/Slicing/Endpoints/ConventionalControllerEndpointDiscovery.cs
?? src/CqrsLens.Reporting/Json/JsonAmbiguousResolution.cs
?? src/CqrsLens.Reporting/Json/JsonServiceCandidate.cs
?? src/CqrsLens.Reporting/Json/JsonSpineComponent.cs
?? src/CqrsLens.Reporting/Json/JsonSpineEndpoint.cs
?? src/CqrsLens.Reporting/Json/JsonSpineRoleEvidence.cs
?? src/CqrsLens.Reporting/Json/JsonUnestablishedBehavior.cs
?? src/CqrsLens.Reporting/Json/JsonUnprovenDiagnostic.cs
?? src/CqrsLens.Reporting/Json/JsonUnprovenPopulations.cs
?? tests/CqrsLens.Tests/F2RelationAndResolutionTests.cs
?? tests/CqrsLens.Tests/F2RoleInventoryTests.cs
```

### `git diff --stat`
```text
 src/CqrsLens.Analysis/Analyzer.cs                  |   20 +-
 .../Resolution/ServiceCallEdgeResolver.cs          |  270 ++--
 .../Resolution/ServiceRegistration.cs              |    3 +-
 .../Resolution/ServiceRegistrationParser.cs        |  648 ++++++++-
 .../Resolution/ServiceResolutionIndexBuilder.cs    |  169 ++-
 .../Endpoints/ControllerEndpointDiscovery.cs       |    6 +
 .../Slicing/ReceiverTypeMapBuilder.cs              |   84 +-
 src/CqrsLens.Analysis/Text/CSharpSyntax.cs         |   58 +-
 src/CqrsLens.Core/Analysis/AnalysisResult.cs       |   16 +-
 src/CqrsLens.Core/Architecture/EndpointInfo.cs     |    1 +
 src/CqrsLens.Core/Spine/ArchitectureSpine.cs       |   38 +-
 src/CqrsLens.Core/Spine/ComponentRole.cs           |    3 +
 src/CqrsLens.Core/Spine/EvidenceOccurrence.cs      |   16 +-
 src/CqrsLens.Core/Spine/EvidenceRule.cs            |   11 +
 src/CqrsLens.Core/Spine/RelationKind.cs            |    1 +
 src/CqrsLens.Core/Spine/SpineQueries.cs            |  200 ++-
 src/CqrsLens.Core/Spine/SpineRelation.cs           |   25 +
 src/CqrsLens.Core/Spine/UnprovenItemKind.cs        |    5 +
 src/CqrsLens.Core/Spine/UnprovenPopulations.cs     |   16 +-
 .../Json/DirectSpineRelationsConverter.cs          |   65 +-
 src/CqrsLens.Reporting/Json/JsonReportDocument.cs  |   18 +
 .../Json/JsonReportDocumentFactory.cs              | 1409 +++++++++++++++++++-
 src/CqrsLens.Reporting/Json/JsonReportGenerator.cs |    3 +-
 src/CqrsLens.Reporting/Json/JsonSpineEvidence.cs   |    1 +
 src/CqrsLens.Reporting/Json/JsonSpineNames.cs      |  581 +++++---
 25 files changed, 3205 insertions(+), 462 deletions(-)
```
