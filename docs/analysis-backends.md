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

I do not want to replace the text scanner in one large change. The planned
sequence for #1 is:

1. keep text mode as the default and move it behind the scanner boundary;
2. add a Roslyn backend for requests, handlers and validators first;
3. move custom marker inheritance/type resolution to symbols;
4. compare both backends on SampleShop and the El-Hakim report before exposing
   mode selection in the CLI;
5. keep text mode as the fallback for incomplete or non-buildable repositories.

The important constraint is that report generation, architecture checks and
Mermaid output should not care which scanner produced the model. That keeps the
Roslyn work focused on correctness rather than turning it into a CLI/report
rewrite at the same time.
