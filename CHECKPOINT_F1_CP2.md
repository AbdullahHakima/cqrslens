# Checkpoint F1 — CP2: AnalysisResult Consolidation, Consumer Migration & Additive JSON

- **Stage:** CqrsLens F1 — Unify on the F0 Spine (`BRIEF_F1.md`, `F0_METAMODEL_v2.1.md`)
- **Checkpoint:** CP2 (`status: done`)
- **Branch:** `foundations/f1-spine` (base commit `bd49fc3`, `release: prepare 1.1.0`; working tree uncommitted per H9)
- **Scope note:** Existing functionality only (H1). No new checks, shape families, analyzer capabilities, CLI flags, version bumps, or `PackageReference` changes (H1, H5).

---

## 1. Spine-Centric `AnalysisResult` & Migration Map

`AnalysisResult.Spine` (`ArchitectureSpine`, `src/CqrsLens.Core/Analysis/AnalysisResult.cs`) is now the single canonical store for projects, layers, components, endpoints, deduplicated typed relations, and unproven populations. All legacy lists and edge collections read from and mutate the spine through version-stamped `SpineProjectedCollection<T>` views without duplicating storage:

| Legacy `AnalysisResult` / Domain Surface | Canonical Spine Backing | Notes |
|---|---|---|
| `Projects`, `Layers` | `ArchitectureSpine.Projects`, `ArchitectureSpine.Layers` | Per-project `ProjectId`, `LayerProvenance`, and `LayerStrength` tracked on `ProjectNode` |
| `Endpoints` | `ArchitectureSpine.Endpoints` (`EndpointNode`) | Mapping-site `EndpointId` with `VerbStrength`, `RouteStrength`, `IsInferred`, and structured `Evidence` |
| `Requests`, `Handlers`, `Validators`, `Behaviors`, `Notifications`, `NotificationHandlers`, `DbContexts`, `Services` | `ArchitectureSpine.Components` (`ComponentNode` with explicit `ComponentRole` sets + evidence) | Components keep stable `SpineId` (`ComponentId`) |
| `FlowEdges` (`List<FlowEdge>` -> `SpineFlowEdgeList`) | `SpineQueries.FlowEdges` over `ArchitectureSpine.RelationsInDiscoveryOrder` | Preserves original discovery-order tie-breaking; synthetic `HandlerToRequest`/`HandlerToNotification` occurrences marked `ExcludeFromDispatchCallSites` |
| `ResolvedEdges` (`SpineResolvedEdgeList`) | `SpineQueries.ResolvedEdges` (`UsesDataResolved`) | Bidirectional add/remove/clear updates `ArchitectureSpine` directly |
| `ServiceCallEdges` (`SpineServiceCallEdgeList`) | `SpineQueries.ServiceCallEdges` (`Calls`) | Deduplicated on `(From, Via, Method, To)` |
| `DispatchCallSites` (`SpineDispatchCallSiteList`) | `SpineQueries.DispatchCallSites` (`Dispatches`) | Excludes non-dispatch synthetic links |
| `UnresolvedDispatches` (`SpineUnresolvedDispatchList`) | `ArchitectureSpine.Unproven.UnresolvedDispatches` | Mutating legacy collection updates `UnprovenPopulations` directly |
| Per-component string link lists (`Handlers`, `Validators`, `TouchedEntities`, `ReferenceNames`, etc.) | `SpineLinkList` (`SpineLinkKind`) | Live projection of incoming/outgoing `SpineRelation`s (e.g., `Serves`, `Validates`, `TouchesData`, `References`) |
| Unproven / ambiguous facts | `ArchitectureSpine.Unproven` (`UnprovenPopulations`) | Explicit buckets: `UnresolvedDispatches`, `AmbiguousResolutions`, `AmbiguousLinks`, `UnresolvedContracts`, `UnestablishedBehaviors` |

### Key Spine Query & Performance Mechanisms
- `SpineQueries` (`src/CqrsLens.Core/Spine/SpineQueries.cs`): shared read-only projections (`FlowEdges`, `ResolvedEdges`, `ServiceCallEdges`, `DispatchCallSites`, `ProjectReferences`, `FormatProjectReferenceTarget`) backed by cached arrays on `ArchitectureSpine`.
- `ArchitectureSpine` (`src/CqrsLens.Core/Spine/ArchitectureSpine.cs`): tracks monotonic versions (`Version`, `ProjectsVersion`, `ComponentsVersion`, `EndpointsVersion`) used by `SpineProjectedCollection<T>` (`CacheStamp`/`InvalidateCache`/`ReadProjection`) and `SpineIdentityResolver` lookup caches; uses `Array.Sort` for sorted views and gates relation dictionary index construction on `RelationIndexThreshold = 64` (exposing `HasOutgoingRelation`, `CountOutgoingRelations`, `HasIncomingRelation`, `CollectOutgoingRelationsUnordered`, and `UnindexedRelations` for zero-allocation checks).
- `AnalysisResult.OnProjectAdded` guards `RehomeSyntheticProject` / `RebindShapeProjectIds` with `ArchitectureSpine.HasAnySyntheticProjects`.

---

## 2. Consumer Migration Inventory

All `Core`, `Analysis`, and `Reporting` producers/consumers were migrated onto the unified model:

- **`src/CqrsLens.Core`:** `Analysis/AnalysisResult.cs`, `Spine/` (`SpineQueries.cs`, `SpineProjectedCollection.cs`, `SpineFlowEdgeList.cs`, `SpineResolvedEdgeList.cs`, `SpineServiceCallEdgeList.cs`, `SpineDispatchCallSiteList.cs`, `SpineUnresolvedDispatchList.cs`, `SpineLinkList.cs`, `SpineLinkKind.cs`, `SpineTrackedList.cs`, `SpineIdentityResolver.cs`, plus CP1 types), `Architecture/` (`BehaviorInfo.cs`, `CqrsNotification.cs`, `CqrsRequest.cs`, `DbContextInfo.cs`, `EndpointInfo.cs`, `EntityRelationshipInfo.cs`, `HandlerInfo.cs`, `NotificationHandlerInfo.cs`, `ProjectInfo.cs`, `ServiceCandidate.cs`, `ValidatorInfo.cs`).
- **`src/CqrsLens.Analysis`:**
  - `Analyzer.cs`
  - `Checks/`: `FlowReachabilityChecks.cs`, `NotificationChecks.cs`, `ProjectLayerChecks.cs`, `ProjectNameIndex.cs`, `RequestHandlerChecks.cs`, `RequestValidatorChecks.cs`
  - `EfCore/EfRelationshipFactory.cs`
  - `Flow/`: `DispatchCallSiteScanner.cs`, `FlowAnalyzer.cs`
  - `Linking/`: `BehaviorRegistrationResolver.cs`, `BehaviorShapeHelper.cs`, `BehaviorWrapsResolver.cs`, `NotificationLinker.cs`, `RequestLinker.cs`
  - `Resolution/`: `HandlerDbContextResolver.cs`, `ServiceCallEdgeResolver.cs`, `ServiceResolutionIndexBuilder.cs`
  - `Roslyn/RoslynBehaviorShapeReader.cs`
  - `Slicing/`: `Endpoints/` (`ControllerEndpointDiscovery.cs`, `EndpointRecorder.cs`, `FastEndpointDiscovery.cs`, `MinimalApiEndpointDiscovery.cs`), `HandlerDbSetEffects.cs`, `HandlerDependencyEffects.cs`, `HandlerDispatchEffects.cs`, `HandlerEffectContext.cs`, `HandlerEffectRollup.cs`, `HandlerMemberEffects.cs`, `HandlerSliceEnricher.cs`
  - `Text/ShapeApplier.cs`
- **`src/CqrsLens.Reporting`:**
  - `Agent/`: `AgentArchitectureDnaWriter.cs`, `AgentArchitectureMapGenerator.cs`, `AgentClaimVerifier.cs`, `AgentRequestImpactGenerator.cs`, `AgentSliceDataGenerator.cs`
  - `Configuration/ConfigurationView.cs`
  - `Html/`: `HtmlDiagramSectionRenderer.cs`, `RequestExplorerIndex.cs`
  - `Json/`: `JsonReportDocument.cs`, `JsonReportDocumentFactory.cs`, `JsonReportDocumentReader.cs`, `JsonSpineEvidence.cs`, `JsonSpineNodeRef.cs`, `JsonSpineRelation.cs`
  - `Markdown/`: `MarkdownCqrsWriter.cs`, `MarkdownDiagramWriter.cs`
  - `Mermaid/`: `FlowDiagramPlanner.cs`, `NotificationDiagramBuilder.cs`, `ProjectDiagramData.cs`, `ProjectDiagramSyntax.cs`
  - `Sequences/RequestSequenceBuilder.cs`

---

## 3. Promoted Relations, Resolution Semantics & F0 §4.3 `Wraps`

1. **`Validates` and `TouchesData` promotion:** Promoted to first-class `RelationKind.Validates` (`validator -> request`) and `RelationKind.TouchesData` (`handler -> DbSet/entity member`, optionally carrying `Via` interface/repository refs) in `ArchitectureSpine.Relations`, verified on `GoldenCorpus` by `SpineF1ParityAndGatesTests.PromotesValidatesAndTouchesDataRelations_OnGoldenCorpus`.
2. **`ResolvesTo` candidate-vs-binding semantics:** Captures `ResolutionSemantics.StaticCandidate` (`EvidenceStrength.Inferred`) when only a static single-implementation candidate exists, `ResolutionSemantics.DeclaredBinding` (`EvidenceRule.ServiceRegistrationBinding`, `EvidenceStrength.Proven`) when explicitly bound in DI, and `ResolutionSemantics.Confirmed` (`EvidenceStrength.Proven`) when single candidate matches binding. Interfaces with multiple ambiguous implementations do not emit `ResolvesTo` edges and are recorded in `ArchitectureSpine.Unproven.AmbiguousResolutions` (verified by `ResolvesTo_CapturesCandidateVsBindingAndExcludesAmbiguous`).
3. **`Wraps` applicability rules (`src/CqrsLens.Analysis/Linking/BehaviorWrapsResolver.cs`, F0 §4.3):**
   - **Closed request marker binding:** Emits `RelationKind.Wraps` with `EvidenceRule.BehaviorClosedBinding` when `ClosedRequestTarget` resolves to a request, or adds `UnestablishedBehaviorReason.ClosedFormUnknown` to `Spine.Unproven.UnestablishedBehaviors`.
   - **Open-generic registration:** When `IsOpenGenericRegistration && RegistrationLocation is not null`, `TryEvaluateConstraints` evaluates `RequestTypeConstraints` and emits `RelationKind.Wraps` (`EvidenceRule.BehaviorOpenGenericRegistration`) only to requests whose `ViaMarker` or base types satisfy all interface constraints; unevaluated constraints (e.g., generic/`struct`/`unmanaged` constraints or zero matches) record `UnestablishedBehaviorReason.GenericConstraintsNotEvaluated`.
   - **Unproven behaviors:** Behaviors without closed targets or evaluable open-generic registrations emit zero `Wraps` relations and are recorded under `UnestablishedBehaviorReason.AssemblyScanningOrExternal` or `UnestablishedBehaviorReason.NoRegistrationSite` (verified by `Wraps_EnforcesClosedOpenGenericAndUnprovenRules_F0Section43`).
4. **H7 `RequestSequenceBuilder` rendering gap (documented, behavior intentionally unchanged):**
   In `src/CqrsLens.Reporting/Sequences/RequestSequenceBuilder.cs` lines 92–95:
   ```csharp
        // H7 / F0 §4.3 rendering gap: sequence diagrams still render BehaviorOrdering.Flattened(result.Behaviors)
        // for every request instead of filtering by RelationKind.Wraps applicability. This gap is intentionally
        // preserved untouched in F1 so rendered output remains byte-identical to baseline (H3/H7).
        var behaviors = BehaviorOrdering.Flattened(result.Behaviors);
   ```
5. **`LayerProvenance` (`src/CqrsLens.Core/Spine/LayerProvenance.cs`):**
   Tracked per project assignment (`LayerProvenance.Configured` vs `LayerProvenance.Guessed`). Guessed projects never carry `EvidenceStrength.Proven` for `LayerStrength`, while configured overrides receive `LayerProvenance.Configured` and `EvidenceStrength.Proven` (verified by `LayerProvenance_TracksConfiguredOverridesIndependentlyFromGuessed`).

---

## 4. Additive JSON `"relations"` Schema

`JsonReportDocument` (`src/CqrsLens.Reporting/Json/JsonReportDocument.cs:53`) appends a single optional property at the end of the document (`schemaVersion` remains `1`):

- Top-level `"relations"`: `List<JsonSpineRelation>?` serialized with `JsonIgnoreCondition.WhenWritingNull` — omitted when empty, and populated in canonical `ArchitectureSpine.Relations` `RelationKey` sorted order when non-empty (25 entries on `GoldenCorpus` Text scan).
- `JsonSpineRelation` (`JsonSpineRelation.cs`): `kind` (`string`), `from` (`JsonSpineNodeRef`), `to` (`JsonSpineNodeRef`), `via` (`List<JsonSpineNodeRef>?`, omitted when empty), `method` (`string?`, omitted when null/empty), `strength` (`string`), `occurrenceCount` (`int`), `occurrences` (`List<JsonSpineEvidence>`).
- `JsonSpineNodeRef` (`JsonSpineNodeRef.cs`): `kind`, `key`, `name`, plus nullable `componentId`, `endpointId`, `projectId`, `symbolId`, `member`, `reason` (omitted when null).
- `JsonSpineEvidence` (`JsonSpineEvidence.cs`): `rule`, `origin`, `strength`, plus nullable `location` (`file`, `line`, `column`), `locationOmittedReason`, `via`, `resolutionSemantics`, `scopeProject`, `testProject` (`true` or omitted when null/false).
- Verified by `JsonReport_EmitsAdditiveTopLevelRelations_AndOmitsWhenEmpty` (including `JsonReportGenerator.ReadDocument` round-trip).

---

## 5. Parity & Test Summary

- **Full test suite:** `651 passed, 68 skipped, 719 total, 0 failed` (the 68 skipped tests are existing external-corpus `FrozenQuestionSet` corpus tests when external corpora are outside the workspace).
- **Fixture parity vs baseline `bd49fc3`:** Cross-checked across all 6 in-repo fixtures (`GoldenCorpus`, `SampleShop`, `Adversarial`, `DiffPair/base`, `DiffPair/head`, `StrictFailure`) in both `Text` and `Roslyn` modes with `--generated-at 2026-01-15T12:00:00Z`. Markdown, HTML, console, SARIF, summary, agent, `--diff`, `--strict` exit code/output, MCP stdio tool outputs, and JSON non-`"relations"` sections matched baseline byte-for-byte.

---

## 6. Open Issues & Reproduction

- **Open issue (intentional per H7):** `RequestSequenceBuilder` still renders `BehaviorOrdering.Flattened(result.Behaviors)` instead of filtering by `RelationKind.Wraps` so F1 rendered output remains byte-identical to baseline (`H3`).
- **Reproduction commands:**
  ```bash
  dotnet test CqrsLens.sln -c Release --nologo
  dotnet test CqrsLens.sln -c Release --nologo --no-build --filter "FullyQualifiedName~SpineF1ParityAndGatesTests"
  ```
