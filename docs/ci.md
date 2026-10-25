# CI integration

CqrsLens can produce the human report, a SARIF file, GitHub Actions annotations,
and a job summary in a single scan. The same switches work locally, so CI can be
debugged with the installed tool.

## GitHub Action

```yaml
permissions:
  contents: read
  security-events: write   # only needed for upload-sarif

steps:
  - uses: actions/checkout@v4

  - uses: AbdullahHakima/cqrslens@v0.5.1
    with:
      path: src
      output: ARCHITECTURE.md
      format: md
      strict: 'true'
      baseline: cqrslens-baseline.json
      config: .cqrslens.json
      sarif-output: cqrslens-results.sarif
      upload-sarif: 'true'
      annotations: 'true'
```

The Action installs the requested NuGet tool version and passes paths relative to
the repository root. It uploads `cqrslens-architecture-report` and
`cqrslens-sarif` artifacts even when the strict check fails.

### Inputs

| Input | Default | Meaning |
| --- | --- | --- |
| `path` | `.` | Folder to scan. |
| `output` | `ARCHITECTURE.md` | Markdown/HTML/JSON report path. |
| `format` | `md` | `md`, `html`, or `json`. |
| `render` | empty | `mermaid` or `svg` for HTML diagrams; empty uses config, then `mermaid`. |
| `strict` | `false` | Fail on architecture-check errors; with `baseline`, only new errors. |
| `markers` | empty | Comma-separated custom request markers. |
| `handler-markers` | empty | Comma-separated handler roots for external wrapper interfaces. |
| `analysis` | empty | `text` or `roslyn`; empty uses config, then text. |
| `config` | empty | `.cqrslens.json` path; empty auto-discovers at the scan root. |
| `baseline` | empty | Baseline file for known/new matching. |
| `sarif-output` | `cqrslens-results.sarif` | SARIF path; empty disables SARIF. |
| `upload-sarif` | `false` | Upload SARIF to GitHub code scanning. |
| `annotations` | `true` | Emit file/line annotations for new findings. |
| `version` | release version | NuGet tool version installed by the Action. |

Outputs: `report-path` and `sarif-path`.

### SARIF upload and permissions

`upload-sarif: 'true'` uses `github/codeql-action/upload-sarif` and requires
`security-events: write`. GitHub code scanning may require GitHub Advanced
Security for private repositories. The SARIF artifact is still produced when
upload is disabled, so another tool can consume it.

## CLI

```bash
cqrslens src \
  --config .cqrslens.json \
  --baseline cqrslens-baseline.json \
  --strict \
  --sarif cqrslens-results.sarif \
  --summary cqrslens-summary.md \
  --github-annotations \
  --ci-root "$GITHUB_WORKSPACE" \
  -o ARCHITECTURE.md
```

- `--sarif FILE` writes SARIF 2.1.0 alongside the normal report. Results include
  rule metadata, source locations, stable fingerprints and `baselineState`
  (`new` / `unchanged`) when a baseline is present.
- `--summary FILE` writes a compact Markdown summary. The Action appends it to
  `$GITHUB_STEP_SUMMARY`; when using the CLI directly, append it yourself.
- `--github-annotations` emits workflow commands for new findings. Known
  baseline findings are omitted.
- `--ci-root DIR` rebases source paths from the scan root to the repository root,
  so `path: src` produces annotations and SARIF URIs like
  `src/Features/PingCommand.cs`.

Scan diagnostics remain in the full report and the summary. Strict checks still
skip when a scan is incomplete or ambiguous; new error annotations may still be
useful, but `--strict` will not fail on low-confidence data.
