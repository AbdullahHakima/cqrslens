# Real-world validation

CqrsLens is tested against a small, pinned corpus of public MediatR / Clean
Architecture repositories in addition to its unit and fixture tests. The corpus
is deliberately informational: it shows where the text scanner works today and
where its static heuristics stop. It is **not** part of the PR or release gates,
so an external repository changing or disappearing cannot block a release.

## Latest snapshot

CqrsLens `0.5.0` development after #51, text analysis, 2026-10-22:

| Repository | Commit | Path | Markers | Handler markers | Projects | Requests | Handlers | Validators | Checks (E/W) | Diagnostics (E/W/I) | Time |
| --- | --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| [jasontaylordev/CleanArchitecture](https://github.com/jasontaylordev/CleanArchitecture) | `968abf7dc488` | `.` | `-` | `-` | 13 | 11 | 11 | 6 | 0/3 | 0/0/0 | 417 ms |
| [ardalis/CleanArchitecture](https://github.com/ardalis/CleanArchitecture) | `e7c79be54ba0` | `.` | `ICommand,IQuery` | `ICommandHandler,IQueryHandler` | 22 | 23 | 23 | 21 | 0/13 | 0/0/0 | 585 ms |
| [kgrzybek/modular-monolith-with-ddd](https://github.com/kgrzybek/modular-monolith-with-ddd) | `91c8ef24b4cb` | `src` | `ICommand,IQuery` | `-` | 46 | 105 | 105 | 5 | 0/71 | 0/0/0 | 1190 ms |

Timings are machine-dependent; counts and commits are the reproducible part.
Full Markdown reports, SARIF, logs and `results.tsv` come from the workflow
artifact. The pinned refs live in [`validation/corpus.tsv`](../validation/corpus.tsv).
The released `0.4.0` snapshot predates these scanner changes and linked far fewer
shapes; this table is a development snapshot, not the released package's results.

### What this snapshot says

- **MediatR-native app:** `jasontaylordev/CleanArchitecture` remains fully linked
  (11 requests, 11 handlers, 6 validators). Its three warnings are commands that
  genuinely have no validator in the scanned tree.
- **External command/handler wrappers:** `ardalis/CleanArchitecture` detects 23
  requests and now links all 23 handlers after configuring
  `ICommandHandler,IQueryHandler` as handler roots. The explicit roots let the
  scanner read the first generic argument of the external wrapper, so the 23
  `MissingHandler` errors are gone. Without handler markers the default scan still
  links zero handlers and reports the wrapper gaps instead of guessing; the
  corpus row opts in explicitly.
- **FastEndpoints validators:** 21 `Validator<T>` classes are now found and
  linked to their endpoint request DTOs, and the 21 `UnresolvedContract` info
  items are gone. The 13 `CommandWithoutValidator` warnings remain because they
  are genuine: those validators target web request DTOs such as
  `CreateContributorRequest`, while the MediatR commands have no validators in
  the pinned tree. This is not a missed link.
- **Per-module contracts and request bases:** `modular-monolith-with-ddd` now
  finds 105 requests and links all 105 handlers, up from 2 requests and 120
  handlers in the #52 snapshot. The previous extra 15 handlers were duplicate
  wrapper matches; generic/non-generic declarations are now resolved by arity,
  so each handler links to its own request. Request discovery follows each
  module's source-declared `CommandBase` / `CommandBase<T>` / `QueryBase<T>`
  chains and substitutes concrete response types such as `Guid` and
  `List<MeetingCommentDto>`. Five validators are found and linked through direct
  FluentValidation inheritance.
- **The 71 modular warnings are real coverage gaps.** The sample defines only
  five command validators, so `CommandWithoutValidator` correctly reports the
  remaining discovered commands. There are no `MissingHandler` errors. The
  warnings are useful validation coverage, not base-class false positives.
- **Diagnostics are informative, not a clean bill of health.** No files were
  skipped and there are no scan diagnostics in this snapshot. The configured
  handler roots and built-in FastEndpoints validator root account for the known
  external shapes. `AmbiguousContract` and `UnresolvedContract` remain
  informational, appear in Markdown, the CI summary and SARIF, and do not disable
  strict confidence on their own.

## Known limitations

These are user-facing limits, not promises that the current corpus can fix:

1. **Base-class discovery stops at the scanned source boundary.** Source-declared
   request base classes are followed transitively, including generic response
   substitution. A chain that leaves the scanned source cannot be continued; it
   is surfaced as an `UnresolvedContract` info diagnostic instead of being
   guessed. External handler wrappers stay opaque unless they are named with
   `--handler-markers` / `handlerMarkers`; source-declared wrappers are followed
   automatically.
2. **Using directives are parsed, not evaluated by MSBuild.** Namespace and
   `using` resolution handles explicit local, global and alias imports. SDK
   implicit usings, generated global usings, conditional compilation, extern
   aliases and project references are not evaluated, so a contract can still be
   invisible even though the compiler can see it. When multiple source
   candidates remain, `AmbiguousContract` is reported instead of guessing.
3. **Validator frameworks vary.** FluentValidation `AbstractValidator<T>` and
   `IValidator<T>`, plus FastEndpoints `Validator<T>`, are recognized. Other
   external validator wrappers, custom validation pipelines and manual validation
   are not.
4. **No MSBuild evaluation.** Conditional compilation, imported props, generated
   sources, source generators and assembly references are not evaluated. Text
   mode is intentionally build-free; the Roslyn mode is still a source-only
   opt-in and does not replace a build.
5. **Notifications, streams and domain events are out of scope.** Only
   request/response handlers are modeled.
6. **Layer classification is heuristic.** Non-standard project names can be
   wrong; use the `layers` configuration to override project layers.
7. **Sample/test projects are scanned.** The corpus scans whole repositories
   where practical, so templates and test helpers can contribute counts. Use
   `include` / `exclude` (or a narrower `path`) for production-only scans.

## Run it locally

Build first, then use Bash (Git Bash on Windows):

```bash
dotnet build CqrsLens.sln -c Release
bash scripts/validate-real-world.sh
```

Outputs go to `artifacts/validation/`:

- `validation.md` — this table with run metadata.
- `results.tsv` — machine-readable per-repository metrics and status.
- `reports/<name>.md`, `reports/<name>.sarif`, `reports/<name>-summary.md`.
- `logs/<name>.log` — CLI output used for parsing.

Overrides:

```bash
CQRSLENS_VALIDATION_OUTPUT=/tmp/cqrslens-validation \
CQRSLENS_VALIDATION_KEEP=1 \
bash scripts/validate-real-world.sh
```

`CQRSLENS_VALIDATION_TOOL` selects another built `cqrslens.dll`; keep it from the
same source revision when comparing runs. Clones are removed by default.

## Workflow

[`real-world-validation.yml`](../.github/workflows/real-world-validation.yml)
runs nightly and on `workflow_dispatch`. It publishes the table to the job
summary and uploads the reports/SARIF/logs for 30 days. It is not triggered by
pull requests and is not a required check.

## Adding a repository

1. Pick a public repository with a clear license and a useful MediatR/CQRS
   shape.
2. Add its full commit SHA (not a branch) and scan path to
   `validation/corpus.tsv`; add custom request or handler markers only when the
   project documents them.
3. Run the script locally and review the report, not just the counts.
4. Update the snapshot table and limitations above when the corpus changes.
5. File focused scanner issues for false positives/negatives found here. Do not
   silently change scanner behavior to make a corpus row look better.

## Corpus

| Repository | License | Why it is included |
| --- | --- | --- |
| [jasontaylordev/CleanArchitecture](https://github.com/jasontaylordev/CleanArchitecture) | MIT | Direct MediatR requests, handlers and FluentValidation validators; the happy path. |
| [ardalis/CleanArchitecture](https://github.com/ardalis/CleanArchitecture) | MIT | Popular template with external command/handler wrappers and FastEndpoints validation; exercises explicit handler roots and validators whose targets are web DTOs rather than CQRS requests. |
| [kgrzybek/modular-monolith-with-ddd](https://github.com/kgrzybek/modular-monolith-with-ddd) | MIT | Multi-module DDD app with per-module contracts and generic request base classes; exercises module-aware resolution and base-class traversal. |

Full commit SHAs are pinned in the corpus file. Updating a pin is a deliberate
review action, not an automatic nightly bump.
