# CqrsLens Roadmap

> This is a living document. It is updated with every release — items move from
> planned to shipped, and new items are added as the community asks for them.
> Have an idea? Open a [feature request](../../issues/new/choose) or pick up an
> item below — see [CONTRIBUTING.md](CONTRIBUTING.md).

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

## v1.1 — "Stronger" (analysis depth)

- **Roslyn-based semantic analysis** — replace text/regex scanning with real
  symbol resolution, so detection survives formatting, aliases and indirection
- **Custom marker interfaces** — follow home-grown `ICommand<T>` /
  `IMessage<T>` style abstractions that derive from `IRequest<T>`
- **Namespace-aware linking** — same-named requests in different namespaces
  link to their own handlers/validators instead of sharing links
- **Multiple DbContexts per file** — attribute DbSets to the context class
  that declares them, not to the file

## v1.2 — "Easier" (developer experience)

- **HTML report output** — diagrams rendered in the page, no Markdown viewer
  or Mermaid plugin needed
- **Colored console output + progress** while scanning large solutions
- **`--format md|html|json`** — JSON output for tooling and dashboards
- **Config file (`cqrslens.json`)** — include/exclude paths, custom layer
  names, output defaults per repository

## v1.3 — "More useful" (new capabilities)

- **EF Core ER diagram** — entity relationships drawn from entity classes
  (navigations + foreign keys), not just DbSet lists
- **GitHub Action** — regenerate `ARCHITECTURE.md` automatically on pull
  requests and post the diagram diff
- **More mediators** — request detection for Mediator (martinothamar) and
  Wolverine, not only MediatR
- **Architecture-rule checks** — fail CI when a rule breaks, e.g.
  "Application must not reference Infrastructure", with a non-zero exit code

## Later / under consideration

- Interactive HTML explorer (click a request → see handler, validators, file)
- Blame/history view: how the CQRS surface changed between commits
- Support for F# projects
