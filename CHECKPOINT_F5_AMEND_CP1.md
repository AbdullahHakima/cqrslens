# Stage F5 Amendment Checkpoint 1 — Fix Proof & Repro Pack Re-Scan

- **Date:** 2026-11-17
- **Status:** `DONE` (`CP1` complete; proceeding to `CP2`)
- **Branch:** `foundations/f5-spine-reports` (HEAD `f8902e9e75ac62d936d285b5e42f2be536228f25`)
- **Antigravity CLI Version:** `1.5.10` (`agy --version`)
- **Corpus Pin:** `/data/workspace/corpora/nopcommerce` (`release-4.90.0` at `e7a52b9199f9c7651839e92826df47f668f3c767`, clean read-only)
- **Repro Pack:** `/tmp/mediator-repro/` (read-only, untouched)

---

## 1. Starting Tree & Untouched F5 Hash Verification

Verified before any edit that `HEAD` was `f8902e9e75ac62d936d285b5e42f2be536228f25` on `foundations/f5-spine-reports` with the exact F5 CP3 working tree (`3` modified files + `8` untracked files/dirs + `BRIEF_F5_AMENDMENT.md`). All `7` F5 production/test files retain identical SHA-256 hashes before and after CP1:

| F5 File | SHA-256 (Unchanged) |
|---|---|
| [DocsBuildingBlocksPageWriter.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Docs/DocsBuildingBlocksPageWriter.cs) | `6f6ddae6ed9a00dba0c4471f7f676959882726e14bdbba383682c5ffca5def0f` |
| [HtmlReportGenerator.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Html/HtmlReportGenerator.cs) | `98265f70ef0225b3f3ea5a985b14a748c12d1b6e4df5ac8e5e2286052fc58c50` |
| [MarkdownReportWriter.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Markdown/MarkdownReportWriter.cs) | `2d846f2c105672861d80c2998c21b975a8d050820c750054162e092b9ab7da71` |
| [HtmlSpineSectionRenderer.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Html/HtmlSpineSectionRenderer.cs) | `94040ff3f42306c3391dcf73d7eb003f3b48f226308ae6bfbb6c3524d3b38f42` |
| [MarkdownSpineWriter.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Markdown/MarkdownSpineWriter.cs) | `92de952f316eed3c9dc7834ba5feb2cb7cd22375fd570d14f7221694322d66be` |
| [SpineReportView.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Spine/SpineReportView.cs) | `e3c90084b37a107edad43191172907d68f798c748d7f69629c6ef818251c86cf` |
| [F5SpineReportTests.cs](file:///data/workspace/cqrslens/tests/CqrsLens.Tests/F5SpineReportTests.cs) | `9e34e6416a0e1777f2e2842164130ca0090141e9f850f8d6521d70deb269ddf8` |

---

## 2. Production Change (`D-A1`, `H-A1`)

The sole production change is the `UsesMediator` package-reference predicate in [MediatorLibraryDetector.cs](file:///data/workspace/cqrslens/src/CqrsLens.Analysis/Contracts/MediatorLibraryDetector.cs#L27-L33):

```diff
--- a/src/CqrsLens.Analysis/Contracts/MediatorLibraryDetector.cs
+++ b/src/CqrsLens.Analysis/Contracts/MediatorLibraryDetector.cs
@@ -27,8 +27,8 @@ internal static partial class MediatorLibraryDetector
     private static bool UsesMediator(IReadOnlyList<ProjectInfo> projects, IReadOnlyList<SourceFile> files)
         => projects.Any(p =>
                p.PackageReferences.Any(r =>
-                   r.Equals("Mediator.Abstractions", StringComparison.OrdinalIgnoreCase) ||
-                   r.Equals("Mediator.SourceGenerator", StringComparison.OrdinalIgnoreCase)) ||
+                   r.Equals("Mediator", StringComparison.OrdinalIgnoreCase) ||
+                   r.StartsWith("Mediator.", StringComparison.OrdinalIgnoreCase)) ||
                p.ProjectUsings.Any(u => u.Target == "Mediator")) ||
            files.Any(f => MediatorUsingRegex().IsMatch(f.Code));
```

`UsesWolverine`, `MediatorUsingRegex`, `WolverineUsingRegex`, and all other files under `src/` are untouched.

---

## 3. New Test Suite Additions (`D-A2`, `H-A3`)

Added [MediatorPackageDetectionTests.cs](file:///data/workspace/cqrslens/tests/CqrsLens.Tests/MediatorPackageDetectionTests.cs) (`0` existing test files modified) with `5` test methods covering `9` test cases total:

1. `BareMediatorPackage_WithoutUsingDirective_DetectsRequestsHandlersAndControllerEndpoint` (`[Theory]`, `Text` + `Roslyn`) — bare `Mediator` package, fully-qualified types, no `using Mediator;` (fixture `B` shape).
2. `BareMediatorPackage_WithUsingDirective_DetectsRequestsHandlersAndControllerEndpoint` (`[Theory]`, `Text` + `Roslyn`) — bare `Mediator` package + `using Mediator;` (`using`-path regression on meta-package).
3. `MediatorAbstractionsPackage_WithoutUsingDirective_DetectsRequestsHandlersAndControllerEndpoint` (`[Theory]`, `Text` + `Roslyn`) — `Mediator.Abstractions` package, fully-qualified types, no `using Mediator;` (fixture `B3` shape).
4. `UnrelatedMediatorXPackage_WithoutUsingDirective_DoesNotEnableMediatorConvention` (`[Fact]`, `Text`) — `MediatorX` package + fully-qualified `Mediator.ICommand`, asserts empty conventions and `0` requests.
5. `FutureMediatorDotSubPackage_WithoutUsingDirective_DetectsRequestsAndHandlers` (`[Theory]`, `Text` + `Roslyn`) — hypothetical `Mediator.Example` sub-package, fully-qualified types, no `using Mediator;`.

### Test Execution (`DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH`)
- Full suite (`dotnet test CqrsLens.sln -c Release --nologo`):
  - `Passed! - Failed: 0, Passed: 769, Skipped: 68, Total: 837, Duration: 5 s - CqrsLens.Tests.dll (net8.0)` (`68` skipped external-corpus tests absent on Box #4).
- Filtered new tests (`dotnet test CqrsLens.sln -c Release --no-build --nologo --filter "FullyQualifiedName~MediatorPackageDetectionTests"`):
  - `Passed! - Failed: 0, Passed: 9, Skipped: 0, Total: 9, Duration: 818 ms - CqrsLens.Tests.dll (net8.0)`

---

## 4. Repro Pack Re-Scan (`A–G`, `B2`, `B3` in Both Modes, `D-A4`, `H-A2`)

Scanned all `9` fixtures in `/tmp/mediator-repro/` in both `text` and `roslyn` modes (`--generated-at 2026-11-17T00:00:00Z`) using `/data/workspace/cqrslens/src/CqrsLens.Cli/bin/Release/net8.0/cqrslens.dll`:

| Fixture | Mode | Exit | Conventions | Requests / Handlers / Endpoints | Linked Endpoints (`targetRequest`) | Spine (`Comp / Rel / EP / Unprov`) | Comparison vs 2.0.0 Baseline JSON |
|---|---|---:|---|---|---|---|---|
| `A` | `text` | `0` | `["Mediator"]` | `2 / 2 / 2` | `CreateOrderCommand`, `GetOrderQuery` | `5 / 8 / 2 / 0` | **`0`-byte diff vs `A.json`** (`19,109` B) |
| `A` | `roslyn` | `0` | `["Mediator"]` | `2 / 2 / 2` | `CreateOrderCommand`, `GetOrderQuery` | `5 / 8 / 2 / 0` | `text == roslyn` (modulo `scanner`) |
| `B` | `text` | `0` | `["Mediator"]` | **`2 / 2 / 1`** | `OrdersController.Get -> GetOrderQuery` | `5 / 5 / 1 / 0` | **FIXED** (`7,191 -> 14,603` B vs `B.json`) |
| `B` | `roslyn` | `0` | `["Mediator"]` | **`2 / 2 / 1`** | `OrdersController.Get -> GetOrderQuery` | `5 / 5 / 1 / 0` | **FIXED** (`3,569 -> 14,605` B vs `B-roslyn.json`; `H-A2` PASS) |
| `B2` | `text` | `0` | `["Mediator"]` | `2 / 2 / 1` | `OrdersController.Get -> GetOrderQuery` | `5 / 5 / 1 / 0` | **`0`-byte diff vs `B2.json`** (`14,604` B) |
| `B2` | `roslyn` | `0` | `["Mediator"]` | `2 / 2 / 1` | `OrdersController.Get -> GetOrderQuery` | `5 / 5 / 1 / 0` | `text == roslyn` (modulo `scanner`) |
| `B3` | `text` | `0` | `["Mediator"]` | `2 / 2 / 1` | `OrdersController.Get -> GetOrderQuery` | `5 / 5 / 1 / 0` | **`0`-byte diff vs `B3.json`** (`14,617` B) |
| `B3` | `roslyn` | `0` | `["Mediator"]` | `2 / 2 / 1` | `OrdersController.Get -> GetOrderQuery` | `5 / 5 / 1 / 0` | `text == roslyn` (modulo `scanner`) |
| `C` | `text` | `0` | `["Mediator"]` | `2 / 2 / 2` | `CreateOrderCommand`, `GetOrderQuery` | `5 / 8 / 2 / 0` | **`0`-byte diff vs `C.json`** (`19,049` B) |
| `C` | `roslyn` | `0` | `["Mediator"]` | `2 / 2 / 2` | `CreateOrderCommand`, `GetOrderQuery` | `5 / 8 / 2 / 0` | `text == roslyn` (modulo `scanner`) |
| `D` | `text` | `0` | `["Mediator"]` | `2 / 2 / 2` | `CreateOrderCommand`, `CancelOrderCommand` | `5 / 6 / 2 / 2` | **`0`-byte diff vs `D.json`** (`17,703` B) |
| `D` | `roslyn` | `0` | `["Mediator"]` | `2 / 2 / 2` | `CreateOrderCommand`, `CancelOrderCommand` | `5 / 6 / 2 / 2` | `text == roslyn` (modulo `scanner`) |
| `E` | `text` | `0` | `["Mediator"]` | `1 / 1 / 1` | `CreateOrderCommand` | `5 / 7 / 2 / 1` | **`0`-byte diff vs `E.json`** (`18,307` B) |
| `E` | `roslyn` | `0` | `["Mediator"]` | `1 / 1 / 1` | `CreateOrderCommand` | `5 / 7 / 2 / 1` | `text == roslyn` (modulo `scanner`) |
| `F` | `text` | `0` | `["Mediator"]` | `1 / 1 / 1` | `CreateOrderCommand` | `3 / 5 / 1 / 0` | **`0`-byte diff vs `F.json`** (`11,864` B) |
| `F` | `roslyn` | `0` | `["Mediator"]` | `1 / 1 / 1` | `CreateOrderCommand` | `3 / 5 / 1 / 0` | `text == roslyn` (modulo `scanner`) |
| `G` | `text` | `0` | `[]` *(omitted)* | `2 / 2 / 2` | `CreateOrderCommand`, `GetOrderQuery` | `5 / 8 / 2 / 0` | **`0`-byte diff vs `G.json`** (`19,009` B) |
| `G` | `roslyn` | `0` | `[]` *(omitted)* | `2 / 2 / 2` | `CreateOrderCommand`, `GetOrderQuery` | `5 / 8 / 2 / 0` | `text == roslyn` (modulo `scanner`) |

- **`H-A2` Roslyn guardrail:** Fixture `B` in `roslyn` mode matches `B` in `text` mode identically (modulo `"scanner"`), and matches `B3.json` identically (modulo `"rootName"` and `projects[0].packages`), with `conventions: ["Mediator"]`, `2` requests (`CreateOrderCommand`, `GetOrderQuery`), both handlers linked, controller endpoint `OrdersController.Get` linked (`targetRequest: "GetOrderQuery"`), `5` components, `5` relations, `1` spine endpoint, `0` unproven items.
