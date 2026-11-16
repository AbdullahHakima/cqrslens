# CHECKPOINT — F1 Perf Fix

Branch: `foundations/f1-spine` (uncommitted on `bd49fc3`; never committed, never pushed)  
Scope: Pure optimization of F1 spine per-scan overhead and `relations` JSON serialization with byte-identical outputs across all modes, formats, and auxiliary CLI/MCP flows.

---

## 1. Hotspots Found & Profiling Evidence

In-process `DeepProfiler` (1 warmup + 3 GC-isolated iterations + `EventListener` allocation-tick sampling) on **ABP** (`696` projects, `2,098` services, `50` service-call edges, `82` DbContexts, `232` components, `3,663` relations / `3,701` occurrences; `6,129,977`-byte JSON vs `~1.2 MB` `bd49fc3` without `relations`) and **modular-monolith/src** isolated two independent regression sources:

### 1A. Spine Per-Scan Overhead (`Analyze` + `Markdown` / graph paths)

| Metric (ABP text, 1 warmup + 3 runs median) | `Base (bd49fc3)` | `F1-before` (pre-opt) | `F1-final` (post-opt) | Delta (`F1-final` vs `F1-before`) | Delta (`F1-final` vs `Base`) |
| --- | ---: | ---: | ---: | ---: | ---: |
| `Analyze Total` | `2315.22 ms` (`2480.44 MB`) | `2754.51 ms` (`2934.41 MB`) | **`1495.90 ms` (`1333.34 MB`)** | **`-45.7%` ms / `-54.6%` MB** | **`-35.4%` ms / `-46.2%` MB** |
| `Phase Discover projects` | `76.67 ms` (`30.45 MB`) | `157.03 ms` (`121.12 MB`) | **`83.31 ms` (`32.08 MB`)** | `-46.9%` ms / `-73.5%` MB | `+8.7%` ms / `+5.4%` MB |
| `Phase Resolve services` | `109.95 ms` (`66.99 MB`) | `178.16 ms` (`269.97 MB`) | **`147.51 ms` (`70.21 MB`)** | `-17.2%` ms / `-74.0%` MB | `+34.2%` ms / `+4.8%` MB |
| `Phase Architecture checks` | `1057.85 ms` (`1156.93 MB`) | `1347.04 ms` (`1312.54 MB`) | **`166.89 ms` (`0.72 MB`)** | **`-87.6%` ms / `-99.9%` MB** | **`-84.2%` ms / `-99.9%` MB** |
| — `ProjectLayerChecks` (cached result) | `1013.33 ms` (`1156.75 MB`) | `1339.28 ms` (`1306.92 MB`) | **`168.30 ms` (`0.24 MB`)** | **`-87.4%` ms / `-100.0%` MB** | **`-83.4%` ms / `-100.0%` MB** |
| `Markdown Generate` | `11.91 ms` (`9.40 MB`) | `35.00 ms` (`10.46 MB`) | `31.68 ms` (`9.81 MB`) | `-9.5%` ms / `-6.2%` MB | — |
| — `DiagramBuilder.ProjectGraph` | `2.52 ms` (`3.08 MB`) | `9.29 ms` (`3.59 MB`) | **`3.40 ms` (`3.26 MB`)** | `-63.4%` ms | — |
| — `DiagramBuilder.LayerGraph` | `2.26 ms` (`3.14 MB`) | `8.38 ms` (`3.65 MB`) | **`5.71 ms` (`3.32 MB`)** | `-31.9%` ms | — |

`AllocationTick` sampling during `Analyze` on ABP:
- `System.String`: `1121.33 MB` (`bd49fc3`) → `2004.64 MB` (`F1-before`) → **`1120.40 MB`** (`F1-final`)
- `CqrsLens.NodeReference`: **`329.39 MB`** (`F1-before`) → **eliminated from top-15** (`0` ticks in sample)
- `Entry[System.String,CqrsLens.ProjectId][]`: **`31.38 MB`** (`F1-before`) → **eliminated**

Root causes & code locations:
1. **Batch project ingestion & premature lookup rebuilds** ([AnalysisResult.cs](file:///data/workspace/cqrslens/src/CqrsLens.Core/Analysis/AnalysisResult.cs), [SpineProjectedCollection.cs](file:///data/workspace/cqrslens/src/CqrsLens.Core/Spine/SpineProjectedCollection.cs), [SpineTrackedList.cs](file:///data/workspace/cqrslens/src/CqrsLens.Core/Spine/SpineTrackedList.cs)): `Analyzer` called `result.Projects.AddRange(loadedProjects)`, which fell back to per-item `AddCore(item)` (~696 sequential calls). Each call rebuilt `SpineIdentityResolver`'s project lookup and resolved forward-referenced projects into synthetic `__synthetic__/` nodes that were immediately rehomed as later projects arrived (`Discover projects`: `157.03 ms / 121.12 MB`).
2. **`O(P)` directory scan per `ResolveProjectForLocation` call** ([SpineIdentityResolver.cs](file:///data/workspace/cqrslens/src/CqrsLens.Core/Spine/SpineIdentityResolver.cs)): `ResolveProjectForLocation` performed a linear scan over all `696` projects and allocated `relDir + "/"` strings for every resolved service/call location (`Resolve services`: `269.97 MB`, `31.38 MB` dictionary-table churn). Also [ServiceCallEdgeResolver.cs](file:///data/workspace/cqrslens/src/CqrsLens.Analysis/Resolution/ServiceCallEdgeResolver.cs) scanned `resolutionBySignature.Values` linearly per interface call.
3. **Unindexed spine queries & DFS allocation in `ProjectLayerChecks` / diagrams** ([ArchitectureSpine.cs](file:///data/workspace/cqrslens/src/CqrsLens.Core/Spine/ArchitectureSpine.cs), [SpineQueries.cs](file:///data/workspace/cqrslens/src/CqrsLens.Core/Spine/SpineQueries.cs), [SpineLinkList.cs](file:///data/workspace/cqrslens/src/CqrsLens.Core/Spine/SpineLinkList.cs), [ProjectLayerChecks.cs](file:///data/workspace/cqrslens/src/CqrsLens.Analysis/Checks/ProjectLayerChecks.cs), [MarkdownDiagramWriter.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Markdown/MarkdownDiagramWriter.cs), [HtmlDiagramSectionRenderer.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Html/HtmlDiagramSectionRenderer.cs)): `ProjectLayerChecks.ReportCircularDependencies` repeatedly constructed `NodeReference.ForProject`, scanned/collected outgoing relations from the spine, and sorted arrays during DFS (`1339.28 ms / 1306.92 MB`); diagram/query helpers triggered full `BuildSortedRelations()` sorts (`~3,663` relations) when discovery order or a `RelationKind` presence check sufficed.
4. **Per-reference `NodeReference` / identity string recomputation** ([ProjectId.cs](file:///data/workspace/cqrslens/src/CqrsLens.Core/Spine/ProjectId.cs), [ComponentId.cs](file:///data/workspace/cqrslens/src/CqrsLens.Core/Spine/ComponentId.cs), [EndpointId.cs](file:///data/workspace/cqrslens/src/CqrsLens.Core/Spine/EndpointId.cs), [SymbolId.cs](file:///data/workspace/cqrslens/src/CqrsLens.Core/Spine/SymbolId.cs), [NodeReference.cs](file:///data/workspace/cqrslens/src/CqrsLens.Core/Spine/NodeReference.cs), [SpineRelation.cs](file:///data/workspace/cqrslens/src/CqrsLens.Core/Spine/SpineRelation.cs)): Repeated `NodeReference.ForProject/Component/Endpoint` allocations (`329.39 MB`), redundant `.Replace('\\', '/')` scans, uncached `ProjectId.GetHashCode()`, uncached `SymbolId`/`EndpointId` `ToString()`, and single-occurrence array sort overhead in `SpineRelation`.

### 1B. `relations` JSON Serialization

| Metric (ABP JSON, `3,663` relations / `3,701` occurrences) | `Base (bd49fc3)` | `F1-before` (pre-opt) | `F1-final` (post-opt) | Delta (`F1-final` vs `F1-before`) |
| --- | ---: | ---: | ---: | ---: |
| `Json Full(fresh r2)` (actual `JsonReportGenerator.Generate`) | `4.15 ms` (`6.39 MB`) | `102.76 ms` (`30.12 MB`) | **`12.90 ms` (`13.13 MB`)** | **`-87.4%` (`8.0x` faster) / `-56.4%` MB** |
| `Json CreateDoc(res)` (`CreateDocument` path) | `0.89 ms` (`1.19 MB`) | `12.12 ms` (`6.70 MB`) | **`7.82 ms` (`3.48 MB`)** | `-35.5%` ms / `-48.1%` MB |

`DeepProfiler` confirmation on `modular-monolith/src` (default markers, `46` projects, `710` relations): `Analyze Total` = `441.25 ms` (`74.86 MB`), `Json Full(fresh r2)` = `9.35 ms` (`5.74 MB`).

`AllocationTick` sampling in `JsonReportGenerator.Generate` on ABP:
- `F1-before`: `System.String` `25.62 MB` (reflection enum formatting, DTO property materialization, `JsonSerializer.Serialize(...) + "\n"` full string copy), `JsonSpineNodeRef` `1.12 MB`, `List<JsonSpineEvidence>` `0.31 MB`, boxed enums (`EvidenceRule`, `RelationKind`, `NodeReferenceKind`, `EvidenceStrength`).
- Phase 2 (before comparator fix) still sampled `4.07 MB` of boxed `RelationKind` (`2.24 MB`) and `NodeReferenceKind` (`1.83 MB`) from `Enum.CompareTo` inside `RelationKey.CompareTo` / `NodeReference.CompareTo` during `Spine.Relations` sorting.
- `F1-final`: **`1.53 MB` total sampled ticks** in `JsonReportGenerator.Generate`; zero boxed enums, zero DTO relation/node/evidence graphs, zero extra `\n`-concatenation string copy.

---

## 2. Changes Made & Output-Preservation Invariants

### Spine Per-Scan Optimizations
- **Identity & NodeRef caching** ([ProjectId.cs](file:///data/workspace/cqrslens/src/CqrsLens.Core/Spine/ProjectId.cs), [ComponentId.cs](file:///data/workspace/cqrslens/src/CqrsLens.Core/Spine/ComponentId.cs), [EndpointId.cs](file:///data/workspace/cqrslens/src/CqrsLens.Core/Spine/EndpointId.cs), [SymbolId.cs](file:///data/workspace/cqrslens/src/CqrsLens.Core/Spine/SymbolId.cs), [NodeReference.cs](file:///data/workspace/cqrslens/src/CqrsLens.Core/Spine/NodeReference.cs)):
  - Cached `_hashCode` on `ProjectId` (`StringComparer.Ordinal.GetHashCode(Value)`) and skipped `.Replace('\\', '/')` when `'\\'` is absent.
  - Cached `_toString` on immutable `SymbolId` and `EndpointId`.
  - Added `internal AsNodeReference()` lazy singletons on `ProjectId`, `ComponentId`, and `EndpointId`, returned by `NodeReference.ForProject/ForComponent/ForEndpoint`. Because `NodeReference` instances are immutable and equality/comparison are structural, returning the cached instance preserves every value, hash, and sort order.
- **Unboxed enum comparisons** ([RelationKey.cs](file:///data/workspace/cqrslens/src/CqrsLens.Core/Spine/RelationKey.cs), [NodeReference.cs](file:///data/workspace/cqrslens/src/CqrsLens.Core/Spine/NodeReference.cs), [EvidenceOccurrence.cs](file:///data/workspace/cqrslens/src/CqrsLens.Core/Spine/EvidenceOccurrence.cs)):
  - Replaced `Kind.CompareTo(other.Kind)`, `Rule.CompareTo(...)`, `Origin.CompareTo(...)`, and `Strength.CompareTo(...)` with `((int)...).CompareTo((int)other...)`. Preserves exact `Int32`-backed enum sort order without boxing.
- **Batch project registration** ([SpineProjectedCollection.cs](file:///data/workspace/cqrslens/src/CqrsLens.Core/Spine/SpineProjectedCollection.cs), [SpineTrackedList.cs](file:///data/workspace/cqrslens/src/CqrsLens.Core/Spine/SpineTrackedList.cs), [AnalysisResult.cs](file:///data/workspace/cqrslens/src/CqrsLens.Core/Analysis/AnalysisResult.cs)):
  - Added `internal virtual AddRangeCore` (`internal override` in `SpineTrackedList<T>`) with `onRangeAdded` batch callback `OnProjectsAddedRange`. Registers all `ProjectNode`s into `_spine` before attaching references, producing the exact same final `ProjectNode`s and `References` relations as sequential addition after synthetic rehoming, without quadratic `EnsureProjectLookup` rebuilds or synthetic project churn.
- **Segment-trie project directory lookup** ([SpineIdentityResolver.cs](file:///data/workspace/cqrslens/src/CqrsLens.Core/Spine/SpineIdentityResolver.cs)):
  - Split `_projectLookupStamp` from `_projectDirLookupStamp`; built a case-insensitive path-segment trie (`ProjectDirTrieNode`, linear scan `<= 6` children, binary search `> 6`) plus `_rootDirProject`. Traverses normalized `'/'`-separated segments cleanly in `O(depth)` time and zero string allocation (`ReadOnlySpan<char>`), selecting the identical deepest directory match on exact segment boundaries as the prior prefix scan.
- **Discovery-order list, lazy endpoint indexes, and fast `HasRelation`** ([ArchitectureSpine.cs](file:///data/workspace/cqrslens/src/CqrsLens.Core/Spine/ArchitectureSpine.cs), [SpineRelation.cs](file:///data/workspace/cqrslens/src/CqrsLens.Core/Spine/SpineRelation.cs), [SpineQueries.cs](file:///data/workspace/cqrslens/src/CqrsLens.Core/Spine/SpineQueries.cs), [SpineLinkList.cs](file:///data/workspace/cqrslens/src/CqrsLens.Core/Spine/SpineLinkList.cs), [MarkdownDiagramWriter.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Markdown/MarkdownDiagramWriter.cs), [HtmlDiagramSectionRenderer.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Html/HtmlDiagramSectionRenderer.cs)):
  - Maintained monotonic `_discoveryOrderList` in `ArchitectureSpine` (`BuildRelationsInDiscoveryOrder()` copies clean list with no sort; marked dirty and re-sorted by `DiscoverySequence` only on `RemoveRelation`/`RemoveRelations`/`RehomeSyntheticProject`).
  - Added lazy `_outgoingDiscoveryByFromKind` / `_incomingDiscoveryByToKind` indexes (above threshold) and `HasRelation(RelationKind)`. Updated diagram writers (`HasRelation(RelationKind.UsesDataResolved)`), `SpineQueries`, and `SpineLinkList` to query indexed discovery order cleanly without building full sorted `Spine.Relations`.
  - Added `internal SpineRelation(RelationKey key, EvidenceOccurrence initialOccurrence)` to reuse already-built keys and fast-pathed `Occurrences` (`Count <= 1`) / `Strength` (`Count == 1`).
- **Precomputed layer-cycle adjacency & interface location index** ([ProjectLayerChecks.cs](file:///data/workspace/cqrslens/src/CqrsLens.Analysis/Checks/ProjectLayerChecks.cs), [ServiceCallEdgeResolver.cs](file:///data/workspace/cqrslens/src/CqrsLens.Analysis/Resolution/ServiceCallEdgeResolver.cs)):
  - `ProjectLayerChecks.ReportCircularDependencies` builds sorted project dependency adjacency once from `result.Spine` before DFS instead of querying/sorting per DFS visit. Cycle detection and ordering remain identical.
  - `ServiceCallEdgeResolver` indexes `interfaceLocationByName` once instead of linearly scanning `resolutionBySignature.Values`.

### Relations JSON Serialization Optimizations
- **Direct UTF-8 streaming converter & source-generated STJ context** ([JsonSpineNames.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Json/JsonSpineNames.cs), [PooledByteBufferWriter.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Json/PooledByteBufferWriter.cs), [DirectSpineRelationPlaceholder.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Json/DirectSpineRelationPlaceholder.cs), [DirectSpineRelationsConverter.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Json/DirectSpineRelationsConverter.cs), [JsonReportSerializerContext.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Json/JsonReportSerializerContext.cs), [JsonReportDocument.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Json/JsonReportDocument.cs), [JsonReportDocumentFactory.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Json/JsonReportDocumentFactory.cs), [JsonReportGenerator.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Json/JsonReportGenerator.cs)):
  - `JsonReportGenerator.Generate` calls `JsonReportDocumentFactory.CreateForSerialization(result)`, placing a zero-capacity `DirectSpineRelationPlaceholder` wrapping `result.Spine.Relations` into `Relations` (still `null` when empty, preserving key omission).
  - `DirectSpineRelationsConverter` (wired via `[JsonConverter]` on `JsonReportDocument.Relations` and serialized through `JsonReportSerializerContext` with `WriteIndented = true`, `CamelCase`, `WhenWritingNull`) writes every `SpineRelation`, `NodeReference`, `EvidenceOccurrence`, and `SourceLocation` straight to `Utf8JsonWriter` using `JsonSpineNames` pre-encoded `JsonEncodedText` tokens, exact property declaration order, and identical null/default omissions (`via.Count == 0`, `string.IsNullOrEmpty(method)`, `!isTestProject`, etc.). `Read` delegates cleanly to STJ deserialization for `JsonReportDocumentReader`.
  - `PooledByteBufferWriter` rents an `ArrayPool<byte>.Shared` buffer, writes indented UTF-8 JSON plus the trailing `(byte)'\n'`, and decodes directly once to `string`.
  - `JsonReportDocumentFactory.Create` also memoizes `NodeReference -> JsonSpineNodeRef` and avoids LINQ allocations so `CreateDocument` remains fast and bit-identical.

### Parity Tests Added
- Added [SpinePerfParityTests.cs](file:///data/workspace/cqrslens/tests/CqrsLens.Tests/SpinePerfParityTests.cs) (`12` test cases: enum token parity across all 6 enums against `JsonNamingPolicy.CamelCase`, byte-identical `JsonReportGenerator.Generate` vs reflection DTO serialization across all 7 fixture/mode cases + synthetic all-optional/special-char escaping + `ReadDocument` round-trip, `Projects.AddRange` vs sequential `Add`, `ProjectDirTrieNode` edge cases including root/nested/>6 siblings/backslashes/prefix boundary, and `ArchitectureSpine` indexed queries across removals & synthetic rehoming).

---

## 3. Warmed CLI A/B Medians (`bd49fc3` vs `F1-before` vs `F1-final`)

All CLI benchmarks used `ab_bench.py` (`dotnet <cli.dll> <target> ...`, `1` warmup + `5` interleaved runs in Release mode, pinned `--no-banner`):

### Pre-optimization regression baseline (`bd49fc3` vs `F1-before`, initial session)
| Case | `Base (bd49fc3)` med | `F1-before` med | Delta |
| --- | ---: | ---: | ---: |
| `abp_md` | `4858.8 ms` | `5640.4 ms` | **`+16.08%`** (breach) |
| `abp_json` | `5093.2 ms` | `6013.8 ms` | **`+18.08%`** (breach) |
| `modular_md` (`--markers ICommand,IQuery`) | `848.8 ms` | `879.5 ms` | `+3.61%` |
| `modular_json` (`--markers ICommand,IQuery`) | `787.8 ms` | `872.7 ms` | **`+10.77%`** (breach) |
| `northwind_md` | `763.3 ms` | `805.1 ms` | `+5.48%` |
| `northwind_json` | `726.7 ms` | `778.0 ms` | `+7.05%` |

### Final post-optimization A/B (`bd49fc3` vs `F1-final`, same session)
*(Note: the printed banner in `ab_bench.py` hardcodes the label `"vs F1-before"`, while CLI argument `/tmp/cqrslens-f1-final/cqrslens.dll` ran the final Release optimization build.)*

| Case | `Base (bd49fc3)` med (5 runs ms) | `F1-final` med (5 runs ms) | Delta (`F1-final` vs `Base`) | Target |
| --- | ---: | ---: | ---: | --- |
| **`abp_md`** | `5451.4 ms` (`[5481.0, 5451.4, 4982.2, 5519.8, 4943.3]`) | **`4610.2 ms`** (`[4737.2, 4385.7, 4610.2, 4639.0, 4497.0]`) | **`-15.43%`** | PASS (`<= +5%` aim, `< +10%` bound) |
| **`abp_json`** | `5031.5 ms` (`[5447.1, 5031.5, 5081.7, 4992.5, 5026.7]`) | **`4388.1 ms`** (`[4394.1, 4397.0, 4388.1, 4166.8, 4379.8]`) | **`-12.79%`** | PASS (`<= +5%` aim, `< +10%` bound) |
| `modular_md` (`--markers ICommand,IQuery`) | `836.8 ms` (`[809.6, 863.1, 834.2, 836.8, 880.6]`) | `898.7 ms` (`[852.8, 937.4, 873.5, 898.7, 920.8]`) | `+7.40%` | PASS (`< +10%`) |
| `modular_json` (`--markers ICommand,IQuery`) | `866.0 ms` (`[838.7, 848.4, 887.3, 893.9, 866.0]`) | `894.6 ms` (`[894.6, 887.6, 856.6, 960.2, 990.5]`) | `+3.30%` | PASS (`< +10%`) |
| `northwind_md` | `773.9 ms` (`[769.2, 774.0, 747.3, 773.9, 802.6]`) | `849.6 ms` (`[852.9, 808.7, 780.2, 852.0, 849.6]`) | `+9.78%` | PASS (`< +10%`) |
| `northwind_json` | `780.9 ms` (`[785.1, 807.2, 776.1, 746.6, 780.9]`) | `810.2 ms` (`[818.5, 816.4, 810.2, 780.3, 799.3]`) | `+3.75%` | PASS (`< +10%`) |

Every tested corpus is under the `< +10%` hard bound, and ABP (the target corpus) is **12.8%–15.4% faster than pre-F1 `bd49fc3`** even while emitting the full `6,129,977`-byte JSON report with `3,663` relations.

*(Scoping note: the cloned real-world benchmarks/profiles ran against the compiled optimization build immediately before the final metadata-only visibility narrowing of `SpineProjectedCollection.AddRangeCore` from `protected` to `internal`, which emits identical runtime execution paths and outputs; `eshoponweb` was not cloned/run in this offline VM.)*

---

## 4. Byte-Identity Proof

### 4A. `F1-before` vs `F1-final` SHA-256 & Exit-Code Verification (`47/47` checks passed)
Verified via `verify_sha256.py` with `--generated-at 2026-01-01T00:00:00Z --no-banner` across `abp_text`, `modular_text` (`--markers ICommand,IQuery`), `northwind_text`, `goldencorpus_text`, `adversarial_text`, `sampleshop_text`, `sampleshop_roslyn`, `diffpair`, and `strict`. All `47` checks matched `F1-before` **100% byte-for-byte** (exact SHA-256 hashes & byte lengths listed below). Following the final `AddRangeCore` `protected` → `internal` visibility change, all `28` in-repo SHA-256 checks (`sampleshop_text`, `sampleshop_roslyn`, `goldencorpus_text`, `adversarial_text`, `diffpair`, `strict`) plus `strict` exit code `4` and `--mcp` stdio (`SampleShop`) were re-executed directly against the final binary and matched identically:

| Corpus / Case | Artifact | Bytes | Identical SHA-256 (`F1-before` == `F1-final`) |
| --- | --- | ---: | --- |
| `abp_text` | `report.json` (incl. `3,663` `relations`) | `6,129,977` | `5ce2e80ea90dc43ec55a8a74c287a7ea9b8254fb7cad680dbbd72aa586e58b46` |
| `abp_text` | `report.md` | `125,964` | `9c46e91455bd7f7c2d6a7fac756c0e3f55302942bf4ec9c3d2ebd8438571bd0d` |
| `abp_text` | `report.html` | `165,943` | `7074e0088be0a3933ae76a9604941f02096a905a85e9d04258bb2edf1fd160de` |
| `abp_text` | `report.txt` (`agent`) | `244,001` | `5ce41b3f0490ed34c0fef16bb81511a0d048d11542be3d6e58d704ed2ab02666` |
| `abp_text` | `summary.md` | `2,828` | `0c4db806630b9c807a133077a8a1157b8e7d05686d49fcfd3d6c998f6fc52369` |
| `abp_text` | `report.sarif` | `7,846` | `bc5492dd9f3eadbf4fe51f58e1ee20c4ab99fe3a9cc443492274628424af3c6a` |
| `modular_text` | `report.json` | `2,639,224` | `ea0e6ef46cf0fffe335eab07c9459daf64e1a031d75cee13ab2badef02efbb44` |
| `modular_text` | `report.md` | `327,952` | `5713f0856970f913921038a9b749b8b46076e6cb9164ca117dd22113ddf60d27` |
| `modular_text` | `report.html` | `860,127` | `0f5cbb1b5d3493a88e2d95807bbc299259286ad8d08c407004b074aa23af0b3f` |
| `modular_text` | `report.txt` (`agent`) | `179,365` | `af6518f3c82b73d63e0c029acc64150a84cc917421430b741b93765636388d41` |
| `modular_text` | `summary.md` | `3,705` | `a72ba1f58aa126cce583cbb3288aaff93f42fbd8abf4f2760a981313099ff45f` |
| `modular_text` | `report.sarif` | `252,092` | `565720e5b8de56c6909db9f8434c119461d93e208084134edbf88d081f801908` |
| `northwind_text` | `report.json` | `437,814` | `82d775353c702bd01f9d18c3f4979bc8fee13d6008c2847595daed2aef21d255` |
| `northwind_text` | `report.md` | `62,065` | `e5e30c5c1051fb90d7e1a39cea45eb1c51b8e96974eff8edb5cfd957fa077f8e` |
| `northwind_text` | `report.html` | `154,722` | `b4f73905f1e7aac4cf2375f88360d6d2f2018a024f65b68363b850aaeedbb61f` |
| `northwind_text` | `report.txt` (`agent`) | `21,131` | `926929bf2104c4e935169705afffce8df5f8f1b30be264f5673f61228485a4bb` |
| `northwind_text` | `summary.md` | `2,237` | `8ff3edb53f64ad043f327db3df3f8a0723887e64b3e2b2d1be817bb10a737b0d` |
| `northwind_text` | `report.sarif` | `9,590` | `c1f4f8ca2dbbb50e6b66e08643e2a2ae09bf8ea4a440326c6a4601add664c141` |
| `goldencorpus_text` | `report.json` | `60,600` | `d539853d63593d472552831ba173ff14b5dcfee0feb1522435f9dd69de6e2677` |
| `goldencorpus_text` | `report.md` | `20,957` | `c239d04b379e0919f9225aa436910256a58810fcf20e7dd133afafddc2fa7052` |
| `goldencorpus_text` | `report.html` | `53,906` | `37e6a2d9df049fc5a28abec7ffce67c1a74a86d39978b931af68d6dfc5b8d13b` |
| `goldencorpus_text` | `report.txt` (`agent`) | `5,027` | `81245c7975448d27028253b1542a51a434fd5e017f52947ea4e3e364764c1b23` |
| `goldencorpus_text` | `summary.md` | `590` | `2656a0cf0dadf6f49d20d0f833f19cf818a5743dedb9eb7152773c9a823ad19b` |
| `goldencorpus_text` | `report.sarif` | `2,250` | `641e4c1568c2008a2b17742100c6e87df9971eaa112b702a58b807cb32d7eaae` |
| `adversarial_text` | `report.json` | `13,923` | `c13e1b31fbb1c806f51e8decae4979c785f98b60b988b9c9a29835dc4e4b9e23` |
| `adversarial_text` | `report.md` | `13,972` | `9aa614789f005daf7dde71506321db5f79f42e616e92aec8ef26161f0fedfb1e` |
| `adversarial_text` | `report.html` | `39,348` | `e714a2217298747fd4bd207a214c7c438fd03da5771c0ac65580d6d184d65990` |
| `adversarial_text` | `report.txt` (`agent`) | `2,244` | `edec8711e4cbf4f4a5c359cc337edb7381581bc8b3bf91c199c5deb40e21e491` |
| `adversarial_text` | `summary.md` | `732` | `5cef7c26d97a6a248b236ea8641b7f3a2e06dca6a302768844fc8a9571f722cc` |
| `adversarial_text` | `report.sarif` | `3,151` | `daaf854bbe7e6b56490d5d487c5ff715a5d6252f09e75a21e64497670d2e8e18` |
| `sampleshop_text` | `report.json` | `42,687` | `96d5318ee9227e30664b26c20636ed6093cda64d0dfd538f89a1ff5a0379c808` |
| `sampleshop_text` | `report.md` | `18,499` | `d56fe0d8c37da7bc70d0db9259d4e966ab8faa2b4010b1760169236d5f576cbb` |
| `sampleshop_text` | `report.html` | `52,231` | `2d91f6e7a1fe6962f2cc4587fd98ea09d9dacd75b8891f6d4d5020afc806178f` |
| `sampleshop_text` | `report.txt` (`agent`) | `4,796` | `6f845ce281d2a536c2d3f6c24758710e7737f13046c09beb1ab2390d43974af1` |
| `sampleshop_text` | `summary.md` | `1,343` | `a62cdb5d36777aab7a4d6c038c3c15295ffc483cc75d27429060cf8bd115bf8d` |
| `sampleshop_text` | `report.sarif` | `5,465` | `546925f5209b296f7fac034071dddf8167279c245a5a2ff565bbf74ca6e5bcc2` |
| `sampleshop_roslyn` | `report.json` | `42,689` | `e466f4051f9562ba53d910014a3d45ab986a63fdde371e361aab7f8717b419e6` |
| `sampleshop_roslyn` | `report.md` | `18,501` | `6630bc4b72df6c2a813d8a75709e24ebbecfeb6c5075421c9afe9633b7cf5c33` |
| `sampleshop_roslyn` | `report.html` | `52,233` | `a37ece581a88f1e21f4893d2989fa355959e22e65893147d4bc167e2bacc37c9` |
| `sampleshop_roslyn` | `report.txt` (`agent`) | `4,798` | `8bf081077b3aeb93bc70ba5767fe7869d39c648768ad7eaf6cf52bc7d11a2f41` |
| `sampleshop_roslyn` | `summary.md` | `1,345` | `4a821033a294e3c2ddd520e2c4da0a854fafdb567d2f021cbd558a7c6bfdba19` |
| `sampleshop_roslyn` | `report.sarif` | `5,467` | `d461e66b6b9960eb502017647a91f070b0ce91470c9b5899ea8658206e0b9e79` |
| `diffpair` | `diff.md` | `2,057` | `0c3845fb4bb29b3ed1dfd46a4e0433ac36f354cd9b12569c6c7d78d85d5cc034` |
| `diffpair` | `diff.json` | `2,653` | `20203ce97b72a5755262ef3d9b5ed2e3b55a157264b16e4f394b353e56c064ba` |
| `diffpair` | `head.json` | `1,643` | `fc44ddcbea5f5a1278e1730d4d3f51ffaa5ec405e7b9c1940f2537c8f8be1e86` |
| `strict` | `strict.md` | `6,496` | `7ab3be40f33b1683db6f6467ad8d65fb38c9f2b8d929de51f604f39cb7044492` |
| `strict` | exit code | — | `4` == `4` |

### 4B. Determinism, MCP Stdio, & `bd49fc3` Non-`relations` Parity
- **Determinism (ABP)**: Two independent runs of `F1-final` on `/tmp/corpora/abp` emitted byte-identical `json` (`5ce2e80ea90dc43ec55a8a74c287a7ea9b8254fb7cad680dbbd72aa586e58b46`) and `md` (`9c46e91455bd7f7c2d6a7fac756c0e3f55302942bf4ec9c3d2ebd8438571bd0d`).
- **MCP stdio (`--mcp`)**: `SampleShop` smoke test (`initialize`, `ping`, `tools/list`, `get_architecture_dna`, `trace_slice`, `check_architecture`, `trace_request_impact`; `7` JSON-RPC responses, `17,894` bytes) matched `F1-before` byte-for-byte (verified both pre- and post-visibility edit).
- **`bd49fc3` vs `F1-final` non-`relations` parity (`abp_text`, `modular_text`, `northwind_text`)**: Markdown matched `bd49fc3` byte-for-byte (`True` across all 3 corpora); shared JSON keys had `0` differences (`[]`), with `'relations'` as the sole added top-level JSON key.

---

## 5. Suite & Constraint Verification

- **Test suite (`dotnet test CqrsLens.sln -c Release --nologo`)**: **Passed: `663`, Failed: `0`, Skipped: `68`, Total: `731`** (`2 s`, run after the final edit).
  - Gain vs baseline (`719`): `+12` new passing tests/theory cases in [SpinePerfParityTests.cs](file:///data/workspace/cqrslens/tests/CqrsLens.Tests/SpinePerfParityTests.cs).
  - The `68` skipped tests are external-corpus frozen tests skipped because `CQRSLENS_ABP_ROOT` / `CQRSLENS_CORPUS_ROOT` were unset in the test environment.
- **Constraint compliance**:
  - `git diff bd49fc3 -- '*.csproj' Directory.Build.props CHANGELOG.md README.md ROADMAP.md tests/CqrsLens.Tests/FrozenQuestionSet.cs` is empty; [RequestSequenceBuilder.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Sequences/RequestSequenceBuilder.cs) untouched during this perf pass.
  - Zero new public/protected APIs or NuGet packages added; zero config, version, changelog, or README edits; working tree left uncommitted and unpushed per brief.
