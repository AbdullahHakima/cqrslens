# Real-world validation

CqrsLens is tested against a small, pinned corpus of public MediatR / Clean
Architecture repositories in addition to its unit and fixture tests. The corpus
is deliberately informational: it shows where the text scanner works today and
where its static heuristics stop. It is **not** part of the PR or release gates,
so an external repository changing or disappearing cannot block a release.

## Latest snapshot

CqrsLens `0.5.0` development after #53, text analysis, 2026-10-22:

| Repository | Commit | Path | Markers | Projects | Requests | Handlers | Validators | Checks (E/W) | Diagnostics (E/W/I) | Time |
| --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| [jasontaylordev/CleanArchitecture](https://github.com/jasontaylordev/CleanArchitecture) | `968abf7dc488` | `.` | `-` | 13 | 11 | 11 | 6 | 0/3 | 0/0/0 | 652 ms |
| [ardalis/CleanArchitecture](https://github.com/ardalis/CleanArchitecture) | `e7c79be54ba0` | `.` | `ICommand,IQuery` | 22 | 23 | 0 | 0 | 23/13 | 0/0/44 | 470 ms |
| [kgrzybek/modular-monolith-with-ddd](https://github.com/kgrzybek/modular-monolith-with-ddd) | `91c8ef24b4cb` | `src` | `ICommand,IQuery` | 46 | 105 | 105 | 5 | 0/71 | 0/0/0 | 1595 ms |

Timings are machine-dependent; counts and commits are the reproducible part.
Full Markdown reports, SARIF, logs and `results.tsv` come from the workflow
artifact. The pinned refs live in [`validation/corpus.tsv`](../validation/corpus.tsv).
The released `0.4.0` snapshot had 0 handlers for the modular monolith and no
informational diagnostics for these repositories; this table is a development
snapshot, not the released package's results.

### What this snapshot says

- **MediatR-native app:** `jasontaylordev/CleanArchitecture` remains fully linked
  (11 requests, 11 handlers, 6 validators). Its three warnings are commands that
  genuinely have no validator in the scanned tree.
- **External command/handler wrappers:** `ardalis/CleanArchitecture` is detected
  as 23 requests after configuring `ICommand,IQuery`, but its handlers derive
  from a wrapper supplied by an external package. The scanner now reports 44
  informational diagnostics instead of failing silently: 13 `ICommandHandler`,
  10 `IQueryHandler`, and 21 FastEndpoints `Validator` references have no source
  declaration. These are the known #54 and #51 gaps, not new architecture
  findings. The 23 `MissingHandler` errors and 13 validator warnings remain
  **false positives** until those wrappers are supported.
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
  skipped and there are no error/warning scan diagnostics. The 44 ardalis info
  items are actionable gaps surfaced by `UnresolvedContract`. `AmbiguousContract`
  and `UnresolvedContract` are informational, appear in Markdown, the CI summary
  and SARIF, and do not disable strict confidence on their own.

## Known limitations

These are user-facing limits, not promises that the current corpus can fix:

1. **Base-class discovery stops at the scanned source boundary.** Source-declared
   request base classes are followed transitively, including generic response
   substitution. A chain that leaves the scanned source cannot be continued; it
   is surfaced as an `UnresolvedContract` info diagnostic instead of being
   guessed. External handler wrappers are still opaque: a handler implementing
   an external `ICommandHandler` cannot be linked to `IRequestHandler` (see
   [#54](https://github.com/AbdullahHakima/cqrslens/issues/54)).
2. **Using directives are parsed, not evaluated by MSBuild.** Namespace and
   `using` resolution handles explicit local, global and alias imports. SDK
   implicit usings, generated global usings, conditional compilation, extern
   aliases and project references are not evaluated, so a contract can still be
   invisible even though the compiler can see it. When multiple source
   candidates remain, `AmbiguousContract` is reported instead of guessing.
3. **Validator frameworks vary.** FluentValidation `AbstractValidator<T>` and
   `IValidator<T>` are recognized. FastEndpoints `Validator<T>` (see
   [#51](https://github.com/AbdullahHakima/cqrslens/issues/51)), custom
   validation pipelines and manual validation are not.
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
   `validation/corpus.tsv`; add custom markers only when the project documents
   them.
3. Run the script locally and review the report, not just the counts.
4. Update the snapshot table and limitations above when the corpus changes.
5. File focused scanner issues for false positives/negatives found here. Do not
   silently change scanner behavior to make a corpus row look better.

## Corpus

| Repository | License | Why it is included |
| --- | --- | --- |
| [jasontaylordev/CleanArchitecture](https://github.com/jasontaylordev/CleanArchitecture) | MIT | Direct MediatR requests, handlers and FluentValidation validators; the happy path. |
| [ardalis/CleanArchitecture](https://github.com/ardalis/CleanArchitecture) | MIT | Popular template with external command/handler wrappers and FastEndpoints validation; exposes external-base-type gaps. |
| [kgrzybek/modular-monolith-with-ddd](https://github.com/kgrzybek/modular-monolith-with-ddd) | MIT | Multi-module DDD app with per-module contracts and generic request base classes; exercises module-aware resolution and base-class traversal. |

Full commit SHAs are pinned in the corpus file. Updating a pin is a deliberate
review action, not an automatic nightly bump.
