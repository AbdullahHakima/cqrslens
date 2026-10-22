# Baselines and new-only CI checks

A baseline lets an existing codebase adopt CqrsLens without making every
pre-existing architecture finding a merge blocker. The first scan records the
current findings; later scans mark those findings as **known** and `--strict`
fails only when a **new** error appears.

## Bootstrap a baseline

```bash
# Record the current architecture checks once, then commit the file.
cqrslens . --write-baseline cqrslens-baseline.json -o ARCHITECTURE.md

git add cqrslens-baseline.json ARCHITECTURE.md
git commit -m "chore: record CqrsLens architecture baseline"
```

In CI, compare against it:

```bash
cqrslens . --baseline cqrslens-baseline.json --strict -o ARCHITECTURE.md
```

`--write-baseline` records the findings, marks them as known for this run, and
continues normally. It is intended for the initial adoption commit; do not run
it automatically in CI.

## How matching works

Each finding is keyed by:

1. the rule code (`MissingHandler`, `LayerViolation`, ...),
2. the target identity (for example `App.Features.PingCommand`), and
3. the root-relative source file.

Line and column numbers are stored for readability but do **not** participate in
matching. Moving a declaration within a file therefore keeps it known. Moving a
violation to a different file makes it new, which is intentional: baseline files
are expected to be reviewed and regenerated when symbols are intentionally
moved or removed.

Duplicate findings with the same key are counted, so a baseline entry covers one
occurrence at a time.

## Report behavior

With `--baseline`, the Architecture Checks section and HTML report separate
**new** findings from **known** findings. The CLI prints counts such as:

```text
Baseline: 3 known finding(s), 1 new finding(s).
```

`--strict` uses the new count when a baseline is present. Rule severity
overrides in `.cqrslens.json` are applied before matching; turning a rule off
also removes it from the baseline.

## Baseline file

The file is deterministic JSON with a schema version:

```json
{
  "schemaVersion": 1,
  "findings": [
    {
      "code": "MissingHandler",
      "target": "App.Features.PingCommand",
      "file": "src/App/Features/PingCommand.cs",
      "project": "App",
      "severity": "error",
      "message": "Request `App.Features.PingCommand` has no handler.",
      "line": 4,
      "column": 1
    }
  ]
}
```

Only `code`, `target`, and `file` are used for matching. Regenerate the file
with `--write-baseline` after intentionally fixing, moving, or renaming findings;
invalid or missing schema versions are reported as usage errors.
