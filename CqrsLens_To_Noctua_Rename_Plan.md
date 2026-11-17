# Rename plan — CqrsLens → Noctua

Drafted 2026-11-02 (Mariam, at Abdullah's direction). Status: PLAN ONLY — nothing
executed. **Abdullah's ruling 2026-11-02 ~18:23: this is a draft. It gets revised
and finalized only after 1.0.0 is finished and released, when the real codebase
and real outputs exist — nothing in it is to be treated as settled fact about the
tool's final capabilities before then.** Execution is gated on: Phase 6 closed, the other-tools + real-benchmark
round done, and Abdullah's explicit go. The rename lands WITH the 1.0.0 release so
users migrate exactly once.

## Why

The tool's scope is now full architecture/design reporting (layers, data model,
endpoints, findings, drift) with CQRS as one lens among several — Phase 6 completes
that move. "CqrsLens" names the original niche; "Noctua" names the tool. The site
already carries the Noctua brand (its config marks the name as the paired identity,
rename-in-one-place by design).

## Verified facts the plan rests on

- NuGet package ID **Noctua is available** (flat-container check 2026-11-02: no
  such package).
- Published NuGet versions are **immutable**: CqrsLens 0.1.0–0.9.0 stay installable
  and functional forever. A rename cannot harm them; it can only strand users if
  the signposting is done badly — hence the deprecation + dual-publish steps below.
- NuGet cannot rename an ID in place. New package + deprecate old is the only path.
- The packaging core is **two lines in one file**:
  `src/CqrsLens.Cli/CqrsLens.Cli.csproj` — `<PackageId>CqrsLens</PackageId>` and
  `<ToolCommandName>cqrslens</ToolCommandName>`.
- GitHub repo renames redirect web URLs, git remotes, and release links — as long
  as no new repo is created under the old name afterwards.

## The rename surface — three tiers

### MUST rename (user-visible)
| Surface | Where | Change |
|---|---|---|
| NuGet package ID | Cli.csproj `PackageId` | `CqrsLens` → `Noctua` |
| Tool command | Cli.csproj `ToolCommandName` | `cqrslens` → `noctua` |
| Console banner / help header | Cli program strings | "CqrsLens x.y.z" → "Noctua x.y.z" |
| Report stamps | Reporting (MD/HTML/JSON headers, watermark default) | name string |
| SARIF tool name | `SarifReportGenerator.cs` (`"name", "CqrsLens"`) | → `Noctua` |
| MCP server name | `McpRequestRouter.cs` serverInfo | → `Noctua` |
| GitHub Action | `action.yml` name/description + README usage | `AbdullahHakima/noctua@…` after repo rename |
| Version pins | README banner/sample, docs/ci.md, ROADMAP, `VersionTests` expectations | regenerate against the new name (the pin test will fail until all copies move together — by design) |
| Config file | `ConfigurationLoader.FileName` | primary becomes `.noctua.json` — see Compat |
| Repo | GitHub settings | `cqrslens` → `noctua` (redirects cover old links) |
| Release workflow | `.github/workflows` — package filename checks, release body | `Noctua.<version>.nupkg` |

### COMPAT (keep working, deliberately)
- **Config filename:** Noctua reads `.noctua.json` first, then falls back to
  `.cqrslens.json` forever (one `if File.Exists` — cheap, permanent insurance for
  every repo that already has the old file).
- **Baselines & diffs:** they compare findings, not name stamps — old baselines
  stay valid across the rename. State this in the release notes; add one test that
  a 0.9.0-written baseline still gates a Noctua 1.0.0 `--strict` run.
- **Old package:** never unlist CqrsLens. Deprecate with alternate-package pointer
  (below).

### LEAVE ALONE (internal, invisible)
- C# namespaces (`CqrsLens.*`), project and assembly names, test project names.
  This is a CLI tool, not a library — nobody compiles against its internals, so
  the names are invisible to users. Renaming them is hundreds of files of pure
  churn risk in a codebase already flagged for architecture cleanup; if it ever
  happens, it belongs to a later refactor, not the rename release.

## Sequence

1. **Preconditions:** Phase 6 closed and merged; benchmark round done; 1.0.0
   content frozen. Abdullah's go.
2. **Rename branch** off main: the MUST table above + fallback config read +
   baseline-compat test. Full suite green; the 4-corpus battery re-run (counts
   must equal the frozen 0.9.0 numbers — the rename changes strings, never
   findings).
3. **Version:** set 1.0.0 in `Directory.Build.props` (+ all pins together).
4. **Dual-publish 1.0.0 (transition release):** build once, publish the same
   binaries as `Noctua 1.0.0` (the future) and as a final `CqrsLens 1.0.0` whose
   README banner and console banner carry one line: "CqrsLens is now Noctua —
   `dotnet tool uninstall -g CqrsLens && dotnet tool install -g Noctua`".
   Existing users who update normally land on the notice instead of silence.
5. **GitHub:** rename repo → create the v1.0.0 release there (old release URLs
   redirect). Verify an old link (e.g. the v0.9.0 release) resolves.
6. **Deprecate:** on nuget.org, mark ALL CqrsLens versions deprecated, reason
   "package renamed", alternate package = Noctua, custom message with the
   install commands. (Done after Noctua 1.0.0 is verified live, never before.)
7. **Verify like a release, because it is one:** flat-container shows Noctua
   1.0.0; clean-environment `dotnet tool install -g Noctua` → `noctua --version`
   prints 1.0.0; a real scan runs; CqrsLens page shows the deprecation banner;
   `dotnet tool install -g CqrsLens` still works (old users unharmed).
8. **Comms, his voice, no process talk:** README on the renamed repo leads with
   Noctua and one "formerly CqrsLens" line; the Noctua site flips BRAND
   (name/version/install command — it was built for this), changelog gains the
   1.0.0 entry; one short blog/LinkedIn note at his approval. The old blog post
   stays as-is historically, with its links still resolving via redirects.

## Decisions for Abdullah (defaults recommended)

1. **Repo name:** rename to `noctua` (recommended) vs keep the repo as
   `cqrslens` and rename only the package. Repo rename is one settings click and
   redirects are reliable — but the Action path changes to `AbdullahHakima/noctua@v1`.
2. **Config fallback:** read `.cqrslens.json` forever (recommended) vs one-cycle
   fallback then drop.
3. **Namespaces:** leave `CqrsLens.*` internal names untouched (recommended).
4. **Transition:** dual-publish 1.0.0 under both IDs (recommended) vs hard cut —
   Noctua only, CqrsLens line ends at 0.9.0 with deprecation only.

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| Users never learn of the move | Dual-publish notice + nuget.org deprecation banner + README line |
| Download count resets to 0 on the new page | Accepted cost; Noctua page starts at 1.0.0 with the full history linked from the old page |
| Search shows the old name for months | Deprecation alternate-package pointer routes nuget.org searchers; site + repo carry the new name |
| A missed name string ships (stamp, SARIF, MCP) | Pre-release grep gate: zero user-visible "CqrsLens" strings in a generated report set (MD/HTML/JSON/SARIF/agent) from the rename build |
| "Noctua" is also a PC-cooling brand | Different industry; noted, accepted (his call stands) |
| Someone re-registers/uses the old repo name, breaking redirects | Never create a new `cqrslens` repo; keep the rename in place |

## What does NOT change

- The version line: 1.0.0 follows 0.9.0 — no restart, no gap.
- Every existing install, report, baseline, and CI pipeline using CqrsLens keeps
  working unchanged.
- The roadmap gates: Phase 6 first, benchmarks second, rename rides the 1.0.0
  release — this plan changes no priorities.
