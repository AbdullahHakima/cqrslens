# F3 Intent Layer Format & Check Specification

Status: Settled specification for Stage F3 (`BRIEF_F3.md` D3, R-INTENT; `F0_METAMODEL_v2.1.md` §10; `CqrsLens_Modeling_Foundations.md` R4).

## 1. File Location & Lifecycle

- **File location:** Top-level `"intent"` object inside `.cqrslens.json` (default `<scanRoot>/.cqrslens.json`, or the explicit file passed via `--config <file>`).
- **Read-only guarantee:** CqrsLens / Noctua reads `.cqrslens.json` and its `"intent"` section strictly read-only. The tool **never** creates, modifies, rewrites, or edits `.cqrslens.json` or any declared module, boundary, surface entry, or owner.
- **No-intent parity:** If `.cqrslens.json` is absent, or if the `"intent"` property is omitted or `null`, intent checking does not run: zero intent findings are emitted, existing outputs are unchanged, and exported docs pages omit intent-specific sections.

## 2. Strict Parsing & Fail-Closed Validation

The `"intent"` section is deserialized with `PropertyNameCaseInsensitive = true` and `UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow` (matching `ConfigurationLoader`). Any schema, structural, or semantic validation error throws `ConfigurationException` (CLI exit code `1` / `ExitCodes.Usage`, and `--check-docs` fails closed non-zero):

1. Unknown/unmapped properties anywhere inside `"intent"` or its sub-objects are rejected.
2. Empty or whitespace-only keys or strings (module names, project selectors, component selectors, boundary `from`/`to`, surface items, owner strings) are rejected.
3. Glob or wildcard characters (`*`, `?`, `[`, `]`) are forbidden in all selectors (`modules.*.projects`, `modules.*.components`, `publicSurface.endpoints`, `publicSurface.components`). Only exact identifiers and stated boundary-safe prefixes are allowed.
4. Module names are compared case-sensitively (`StringComparer.Ordinal`) when resolving references, and duplicate/conflicting normalized keys that differ only by leading/trailing whitespace are rejected.
5. `boundaries[].from` and `boundaries[].to` must be non-empty, distinct (`from != to`), and each must name a module declared in `intent.modules`.
6. `ownership` keys must be non-empty and each must name a module declared in `intent.modules`, mapped to a non-empty owner string.
7. `severity` (on a boundary or on `publicSurface`) must be `"warning"` or `"error"` (case-insensitive). Default when omitted or `null`: `"warning"`. Values like `"off"` or unknown severities are rejected.
8. Each module under `modules` must declare at least one non-empty selector across `projects` and `components`.
9. If `publicSurface` is present (non-null), at least one of `endpoints` or `components` must be non-null (an empty `[]` list is valid and asserts an intentionally empty public surface for that dimension, whereas `{}` with both null is rejected).

## 3. Schema Reference

```json
{
  "intent": {
    "modules": {
      "<ModuleName>": {
        "projects": ["<ProjectNameOrProjectId>"],
        "components": ["<ComponentIdOrSymbolPrefix>"]
      }
    },
    "boundaries": [
      {
        "from": "<FromModuleName>",
        "to": "<ToModuleName>",
        "severity": "warning"
      }
    ],
    "publicSurface": {
      "endpoints": ["<EndpointSelector>"],
      "components": ["<ComponentSelector>"],
      "severity": "warning"
    },
    "ownership": {
      "<ModuleName>": "<OwnerNameOrTeam>"
    }
  }
}
```

### 3.1 `modules`

- **Type:** `object` (`Dictionary<string, IntentModuleConfiguration>`), optional.
- **Fields per module:**
  - `projects` (`string[]`, optional): Explicit project names (`ProjectNode.Name`, case-insensitive) or root-relative project IDs (`ProjectNode.Id.Value`, normalized `/` separators, exact ordinal match).
  - `components` (`string[]`, optional): Explicit component selectors or stated prefixes matching `ComponentNode` or typed references:
    1. Exact `ComponentId` (`<ProjectId>::<QualifiedName>`, ordinal match).
    2. Exact `SymbolId.QualifiedName` or `SymbolId.FullName` (ordinal match).
    3. Exact simple `ComponentNode.Name` (ordinal match).
    4. Stated prefix rule: selector `P` matches `ComponentId.Value`, `SymbolId.QualifiedName`, or `SymbolId.FullName` when the value equals `P` or starts with `P` immediately followed by `.`, `::`, `` ` ``, or `<` (identifier/namespace boundary prefix; no arbitrary substring matching and no globs).
- **Module membership:** A `ComponentNode` belongs to module `M` if its declaring `ProjectId` matches any selector in `M.projects` OR the component matches any selector in `M.components`. A target `NodeReference` in a `Calls` relation belongs to `M` if:
  - Its resolved `ComponentId` belongs to `M`, OR
  - Its `SymbolId` matches `M.projects` (via `SymbolId.Project`) or `M.components` (exact/prefix on `QualifiedName`/`FullName`), OR
  - Its `ProjectId` matches `M.projects`.
- **Stale-intent check (`C-INTENT-STALE`):** Every project selector that matches `0` `ProjectNode`s in the scanned model, and every component selector that matches `0` `ComponentNode`s (or `SymbolId` targets) in the model, emits a finding with `Code = "C-INTENT-STALE"`, `Severity = CheckSeverity.Warning` (unless overridden via `rules`), `Target = "<ModuleName>:<Selector>"`.

### 3.2 `boundaries`

- **Type:** `array` of objects, optional.
- **Fields per entry:**
  - `from` (`string`, required): Source module name (must exist in `intent.modules`).
  - `to` (`string`, required): Forbidden target module name (must exist in `intent.modules`, must differ from `from`).
  - `severity` (`string`, optional, default `"warning"`): `"warning"` or `"error"`.
- **Ordering:** Evaluated in canonical sorted order of `(from, to, severity)` and merged into `AnalysisResult.Checks` sorted via `CheckFindingOrdering.Sort` (errors before warnings, then `Code`, then `Message`).

### 3.3 `publicSurface`

- **Type:** `object`, optional.
- **Fields:**
  - `endpoints` (`string[]?`, optional): Declared public HTTP endpoints. A selector matches an `EndpointNode` if it equals (case-sensitive ordinal for routes/IDs, case-insensitive ordinal for HTTP method prefixes):
    - `EndpointNode.Id.ToString()` (`<ProjectId>::<SiteKey>#<Ordinal>`)
    - `EndpointNode.DisplayLabel` (e.g. `POST /orders`, `GET /api/items`)
    - `"{HttpMethod} {Route}"` (case-insensitive HTTP method, exact route)
    - `"{HttpMethod} {Name}"` (case-insensitive HTTP method, exact name)
    - `EndpointNode.Name` (exact ordinal)
    - `EndpointNode.Route` (exact ordinal, when non-empty)
  - `components` (`string[]?`, optional): Declared public components. A selector matches a `ComponentNode` if it equals (exact ordinal):
    - `ComponentNode.Id.Value`
    - `ComponentNode.Symbol.QualifiedName`
    - `ComponentNode.Symbol.FullName`
    - `ComponentNode.Name`
  - `severity` (`string?`, optional, default `"warning"`): `"warning"` or `"error"`.

### 3.4 `ownership`

- **Type:** `object` (`Dictionary<string, string>`), optional.
- **Semantics:** Maps a declared module name (must exist in `intent.modules`) to a human/team owner string.
- **Honesty invariant:** Ownership is **never** checked against source code (no static evidence for ownership exists). It produces **zero** findings. It is rendered in `02-building-blocks.md` (and module listings when applicable) explicitly labelled `declared, unverified`. Any declared module grouping without boundary or surface consequence is likewise marked `declared, unverified`.

## 4. Check Set & Finding Shapes

Only the chartered F3 intent checks exist (`C-BOUNDARY`, `C-SURFACE`, and stale-module-selector `C-INTENT-STALE`):

| Code | Check | Condition | `Target` | `Location` | `Severity` |
|---|---|---|---|---|---|
| `C-BOUNDARY` | Forbidden cross-module call | A `RelationKind.Calls` relation in `Spine.Relations` has source in `boundary.from` and target in `boundary.to` | Source `ComponentId.Value` | First sorted `EvidenceOccurrence.Location` of the `Calls` relation | `boundary.severity` (default `warning`) |
| `C-SURFACE` | Undeclared public endpoint | `publicSurface.endpoints` is non-null and a model `EndpointNode` matches no declared selector | `EndpointNode.Id.ToString()` | `EndpointNode.Location` | `publicSurface.severity` (default `warning`) |
| `C-SURFACE` | Absent declared public endpoint (stale intent) | A selector in `publicSurface.endpoints` matches no model `EndpointNode` | Selector string | `null` | `publicSurface.severity` (default `warning`) |
| `C-SURFACE` | Undeclared public component | `publicSurface.components` is non-null and a model `ComponentNode` matches no declared selector | `ComponentNode.Id.Value` | `ComponentNode.Location` | `publicSurface.severity` (default `warning`) |
| `C-SURFACE` | Absent declared public component (stale intent) | A selector in `publicSurface.components` matches no model `ComponentNode` | Selector string | `null` | `publicSurface.severity` (default `warning`) |
| `C-INTENT-STALE` | Stale module selector | A project or component selector in `intent.modules[M]` matches zero projects/components in the model | `"{ModuleName}:{Selector}"` | `null` | `warning` |

### `C-BOUNDARY` Message Format & Evidence
Every `C-BOUNDARY` finding includes:
- Forbidden boundary rule: `module '<from>' must not call '<to>'`
- Caller identity (`from` `ComponentId`) and callee identity (`to` `NodeReference.Name`)
- Concrete `file:line` evidence from the `Calls` relation's sorted occurrences (plus `Location` set to the primary occurrence `SourceLocation`).

### Strict & Baseline Interaction
- Intent findings are `CheckFinding` records placed in `AnalysisResult.Checks` (subject to existing `.cqrslens.json` `rules` overrides and `--baseline` matching).
- By default (`--check-docs <dir>` without `--strict`), intent findings are reported in `06-findings.md` and in the gate's stdout report without failing a fresh docs tree.
- Under `--strict` (`--check-docs <dir> --strict`), any active (`!IsKnown`) `CheckSeverity.Error` finding — including error-severity `C-BOUNDARY` / `C-SURFACE` findings — causes `--check-docs` to exit `4` (`ExitCodes.StrictFailure`).
- Without `--export-docs` or `--check-docs`, scans with `.cqrslens.json` `intent` run `ArchitectureChecker` identically and emit the exact same `CheckFinding`s; repos without an `intent` section in `.cqrslens.json` behave identically to pre-F3.
