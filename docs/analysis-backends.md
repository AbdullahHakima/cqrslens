# Analysis backends

CqrsLens has two separate jobs during a scan:

1. find the source-level architecture shapes (requests, handlers, validators,
   behaviors and DbContexts);
2. link and report those shapes together with project references and checks.

The text scanner is intentionally build-free. It is fast, works on solutions that
do not compile, and remains the default even now that Roslyn mode is available.

The backend boundary is `IArchitectureScanner`. `Analyzer` owns file discovery,
project ownership, the shared `SourceWorkspace`, linking, deep-slice analysis,
checks and phase timing; a scanner only turns the parsed workspace into the shared
analysis model. Both scanners reuse the same per-file `CSharpScanner` scan and
source masks, so selecting Roslyn does not duplicate text parsing.

## Roslyn backend

The rollout and hardening for #1:

1. text mode stays the default behind the scanner boundary;
2. Roslyn covers requests, handlers, validators, behaviors and DbContexts
   (including EF Core entity/relationship ER models);
3. `--analysis roslyn` exposes that backend as an opt-in mode;
4. `CsprojParser` evaluates ancestor `Directory.Build.props`, `.csproj` and
   `Directory.Build.targets` files for `<Using>` (`Include`, `Alias`, `Static`,
   `Remove`), `<PackageReference>`, and `<ImplicitUsings>`, applying project-level
   usings in both text and Roslyn modes;
5. `global using` directives (both from `.cs` files and MSBuild `<Using>` items)
   are scoped per project so module-local global usings never leak into sibling
   projects, with `#line 1` mapping preserving exact source locations;
6. `samples/SampleShop`, base-class request hierarchies (`QueryBase<T>`),
   module-local contracts, FastEndpoints validators, and configured external
   `--markers` / `--handler-markers` produce matching shapes and
   `AmbiguousContract` / `UnresolvedContract` diagnostics in both modes;
7. text mode remains the fallback for incomplete or non-buildable repositories.

The Roslyn backend builds an in-memory compilation containing all source groups
from the scan, plus runtime assemblies. This lets handlers in one project bind to
requests declared in another project. Real applications reference MediatR,
FluentValidation, FastEndpoints and EF Core assemblies that are not part of the
runtime reference set, so the backend injects minimal contract shapes only when
the source does not already declare them. Those shims are for symbol binding;
they do not execute package code or claim that the target solution builds.

When a project or file cannot be scanned, Roslyn isolation records skipped files
and structured diagnostics instead of silently returning an empty architecture.
Those diagnostics also disable confidence-sensitive `--strict` failures.

The important constraint is that report generation, architecture checks and
Mermaid output should not care which scanner produced the model. That keeps the
Roslyn work focused on correctness rather than turning it into a CLI/report
rewrite at the same time.
