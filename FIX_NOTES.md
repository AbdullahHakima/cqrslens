# FIX NOTES — Phase 1 (performance only)

## Summary

The "Parse sources" pathology was **catastrophic backtracking in the
`PropertyRegex`** in `src/CqrsLens.Analysis/Text/SourceFileScanner.cs`, triggered
by the multi-megabyte single-line string literals in NorthwindTraders'
`SampleDataSeeder.cs`. The fix is a one-token, semantics-preserving regex guard.
NorthwindTraders now scans in **0.60 s (text)** and **1.75 s (roslyn)** on this
machine, with byte-identical findings.

## Root cause (exact pattern and location)

`SourceFileScanner.PopulateProperties` runs `PropertyRegex().Matches(code, start)`
over every class body. The `<type>` group is:

```
(?<type>(?:global::)?[A-Za-z_]\w*(?:\s*\.\s*[A-Za-z_]\w*)* ... )\s+(?<name>[A-Za-z_]\w*) ... { get/set }
```

The pattern requires whitespace (`\s+`) after the type. Inside a string literal
such as the seeder's hex blob, the contents are one long run of *word* characters
(`FABCDEF0123456789…`, no whitespace). At **every** offset in that run the engine
restarts the pattern, `[A-Za-z_]\w*` greedily consumes the remainder of the run,
then `\s+` fails and the engine backtracks one character at a time to try again.
That is **O(run²)**, and the seeder contains several 21k–43k-character runs, so
the file alone cost ~125 s (text) — 99.7% of the whole scan. It is CPU-bound and
allocation-free, exactly as the profile showed.

Measurement that isolated it (single seeder file, text mode):

| Stage | Before | After |
| --- | ---: | ---: |
| `SourceMask.Build` | 25 ms | (unchanged) |
| `NamespaceMap` + usings | 12 ms | (unchanged) |
| interface declaration regex | 0 ms | (unchanged) |
| class declaration regex | 9 ms | (unchanged) |
| **`PopulateProperties` (PropertyRegex)** | **130 055 ms** | **~200 ms** |

`PropertyRegex` alone over the file body timed out at 296 s in isolation. The
other regexes and the mask build were already linear.

## What changed

`src/CqrsLens.Analysis/Text/SourceFileScanner.cs`: added a leading
`(?<![A-Za-z0-9_])` to `PropertyRegex`.

- It is a **zero-width** assertion, so it cannot change capture-group contents.
- A C# property's type token never begins immediately after an identifier
  character, so the guard only rejects start offsets inside a `\w` run — the
  ones that caused the quadratic restart. Every legitimate match is still found
  at the same offset (the engine would already have consumed that offset as part
  of the surrounding identifier).
- Result: the engine tries each `\w` run roughly once instead of once per
  character, making the scan linear in the input size. No skipping, truncation,
  sampling, or line-length cap. The culprit file is parsed in full.

### Correctness proof of "byte-identical"

I ran the exact before/after `PropertyRegex` (same captures: attrs, mods, type,
name, rest) over **every `.cs` file in all four corpora (1,587 files)** and
compared the full match set. The only reported difference was
`SampleDataSeeder.cs`, where the *old* pattern hit the 60 s regex timeout (no
matches) while the new one returns its true 0 matches (the seeder declares no
properties; the old timeout path also produced 0). All other 1,586 files were
identical.

End-to-end, before and after JSON reports (`--generated-at` pinned) are
**byte-identical** for all 8 runs (text + roslyn × 4 corpora).

## Timings measured on this machine

Wall clock for a full `cqrslens <corpus> --analysis <mode> --format json`
(`--generated-at` pinned so only scan work counts):

| Corpus | Mode | Before | After | Parse (before → after) |
| --- | --- | ---: | ---: | ---: |
| northwindtraders (157 files) | text | **118.97 s** | **0.60 s** | 118 557 ms → 198 ms |
| northwindtraders | roslyn | **120.12 s** | **1.75 s** | 118 367 ms → 216 ms |
| eshoponweb (254 files) | text | 0.42 s | 0.40 s | 141 ms → 105 ms |
| eshoponweb | roslyn | 1.43 s | 1.43 s | 180 ms → 99 ms |
| eshop (114 files) | text | 0.41 s | 0.34 s | 157 ms → 79 ms |
| eshop | roslyn | 1.28 s | 1.20 s | 178 ms → 61 ms |
| modular-monolith (1 062 files) | text | 1.05 s | 0.75 s | 507 ms → 176 ms |
| modular-monolith | roslyn | 2.49 s | 2.26 s | 432 ms → 156 ms |

(The brief quoted ~295 s for Northwind; this machine measured ~119 s before the
fix — still ~200× over budget. The after numbers meet the ≤ 5 s text / ≤ 15 s
roslyn targets with wide margin.)

Focused single-file repro (`.../System/Commands/SeedSampleData/SampleDataSeeder.cs`):
**118 973 ms → 0.37 s** end to end (parse 130 155 ms → 205 ms).

## Findings

All corpora reports are byte-identical before/after. Counts after the fix:

| Corpus | Requests | Handlers | Validators | Notifications | Notif. handlers | Errors | Warnings |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| northwindtraders | 19 (11 cmd / 8 qry) | 19 | 4 | 1 | 1 | 0 | 8 |
| eshoponweb | 2 | 2 | 0 | 0 | 0 | 0 | 0 |
| eshop | 9 | 15 | 4 | 7 | 7 | 1 | 5 |
| modular-monolith | 105 | 105 | 5 | 86 | 30 | 0 | 133 |

Northwind matches acceptance criterion 1 exactly (19 requests / 19 handlers /
4 validators / 1 notification `CustomerCreated` / 1 notification handler /
0 errors / 8 genuine `CommandWithoutValidator` warnings). eShop's error and
modular-monolith's warnings are unchanged and belong to other phases.

Two honesty notes:

- eShop reports **15** handler types, not the brief's "8". The difference is the
  generic `IdentifiedCommand`, which has 7 handlers; excluding it, eshop has 8.
  Crucially the value is **identical before and after** (no regression), and it
  is out of scope for this phase.
- TEXT vs ROSLYN counts agree on every corpus. (The full JSON differs only in
  pre-existing `dbContext` ordering/detail, also identical before and after.)

## Regression fixture

- `tests/CqrsLens.Tests/PerformanceLongLineTests.cs` — builds a temp solution
  containing a 128 KiB single-line hex-string data file (the Problem 1 shape)
  plus a request/handler, then asserts the text scan finishes under 20 s and the
  findings/skipped-file state are correct. Validated as a real detector: **fails
  on the old scanner (113 376 ms) and passes on the fixed one (118 ms)**.
- `benchmarks/CqrsLens.Benchmarks/Program.cs` — `--generate` now writes a 512 KiB
  long-line `SeedData.cs` into the synthetic solution, so the benchmark workflow
  exercises the long-word-run parse path on every run (206 ms for the 5-slice
  synthetic solution after the fix).

## `--slice` (criterion 4)

`cqrslens <northwindtraders> --analysis text --slice Customer` completes in
**0.59 s** — the same order of magnitude as the fixed full scan, and finds the
expected 5 slices.

## `dotnet test`

```
dotnet test CqrsLens.sln -c Release --no-build
Passed! - Failed: 0, Passed: 343, Skipped: 0, Total: 343
```

Includes the new `PerformanceLongLineTests`.

## Scope

Performance only. No change to the MultipleHandlers accuracy problem, no parser
public-surface change, no new package dependencies, no skipping/truncation/
sampling of any file.
