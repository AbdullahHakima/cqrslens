# CI integration

CqrsLens can produce the human report, an architecture diff against a base scan,
a sticky pull request comment, a SARIF file, GitHub Actions annotations, and a
job summary in a single scan. The same switches work locally, so CI can be
debugged with the installed tool.

## GitHub Action

```yaml
permissions:
  contents: read
  pull-requests: write     # only needed for comment-pr
  security-events: write   # only needed for upload-sarif

steps:
  - uses: actions/checkout@v4

  - uses: AbdullahHakima/cqrslens@v0.7.0
    with:
      path: src
      output: ARCHITECTURE.md
      format: md
      strict: 'true'
      baseline: cqrslens-baseline.json
      config: .cqrslens.json
      comment-pr: 'true'
      sarif-output: cqrslens-results.sarif
      upload-sarif: 'true'
      annotations: 'true'
```

The Action installs the requested NuGet tool version and passes paths relative to
the repository root. It uploads `cqrslens-architecture-report`,
`cqrslens-architecture-diff` (when a diff is generated), and `cqrslens-sarif`
artifacts even when the strict check fails.

### Pull request architecture diff comments

Set `comment-pr: 'true'` (with `pull-requests: write` permission) to post or
update a sticky pull request comment marked with
`<!-- cqrslens-architecture-diff -->`:

- When `diff-base` is omitted on a `pull_request` workflow, the Action fetches
  `origin/$GITHUB_BASE_REF` into a temporary worktree and runs `--diff` against
  the base branch automatically.
- You can also pass an explicit `diff-base` path (either a base `ARCHITECTURE.json`
  file or a base checkout folder) and `diff-output` path (defaults to
  `cqrslens-diff.md`).
- Subsequent pushes to the same pull request update the existing CqrsLens comment
  in place rather than creating duplicate comments.

### Inputs

| Input | Default | Meaning |
| --- | --- | --- |
| `path` | `.` | Folder to scan. |
| `output` | `ARCHITECTURE.md` | Markdown/HTML/JSON report path. |
| `format` | `md` | `md`, `html`, or `json`. |
| `render` | empty | `mermaid` or `svg` for HTML diagrams; empty uses config, then `mermaid`. |
| `watermark` | empty | Optional custom watermark stamp embedded in console and generated reports. |
| `strict` | `false` | Fail on architecture-check errors; with `baseline`, only new errors. |
| `markers` | empty | Comma-separated custom request markers. |
| `handler-markers` | empty | Comma-separated handler roots for external wrapper interfaces. |
| `analysis` | empty | `text` or `roslyn`; empty uses config, then text. |
| `config` | empty | `.cqrslens.json` path; empty auto-discovers at the scan root. |
| `baseline` | empty | Baseline file for known/new matching. |
| `diff-base` | empty | Base `ARCHITECTURE.json` file or folder to diff against; auto-scanned on PRs when `comment-pr: 'true'`. |
| `diff-output` | empty | Diff report file path (`cqrslens-diff.md` when diffing is active, or `.json`). |
| `comment-pr` | `false` | Post or update a sticky pull request comment with the architecture diff. |
| `github-token` | `${{ github.token }}` | GitHub token used when `comment-pr` is `true`. |
| `sarif-output` | `cqrslens-results.sarif` | SARIF path; empty disables SARIF. |
| `upload-sarif` | `false` | Upload SARIF to GitHub code scanning. |
| `annotations` | `true` | Emit file/line annotations for new findings. |
| `version` | release version | NuGet tool version installed by the Action. |

Outputs: `report-path`, `sarif-path`, and `diff-path`.

### SARIF upload and permissions

`upload-sarif: 'true'` uses `github/codeql-action/upload-sarif` and requires
`security-events: write`. GitHub code scanning may require GitHub Advanced
Security for private repositories. The SARIF artifact is still produced when
upload is disabled, so another tool can consume it.

## CLI

```bash
# Optional: export a base snapshot once (or pass a base checkout folder directly to --diff)
cqrslens src -o base-architecture.json

# Scan current branch and emit report + diff + SARIF + summary + annotations
cqrslens src \
  --config .cqrslens.json \
  --baseline cqrslens-baseline.json \
  --diff base-architecture.json \
  --diff-output cqrslens-diff.md \
  --strict \
  --sarif cqrslens-results.sarif \
  --summary cqrslens-summary.md \
  --github-annotations \
  --ci-root "$GITHUB_WORKSPACE" \
  -o ARCHITECTURE.md
```

- `--diff PATH_OR_JSON` compares the current scan against a base
  `ARCHITECTURE.json` report or a base solution folder.
- `--diff-output FILE` writes the architecture diff report as PR-comment-ready
  Markdown (with `<!-- cqrslens-architecture-diff -->` and a Mermaid CQRS delta
  diagram) or as structured JSON when `FILE` ends with `.json`.
- `--sarif FILE` writes SARIF 2.1.0 alongside the normal report. Results include
  rule metadata, source locations, stable fingerprints and `baselineState`
  (`new` / `unchanged`) when a baseline is present.
- `--summary FILE` writes a compact Markdown summary (including the architecture
  diff headline when `--diff` is active). The Action appends the diff or summary
  to `$GITHUB_STEP_SUMMARY`; when using the CLI directly, append it yourself.
- `--github-annotations` emits workflow commands for new findings. Known
  baseline findings are omitted.
- `--ci-root DIR` rebases source paths from the scan root to the repository root,
  so `path: src` produces annotations, diff locations, and SARIF URIs like
  `src/Features/PingCommand.cs`.

Scan diagnostics remain in the full report and the summary. Strict checks still
skip when a scan is incomplete or ambiguous; new error annotations may still be
useful, but `--strict` will not fail on low-confidence data.
