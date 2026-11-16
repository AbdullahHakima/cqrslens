# CHECKPOINT F1 — CP1: Spine types (additive, no behavior change)

- **Status:** done
- **Branch:** `foundations/f1-spine` (uncommitted working tree, based on `bd49fc3`)
- **Scope check:** Additive spine types in `CqrsLens.Core/Spine/` and unit tests in `tests/CqrsLens.Tests/SpineTypesTests.cs`. Zero consumer changes, zero `AnalysisResult` changes, zero version/README/CHANGELOG changes, zero new PackageReferences.

## Files changed / added

`git status --short`:
```text
?? BRIEF_F1.md
?? F0_METAMODEL_v2.1.md
?? src/CqrsLens.Core/Spine/
?? tests/CqrsLens.Tests/SpineTypesTests.cs
```

`git diff --stat`:
(No tracked files modified in CP1.)

Files added under `src/CqrsLens.Core/Spine/` (28 files, one top-level type per file, `namespace CqrsLens;`):
- `src/CqrsLens.Core/Spine/AmbiguousResolutionEntry.cs`
- `src/CqrsLens.Core/Spine/ArchitectureSpine.cs`
- `src/CqrsLens.Core/Spine/ComponentId.cs`
- `src/CqrsLens.Core/Spine/ComponentNode.cs`
- `src/CqrsLens.Core/Spine/ComponentRole.cs`
- `src/CqrsLens.Core/Spine/ComponentRoleEvidence.cs`
- `src/CqrsLens.Core/Spine/EndpointId.cs`
- `src/CqrsLens.Core/Spine/EndpointNode.cs`
- `src/CqrsLens.Core/Spine/EvidenceOccurrence.cs`
- `src/CqrsLens.Core/Spine/EvidenceOrigin.cs`
- `src/CqrsLens.Core/Spine/EvidenceRule.cs`
- `src/CqrsLens.Core/Spine/EvidenceStrength.cs`
- `src/CqrsLens.Core/Spine/FactProvenance.cs`
- `src/CqrsLens.Core/Spine/LayerProvenance.cs`
- `src/CqrsLens.Core/Spine/NodeReference.cs`
- `src/CqrsLens.Core/Spine/NodeReferenceKind.cs`
- `src/CqrsLens.Core/Spine/ProjectId.cs`
- `src/CqrsLens.Core/Spine/ProjectNode.cs`
- `src/CqrsLens.Core/Spine/RelationKey.cs`
- `src/CqrsLens.Core/Spine/RelationKind.cs`
- `src/CqrsLens.Core/Spine/ResolutionSemantics.cs`
- `src/CqrsLens.Core/Spine/SpineRelation.cs`
- `src/CqrsLens.Core/Spine/SymbolId.cs`
- `src/CqrsLens.Core/Spine/UnestablishedBehaviorEntry.cs`
- `src/CqrsLens.Core/Spine/UnestablishedBehaviorReason.cs`
- `src/CqrsLens.Core/Spine/UnprovenDiagnosticEntry.cs`
- `src/CqrsLens.Core/Spine/UnprovenItemKind.cs`
- `src/CqrsLens.Core/Spine/UnprovenPopulations.cs`

Tests added:
- `tests/CqrsLens.Tests/SpineTypesTests.cs` (12 unit tests)

## F0 Concept Mapping

| F0 Concept | Type Name(s) | File(s) |
|---|---|---|
| ProjectId (§2) | `ProjectId` | `src/CqrsLens.Core/Spine/ProjectId.cs` |
| SymbolId (§2, nested + open/closed generics via `TypeFullName`) | `SymbolId` | `src/CqrsLens.Core/Spine/SymbolId.cs` |
| ComponentId (§2) | `ComponentId` | `src/CqrsLens.Core/Spine/ComponentId.cs` |
| EndpointId (§2 v2.1, mapping-site + ordinal, route/verb excluded) | `EndpointId` | `src/CqrsLens.Core/Spine/EndpointId.cs` |
| Structural nodes (§3, §6) | `ProjectNode`, `LayerProvenance` | `src/CqrsLens.Core/Spine/ProjectNode.cs`, `LayerProvenance.cs` |
| Components with evidenced role sets (§2.1, §3.1) | `ComponentNode`, `ComponentRole`, `ComponentRoleEvidence` | `src/CqrsLens.Core/Spine/ComponentNode.cs`, `ComponentRole.cs`, `ComponentRoleEvidence.cs` |
| Endpoint component + per-fact provenance (§3.1, §3.3, §5.3) | `EndpointNode`, `FactProvenance` | `src/CqrsLens.Core/Spine/EndpointNode.cs`, `FactProvenance.cs` |
| Typed node references (§3.2) | `NodeReference`, `NodeReferenceKind` | `src/CqrsLens.Core/Spine/NodeReference.cs`, `NodeReferenceKind.cs` |
| Unified relations & deduplication key (§4, §5.1 v2.1) | `SpineRelation`, `RelationKind`, `RelationKey` | `src/CqrsLens.Core/Spine/SpineRelation.cs`, `RelationKind.cs`, `RelationKey.cs` |
| Structured evidence & candidate/binding semantics (§4.4, §5.2, §5.3) | `EvidenceOccurrence`, `EvidenceRule`, `EvidenceOrigin`, `EvidenceStrength`, `ResolutionSemantics` | `src/CqrsLens.Core/Spine/EvidenceOccurrence.cs`, `EvidenceRule.cs`, `EvidenceOrigin.cs`, `EvidenceStrength.cs`, `ResolutionSemantics.cs` |
| Unproven populations (§4.3, §5.4) | `UnprovenPopulations`, `UnprovenItemKind`, `AmbiguousResolutionEntry`, `UnprovenDiagnosticEntry`, `UnestablishedBehaviorEntry`, `UnestablishedBehaviorReason` | `src/CqrsLens.Core/Spine/UnprovenPopulations.cs` and sibling files |
| Spine aggregate (§1, §8) | `ArchitectureSpine` | `src/CqrsLens.Core/Spine/ArchitectureSpine.cs` |

## Design Decisions Taken

1. **Active F1 taxonomy only (H1 compliance):** `ComponentRole` defines the 9 active F1 roles (`Request`, `Handler`, `Notification`, `NotificationHandler`, `Validator`, `Behavior`, `DbContext`, `Entity`, `Endpoint`) and `RelationKind` defines the 13 active F1 relations (`References`, `Serves`, `Validates`, `Wraps`, `Dispatches`, `Publishes`, `HandlesNotification`, `UsesData`, `UsesDataResolved`, `ResolvesTo`, `Calls`, `TouchesData`, `Relates`). F2-planned items (`Service`, `Repository`, `Controller`, `BackgroundWorker`, `Exposes`) are omitted until F2 per H1 ("No new shape families").
2. **Value equality & deterministic ordering:** `SymbolId`, `NodeReference`, `RelationKey`, and `EvidenceOccurrence` precompute hash codes and implement sequence-aware equality over ordered `ContainingTypes`, `TypeArguments`, and `Via` lists, plus total ordering (`IComparable<T>` with `<, <=, >, >=` operators). `ArchitectureSpine` sorts collections deterministically on read regardless of insertion order.
3. **Mapping-level `EndpointId` vs symbol-level `ComponentId`:** `ComponentId` wraps `SymbolId` exclusively for symbol-derived components (`One declared symbol yields at most one component`). `EndpointId` captures `(ProjectId, MappingSite, Ordinal)` with `FromSyntheticSite` reserved `internal` for unlocated legacy unit-test fixtures during CP2 migration.
4. **Invariants enforced by construction:** `ComponentRoleEvidence` requires `>= 1` `EvidenceOccurrence`; `EndpointNode` requires `>= 1` `EvidenceOccurrence`; `EvidenceOccurrence` requires either a non-null `Location` or an explicit `LocationOmittedReason`; `SpineRelation.AddOccurrence` rejects any occurrence whose `Via` path disagrees with `RelationKey.Via`.
5. **External interfaces in `NodeReference`:** `NodeReference.ForInterface(SymbolId)` addresses scanned interfaces while `NodeReference.ForExternalInterface(string)` addresses interfaces declared outside the scan (e.g., registered MS-DI/framework interfaces), separate from `NodeReference.ForExternal(string)` used for external implementation targets.

## Test Results

- Full solution suite:
  `Passed! - Failed: 0, Passed: 633, Skipped: 68, Total: 701, Duration: 3 s - CqrsLens.Tests.dll (net8.0)`
  (689 existing tests = 621 passed + 68 skipped external-corpus tests; + 12 new `SpineTypesTests` = 701 total, 0 failures.)
- Targeted CP1 + architecture boundary tests:
  `Passed! - Failed: 0, Passed: 20, Skipped: 0, Total: 20, Duration: 61 ms - CqrsLens.Tests.dll (net8.0)`
- `CqrsLens.Core` built with 0 warnings.

## Open Issues

None at CP1.

## Reproduction Commands

```bash
export DOTNET_ROOT=$HOME/.dotnet
export PATH=$HOME/.dotnet:$PATH
dotnet test CqrsLens.sln -c Release --nologo
dotnet test CqrsLens.sln -c Release --nologo --no-build --filter "FullyQualifiedName~SpineTypesTests|FullyQualifiedName~ArchitectureBoundaryTests"
```
