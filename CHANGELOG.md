# Changelog

All notable changes to CqrsLens are documented here.

## [0.5.1] - 2026-10-23

### Changed

- Mermaid reports hardened for GitHub rendering: role-prefixed ASCII node ids,
  reserved-word handling, GitHub-safe label entities, and diagrams split at
  15 requests grouped by project.

### Fixed

- Text mode missed DbContext classes declared with a primary constructor, such
  as `class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext`.
  The base-call argument list is now stripped before the base type is matched,
  and the declaration scan no longer reads `DbContext(options)` as part of the
  parameter list when it is the only base entry. These contexts and their
  `DbSet<>` properties are detected, matching the Roslyn backend.

## [0.5.0] - 2026-10-22

### Added

- **FastEndpoints validators**: `Validator<T>` is recognized as a validator root
  in text mode, and the Roslyn backend shims the same contract. Its first generic
  argument is linked with the existing FluentValidation identity rules, so
  endpoint validators appear in the CQRS map and no longer produce
  `CommandWithoutValidator` warnings when they target a request (#51).
- **External handler wrappers**: `--handler-markers ICommandHandler,IQueryHandler`
  and the `handlerMarkers` config key (plus the Action's `handler-markers` input)
  describe wrappers supplied by a package. CqrsLens reads the first generic
  argument as the request type, matching MediatR. Without configuration, external
  wrappers remain opaque and the scan diagnostic points to the option (#54).
- **Module-aware contract resolution**: same-named requests, handlers and
  validators are resolved through each file's namespace and `using` directives,
  including global usings and namespace aliases. Genuine ambiguity is reported as
  an informational diagnostic instead of linking the wrong contract (#52).
- **Source-declared request base classes**: `CommandBase` / `QueryBase<T>` chains
  declared in the scanned source are followed transitively, with concrete generic
  response substitution and arity-aware handler/validator resolution (#53).

### Changed

- The real-world validation corpus records request and handler roots separately;
  the ardalis row opts into `ICommandHandler,IQueryHandler`. The snapshot now
  links 23/23 handlers there and recognizes 21 FastEndpoints validators, while
  keeping the 13 genuine command-without-validator warnings.
- FastEndpoints validators are discovered by default; other external validator
  wrappers still require source declarations. Text analysis remains the default
  and Roslyn remains opt-in.

## [0.4.0] - 2026-10-22

### Added

- **Project configuration**: `.cqrslens.json` (with a published JSON schema) for
  `analysis`, custom `markers`, source `include`/`exclude` globs, project `layers`,
  and per-rule severities. Unknown keys and invalid values are usage errors;
  CLI flags override config values (#43).
- **Baselines and new-only strict checks**: `--write-baseline` captures current
  findings, `--baseline` matches them by rule, target identity and root-relative
  file (not line number), and `--strict` fails only on new errors. Known findings
  stay quiet across unrelated edits and moved lines (#44).
- **CI-native outputs**: SARIF 2.1.0 with rule metadata, locations, stable
  fingerprints and baseline state; `--summary` Markdown; and
  `--github-annotations` for new findings. `--ci-root` rebases paths so a scan of
  `src/` still produces repository-relative annotations and SARIF URIs (#45).
- **Runtime compatibility**: the packed global tool rolls forward to .NET 9/10
  while still building on .NET 8. CI verifies the installed package on .NET
  8, 9 and 10; the composite Action sets up .NET 10 (#46).
- **Real-world validation**: a pinned public MediatR/Clean Architecture corpus,
  a local runner, and a nightly/manual non-blocking workflow that publishes
  counts, diagnostics, reports and SARIF. Known limitations and false
  positives are documented before they become 1.0 contracts (#47).

### Changed

- The GitHub Action exposes `config`, `baseline`, `sarif-output`, `upload-sarif`
  and `annotations` inputs, uploads both report and SARIF artifacts, writes a job
  summary, and passes `--ci-root "$GITHUB_WORKSPACE"` so subfolder scans stay
  repository-relative.
- Text analysis remains the default; `--analysis roslyn` remains an explicit
  opt-in.

## [0.3.0] - 2026-10-22

### Added

- **Opt-in Roslyn analysis** (`--analysis roslyn`) that resolves real
  MediatR/FluentValidation/EF Core contracts, compiles source groups together,
  reports unresolved contracts, and keeps text mode as the default and fallback.
  `samples/SampleShop` parity is enforced in both modes (#1, #28).
- **Traceable analysis results**: source locations on every request, handler,
  validator, behavior and DbContext; structured diagnostics for skipped files,
  unresolved contracts, scanner failures and ambiguous links; scanner identity
  and a single `GeneratedAt` timestamp. Reports render the diagnostics section
  in Markdown and HTML (#29).
- **Reproducible reports** via `--generated-at`; timing is reported as `0 ms`
  in this mode so golden files are byte-identical (#31).
- Installed-package smoke tests, an Ubuntu/Windows CI matrix, locked NuGet
  restore, and an adversarial fixture with a golden CQRS map (#30).

### Fixed

- Test projects are classified as `Tests`, not production layers, and duplicate
  project names no longer produce false layer violations (#22).
- Custom handler/validator interfaces, multiple implemented interfaces, generic
  open-type identity and nested type identity are resolved correctly (#23, #26).
- Multi-namespace files, nested namespaces, and braces inside comments, strings,
  chars and raw literals no longer corrupt shape identities or links
  (#15, #25).
- EF Core `DbContext`/`DbSet` detection uses balanced declarations and no longer
  misattributes DbSets across classes (#26).
- Project discovery skips only root-relative `bin`/`obj` segments, handles
  inaccessible paths and reparse points, creates output directories, and returns
  stable CLI exit codes (#27).
- Mermaid ids are collision-free with readable stable suffixes; report labels,
  Markdown tables and HTML are escaped; Mermaid is pinned to `10.9.4` with a
  SHA-384 SRI check and documented as requiring network on first view
  (#13, #16, #31).
- The GitHub Action pins a published version, exposes `markers` and `analysis`
  inputs, always uploads the report, and the release workflow refuses a tag that
  does not match the package version (#24).

### Changed

- `--strict` only exits with code `4` when architecture errors are found and the
  scan is complete enough to trust them; incomplete scans report diagnostics.
- CI now restores lock files, builds and tests on Ubuntu and Windows, scans the
  sample through the packed `.nupkg`, and verifies CLI exit codes.

## [0.2.1] - 2026-10-21

### Fixed

- Keep handler and validator identities fully-qualified after linking, so
  same-named types in different namespaces remain separate Mermaid nodes (#7).
- Ignore generic `where` constraints when detecting implemented request shapes,
  avoiding false requests such as pipeline behaviors constrained by `IRequest<T>`
  (#8).
- Resolve custom marker-interface inheritance by namespace before falling back
  to a simple-name match, preventing unrelated `ICommand<T>` interfaces from
  sharing inheritance (#9).
- Pin the GitHub Action to the matching CqrsLens package version, so a tagged
  action does not silently install a later NuGet release (#10).

## [0.2.0] - 2026-10-21

### Added

- **Parallel scanning** — source files are scanned concurrently (deterministic,
  sorted output as before), and every report now ends with a timing footer:
  `Generated by CqrsLens 0.2.0 in {elapsed} ms — {fileCount} files scanned.`
- **Custom marker interfaces** (#2) — requests are followed through interface
  inheritance: any interface chain reaching `IRequest` (e.g. a home-grown
  `ICommand<T> : IRequest<T>`) now yields requests. New `--markers
  ICommand,IQuery,IMessage` option names additional request roots for marker
  hierarchies that do not derive from `IRequest`.
- **Namespace-aware linking** (#3) — requests, handlers and validators are keyed
  by fully-qualified name; edges resolve by namespace first, and a bare
  simple-name match is used only when it is unambiguous. Same-named requests in
  different namespaces no longer share handlers/validators (diagram labels stay
  short).
- **Multiple DbContexts per file** (#4) — `DbSet`s are attributed to the
  DbContext class that declares them (class-span analysis) instead of to the
  whole file.
- **HTML output** (#5) — `--format md|html` (default `md`; a `.html` output
  path implies HTML). The HTML report is a single portable file with embedded
  CSS, summary stat cards, and diagrams/tables rendered via Mermaid in the
  browser (Mermaid is loaded from a pinned, SRI-checked CDN, so first view needs
  network access). Diagram rendering sits behind a single seam method, so a
  future pre-rendered mode can replace it without touching the generator.
- **Architecture checks + `--strict`** — every report gains an
  *Architecture Checks* section: `MissingHandler` (error), `LayerViolation`
  (error — a Domain project referencing Infrastructure / Api / Web), and
  `CommandWithoutValidator` (warning). `--strict` exits with code `4` when any
  error is found (the report is still written).
- **GitHub Action** — `action.yml` at the repo root: a composite action that
  installs CqrsLens, scans, and uploads the report as a workflow artifact
  (inputs: `path`, `output`, `format`, `strict`).

## [0.1.0] - 2026-10-21

### Added

- `cqrslens <path> [-o OUTPUT.md]` CLI (installable as a .NET tool) that statically
  scans a .NET solution without building it.
- Project dependency graph (Mermaid flowchart) from `.csproj` ProjectReferences.
- CQRS map: MediatR requests (`IRequest` / `IRequest<T>`), their handlers
  (`IRequestHandler<...>`) and validators (FluentValidation `AbstractValidator<T>` /
  `IValidator<T>`), drawn as a Mermaid diagram plus a table.
- Pipeline behavior detection (`IPipelineBehavior<...>` implementations).
- Layer diagram grouping projects by Clean Architecture naming convention
  (Domain / Application / Infrastructure / Api / Web / UI), with top-level-folder
  fallback.
- DbContext overview: `DbContext` classes with their `DbSet<T>` entities.
- Sample solution (`samples/SampleShop`) and a generated sample report under
  `docs/sample-output/`.
