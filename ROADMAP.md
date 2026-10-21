# CqrsLens Roadmap

> This is a living document. It is updated with every release — items move from
> planned to shipped, and new items are added as the community asks for them.
> Have an idea? Open a [feature request](../../issues/new/choose) or pick up an
> item below — see [CONTRIBUTING.md](CONTRIBUTING.md).

## Shipped in 0.2.0

The v1.1 "Stronger" analysis items and the first v1.2/v1.3 items landed early,
together in 0.2.0:

- **Custom marker interfaces** (#2) — requests followed through interface
  inheritance (chains reaching `IRequest`), plus `--markers` roots for
  hierarchies that do not derive from `IRequest`
- **Namespace-aware linking** (#3) — fully-qualified resolution; same-named
  requests in different namespaces keep their own handlers/validators
- **Multiple DbContexts per file** (#4) — DbSets attributed per context class
- **HTML report output** (#5) — `--format md|html`; self-contained HTML with
  stat cards and in-browser Mermaid rendering (behind a single rendering seam)
- **Architecture-rule checks** (from v1.3, shipped early) — `MissingHandler`
  and `LayerViolation` errors, `CommandWithoutValidator` warnings, and a
  `--strict` flag that exits `4` on errors
- **GitHub Action** (from v1.3, shipped early) — composite action that scans
  and uploads the report as a workflow artifact
- **Parallel scanning + timing footer** — concurrent file scanning with
  deterministic output; reports end with elapsed time and file count

## v1.0 — Shipped (as v0.1.0)

The first working lens over a Clean Architecture + CQRS codebase:

- Static analysis of any .NET solution folder — no build required
- Project dependency graph (Mermaid flowchart, from `.csproj` ProjectReferences)
- Clean Architecture layer diagram (projects grouped by naming convention)
- CQRS map: every `IRequest` / `IRequest<T>` → its handler, with validators
  linked per request, plus a full request table (type, response, handler,
  validators, project)
- Pipeline behavior detection (`IPipelineBehavior<,>` implementations)
- DbContext overview (DbSet property → entity table)
- Markdown report (`ARCHITECTURE.md`) with embedded Mermaid diagrams
- Ships as a `dotnet tool` (`cqrslens`), MIT licensed, with unit tests and CI

## Next milestone — v0.3 / v1.0 "Roslyn semantic analysis"

The headline work: replace text/regex scanning with real symbol resolution,
so detection survives formatting, aliases, partial classes and indirection,
and results become exact rather than best-effort.

- **Roslyn-based semantic analysis** (#1) — full symbol resolution for
  requests, handlers, validators, behaviors and DbContexts
- **Pre-rendered SVG diagrams (AOT Mermaid compile, Ondine-style)** —
  `--render svg` producing zero-JS HTML reports: diagrams compiled ahead of
  time instead of rendered by Mermaid.js in the browser (the HTML generator
  already isolates rendering behind one seam method for this)

## v1.2 — "Easier" (developer experience, remaining)

- **Colored console output + progress** while scanning large solutions
- **`--format json`** — JSON output for tooling and dashboards
- **Config file (`cqrslens.json`)** — include/exclude paths, custom layer
  names, marker defaults, output defaults per repository

## v1.3 — "More useful" (new capabilities, remaining)

- **EF Core ER diagram** — entity relationships drawn from entity classes
  (navigations + foreign keys), not just DbSet lists
- **GitHub Action: PR diff comments** — post the architecture diff on pull
  requests (the scan-and-upload action itself shipped in 0.2.0)
- **More mediators** — request detection for Mediator (martinothamar) and
  Wolverine, not only MediatR

## Later / under consideration

- Interactive HTML explorer (click a request → see handler, validators, file)
- Blame/history view: how the CQRS surface changed between commits
- Support for F# projects
