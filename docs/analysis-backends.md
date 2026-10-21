# Analysis backends

CqrsLens has two separate jobs during a scan:

1. find the source-level architecture shapes (requests, handlers, validators,
   behaviors and DbContexts);
2. link and report those shapes together with project references and checks.

The current text scanner is intentionally build-free. It is fast, works on
solutions that do not compile, and should stay available even after Roslyn mode
lands.

The backend boundary is `IArchitectureScanner`. `Analyzer` still owns file
discovery, project ownership, linking, checks and timing; a scanner only turns
source files into the shared analysis model.

## Roslyn rollout

The rollout for #1 is incremental:

1. text mode stays the default behind the scanner boundary;
2. Roslyn covers requests, handlers, validators, behaviors and DbContexts;
3. `--analysis roslyn` exposes that backend for explicit testing;
4. the next step is evaluated project/package references plus parity checks on
   SampleShop and the El-Hakim report;
5. text mode remains the fallback for incomplete or non-buildable repositories.

The current Roslyn backend builds in-memory compilations from source files and
runtime assemblies. It does not yet evaluate the target solution through MSBuild,
so external package/project symbols can remain unresolved. The mode is therefore
experimental rather than the default.

The important constraint is that report generation, architecture checks and
Mermaid output should not care which scanner produced the model. That keeps the
Roslyn work focused on correctness rather than turning it into a CLI/report
rewrite at the same time.
