# Real-world validation

CqrsLens is tested against a small, pinned corpus of public MediatR / Clean
Architecture repositories in addition to its unit and fixture tests. The corpus
is deliberately informational: it shows where the text scanner works today and
where its static heuristics stop. It is **not** part of the PR or release gates,
so an external repository changing or disappearing cannot block a release.

## Latest snapshot

CqrsLens `0.4.0`, text analysis, 2026-10-22:

| Repository | Commit | Path | Markers | Projects | Requests | Handlers | Validators | Checks (E/W) | Diagnostics (E/W/I) | Time |
| --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| [jasontaylordev/CleanArchitecture](https://github.com/jasontaylordev/CleanArchitecture) | `968abf7dc488` | `.` | `-` | 13 | 11 | 11 | 6 | 0/3 | 0/0/0 | 938 ms |
| [ardalis/CleanArchitecture](https://github.com/ardalis/CleanArchitecture) | `e7c79be54ba0` | `.` | `ICommand,IQuery` | 22 | 23 | 0 | 0 | 23/13 | 0/0/0 | 1192 ms |
| [kgrzybek/modular-monolith-with-ddd](https://github.com/kgrzybek/modular-monolith-with-ddd) | `91c8ef24b4cb` | `src` | `ICommand,IQuery` | 46 | 2 | 0 | 5 | 2/0 | 0/0/0 | 2299 ms |

Timings are machine-dependent; counts and commits are the reproducible part.
Full Markdown reports, SARIF, logs and `results.tsv` come from the workflow
artifact. The pinned refs live in [`validation/corpus.tsv`](../validation/corpus.tsv).

### What this snapshot says

- **MediatR-native app:** `jasontaylordev/CleanArchitecture` is fully linked
  (11 requests, 11 handlers, 6 validators). Its three warnings are commands that
  genuinely have no validator in the scanned tree.
- **External command/handler wrappers:** `ardalis/CleanArchitecture` is detected
  as 23 requests after configuring `ICommand,IQuery`, but its handlers derive
  from a wrapper supplied by an external package. The scanner cannot see that
  wrapper's inheritance, so all 23 `MissingHandler` errors are **false
  positives** in this snapshot. The 13 command-without-validator warnings are
  also largely false positives: this repo uses FastEndpoints `Validator<T>`,
  not FluentValidation `AbstractValidator<T>`.
- **Per-module contracts and request base classes:** `modular-monolith-with-ddd`
  defines `ICommand` / `IQuery` in each module and most requests derive from
  `CommandBase` / `QueryBase`. Only two direct `IQuery` requests are found, and
  none of their handlers (hidden behind module-local `IQueryHandler` wrappers)
  are linked. The two `MissingHandler` errors are false positives. Five
  validators are still found through direct FluentValidation inheritance.
- **Diagnostics are clean.** No files were skipped and no ambiguous links were
  reported for these three scans. That is not the same as "complete": the
  false negatives above are silent shape gaps rather than scan diagnostics.

## Known limitations

These are user-facing limits, not promises that the current corpus can fix:

1. **External base types are opaque.** Text mode only follows interfaces and
   abstract base classes declared in the scanned source. A command implementing
   `ICommand` from a NuGet package works when `ICommand` is passed to
   `--markers`, but a handler implementing an external `ICommandHandler` cannot
   be linked to `IRequestHandler` (see [#54](https://github.com/AbdullahHakima/cqrslens/issues/54)).
2. **Custom request base classes are not traversed.** `CommandBase<T>` /
   `QueryBase<T>` hierarchies are not yet expanded when discovering requests;
   classes must implement a recognized marker or `IRequest` directly (see
   [#53](https://github.com/AbdullahHakima/cqrslens/issues/53)).
3. **Same-named interfaces across modules are ambiguous.** The text scanner does
   not use `using` directives to disambiguate `ICommand` when several modules
   declare their own. Configured markers recover direct implementations, but
   handler/validator linking can still fall back to simple names (see
   [#52](https://github.com/AbdullahHakima/cqrslens/issues/52)).
4. **Validator frameworks vary.** FluentValidation `AbstractValidator<T>` and
   `IValidator<T>` are recognized. FastEndpoints `Validator<T>` (see
   [#51](https://github.com/AbdullahHakima/cqrslens/issues/51)), custom
   validation pipelines and manual validation are not.
5. **No MSBuild evaluation.** Conditional compilation, imported props, generated
   sources, source generators and assembly references are not evaluated. Text
   mode is intentionally build-free; the Roslyn mode is still a source-only
   opt-in and does not replace a build.
6. **Notifications, streams and domain events are out of scope.** Only
   request/response handlers are modeled.
7. **Layer classification is heuristic.** Non-standard project names can be
   wrong; use the `layers` configuration to override project layers.
8. **Sample/test projects are scanned.** The corpus scans whole repositories
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
| [kgrzybek/modular-monolith-with-ddd](https://github.com/kgrzybek/modular-monolith-with-ddd) | MIT | Multi-module DDD app with per-module contracts and base request classes; exposes module ambiguity and base-class gaps. |

Full commit SHAs are pinned in the corpus file. Updating a pin is a deliberate
review action, not an automatic nightly bump.
