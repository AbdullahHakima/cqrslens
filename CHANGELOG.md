# Changelog

All notable changes to CqrsLens are documented here.

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
