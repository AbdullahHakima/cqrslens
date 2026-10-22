# CqrsLens Roadmap

What is planned, in the order I expect to work on it. Issues are linked
where they exist; the dates are not promises.

## Done

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

## Next

- **Scanner hardening from real-world validation** (#51–#54) is complete:
  FastEndpoints validators, external handler wrappers, request base classes and
  same-named contract resolution
- **Roslyn hardening**: load evaluated MSBuild/package references and compare
  broader real codebases before considering Roslyn as the default. Text mode
  stays as the fast fallback.
- **EF Core ER diagram** (#6): entity relationships from navigation
  properties and foreign keys, not just DbSet lists.
- **Pre-rendered SVG diagrams**: diagrams compiled to SVG ahead of time
  so the HTML report needs no JavaScript. The HTML generator already
  isolates rendering behind one method for this.

## After that

- `--format json` output for external dashboards and custom tooling
- Colored console output and a progress line while scanning
- More mediators: Mediator (martinothamar) and Wolverine conventions
- GitHub Action mode that comments the architecture diff on a pull request

## Ideas, not planned yet

- Interactive HTML explorer (click a request, see its handler and files)
- Comparing two scans: what changed in the CQRS surface between commits
- F# project support
