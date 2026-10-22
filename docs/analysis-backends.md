# Analysis backends

CqrsLens has two separate jobs during a scan:

1. find the source-level architecture shapes (requests, handlers, validators,
   behaviors and DbContexts);
2. link and report those shapes together with project references and checks.

The text scanner is intentionally build-free. It is fast, works on solutions that
do not compile, and remains the default even now that Roslyn mode is available.

The backend boundary is `IArchitectureScanner`. `Analyzer` still owns file
discovery, project ownership, linking, checks and timing; a scanner only turns
source files into the shared analysis model.

## Roslyn backend

The 0.3.0 rollout for #1:

1. text mode stays the default behind the scanner boundary;
2. Roslyn covers requests, handlers, validators, behaviors and DbContexts;
3. `--analysis roslyn` exposes that backend as an opt-in mode;
4. `samples/SampleShop` is compiled by the test suite and must produce identical
   request, handler, validator, behavior and DbContext signatures in both modes;
5. text mode remains the fallback for incomplete or non-buildable repositories.

The Roslyn backend builds an in-memory compilation containing all source groups
from the scan, plus runtime assemblies. This lets handlers in one project bind to
requests declared in another project. Real applications reference MediatR,
FluentValidation and EF Core assemblies that are not part of the runtime
reference set, so the backend injects minimal contract shapes only when the
source does not already declare them. Those shims are for symbol binding; they do
not execute package code or claim that the target solution builds.

It does not yet evaluate the target solution through MSBuild, so project/package
references outside the shimmed contracts can remain unresolved. When a project or
file cannot be scanned, Roslyn isolation records skipped files and structured
diagnostics instead of silently returning an empty architecture. Those
diagnostics also disable confidence-sensitive `--strict` failures.

The important constraint is that report generation, architecture checks and
Mermaid output should not care which scanner produced the model. That keeps the
Roslyn work focused on correctness rather than turning it into a CLI/report
rewrite at the same time.
