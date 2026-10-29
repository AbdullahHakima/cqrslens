# Architecture

CqrsLens is split into four production projects with a one-way dependency chain.
The split keeps parsing, analysis, rendering and CLI concerns independent, and it is
enforced by `ArchitectureBoundaryTests` rather than convention alone.

## Dependency rule

```text
CqrsLens.Cli
    └── CqrsLens.Reporting
            └── CqrsLens.Analysis
                    └── CqrsLens.Core
```

Arrows point at referenced projects. `Analysis` must never reference `Reporting` or
`Cli`; `Core` has no project references. Reporting may reference Analysis because
analysis models are renderer inputs, but not the reverse.

| Project | Responsibility |
| --- | --- |
| `CqrsLens.Core` | Public analysis model (`AnalysisResult`, shapes, locations, diagnostics), source text masks/namespaces, progress and diff exceptions, shared tool identity. |
| `CqrsLens.Analysis` | Project/source discovery, text and Roslyn scanners, type hierarchy and marker resolution, EF model construction, deep vertical-slice tracing, architecture checks, baseline and diff logic. |
| `CqrsLens.Reporting` | Markdown/HTML/JSON/agent/SARIF/summary renderers, Mermaid and SVG generation, console artwork. |
| `CqrsLens.Cli` | Argument/config parsing, command orchestration, MCP server, the `cqrslens` executable. |

Tests and benchmarks are outside the production graph. Production projects must not
reference either.

## Scan pipeline

`Analyzer` owns the orchestration and records an `AnalysisPhaseTiming` for every
stage. The hot path is intentionally linear:

1. **Discover projects** from `.csproj` files and classify layers.
2. **Read sources** in parallel.
3. **Parse once** into `SourceWorkspace`. Each file is read once, gets one
   `SourceMask` (comments/strings/code positions), and one cheap `CSharpScanner`
   scan (classes, interfaces, usings, properties). The text scanner, Roslyn
   scanner, EF parser and deep-slice analyzer all reuse that workspace.
4. **Scan shapes** with the selected `IArchitectureScanner`. Text mode applies the
   cheap scan directly, then follows marker interfaces, source wrappers and
   notifications. Roslyn mode binds the same workspace through an in-memory
   compilation and its injected contract shims.
5. **Link** handlers and validators to requests by fully-qualified name, falling
   back to a simple name only when it is unambiguous.
6. **Deep slice** request properties, handler dependencies, touched `DbSet`s,
   notifications, controllers, Minimal APIs and FastEndpoints.
7. **Run checks** and attach severities.
8. **Render** to the requested format and destination.

Both scanners produce the same `AnalysisResult`; reporting and checks never branch
on the scanner implementation.

## Class-level responsibilities

Public entry points are thin facades. Their implementation collaborators are
`internal` so callers keep the stable API while each file owns one concern.

**Core**

- `Architecture/` holds the shape model (`CqrsRequest`, `HandlerInfo`, EF
  entities and relationships, endpoints, projects), one type per file.
  `TypeFullName` is the single implementation of full-name composition.
- `Analysis/` holds the aggregate result, diagnostics, progress and timing
  contracts.
- `Baseline/` separates the on-disk document, stable keys, storage and matching.
- `Checks/`, `Configuration/` and `Source/` hold findings, glob/options and
  source-location contracts.

**Analysis**

- `Analyzer` orchestrates discovery, scanning, linking, deep slicing and checks.
  `ProjectLocator` and `SourceWorkspaceBuilder` own workspace discovery and
  parsing, `AnalysisPhaseRecorder` owns phase timing, and `ShapeLinker` with
  `RequestLinker`/`NotificationLinker` (over the generic `LinkIndex<T>`) owns
  shape linking and fail-closed ambiguity diagnostics.
- `CSharpScanner` is the public text-scanner facade; `SourceFileScanner`,
  `ShapeApplier`, `CSharpSyntax` and `ScanIdentityIndex` do the scanning,
  shape application, syntax parsing and fail-closed identity lookup.
  `TextArchitectureScanner` orchestrates `ContractRoots`, `GlobalUsingApplier`,
  `InterfaceMapBuilder`, `ContractDiagnostics`, `MarkerRequestDiscoverer`,
  `MarkerNotificationDiscoverer` and `IndirectShapeDiscoverer`.
- `TypeHierarchy` is a facade over `TypeIndex`, `TypeNameResolver`,
  `GenericBindings`, `RequestShapeResolver` and `ShapeTargetResolver`.
- `DeepSliceAnalyzer` orchestrates endpoint discovery, request payloads, DbSet
  mapping, domain events, handlers, receiver types, dependencies and syntax
  helpers through dedicated collaborators. Endpoint discovery is split into
  `ControllerEndpointDiscovery`, `MinimalApiEndpointDiscovery` and
  `FastEndpointDiscovery` behind `ApiEndpointDiscovery`, with shared
  `EndpointRequestMatcher`, `EndpointRoutes` and `EndpointRecorder`.
  `HandlerSliceEnricher` runs `HandlerTargetEnricher` over `HandlerEffectContext`,
  whose dependency, DbSet, dispatch and domain-member passes are separate
  collaborators (`HandlerDependencyEffects`, `HandlerDbSetEffects`,
  `HandlerDispatchEffects`, `HandlerMemberEffects`); `HandlerEffectRollup`
  projects their effects onto requests. `DomainEventIndexBuilder` orchestrates
  `DomainWrapperIndexer`, `DomainRepositoryIndexer` and
  `DomainMemberEventIndexer` into a `DomainEventIndexCollector`;
  `EnclosingMemberLocator` attributes expressions to their declaring member.
- EF Core model construction shares `EfKeyIndex`, `EfEntityFactory`,
  `EfEntityStubFactory` and `EfEntityOrdering` between
  `EfCoreTextModelBuilder` and `EfCoreRoslynModelBuilder`; relationships share
  `EfRelationshipFactory`, `EfForeignKeyResolver` and
  `EfNavigationRelationshipResolver`. The Roslyn backend indexes declarations
  through `RoslynEntityIndex` and resolves types through `RoslynEntityResolver`.
- `RoslynArchitectureScanner` orchestrates compilation, symbol facts, contract
  diagnostics, CQRS shapes and EF shapes. Each shape family has its own reader:
  `RoslynRequestShapeReader`, `RoslynHandlerShapeReader`,
  `RoslynNotificationShapeReader`, `RoslynNotificationHandlerShapeReader`,
  `RoslynValidatorShapeReader`, `RoslynBehaviorShapeReader` and
  `RoslynEfShapeReader`.
- `EfFluentConfigurationParser` orchestrates `FluentSourceReader` (method bodies
  and statements), `FluentCallReader` (call/member tokenization),
  `FluentTypeResolver` (entity/property resolution), `FluentStatementParser`
  (statement interpretation) and `FluentRelationshipBuilder` (cardinality and
  principal/dependent selection).
- `ArchitectureChecker` is a facade over `ProjectNameIndex`,
  `RequestHandlerChecks`, `ProjectLayerChecks` (layer violations and circular
  dependencies), `RequestValidatorChecks`, `NotificationChecks` and
  `CheckFindingOrdering`.
- `CsprojParser` is a facade over `ProjectFileDiscovery`,
  `MsBuildProjectLoader` and `ProjectReferenceResolver`. `MediatorConventions`
  is a facade over `MediatorLibraryDetector` and `WolverineHandlerDiscovery`.
- `ArchitectureChecker`, `Baseline` and `SourceWorkspace` remain independently
  testable services.

**Reporting**

- `ReportGenerator` renders Markdown; `JsonReportGenerator`,
  `SarifReportGenerator`, `SummaryGenerator` and `GitHubAnnotations` own their
  respective formats.
- `HtmlReportGenerator` assembles the page shell and stat cards.
  `HtmlReportAssets`, `HtmlDiagramSectionRenderer`,
  `HtmlRequestExplorerRenderer`, `HtmlNotificationSectionRenderer`,
  `HtmlBehaviorSectionRenderer`, `HtmlDbContextSectionRenderer`,
  `HtmlDiagnosticsSectionRenderer` and `HtmlCheckRenderer` own individual
  sections; `HtmlReportText` is the shared encoder.
- `DiagramBuilder` is the diagram facade over `MermaidDiagramText`,
  `ProjectDiagramBuilder`, `NotificationDiagramBuilder`,
  `CqrsDiagramBuilder` and `ErDiagramBuilder`.
- `MermaidSvgRenderer` dispatches to `MermaidFlowchartRenderer` and
  `MermaidErRenderer`; `MermaidEntityDecoder` and `MermaidSvgText` provide the
  shared decoding and formatting primitives.
- `AgentReportGenerator` exposes the agent reports while
  `AgentArchitectureMapGenerator`, `AgentDnaIndexGenerator`,
  `AgentEntityImpactGenerator` and `AgentChecksSummaryGenerator` build each
  output. `AgentReportFormatter`, `AgentReportLookups`, `AgentSliceMatcher` and
  `AgentSliceWriter` hold the shared ranking, formatting and trace logic.

**CLI**

- `ArchitectureDiffer` is the public diff facade over
  `ArchitectureSnapshotLoader`, `ArchitectureDiffCalculator` and
  `ArchitectureDiffMarkdownRenderer`; `ArchitectureDiffModels` holds the
  serializable diff contract.

## Where changes belong

- New shape data goes in the matching `CqrsLens.Core/Architecture` type file;
  diagnostics, checks, baseline and configuration contracts each live in their
  own folder, one type per file.
- Discovery, resolution and checks go in `CqrsLens.Analysis`.
- Only presentation belongs in `CqrsLens.Reporting`.
- CLI flags, config validation and output routing stay in `CqrsLens.Cli`.
- Add a golden-corpus expectation and an architecture-boundary assertion when a
  new shape or output field crosses a project boundary.
- Add a section to the matching renderer collaborator rather than growing a
  facade; keep facades limited to orchestration and API compatibility.

## Correctness policy

Text mode is deliberately conservative:

- It does not guess between ambiguous candidates. Ambiguous links are reported as
  diagnostics instead of being silently attached.
- Duplicate notifications in the same namespace fail closed rather than picking one.
- Source-declared wrappers are followed; external package wrappers require explicit
  handler markers.
- Declarations, not arbitrary text, define shapes. Comments and string literals are
  masked before matching.
- Golden fixtures are checked against hardcoded expectations and both text and
  Roslyn must produce the same shape sets. Decoy declarations, determinism and
  root-relative locations are part of the corpus.

## Performance model

The expensive work is parsing, shape application, EF model construction and deep
slicing. The implementation therefore:

- parses each file exactly once per analysis;
- reuses source masks and cheap scans instead of rebuilding them in each analyzer;
- skips indirect hierarchy resolution for roots already handled directly;
- gates endpoint/EF/constructor regex work with cheap source checks;
- uses span-based top-level splitting instead of character-by-character
  concatenation;
- records wall time and allocated bytes per phase for both `--profile` and the
  benchmark harness.

See the README's **Profiling and benchmarks** section for commands and representative
numbers. `scripts/benchmark.ps1` is the repeatable entry point for warm and
cold-process measurements.
