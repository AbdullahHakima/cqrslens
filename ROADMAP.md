# CqrsLens Roadmap

What is planned, in the order I expect to work on it. Issues are linked
where they exist; the dates are not promises.

## Unreleased

- **Universal .NET Architecture Support (Mediator-independent)**:
  - `ArchitectureStyleDetector` (`architectureStyles` in JSON/Markdown/HTML/CLI/`get_architecture_dna`)
    detects Layered / N-Tier, Minimal API, MVC / Controllers, gRPC Services,
    Event-Driven, Background Workers, Blazor / Razor UI, Microservices /
    Distributed, and CQRS / Mediator codebases without assuming MediatR.
  - Adaptive zero-CQRS reporting (`ConsoleRenderer`, `MarkdownRenderer`,
    `HtmlReportRenderer`, `InteractiveReportBuilder`): omits empty
    `0 Requests / 0 Handlers / 0 Behaviors` clutter and adapts stat cards when
    no CQRS contracts exist, while rendering `DirectFlowDiagramBuilder` direct
    application flows and `DirectSequenceBuilder` `Operation Sequences` (`sequenceDiagram`
    with zero `Mediator` participants) — verified on `nopCommerce` (`41`
    projects, `962` system entrypoints, `116` integration flows, `48` SVGs, `12`
    `Operation Sequences`, `4,999 ms` Text scan).
  - `CircularServiceDependency` (`ServiceCycleChecks`) detects circular
    constructor-injected service dependencies in non-test projects (configurable
    in `cqrslens.json` / `KnownRules`, `0` false positives across the 10-repo
    OSS gauntlet).
  - Universal `SliceDesign` / `--draft-slice` / `--slice-design` / `plan_slice_tests` /
    `--tdd-check` (`Command|Query|Request|Endpoint|Grpc|Worker|Consumer|Service`,
    Handler-free blueprints via `SliceDesignDiagramBuilder`, non-`/` triggers
    for `GRPC`/`WORKER`/`CONSUME`), universal `--impact-kind` (`entrypoint`,
    `event`), and `verify_claim` (`communicates with` across `IntegrationFlows`
    plus direct `SystemEntrypoint` evidence) — verified end-to-end on `dotnet/eShop`
    non-MediatR `CatalogApi.CreateItem` (`Endpoint`), `BasketService.UpdateBasket`
    (`Grpc`), and `CatalogItem` (`entity`), plus `12/12` `UniversalArchitectureTests`
    (`799` passing xUnit tests total, `72/72` draft round-trips, `0` parity deltas).
- **Big-picture system intelligence & full coverage**:
  - System-wide entrypoint discovery (`SystemEntrypoints`) across HTTP Minimal
    APIs, MVC controllers, FastEndpoints, gRPC `*Base` services, Blazor
    `@page` / `[Route]` components, Razor Pages, `BackgroundService` /
    `IHostedService` / `IJob` workers, and message consumers
    (`IIntegrationEventHandler<T>`, `IConsumer<T>`, `IHandleMessages<T>`,
    `IEventHandler<T>`, `[CapSubscribe]`) — covering both CQRS and direct
    non-MediatR execution paths with bounded multi-hop DI tracing.
  - Cross-service `IntegrationFlows` (integration-event pub/sub, `.NET Aspire`
    `AddProject` / `AddDotnetProject` / `.WithReference`, typed/named HTTP and
    gRPC clients, and shared `DbContext`s) plus `ServiceProfiles` with runtime
    roles (`Api`, `Worker`, `Ui`, `Orchestrator`, `Library`, `Tests`),
    technology stack detection, and `Start Here` entry files.
  - DDD entity domain-event tracking (`AddDomainEvent` / `RaiseDomainEvent` /
    `QueueDomainEvent`) eliminating false `UnpublishedNotification` warnings,
    clean `ImpactDiagramBuilder` endpoint labels, and deduplicated
    production-only `UnresolvedDispatch` blind spots.
  - C4-style `SystemLandscape` diagram, `--view overview|full`, `--focus NAME`,
    `--exclude-tests`, progressive-disclosure HTML portal (`<nav class="toc">`
    sidebar + native `<details>` accordions, zero-JS SVG with strict CSP),
    additive JSON (`systemEntrypoints`, `integrationFlows`, `serviceProfiles`),
    and 14th MCP tool `get_system_overview`.
- **Modeling, impact & TDD engine**:
  - Rich versioned slice designs (`properties`, `publishes`, `dispatches`,
    `persistence`, `dependencies`, `forbiddenDependencies`) verified via
    `verify_slice_design` or `--slice-design` (supporting both single files and
    recursive `*.slice.json` directory batches).
  - `draft_slice_design` / `--draft-slice` drafts schema-valid v1 slice designs
    from scanned facts and surfaces gaps and open questions.
  - Upgraded `evaluate_architecture_proposal` / `--proposal` with conditional
    MSBuild reference parsing, `TargetFrameworks`, `SelfProjectReference`,
    `UnknownLayerName`, `TransitiveLayerViolation`, coupling `metrics`, and
    `blastRadius`.
  - Bidirectional `analyze_change_impact` / `--impact` tracing across projects,
    slices, services, notifications, DbContexts, entities, and test references
    with `uncoveredHotspots` and `blindSpots`.
  - Closed-loop test intelligence (`TestProjectDiscovery`, enriched
    `plan_slice_tests`, `TrxTestResultsParser`, `verify_tdd_cycle` /
    `--tdd-check --test-results` including recursive directory `*.slice.json`
    batch evaluation `TddBatchEvaluation`) plus expanded `verify_claim` relations
    (`calls service`, `references project`, `touches entity`, `has test reference`,
    and multi-hop DI-resolved `uses DbContext`).
  - Multi-hop DI `Service → Repository → DbContext` / `IDbContextFactory<T>`
    lineage (`ResolvedEdge.ViaChain`, open-generic repository fallback, transitive
    `TouchedEntities`, fail-closed cycle/ambiguity handling) and modern .NET
    entrypoint discovery: chained `MapGroup` + route-first Minimal API method
    groups, Razor Pages (`PageModel` `OnGet*`/`OnPost*` + `.cshtml` `@page` +
    `[BindProperty]`), and Blazor components (`.razor` `@page` / `@code` and
    `.razor.cs` `[Route]`, `HttpMethod = "BLAZOR"`).
  - Visual modeling & TDD reporting (`--format md|html`, `--render mermaid|svg`
    offline zero-JS SVG with strict CSP, and `-o`) across `--proposal` (including
    `--strict`), `--impact`, `--slice-design`, and `--tdd-check`, plus GitHub
    Action (`action.yml`) Markdown step summaries for `slice-design`,
    `tdd-check`, and `test-results`.
  - Hardened MCP server (`McpPathGuard` workspace confinement with
    `--allow-root` / `--allow-external-paths`, three delimiter-isolated MCP
    prompts: `model_feature`, `plan_refactor`, `review_architecture_change`),
    DTD/XXE-safe `SafeXmlLoader`, `BoundedFileReader`, framed stdio bounds, and
    zero-JS SVG strict CSP.

## Done

**1.1.0** (November 2026)

- **DI-resolved DbContext edges**: handlers that depend on a DbContext
  abstraction resolve through the DI registrations to the concrete
  context, and the edge is drawn on flow diagrams, per-request
  sequences, JSON and MCP answers
- **Direct service calls**: calls a handler makes on injected services
  are extracted and drawn — a `serviceCallEdges` field in JSON,
  service-call steps on diagrams and sequences, and service calls in
  MCP request-impact answers
- **Faster Resolve phases**: the service-resolution analysis shares
  one type hierarchy and memoizes type-name resolution, with
  byte-identical reports

**1.0.0** (November 2026)

- **Per-request sequence diagrams**: endpoint → mediator → behaviors →
  handler → DbContext → notifications, drawn per handled request in
  Markdown and HTML
- **An MCP layer for agents**: `verify_claim` answers whether a claim
  about the codebase is supported by the scan, with evidence;
  `diff_architecture`, filtered and grouped `check_architecture`,
  structured `trace_slice`, `trace_request_impact` — all read-only
- **Console identity**: the Noctua owl lockup, lapis palette,
  single-column summary card, `--ascii` mode for terminals that need it
- **Diagram exports completed**: sequence diagrams in `--export-svg`,
  and `--export-mermaid` for standalone .mmd files

**0.9.0** (November 2026)

- **Flow diagrams**: the report now draws the dispatch flow it already
  tracked — endpoint → request → handler → DbContext — in Markdown and
  HTML (Mermaid and inline SVG), each diagram captioned with what to look
  at; dispatches the scanner cannot resolve are counted in the caption,
  never drawn as edges
- **Behavior chain**: the pipeline behaviors around a request drawn as one
  chain, in the order they were found in the code
- **CQRS map splitting**: `--cqrs-map-split count|feature|unsplit` (or
  `cqrsMapSplit` in the config file) — one diagram per feature, or a
  single unsplit map per project
- **Standalone SVG export**: `--export-svg <directory>` writes every
  diagram in the report as its own .svg file, for docs and READMEs

**0.8.0** (October 2026)

- **Dispatch call-site tracking & flow graph**: find `Send` / `Publish` call
  sites across MediatR, Mediator and Wolverine (including handler → request
  chains and handler → notification publishes), export flow edges in JSON, and
  report statically unresolvable dispatches as unresolved instead of guessing
- **Reachability checks**: `UnreachableRequest` and `UnpublishedNotification`
  warnings for handled requests nothing in production sends and handled
  notifications nothing publishes; test-project sends never count
- **Domain events & notifications (`INotification` / `IDomainEvent`)**: discover
  notifications and 1-to-many `INotificationHandler<T>` / `IDomainEventHandler<T>`
  handlers in both `text` and `roslyn` modes, and render fan-out diagrams and tables
  in Markdown, HTML (Mermaid and inline SVG), and JSON reports
- **Expanded architecture rules**: `MultipleHandlers` (error), `ApplicationLayerViolation`
  (error), `CircularProjectDependency` (error), `OrphanValidator` (warning), and
  `OrphanNotification` (warning), all configurable in `.cqrslens.json` and emitted to SARIF

- **Interactive HTML explorer**: click any request (in the Requests table, explorer
  cards, or Mermaid CQRS diagram) to inspect its contract, response type, handlers,
  validators, source `File:Line:Col` locations, and per-request architecture check
  findings, with filter pills (`All`, `Commands`, `Queries`, `With Findings`) and
  live search

**0.7.0** (October 2026)

- **More mediators**: Mediator (martinothamar) and Wolverine conventions,
  detected automatically from package references and `using` directives
- **Comparing two scans (`--diff` / `--diff-output`)**: compare a scan against a
  base `ARCHITECTURE.json` report or base checkout folder to see what changed in
  projects, layers, CQRS requests/handlers/validators, pipeline behaviors, EF Core
  DbContexts, and architecture checks (as Markdown with a CQRS delta diagram or JSON)
- **Pull request architecture diff comments (`action.yml`)**: `comment-pr: 'true'`
  and `diff-base` / `diff-output` inputs automatically diff pull requests against
  their base ref and post or update a sticky PR comment (`<!-- cqrslens-architecture-diff -->`)

**0.6.0** (October 2026)

- **EF Core ER diagram** (#6): entity attributes, primary/foreign keys and
  1:1 / 1:N / M:N relationships from navigation properties, data annotations
  and common Fluent API calls in both text and Roslyn modes
- **Roslyn hardening** (#1): MSBuild `.csproj` and `Directory.Build.props`/`targets`
  `<Using>` / `<PackageReference>` / `<ImplicitUsings>` evaluation, per-project
  `global using` isolation, base-class interface/response propagation, configured
  external `--markers` / `--handler-markers`, and `AmbiguousContract` /
  `UnresolvedContract` diagnostic parity across text and Roslyn modes
- **Pre-rendered SVG diagrams**: `--render svg` and `"render": "svg"` compile
  every flowchart and ER diagram to deterministic inline `<svg>` ahead of time
  so HTML reports require zero JavaScript and work completely offline
- **`--format json` output**: schema-versioned, deterministic JSON report of
  projects, requests, handlers, validators, behaviors, DbContexts (with entities
  and relationships), checks, and diagnostics for external dashboards and custom
  tooling
- **Console ASCII artwork, TrueColor gradients, live progress, and watermarks**:
  24-bit ANSI TrueColor ASCII banner, live progress bar, Unicode box-drawing
  summary card, and custom `--watermark` block ASCII art stamps in console and
  Markdown/HTML/JSON reports

**0.5.1** (October 2026)

- Primary-constructor `DbContext` support in text mode (#66)
- Hardened Mermaid diagrams for GitHub rendering (#67)

**0.5.0** (October 2026)

- FastEndpoints `Validator<T>` discovery in text and Roslyn modes (#51)
- Same-named contract resolution through namespace and `using` directives,
  with informational ambiguity diagnostics (#52)
- Source-declared request base classes with generic response substitution (#53)
- Explicit external handler wrappers via `--handler-markers` / `handlerMarkers`
  and the Action `handler-markers` input (#54)

**0.4.0** (October 2026)

- `.cqrslens.json` configuration with schema, source filters, layer overrides
  and rule severities (#43)
- Baselines and new-only `--strict` checks with stable, line-independent keys
  (#44)
- SARIF 2.1.0, GitHub annotations, job summaries and repository-relative CI
  paths (#45)
- .NET 8/9/10 runtime compatibility and an Action that sets up .NET 10 (#46)
- Pinned real-world validation corpus, nightly informational workflow and
  documented limitations (#47)

**0.3.0** (October 2026)

- Correctness pass for test-project classification, custom handler/validator
  contracts, multi-namespace/brace scanning, EF Core shape detection, project
  discovery and CLI exit codes (#22, #23, #26, #27)
- Opt-in Roslyn backend with real contract shims and SampleShop parity (#1, #28)
- Source locations, structured diagnostics, scanner/timestamp metadata and
  confidence-aware `--strict` (#29)
- Collision-free Mermaid ids, reproducible reports, escaped output and pinned
  SRI-checked Mermaid (#13, #16, #31)
- Version-safe Action, locked restore, Ubuntu/Windows CI and installed-package
  smoke tests (#24, #30)

**0.2.1** (October 2026)

- Correctness pass for namespace-safe handler/validator identities, generic
  constraints, marker-interface resolution, and reproducible Action installs
  (#7–#10)

**0.2.0** (October 2026)

- Custom marker interfaces (#2): requests followed through interface
  inheritance, plus `--markers` roots for hierarchies that do not derive
  from `IRequest`
- Namespace-aware linking (#3): fully-qualified resolution, so same-named
  requests in different namespaces keep their own handlers and validators
- Multiple DbContexts per file (#4): DbSets attributed per context class
- HTML report output (#5): `--format md|html`, one portable file with pinned,
  SRI-checked Mermaid from a CDN
- Architecture checks: `MissingHandler` and `LayerViolation` errors,
  `CommandWithoutValidator` warnings, `--strict` to fail CI on errors
- GitHub Action: scan a repo and upload the report as a workflow artifact
- Parallel scanning, and a timing footer at the end of every report

**0.1.0** (October 2026)

- First release: project dependency graph, layer diagram, CQRS map with
  request table, pipeline behavior detection, DbContext overview,
  Markdown report. Static analysis only, no build of the target solution.

## Ideas, not planned yet

- F# project support
