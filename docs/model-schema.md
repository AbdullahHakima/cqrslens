# Noctua Architecture Model Schema (`schemaVersion: 1`)

Machine-readable contract: [`noctua.model.schema.json`](../noctua.model.schema.json) (JSON Schema draft `2020-12`, `$id`: `https://raw.githubusercontent.com/AbdullahHakima/noctua/main/noctua.model.schema.json`).

This document defines the field-level stability promises for the JSON model emitted by `noctua --format json` (and `-o <path>.json`). Every top-level property, nested type, required/optional rule, and enum value below corresponds 1:1 with [`noctua.model.schema.json`](../noctua.model.schema.json).

---

## Versioning & Stability Policy

1. **`schemaVersion` stays `1`.** Noctua 2.0.0 publishes the settled model contract at `schemaVersion: 1` (the same structural contract built across F1–F3). Existing `--diff` consumers, baselines, MCP queries, and scripts continue working without migration.
2. **Additive-only evolution within `schemaVersion: 1`.** Within version `1`, new capabilities may only add new optional top-level sections or new optional fields on existing objects. Consumers must tolerate absent optional/conditional sections.
3. **Breaking changes require a `schemaVersion` bump + dual-accepting reader.** Removing, renaming, changing the type of, or changing the semantics of any field or section (including legacy sections) is a breaking change that requires incrementing `schemaVersion` and shipping a reader that accepts both the old and new versions.
4. **Strict validation (`additionalProperties: false`).** [`noctua.model.schema.json`](../noctua.model.schema.json) sets `additionalProperties: false` at the root and on every object definition in `$defs`, so schema drift is caught immediately by tests rather than silently tolerated.
5. **How consumers pin the contract.**
   - Verify `schemaVersion === 1` before reading fields.
   - Treat the 22 `stable` top-level fields as always present.
   - Treat the 12 `conditional` and `legacy` top-level fields as absent when empty/null (never assume an empty array is emitted for conditional sections).
   - Prefer `components`, `spineEndpoints`, `relations`, and `unproven` over `legacy` projection sections (`endpoints`, `flowEdges`, `resolvedEdges`, `serviceCallEdges`, `dispatchCallSites`, `unresolvedDispatches`).

### Stability Classes

| Class | Meaning | Presence Rule |
| --- | --- | --- |
| `stable` | Frozen `schemaVersion: 1` contract field or section | Always emitted (arrays emit `[]` when empty) |
| `conditional` | Frozen `schemaVersion: 1` contract field or section emitted on demand | Omitted when `null` or empty (never emitted as `null` or `[]`) |
| `legacy` | Legacy-but-stable `schemaVersion: 1` projection kept for `--diff` and pre-spine consumers | Omitted when empty; removal requires a future major `schemaVersion` bump |

---

## Top-Level Model Document

The root object has 34 declared properties: 22 always-present (`required`) fields and 12 conditional/legacy fields omitted when null or empty.

| Property | Type | Presence | Stability | Purpose |
| --- | --- | --- | --- | --- |
| `schemaVersion` | `integer` (`const: 1`) | Required | `stable` | Model JSON contract version (`1`). |
| `toolVersion` | `string` (`minLength: 1`) | Required | `stable` | Semantic version of Noctua that generated the model (e.g. `"2.0.0"`). |
| `scanner` | `string` (`"text" \| "roslyn" \| "unknown"`) | Required | `stable` | Scanner backend (`"text"` or `"roslyn"` for CLI/analyzer runs; `"unknown"` only for uninitialized in-memory `AnalysisResult` objects). |
| `generatedAt` | `string` (`minLength: 1`) | Required | `stable` | ISO-8601 UTC timestamp (`"O"` round-trip format) of model generation. |
| `elapsedMilliseconds` | `integer` (`>= 0`) | Required | `stable` | Elapsed scan time in milliseconds (`0` when `--generated-at` is pinned). |
| `rootName` | `string` | Required | `stable` | Directory name of the scanned solution root. |
| `discoveredFileCount` | `integer` (`>= 0`) | Required | `stable` | Total `.cs` source files discovered before filtering or read failures. |
| `fileCount` | `integer` (`>= 0`) | Required | `stable` | Source files successfully read and scanned. |
| `watermark` | `string` (`minLength: 1`) | Optional | `conditional` | Custom watermark text when provided via `--watermark` or config; omitted when `null`. |
| `conventions` | `string[]` (`minItems: 1`) | Optional | `conditional` | Detected framework conventions (e.g. `Mediator`, `Wolverine`); omitted when empty. |
| `baselineApplied` | `boolean` | Required | `stable` | Whether `--baseline` or `--write-baseline` was applied. |
| `canRunStrictChecks` | `boolean` | Required | `stable` | Whether the scan is complete and unambiguous enough for `--strict` failure gating. |
| `summary` | [`summary`](#summary) | Required | `stable` | Aggregate count summary of discovered shapes, checks, and legacy flow/dispatch totals. |
| `projects` | [`project[]`](#project) | Required | `stable` | Discovered `.csproj` projects, assigned/guessed layers, project references, and package references. |
| `requests` | [`request[]`](#request) | Required | `stable` | Discovered CQRS commands, queries, and marker requests. |
| `handlers` | [`handler[]`](#handler) | Required | `stable` | Discovered CQRS request handlers. |
| `validators` | [`validator[]`](#validator) | Required | `stable` | Discovered FluentValidation and FastEndpoints validators. |
| `behaviors` | [`behavior[]`](#behavior) | Required | `stable` | Discovered pipeline behaviors. |
| `dbContexts` | [`dbContext[]`](#dbcontext) | Required | `stable` | Discovered EF Core `DbContext` types, each containing `dbSets`, `entities`, and `relationships`. |
| `checks` | [`checkFinding[]`](#checkfinding) | Required | `stable` | Architecture and intent check findings (`C-BOUNDARY`, `C-SURFACE`, `C-INTENT-STALE`, and structural rules). |
| `diagnostics` | [`diagnostic[]`](#diagnostic) | Required | `stable` | Scan completeness, ambiguity, and contract-resolution diagnostics. |
| `skippedFiles` | `string[]` | Required | `stable` | Root-relative paths of source files that could not be read or scanned. |
| `notifications` | [`notification[]`](#notification) | Required | `stable` | Discovered domain events and notifications. |
| `notificationHandlers` | [`notificationHandler[]`](#notificationhandler) | Required | `stable` | Discovered notification handlers. |
| `endpoints` | [`endpoint[]`](#endpoint) (`minItems: 1`) | Optional | `legacy` | Legacy CQRS-linked endpoint list. Omitted when empty; prefer `spineEndpoints`. |
| `flowEdges` | [`flowEdge[]`](#flowedge) (`minItems: 1`) | Optional | `legacy` | Legacy flow edges (`endpointToRequest`, `requestToHandler`, `handlerToRequest`, `handlerToNotification`, `handlerToDbContext`). Omitted when empty; prefer `relations`. |
| `resolvedEdges` | [`resolvedEdge[]`](#resolvededge) (`minItems: 1`) | Optional | `legacy` | Legacy DI-resolved `handlerToDbContextResolved` edges. Omitted when empty; prefer `relations`. |
| `serviceCallEdges` | [`serviceCallEdge[]`](#servicecalledge) (`minItems: 1`) | Optional | `legacy` | Legacy handler/component injected-member `serviceCall` edges. Omitted when empty; prefer `relations`. |
| `dispatchCallSites` | [`dispatchCallSite[]`](#dispatchcallsite) (`minItems: 1`) | Optional | `legacy` | Legacy resolved mediator send/publish call sites. Omitted when empty; prefer `relations`. |
| `unresolvedDispatches` | [`unresolvedDispatch[]`](#unresolveddispatch) (`minItems: 1`) | Optional | `legacy` | Legacy unresolved dispatch call sites. Omitted when empty; also recorded under `unproven.unresolvedDispatches`. |
| `relations` | [`spineRelation[]`](#spinerelation) (`minItems: 1`) | Optional | `conditional` | Unified deduplicated spine relations with structured evidence occurrences. Emitted only when unindexed spine relations exist (`Spine.UnindexedRelations.Count > 0`). |
| `components` | [`spineComponent[]`](#spinecomponent) (`minItems: 1`) | Optional | `conditional` | Deduplicated spine components (`one component per declared symbol`) with evidenced roles. Omitted when empty. |
| `spineEndpoints` | [`spineEndpoint[]`](#spineendpoint) (`minItems: 1`) | Optional | `conditional` | Mapping-level HTTP endpoints (controllers, Minimal APIs, FastEndpoints) with per-fact verb/route provenance. Omitted when empty. |
| `unproven` | [`unprovenPopulations`](#unprovenpopulations) | Optional | `conditional` | First-class unproven, ambiguous, and unresolved populations (`totalCount > 0`). Omitted when `totalCount == 0`. |

---

## Object Definitions (`$defs`)

### `summary`

Aggregate count block (`stable`, `additionalProperties: false`). All 16 fields are `integer` (`>= 0`) and required.

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `projects` | `integer` (`>= 0`) | Yes | Count of `projects`. |
| `requests` | `integer` (`>= 0`) | Yes | Count of `requests`. |
| `handlers` | `integer` (`>= 0`) | Yes | Count of `handlers`. |
| `validators` | `integer` (`>= 0`) | Yes | Count of `validators`. |
| `behaviors` | `integer` (`>= 0`) | Yes | Count of `behaviors`. |
| `dbContexts` | `integer` (`>= 0`) | Yes | Count of `dbContexts`. |
| `notifications` | `integer` (`>= 0`) | Yes | Count of `notifications`. |
| `notificationHandlers` | `integer` (`>= 0`) | Yes | Count of `notificationHandlers`. |
| `endpoints` | `integer` (`>= 0`) | Yes | Count of legacy `endpoints`. |
| `errors` | `integer` (`>= 0`) | Yes | Active error findings (excluding baseline-known items when `baselineApplied` is `true`). |
| `warnings` | `integer` (`>= 0`) | Yes | Active warning findings (excluding baseline-known items when `baselineApplied` is `true`). |
| `knownFindings` | `integer` (`>= 0`) | Yes | Findings marked `known: true` when `baselineApplied` is `true`. |
| `diagnostics` | `integer` (`>= 0`) | Yes | Count of `diagnostics`. |
| `flowEdges` | `integer` (`>= 0`) | Yes | Count of legacy `flowEdges`. |
| `dispatchCallSites` | `integer` (`>= 0`) | Yes | Count of legacy `dispatchCallSites`. |
| `unresolvedDispatches` | `integer` (`>= 0`) | Yes | Count of `unresolvedDispatches`. |

### `location`

Root-relative source coordinate (`additionalProperties: false`).

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `file` | `string` | No | Root-relative forward-slash path to the source file, omitted if `null`. |
| `line` | `integer` (`>= 0`) | Yes | 1-based line number (or `0` if synthetic). |
| `column` | `integer` (`>= 0`) | Yes | 1-based column number (or `0` if synthetic). |

### `project`

Item in `projects[]` (`additionalProperties: false`).

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `name` | `string` | Yes | `.csproj` project name. |
| `layer` | `string` | Yes | Clean Architecture layer name (configured override or guessed from name/folder). |
| `references` | `string[]` | Yes | Sorted `ProjectReference` project names within the scan. |
| `packages` | `string[]` | Yes | Sorted NuGet `PackageReference` IDs declared in `.csproj`. |

### `requestProperty`

Item in `requests[].properties[]` (`additionalProperties: false`).

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `name` | `string` | Yes | Property or record-parameter name. |
| `type` | `string` | Yes | Declared C# type name. |

### `request`

Item in `requests[]` (`additionalProperties: false`).

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `name` | `string` | Yes | Simple type name. |
| `namespace` | `string` (`minLength: 1`) | No | Declaring namespace (omitted when global/empty). |
| `fullName` | `string` | Yes | Namespace + containing types + name. |
| `kind` | `string` (`"Command" \| "Query" \| "Request"`) | Yes | Request classification. |
| `responseType` | `string` | No | Response type argument when generic. |
| `viaMarker` | `string` | No | Custom marker interface name when not direct `IRequest`. |
| `project` | `string` | No | Declaring project name. |
| `handlers` | `string[]` | Yes | Linked handler names. |
| `validators` | `string[]` | Yes | Linked validator names. |
| `properties` | [`requestProperty[]`](#requestproperty) (`minItems: 1`) | No | Request payload properties; omitted when empty. |
| `entrypoints` | `string[]` (`minItems: 1`) | No | Dispatching endpoint labels; omitted when empty. |
| `touchedEntities` | `string[]` (`minItems: 1`) | No | Entities/DbSets touched by handlers; omitted when empty. |
| `publishedNotifications` | `string[]` (`minItems: 1`) | No | Notifications published while handling; omitted when empty. |
| `dispatchedRequests` | `string[]` (`minItems: 1`) | No | Downstream requests dispatched; omitted when empty. |
| `location` | [`location`](#location) | No | Source location of the request declaration. |

### `handler`

Item in `handlers[]` (`additionalProperties: false`).

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `name` | `string` | Yes | Simple type name. |
| `namespace` | `string` (`minLength: 1`) | No | Declaring namespace (omitted when empty). |
| `fullName` | `string` | Yes | Full type name. |
| `requestName` | `string` | Yes | Handled request name. |
| `dependencies` | `string[]` (`minItems: 1`) | No | Constructor-injected dependencies; omitted when empty. |
| `touchedEntities` | `string[]` (`minItems: 1`) | No | Touched DbSets/entities; omitted when empty. |
| `publishedNotifications` | `string[]` (`minItems: 1`) | No | Notifications published by this handler; omitted when empty. |
| `dispatchedRequests` | `string[]` (`minItems: 1`) | No | Requests dispatched by this handler; omitted when empty. |
| `project` | `string` | No | Declaring project name. |
| `location` | [`location`](#location) | No | Source location of the handler declaration. |

### `validator`

Item in `validators[]` (`additionalProperties: false`).

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `name` | `string` | Yes | Simple type name. |
| `namespace` | `string` (`minLength: 1`) | No | Declaring namespace (omitted when empty). |
| `fullName` | `string` | Yes | Full type name. |
| `targetType` | `string` | Yes | Validated request or DTO type name. |
| `project` | `string` | No | Declaring project name. |
| `location` | [`location`](#location) | No | Source location of the validator declaration. |

### `behavior`

Item in `behaviors[]` (`additionalProperties: false`).

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `name` | `string` | Yes | Behavior type name. |
| `project` | `string` | No | Declaring project name. |
| `location` | [`location`](#location) | No | Source location of the behavior declaration. |

### `dbContext`

Item in `dbContexts[]` (`additionalProperties: false`).

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `name` | `string` | Yes | `DbContext` class name. |
| `namespace` | `string` (`minLength: 1`) | No | Declaring namespace (omitted when empty). |
| `project` | `string` | No | Declaring project name. |
| `location` | [`location`](#location) | No | Source location of the `DbContext`. |
| `dbSets` | [`dbSet[]`](#dbset) | Yes | Discovered `DbSet<T>` properties on this context. |
| `entities` | [`entity[]`](#entity) | Yes | Discovered EF entities belonging to this context. |
| `relationships` | [`entityRelationship[]`](#entityrelationship) | Yes | Discovered entity relationships on this context. |

### `dbSet`

Item in `dbContexts[].dbSets[]` (`additionalProperties: false`).

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `property` | `string` | Yes | `DbSet<T>` property name on the `DbContext`. |
| `entity` | `string` | Yes | Simple entity type name. |
| `entityFullName` | `string` | No | Full entity type name when resolved. |

### `entity`

Item in `dbContexts[].entities[]` (`additionalProperties: false`).

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `name` | `string` | Yes | Entity simple name. |
| `namespace` | `string` (`minLength: 1`) | No | Declaring namespace (omitted when empty). |
| `fullName` | `string` | Yes | Full entity type name. |
| `project` | `string` | No | Declaring project name. |
| `location` | [`location`](#location) | No | Source location of the entity declaration. |
| `properties` | [`entityProperty[]`](#entityproperty) | Yes | Scalar, key, foreign-key, and navigation properties. |

### `entityProperty`

Item in `dbContexts[].entities[].properties[]` (`additionalProperties: false`).

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `name` | `string` | Yes | Property name. |
| `type` | `string` | Yes | Declared C# type name. |
| `isKey` | `boolean` | Yes | Whether property is part of the primary key. |
| `isForeignKey` | `boolean` | Yes | Whether property is a foreign key. |
| `isNavigation` | `boolean` | Yes | Whether property is a navigation property. |
| `isCollection` | `boolean` | Yes | Whether navigation property is a collection. |
| `targetEntity` | `string` | No | Target entity name for navigation/FK properties. |
| `isRequired` | `boolean` | Yes | Whether non-nullable / `[Required]` / `IsRequired()`. |
| `location` | [`location`](#location) | No | Source location of the property declaration. |

### `entityRelationship`

Item in `dbContexts[].relationships[]` (`additionalProperties: false`).

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `principalEntity` | `string` | Yes | Principal entity name. |
| `dependentEntity` | `string` | Yes | Dependent entity name. |
| `principalNavigation` | `string` | No | Navigation property on principal entity. |
| `dependentNavigation` | `string` | No | Navigation property on dependent entity. |
| `foreignKey` | `string` | No | Foreign-key property name. |
| `isRequired` | `boolean` | Yes | Whether relationship is required. |
| `kind` | `string` (`"oneToOne" \| "oneToMany" \| "manyToMany"`) | Yes | Relationship cardinality. |
| `configuredBy` | `string` | No | Source configuration mechanism when Fluent/annotated. |

### `checkFinding`

Item in `checks[]` (`additionalProperties: false`).

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `code` | `string` | Yes | Architecture or intent rule code (e.g. `MissingHandler`, `C-BOUNDARY`, `C-SURFACE`, `C-INTENT-STALE`). |
| `severity` | `string` (`"error" \| "warning"`) | Yes | Effective finding severity after config overrides. |
| `message` | `string` | Yes | Human-readable finding message with evidence context. |
| `target` | `string` | No | Stable finding target identifier used in baseline matching. |
| `project` | `string` | No | Owning project name when applicable. |
| `known` | `boolean` | Yes | Whether matched by an active `--baseline` or `--write-baseline`. |
| `location` | [`location`](#location) | No | Source location of the finding. |

### `diagnostic`

Item in `diagnostics[]` (`additionalProperties: false`).

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `code` | `string` | Yes | Diagnostic code (e.g. `AmbiguousContract`, `UnresolvedContract`, `SourceFileSkipped`). |
| `severity` | `string` (`"error" \| "warning" \| "info"`) | Yes | Diagnostic severity. |
| `message` | `string` | Yes | Diagnostic explanation. |
| `project` | `string` | No | Related project name when known. |
| `location` | [`location`](#location) | No | Source location when available. |

### `notification`

Item in `notifications[]` (`additionalProperties: false`).

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `name` | `string` | Yes | Simple notification type name. |
| `namespace` | `string` (`minLength: 1`) | No | Declaring namespace (omitted when empty). |
| `fullName` | `string` | Yes | Full type name. |
| `viaMarker` | `string` | No | Marker interface name when indirect. |
| `project` | `string` | No | Declaring project name. |
| `handlers` | `string[]` | Yes | Linked notification handler names. |
| `location` | [`location`](#location) | No | Source location of the notification declaration. |

### `notificationHandler`

Item in `notificationHandlers[]` (`additionalProperties: false`).

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `name` | `string` | Yes | Simple handler type name. |
| `namespace` | `string` (`minLength: 1`) | No | Declaring namespace (omitted when empty). |
| `fullName` | `string` | Yes | Full handler type name. |
| `notificationName` | `string` | Yes | Handled notification type name. |
| `dependencies` | `string[]` (`minItems: 1`) | No | Constructor-injected dependencies; omitted when empty. |
| `touchedEntities` | `string[]` (`minItems: 1`) | No | Touched DbSets/entities; omitted when empty. |
| `publishedNotifications` | `string[]` (`minItems: 1`) | No | Downstream notifications published; omitted when empty. |
| `dispatchedRequests` | `string[]` (`minItems: 1`) | No | Downstream requests dispatched; omitted when empty. |
| `project` | `string` | No | Declaring project name. |
| `location` | [`location`](#location) | No | Source location of the handler declaration. |

### `endpoint`

Legacy-but-stable item in `endpoints[]` (`additionalProperties: false`).

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `name` | `string` | Yes | Endpoint display name (`Controller.Action` or mapping label). |
| `httpMethod` | `string` | Yes | HTTP verb (`GET`, `POST`, etc., or `UNKNOWN`). |
| `route` | `string` | Yes | Route pattern. |
| `targetRequest` | `string` | Yes | Simple target CQRS request name. |
| `targetRequestFullName` | `string` | No | Full target CQRS request name when resolved. |
| `inferred` | `boolean` | Yes | True if verb or route was convention-inferred. |
| `project` | `string` | No | Declaring project name. |
| `location` | [`location`](#location) | No | Mapping site location. |

### `flowEdge`

Legacy-but-stable item in `flowEdges[]` (`additionalProperties: false`).

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `kind` | `string` (`"endpointToRequest" \| "requestToHandler" \| "handlerToRequest" \| "handlerToNotification" \| "handlerToDbContext" \| "unknown"`) | Yes | Legacy flow edge kind. |
| `from` | `string` | Yes | Source participant name. |
| `to` | `string` | Yes | Target participant name. |
| `project` | `string` | No | Declaring/scope project name. |
| `location` | [`location`](#location) | No | Evidence source location. |

### `resolvedEdge`

Legacy-but-stable item in `resolvedEdges[]` (`additionalProperties: false`).

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `kind` | `string` (`"handlerToDbContextResolved" \| "unknown"`) | Yes | Legacy resolved edge kind. |
| `from` | `string` | Yes | Handler full type name. |
| `via` | `string` | Yes | Injected abstraction interface name. |
| `to` | `string` | Yes | Concrete `DbContext` type name. |
| `project` | `string` | No | Scope project name. |
| `location` | [`location`](#location) | No | Evidence source location. |

### `serviceCallEdge`

Legacy-but-stable item in `serviceCallEdges[]` (`additionalProperties: false`).

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `kind` | `string` (`const: "serviceCall"`) | Yes | Always `"serviceCall"`. |
| `from` | `string` | Yes | Calling handler/component full name. |
| `via` | `string` | Yes | Injected interface name. |
| `method` | `string` | Yes | Invoked method name. |
| `to` | `string` | Yes | Resolved implementation type name. |
| `project` | `string` | No | Scope project name. |
| `location` | [`location`](#location) | No | Call-site location. |

### `dispatchCallSite`

Legacy-but-stable item in `dispatchCallSites[]` (`additionalProperties: false`).

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `target` | `string` | Yes | Resolved target request or notification full name. |
| `targetKind` | `string` (`"request" \| "notification"`) | Yes | Contract kind of the dispatched target. |
| `sender` | `string` | No | Sending type full name when resolved. |
| `project` | `string` | No | Scope project name. |
| `testProject` | `boolean` | Yes | True if call site is inside a test project. |
| `location` | [`location`](#location) | No | Call-site location. |

### `unresolvedDispatch`

Item in `unresolvedDispatches[]` and `unproven.unresolvedDispatches[]` (`additionalProperties: false`).

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `method` | `string` | Yes | Invoked mediator method name (e.g. `Send`, `Publish`). |
| `receiver` | `string` | No | Receiver expression name when known. |
| `project` | `string` | No | Scope project name. |
| `location` | [`location`](#location) | No | Call-site location. |

---

## Spine Contract Types (`$defs`)

### `spineRelation`

Item in `relations[]` (`additionalProperties: false`).

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `kind` | [`relationKind`](#relationkind) | Yes | Directed relation kind. |
| `from` | [`spineNodeRef`](#spinenoderef) | Yes | Source addressable node reference. |
| `to` | [`spineNodeRef`](#spinenoderef) | Yes | Target addressable node reference. |
| `via` | [`spineNodeRef[]`](#spinenoderef) (`minItems: 1`) | No | Ordered intermediate node references (`interface`, `component`, etc.); omitted when empty. |
| `method` | `string` (`minLength: 1`) | No | Method name for `calls` relations; omitted when null/empty. |
| `strength` | [`evidenceStrength`](#evidencestrength) | Yes | Aggregate strength of the relation across its occurrences. |
| `occurrenceCount` | `integer` (`>= 1`) | Yes | Total supporting observations (`occurrences.length`). |
| `occurrences` | [`spineEvidence[]`](#spineevidence) (`minItems: 1`) | Yes | Ordered, deduplicated static evidence occurrences. |

### `spineNodeRef`

Addressable node reference used in `spineRelation.from`, `to`, `via[]`, and `spineEvidence.via[]` (`additionalProperties: false`).

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `kind` | [`nodeReferenceKind`](#nodereferencekind) | Yes | Addressable node category. |
| `key` | `string` | No | Canonical key for the referenced node. |
| `name` | `string` | No | Display/qualified name. |
| `componentId` | `string` | No | `ComponentId` (`<ProjectId>::<QualifiedName>`) when referencing a component. |
| `endpointId` | `string` | No | `EndpointId` (`<ProjectId>::<SiteKey>#<Ordinal>`) when referencing an endpoint. |
| `projectId` | `string` | No | `ProjectId` (root-relative `.csproj` path). |
| `symbolId` | `string` | No | `SymbolId` (`<ProjectId>::<QualifiedName>`). |
| `member` | `string` | No | Member name for `method` / `member` references. |
| `reason` | `string` | No | Unresolved explanation for `unresolved` references. |

### `spineEvidence`

Single static evidence occurrence (`additionalProperties: false`).

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `rule` | [`evidenceRule`](#evidencerule) | Yes | Static evidence rule that admitted the observation. |
| `origin` | [`evidenceOrigin`](#evidenceorigin) | Yes | Source artifact origin category. |
| `strength` | [`evidenceStrength`](#evidencestrength) | Yes | Provenance strength (`proven`, `resolved`, or `inferred`). |
| `location` | [`location`](#location) | No | Source location of this observation; omitted when location-less (e.g. `.csproj` reference). |
| `locationOmittedReason` | `string` | No | Explanation when `location` is intentionally omitted. |
| `via` | [`spineNodeRef[]`](#spinenoderef) (`minItems: 1`) | No | Ordered intermediate hops for this occurrence; omitted when empty. |
| `resolutionSemantics` | [`resolutionSemantics`](#resolutionsemantics) | No | Resolution certainty (`staticCandidate`, `declaredBinding`, `confirmed`). |
| `scopeProject` | `string` | No | Scope project name where observation occurred. |
| `testProject` | `boolean` | No | Emitted (`true`) when the observation originates from a test project. |
| `detail` | `string` | No | Additional structured detail (e.g. published event type on `eventPublishCallSite`). |

### `spineComponent`

Item in `components[]` (`additionalProperties: false`).

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `id` | `string` | Yes | Unique `ComponentId` (`<ProjectId>::<QualifiedName>`). |
| `fullName` | `string` | Yes | Qualified symbol name. |
| `name` | `string` | Yes | Simple type name. |
| `project` | `string` (`minLength: 1`) | No | Declaring `ProjectId` (omitted when empty). |
| `location` | [`location`](#location) | No | Source location of the type declaration. |
| `roles` | [`spineRoleEvidence[]`](#spineroleevidence) | Yes | Evidenced architectural roles carried by this symbol, sorted by role name. |

### `spineRoleEvidence`

Item in `components[].roles[]` (`additionalProperties: false`).

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `role` | [`componentRole`](#componentrole) | Yes | Component role name. |
| `evidence` | [`spineEvidence[]`](#spineevidence) | Yes | Evidence occurrences justifying this role. |

### `spineEndpoint`

Item in `spineEndpoints[]` (`additionalProperties: false`).

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `id` | `string` | Yes | Unique mapping-level `EndpointId` (`<ProjectId>::<SiteKey>#<Ordinal>`). |
| `name` | `string` | Yes | Endpoint display name. |
| `httpMethod` | `string` | Yes | HTTP verb (`GET`, `POST`, etc., or `UNKNOWN`). |
| `route` | `string` | Yes | HTTP route pattern. |
| `verbStrength` | [`evidenceStrength`](#evidencestrength) | Yes | Per-fact strength of `httpMethod` (`proven` vs `inferred`). |
| `routeStrength` | [`evidenceStrength`](#evidencestrength) | Yes | Per-fact strength of `route` (`proven` vs `inferred`). |
| `inferred` | `boolean` | Yes | Legacy compatibility flag (`true` if either `verbStrength` or `routeStrength` is `"inferred"`). |
| `project` | `string` (`minLength: 1`) | No | Declaring `ProjectId` (omitted when empty). |
| `location` | [`location`](#location) | No | Mapping site location. |
| `evidence` | [`spineEvidence[]`](#spineevidence) (`minItems: 1`) | No | Endpoint mapping evidence occurrences; omitted when empty. |

### `unprovenPopulations`

Object in `unproven` (`additionalProperties: false`). Emitted only when `totalCount > 0`.

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `totalCount` | `integer` (`>= 1`) | Yes | Total count across all six unproven buckets. |
| `unresolvedDispatches` | [`unresolvedDispatch[]`](#unresolveddispatch) (`minItems: 1`) | No | Dispatch call sites whose target cannot be resolved statically; omitted when empty. |
| `ambiguousResolutions` | [`ambiguousResolution[]`](#ambiguousresolution) (`minItems: 1`) | No | Interfaces with multiple candidate implementations and no single narrowing; omitted when empty. |
| `ambiguousLinks` | [`unprovenDiagnostic[]`](#unprovendiagnostic) (`minItems: 1`) | No | Ambiguous link/contract diagnostics; omitted when empty. |
| `unresolvedContracts` | [`unprovenDiagnostic[]`](#unprovendiagnostic) (`minItems: 1`) | No | Unresolved contracts, entities, navigations, or configurations; omitted when empty. |
| `unestablishedBehaviors` | [`unestablishedBehavior[]`](#unestablishedbehavior) (`minItems: 1`) | No | Pipeline behaviors without provable per-request `wraps` applicability; omitted when empty. |
| `observations` | [`unprovenDiagnostic[]`](#unprovendiagnostic) (`minItems: 1`) | No | Non-edge observations (convention registrations, service locators, dynamic reflection, etc.); omitted when empty. |

### `ambiguousResolution`

Item in `unproven.ambiguousResolutions[]` (`additionalProperties: false`).

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `interfaceSignature` | `string` | Yes | Canonical interface signature. |
| `interfaceName` | `string` | Yes | Interface name. |
| `interfaceLocation` | [`location`](#location) | No | Source location of the interface declaration. |
| `candidates` | [`serviceCandidate[]`](#servicecandidate) | Yes | Candidate implementations held back from `resolvesTo`. |

### `serviceCandidate`

Item in `unproven.ambiguousResolutions[].candidates[]` (`additionalProperties: false`).

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `type` | `string` | Yes | Candidate implementation full name. |
| `declaredInScan` | `boolean` | Yes | True if declared in scanned source. |
| `fromRegistration` | `boolean` | Yes | True if named by an explicit DI registration. |
| `location` | [`location`](#location) | No | Source declaration location. |
| `registrationLocation` | [`location`](#location) | No | DI registration call location. |

### `unestablishedBehavior`

Item in `unproven.unestablishedBehaviors[]` (`additionalProperties: false`).

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `behaviorComponentId` | `string` | No | `ComponentId` of the behavior if inventoried. |
| `behaviorName` | `string` | Yes | Behavior type name. |
| `reason` | [`unestablishedBehaviorReason`](#unestablishedbehaviorreason) | Yes | Why `wraps` applicability could not be established. |
| `registrationSite` | `string` (`minLength: 1`) | No | Registration site label when known; omitted when empty. |
| `project` | `string` (`minLength: 1`) | No | Project name; omitted when empty. |
| `location` | [`location`](#location) | No | Source location of the behavior. |

### `unprovenDiagnostic`

Item in `unproven.ambiguousLinks[]`, `unproven.unresolvedContracts[]`, and `unproven.observations[]` (`additionalProperties: false`).

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `kind` | [`unprovenItemKind`](#unprovenitemkind) | Yes | Unproven item category. |
| `code` | `string` | Yes | Diagnostic or observation code. |
| `subject` | `string` | Yes | Affected type, file, or symbol name. |
| `message` | `string` | Yes | Explanation of what could not be proven. |
| `project` | `string` (`minLength: 1`) | No | Scope project name; omitted when empty. |
| `location` | [`location`](#location) | No | Source location. |

---

## Enumerations (`$defs`)

### `relationKind`

Allowed values: `references`, `serves`, `validates`, `wraps`, `dispatches`, `publishes`, `handlesNotification`, `usesData`, `usesDataResolved`, `resolvesTo`, `calls`, `touchesData`, `relates`, `exposes`.

### `nodeReferenceKind`

Allowed values: `project`, `layer`, `component`, `endpoint`, `interface`, `method`, `member`, `type`, `external`, `unresolved`.

### `evidenceStrength`

Allowed values: `proven`, `resolved`, `inferred`.

### `evidenceOrigin`

Allowed values: `declaration`, `registration`, `callSite`, `mappingOrAttribute`, `projectFile`, `configuration`.

### `resolutionSemantics`

Allowed values: `staticCandidate`, `declaredBinding`, `confirmed`.

### `evidenceRule`

Allowed values: `projectReference`, `requestMarker`, `handlerMarker`, `validatorMarker`, `behaviorClosedBinding`, `behaviorOpenGenericRegistration`, `notificationMarker`, `notificationHandlerMarker`, `dbContextInheritance`, `entityDiscovery`, `endpointMapping`, `dispatchCallSite`, `publishCallSite`, `endpointDispatch`, `directDbContextReference`, `resolvedDbContextDependency`, `serviceDeclarationCandidate`, `serviceRegistrationBinding`, `serviceConfirmedBinding`, `injectedMemberInvocation`, `deepSliceDataAccess`, `entityNavigation`, `entityAnnotation`, `entityFluentConfiguration`, `controllerDiscovery`, `serviceDiscovery`, `repositoryDiscovery`, `controllerEndpointExposure`, `componentMemberInvocation`, `repositoryDataAccess`, `eventPublishCallSite`, `conventionRegistration`, `serviceLocatorCall`, `reflectionDiscovery`, `httpClientRegistration`.

### `componentRole`

Allowed values: `request`, `handler`, `notification`, `notificationHandler`, `validator`, `behavior`, `dbContext`, `entity`, `endpoint`, `controller`, `service`, `repository`.

### `unprovenItemKind`

Allowed values: `ambiguousLink`, `unresolvedContract`, `unresolvedEntity`, `unresolvedNavigation`, `unresolvedConfiguration`, `conventionRegistration`, `serviceLocator`, `dynamicReflection`, `unresolvedEventConsumer`, `unimplementedInterface`.

### `unestablishedBehaviorReason`

Allowed values: `noRegistrationSite`, `assemblyScanningOrExternal`, `genericConstraintsNotEvaluated`, `closedFormUnknown`.
