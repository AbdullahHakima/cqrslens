# CqrsLens Roadmap

What is planned, in the order I expect to work on it. Issues are linked
where they exist; the dates are not promises.

## Done

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
- HTML report output (#5): `--format md|html`, one self-contained file
- Architecture checks: `MissingHandler` and `LayerViolation` errors,
  `CommandWithoutValidator` warnings, `--strict` to fail CI on errors
- GitHub Action: scan a repo and upload the report as a workflow artifact
- Parallel scanning, and a timing footer at the end of every report

**0.1.0** (October 2026)

- First release: project dependency graph, layer diagram, CQRS map with
  request table, pipeline behavior detection, DbContext overview,
  Markdown report. Static analysis only, no build of the target solution.

## Next

- **Roslyn-based analysis** (#1): the opt-in source-symbol backend covers
  requests, handlers, validators, behaviors and DbContexts behind
  `--analysis roslyn`. It compiles source groups together and safely shims
  missing MediatR/FluentValidation/EF Core contracts; `samples/SampleShop` parity
  with text mode is enforced by tests. Next: load evaluated MSBuild/package
  references and compare broader real codebases before considering it as the
  default. Text mode stays as the fast fallback.
- **EF Core ER diagram** (#6): entity relationships from navigation
  properties and foreign keys, not just DbSet lists.
- **Pre-rendered SVG diagrams**: diagrams compiled to SVG ahead of time
  so the HTML report needs no JavaScript. The HTML generator already
  isolates rendering behind one method for this.

## After that

- `--format json` and a `cqrslens.json` config file (include/exclude
  paths, layer names, marker defaults)
- Colored console output and a progress line while scanning
- More mediators: Mediator (martinothamar) and Wolverine conventions
- GitHub Action mode that comments the architecture diff on a pull request

## Ideas, not planned yet

- Interactive HTML explorer (click a request, see its handler and files)
- Comparing two scans: what changed in the CQRS surface between commits
- F# project support
