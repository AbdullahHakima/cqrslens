# Contributing to CqrsLens

This guide gets you from clone to a mergeable pull request.

## Prerequisites

- .NET 8 SDK (`dotnet --version` should print 8.x)
- Git

## Build

```bash
dotnet build CqrsLens.sln
```

## Run the tests

```bash
dotnet test CqrsLens.sln
```

All tests must pass before a PR is mergeable. New detectors or parser changes
need new tests — the existing suites under `tests/CqrsLens.Tests/` show the
style (small fixture strings/files per case).

## Try the tool locally

The repo ships a sample Clean Architecture solution you can scan end-to-end:

```bash
dotnet run --project src/CqrsLens -- samples/SampleShop -o /tmp/ARCHITECTURE.md
```

Or pack and install the tool from source:

```bash
dotnet pack src/CqrsLens -c Release -o artifacts
dotnet tool install --tool-path /tmp/cqrslens-tools --add-source artifacts CqrsLens
/tmp/cqrslens-tools/cqrslens samples/SampleShop
```

Compare your output with `docs/sample-output/ARCHITECTURE.md` — if a change
alters the sample output intentionally, regenerate that file in the same PR
and mention it in the description.

## Pull request expectations

- One focused change per PR; keep the diff reviewable
- `dotnet build` clean (no warnings) and `dotnet test` green
- Add or update tests for behavior you change
- Add a `CHANGELOG.md` entry under the "Unreleased" heading
- Update `ROADMAP.md` if your change ships or reshapes a roadmap item
- Describe *why* in the PR body, not only *what*

## Looking for a first issue?

The open items in [ROADMAP.md](ROADMAP.md) are the best starting points.
The ER diagram is self-contained parser work, and the JSON output mode
touches mostly the report layer. Comment on the issue before starting so
work isn't duplicated.

## Code style

- Match the existing style of the file you touch (naming, nullability,
  file-scoped namespaces)
- Detectors live in the scanner/analyzer layer; the report generator only
  renders what the analyzer found — keep that separation
- The analyzer must never throw on malformed input files; skip and continue
