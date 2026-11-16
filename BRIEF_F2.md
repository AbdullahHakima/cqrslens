# BRIEF — F2: First widening slice — controllers, services, repositories (CqrsLens foundations)

Driver: Antigravity CLI on box #4. Model: gemini-4-hydrogen-xhigh.
Fresh session for this brief. Repo: /data/workspace/cqrslens, NEW branch
`foundations/f2-controllers`, based on `foundations/f1-spine` @ `3fcbcba`
("Add the architecture spine", 112 files, verified on origin; suite
731/731). Verify HEAD is `3fcbcba` and the working tree is clean apart
from the untracked doc files before creating the branch.
Corpus: nopCommerce pinned `release-4.90.0` @ `e7a52b91`, read-only at
`/data/workspace/corpora/nopcommerce` (see Corpus section).

Read `F0_METAMODEL_v2.1.md` AND `F2_CORPUS_AUDIT.md` in the repo root in
full before writing any code. F0 v2.1 is the approved architectural
baseline; the audit is the hand-audited ground truth F2 must prove
against. This brief is the work order for stage F2 of the foundations
build order. If this brief and F0 or the audit disagree, STOP and record
the disagreement in the checkpoint — do not resolve it yourself. Where
the decision brief's expectations and the audit differ, the AUDIT wins:
notably, there is no `DependencyRegistrar` in nopCommerce 4.90 (audit
§4) — do not build anything that assumes it exists.

## Mission

Inventory controllers, services, and repositories as spine components
with evidenced roles, and prove the reference / DI / call / data edges
between them on the pre-audited non-CQRS corpus (nopCommerce 4.90) plus
all existing corpora. This stage ADDS detection — but only under the
evidence rules in this brief. F0 §4 is absolute: a relation type with no
evidence rule does not enter the model. Every new relation below names
its rule; every rule names its ground-truth anchor in the audit. Nothing
here re-litigates F1: the spine, identities, dedup keys, and evidence
record are taken as built.

## Corpus (frozen; read-only)

| Item | Value |
|---|---|
| Repository | `https://github.com/nopSolutions/nopCommerce.git` |
| Location on box #4 | `/data/workspace/corpora/nopcommerce` |
| Tag / commit | `release-4.90.0` / `e7a52b9199f9c7651839e92826df47f668f3c767` |
| Size | 35 `*.csproj`, 133 `*Controller.cs`, 210 `*Service.cs` (99 interfaces), 3 repository files, 3,464 `.cs` files; net9.0, SDK 9.0.100; linq2db 5.4.1, no EF Core |

Patch tags `release-4.90.1` through `release-4.90.8` exist on the
remote. They were NOT chosen: `4.90.0` stands. Do not re-pin.

F1 baseline (frozen regression line; audit §7; text mode, JSON, pinned
`--generated-at 2026-01-01T00:00:00Z`, 1 warmup + 3 timed runs):

| Metric | Value |
|---|---|
| Median wall time | **3,688 ms** (3,732 / 3,649 / 3,688) |
| Output | 9,644,562 bytes, sha256 `b2825eb86ab15a59fb48430fa1eac7562637d5e383ae5b48351ef515f400b2c3` (all 3 runs byte-identical) |
| Projects / files | 35 / 3,464 |
| Relations | **3,234** — `calls` 2,446, `resolvesTo` 776, `references` 12; 6,830 evidence entries |
| Requests / handlers / notifications / behaviors / DbContexts / endpoints | **0 / 0 / 0 / 0 / 0 / 0** |
| Validators | 105 (FluentValidation; must not move) |
| Checks / diagnostics / errors / warnings / skipped files | 0 / 0 / 0 / 0 / 0 |

F1-final frozen relations lines for the six existing corpora
(`f1-perf-verification.md`, Check 4): ABP 3,624 relations / 3,657
occurrences (json 5,918,776 B, sha `2567a5e5…`); modular 710; northwind
172; eshop 114; ardalis 156 / 160 occurrences; jasontaylor 124 / 132
occurrences; GoldenCorpus 25; SampleShop 14; Adversarial 6;
DiffPair_base 1. F2's relations counts are EXPECTED to grow (new
components and edges): the regression gate is not "counts identical"
but "every delta enumerated with its rule, all other sections
byte-identical" (see H3 and CP3 gate 7).

Corpus handling rules (part of H10): verify `git status` clean in the
corpus before the first scan and after the last run. Never modify the
corpus, never check out another tag, never build it. Text mode scans
source only; the corpus targets net9.0 while the tool runs on .NET 8
and no corpus compilation is involved.

## F2 evidence rules (normative)

### R-CTRL — Controller role

F0 §3.1: a class exposing endpoints — derives from a controller base
(`Controller` / `ControllerBase` or equivalent) or carries endpoint
attributes. ComponentId is the SymbolId (§2); one component per symbol,
roles as a set (§2.1). Ground truth: the 5 slice controllers in
`src/Presentation/Nop.Web/Controllers/` (Catalog 21 actions,
Product 13, Checkout 29, Order 12, ShoppingCart 25 — audit Table A).
Inventory reconciliation (CP1) must account for all 133 `*Controller.cs`
files (91 Presentation, 42 Plugins): every file that does NOT yield a
Controller component must have its reason recorded (abstract base,
non-controller namesake, test-only, or other — read the code, do not
guess).

### R-SVC — Service role

F0 §3.1, verbatim rule: a class injected and called through an
interface or directly — constructor injection + call sites, OR a DI
registration naming it. "Injected" means appearing as a constructor
parameter of a scanned class. "Called" means a `Calls` edge per R-CALL.
"Registration" means an explicit `Add*` call per R-DI.

- **Factories are Services under this rule.** There is no Factory kind
  in the Tier 1 taxonomy; a factory class injected and called through
  its interface satisfies the Service evidence rule. Do not invent a
  Factory role.
- **Settings / config classes are NOT components.** `ISettings`
  implementations and `*Settings` concrete types (e.g. `OrderSettings`,
  `CatalogSettings`) are Tier 2 configuration surface (F0 §4.2): they
  get no role, no component record. Property-only access to an injected
  settings field creates no `Calls` edge (`Calls` requires a method
  invocation — see R-CALL).
- **Framework / external types are External references** (§3.2), never
  components: `ILogger` (the interface), `IHttpContextAccessor`,
  `IActionContextAccessor`, `IUrlHelperFactory`, `IHostApplicationLifetime`,
  `IDistributedCache`, and the like. An in-tree implementation of such a
  contract (e.g. `DefaultLogger`) is judged by the same rule as any
  other class.
- A class with two roles is ONE component with an evidenced role set
  (§2.1, §9.5). Roles never multiply nodes.

### R-REPO — Repository role (rule variant, approved option (a))

F0 §3.1's Repository rule is DbContext-shaped ("injects a DbContext /
is named by a repository convention *and* touches data"). This corpus
has no DbContext, so the rule variant below applies — it is the
explicitly recorded option-(a) variant from the decision brief, and the
driver must record it as such in the checkpoint:

- A class gets the Repository role when it **implements a repository
  contract** (`IRepository<T>` or an equivalent generic repository
  interface) **AND is the resolved implementation of that contract**
  (explicit registration or unique declaration). On this corpus that is
  `EntityRepository<T>` (open-generic registration at
  `NopDbStartup.cs:63`).
- **Injecting `IRepository<T>` alone NEVER confers the Repository
  role.** It confers data edges via R-DATA. A name alone never
  suffices either: role comes from the implementation + resolution
  evidence, not from the name.
- A class that both implements a repository contract and is called as a
  service carries both roles on one component (§2.1).

### R-EXP — Exposes (Controller → Endpoint)

F0 §4: the endpoint is declared on the controller. New relation kind,
chartered by F0 as Planned (F2). Only for endpoints the tool detects
(see R-ENDPOINT). Evidence: the declaring action's location; the
endpoint keeps its mapping-level EndpointId (§2).

### R-ENDPOINT — Endpoint detection (conventional routing; SEVERABLE)

Existing endpoint detection (attribute-routed actions, Minimal API
mappings, FastEndpoints) is unchanged. NEW, as a separately severable
item: controller actions WITHOUT route attributes are detected as
endpoints, with route and verb at strength **Inferred** (F0 §5.3). The
route value must be the conventional pattern as literally derived — the
checkpoint must state exactly what route/verb values are emitted and
what convention produced them. Never present a guessed route or verb as
declared. If no verb can be determined from source or an existing
convention rule, record the verb as inferred/unknown — never silently
default it.

Gate anchors (audit Table A): all 100 slice actions detected, each with
its declaring controller and file:line. `CancelOrder`
(`OrderController.cs:218`, `public async`, no `virtual`) IS an action.
`GetProductToCartDetailsAsync` (`ShoppingCartController.cs:337`,
`protected`) is NOT an action. Commented-out code produces nothing
(`CatalogController.cs:365,368`).

**Severability:** if conventional-routing detection cannot be
implemented without moving the frozen question set beyond the recorded
additions, or cannot distinguish actions from helpers reliably: STOP
that item, record the blocker and the limitation in the checkpoint, and
continue WITHOUT it. CP3 gate 6 then requires `Exposes` proven on
fixtures + frozen corpora only, and the checkpoint must state plainly
that conventionally-routed endpoints are a known limitation. Do not
ship partial endpoint detection.

### R-DI — ResolvesTo evidence (extensions to the Phase 7 index)

The Phase 7 resolution index (MS-DI + Autofac registrations, fail-closed)
is the base. F2 extends its evidence coverage; its fail-closed
semantics and the candidate-vs-binding distinction (F0 §4.4) do not
change: declaration-only = statically resolved candidate; explicit
registration = declared binding; both = confirmed.

- **Registration sites are explicit MS-DI `Add*` calls anywhere in
  scanned code**, including inside `INopStartup.ConfigureServices`
  implementations. The 4.90 mechanism (audit §4): `NopEngine.ConfigureServices`
  (`NopEngine.cs:105-122`) discovers `INopStartup` implementations via
  `FindClassesOfType<INopStartup>()` (contract at `INopStartup.cs:10`),
  orders by `Order`, instantiates via `Activator.CreateInstance`, and
  calls `ConfigureServices`. If the current index only scans known
  startup files, extend discovery to all `INopStartup` implementations
  and record what changed. 547 `Add*` sites in 21 files (audit §4).
- **Open-generic registrations resolve closed-generic injections:**
  `IRepository<>`→`EntityRepository<>` (`NopDbStartup.cs:63`),
  `IConcurrentCollection<>` (`NopStartup.cs:95`),
  `IAttributeService<,>` / `IAttributeParser<,>` /
  `IAttributeFormatter<,>` / `IPluginManager<>` (`NopStartup.cs:250-259`),
  `Lazy<>`→`LazyInstance<>` (`NopStartup.cs:326`). Test anchor:
  `IAttributeParser<CheckoutAttribute, CheckoutAttributeValue>` resolves
  to `AttributeParser` (`AttributeParser.cs:15`, ctor 32).
- **Transitive implementation:** base-list search through inheritance.
  `SynchronizedMemoryCacheManager : MemoryCacheManager` implements
  `IStaticCacheManager` transitively; `MemoryCacheManager` /
  `DistributedCacheManager` (abstract) implement it via `CacheKeyService`.
  All 5 cache implementations must be found as candidates (then
  subjected to the ambiguity rule below).
- **Namesakes resolve by qualified SymbolId only.** `using`-directive
  context decides same-simple-name cases; an unresolvable namesake is
  ambiguous, never guessed. Test anchors: slice controllers'
  `ICatalogModelFactory` resolves to `Nop.Web.Factories` (public), NOT
  `Nop.Web.Areas.Admin.Factories` (registrations namespace-qualified at
  `Nop.Web/Infrastructure/NopStartup.cs:25-88,93-111`); the two
  distinct `IWidgetModelFactory` interfaces (Admin
  `Areas/Admin/Factories/IWidgetModelFactory.cs:8` and Framework
  `Nop.Web.Framework/Factories/IWidgetModelFactory.cs:8`) are two
  different identities, both registered.
- **Ambiguity surfaces, never silently resolves.** Multiple DISTINCT
  registered/declared candidates → an entry in the ambiguous-resolution
  population (F0 §5.4) with all candidates listed; NO `ResolvesTo`
  edge; NO silent pick. Duplicate registrations of the SAME
  implementation collapse to one candidate with N registration
  occurrences. The 8 audit §4.1 cases are the test list:
  1. `IStaticCacheManager` ×5 / `ICacheKeyService` ×5 — config switch,
     `NopStartup.cs:100-131` (Memory / SqlServer / Redis /
     RedisSynchronizedMemory + else; lifetimes differ).
  2. `IThumbService` ×3 — core `ThumbService` (`NopStartup.cs:290`) +
     AzureBlob / CloudflareImages plugin factory lambdas.
  3. `ThumbService` (concrete) ×2 — same type, harmless duplicate:
     collapses to ONE candidate, two occurrences.
  4. `IWorkflowMessageService` ×2 — core `WorkflowMessageService`
     (`NopStartup.cs:192`) + Brevo plugin `BrevoMessageService`.
     A third implementer, `RfqMessageService`, is registered ONLY as
     its concrete type (audit §6.7) and must NOT appear as a third
     candidate: candidates = 2 registered, implementers = 3. Test
     this distinction explicitly.
  5. `IRedisConnectionWrapper` ×2 — same implementation, two
     mutually exclusive switch branches.
  6. `INopAssetHelper` ×2 — `ServiceCollectionExtensions.cs:413,431`
     (bundling branch).
  7. `ILocker` ×2 — `NopStartup.cs:125,129` (distributed-cache
     branch, different implementations).
  8. (Counted within the above: the cache-switch cases cover two
     interfaces; the audit's authoritative total is 8 interfaces.)
- **Convention registrations are NOT per-type evidence** (F0 §4.2, §7
  rule 5; ABP precedent): settings via
  `FindClassesOfType(typeof(ISettings), false)` (`NopStartup.cs:276-286`)
  and event consumers via `FindClassesOfType(typeof(IConsumer<>))`
  (`NopStartup.cs:311-319`) go to the unproven population, never to
  `ResolvesTo` edges.
- **Service-locator resolution is unprovable:** `EngineContext.Current.
  Resolve/ResolveAll`, `GetRequiredService` / `GetServices` outside
  registration sites → unproven-population entries with reason +
  file:line, never edges. The two in-slice sites MUST appear:
  `ProductService.cs:1739,1774` (`Resolve<IWorkflowMessageService>`,
  doubly unprovable — the interface also has the Brevo override).
  Full site list in audit §6.3 (including `EventPublisher` catch
  blocks, `NopDbStartup.cs:60`, settings factory lambda).
- **Plugin-manager resolution is reflection, not DI:** contracts
  resolved through `PluginManager<T>` / `*PluginManager` classes
  (audit §6.5 list: `IPaymentMethod`, `IShippingRateComputationMethod`,
  `IPickupPointProvider`, `IExchangeRateProvider`, `IWidgetPlugin`,
  `IExternalAuthenticationMethod`, `IMultiFactorAuthenticationMethod`,
  `IShipmentTracker`, `IScheduleTask`, `IPlugin`, `ISearchProvider`)
  → unproven population, never `ResolvesTo` edges.
- **Reflection discovery sites** (audit §6.4 `FindClassesOfType` table
  + `Activator.CreateInstance` + `ResolveUnregistered`) → unproven
  population with reason + file:line, never edges.
- **Direct construction, delegate indirection, static holders:** no
  relation. `new` in provider switches
  (`ArtificialIntelligenceHttpClient.cs:33-35`,
  `DataProviderManager.cs:21-31,43-49`), `Func<>` / `predicate?.Invoke`
  (`OrderService.cs:88`; `IRepository.cs` cache-key factory params),
  `Singleton<IPluginsInfo>.Instance` (`PluginService.cs:55`) are
  reviewed-and-excluded: record them in the checkpoint as reviewed,
  draw nothing. Do not invent a relation kind for them.
- **Autofac alternate path:** out of scope. Assume the MS-DI default
  path; existing Autofac handling is unchanged; record the alternate
  container as a known limitation.
- **Test-project registrations:** `BaseNopTest.cs` carries 246
  mirror `Add*` sites. CP1 must determine and record how the current
  index treats registrations in test projects. The slice reconciliation
  (CP2) must hold with production implementations resolving. If
  test-project registrations create false ambiguity on slice
  interfaces, STOP and report — do not invent a test-exclusion rule
  unprompted.

### R-CALL — Calls chains

The existing `Calls` rule is unchanged (F0 §4): a method-body call
whose target expression is *exactly* an injected member. Calls on a
property's return value, a local, a parameter, a `new` object, a
static, `base`/`this`, or an unresolved interface are not edges. F2
additions:

- The from-node may be ANY component (controller, service, factory),
  not only handlers. The resolved implementation, when known, is
  carried in evidence.
- **No transitive `Calls` edges.** Each hop is its own relation with
  its own ordered `Via` (F0 §5.2); chains are traversals over edges,
  never materialized aliases (F0 §4.2). The CP2 checkpoint must
  demonstrate one full chain end to end with file:line at each hop,
  e.g. `CatalogController.Category` → `ICatalogModelFactory` →
  `CatalogModelFactory` → `IProductService` → `ProductService` →
  `IRepository<Product>` (audit Tables A/B).
- **Occurrences, not just first sites:** every call site is an
  evidence occurrence on the one logical relation (dedup key per
  F0 §5.1: kind, from, to, ordered Via, method-if-any). Repeated
  line numbers in Table A (e.g. `@ 129, 129`) are two call sites on
  one line: two occurrences, one relation.
- **Helper-method calls** are recorded per containing method body
  (existing semantics). Table A reconciliation is at the
  union-over-the-controller's-methods level: the edge + occurrence
  lines must match Table A's call-site lines in aggregate, not per
  action. Do not build per-action attribution.
- **Commented-out code produces nothing.** Negative test:
  `_pictureService` appears only in comments at
  `CatalogController.cs:365,368` — no edge, no occurrence.
- Table A counts `_<field>.` occurrences, which include property
  reads (notably on `*Settings` fields). Only genuine method
  invocations on injected members are `Calls` edges; the driver must
  not treat every Table A line as a guaranteed edge. The CP2
  reconciliation must state, for each Table A row, which call sites
  yielded edges and which did not (with the rule that excluded each).

### R-DATA — Data edges, Linq2DB variant (option (a))

- **`TouchesData`, generalized.** From-node: any component (handler or
  service). To-node: the entity Type reference (the `TEntity` of an
  injected `IRepository<T>`). Evidence: the ordered Via
  [injected `IRepository<T>` interface → resolved `EntityRepository<T>`
  implementation → called data method] + the call-site occurrence(s);
  strength Resolved. A data edge REQUIRES an actual method call on the
  injected repository member: injection alone yields `ResolvesTo` only,
  never a data edge.
- **`UsesData` does NOT fire on this corpus.** 0 DbContexts must stay
  0; no DbContext edge may be claimed anywhere — that negative is
  itself a gate (option (a)). The single `DbContext`/`DbSet` mention
  in all of `src` is a comment (`NopEngine.cs:44`) and must create
  nothing.
- **Entity types are Type references** (§3.2), NOT inventoried Entity
  components in F2: there is no EF mapping evidence rule for linq2db,
  and none is to be invented.
- **Ground-truth anchors (audit Table B):** `ProductService` 20 repos
  (ctor 70, lines 78–97); `OrderService` 9 (ctor 41, lines 43–51);
  `CategoryService` 4 (ctor 38, lines 41–44); `ShoppingCartService` 1
  (`IRepository<ShoppingCartItem>`, line 91); `CustomerService` 16
  (ctor 59, lines 63–78). Data-edge entity sets must be SUBSETS of
  the injected `IRepository<T>` sets. **Zero-data-edge negatives:**
  `OrderProcessingService` (ctor 96, 0 repos, pure orchestrator over
  41 services) and `CheckoutModelFactory` (ctor 64, 22 service deps,
  0 repos) MUST have zero data edges. Factories hold no repositories
  anywhere in Table B: no factory gets a data edge.
- **The repository boundary is where the data edge stops.** linq2db
  internals (`IDataContext`, data-provider calls inside
  `EntityRepository`) are out of F2 scope; record as a limitation.

### R-PUB — nopCommerce pub/sub (NOT MediatR Publishes)

nopCommerce has its own publish/subscribe: `IEventPublisher` /
`IConsumer<T>` (`IEventPublisher.cs:6`; `EventPublisher.cs`, consumers
via `ResolveAll<IConsumer<TEvent>>()` at lines 23 and 60). It is
DISTINCT from MediatR and must be treated as a separate,
reflection-resolved mechanism:

- **Publish call sites are `Calls` edges** to the `Publish` /
  `PublishAsync` method reference on `IEventPublisher`, with the
  published event type carried in the evidence. 15 publish calls in
  slice files; event types statically known, e.g. `OrderPlacedEvent`
  (`OrderProcessingService.cs:1590,1960`), `OrderPaidEvent` (`:1097`),
  `OrderStatusChangedEvent` (`:1010`),
  `ShoppingCartItemMovedToOrderItemEvent` (`:1313`).
- **NO `Publishes` relation for these calls.** The resolved targets
  are not inventoried notifications; 0 notifications must stay 0. No
  new relation kind is chartered for this mechanism — do not invent
  one.
- **Consumers are unproven:** publish→consumer edges are never drawn
  (reflection-resolved; audit §6.11). Record the consumer-resolution
  gap in the unproven population.
- **`INotificationService` (admin-toast service, 160 grep hits) is NOT
  a notification contract.** The 0-notifications gate covers it: no
  Notification component, no `Publishes` edge, anywhere on this corpus.

### R-UNPROVEN — Unproven populations are first-class (F0 §5.4)

The following surface in the unproven population with reason +
file:line, and NEVER as edges:

- Ambiguous resolutions (the 8 audit §4.1 cases, with candidates).
- Unimplemented interfaces: `ISearchProvider`
  (`ISearchProvider.cs:8`), `IAdminMenuPlugin` (referenced only via
  `IPluginManager<IAdminMenuPlugin>`, `AdminMenu.cs:31,49`).
- Service-locator sites (audit §6.3), reflection-discovery sites
  (audit §6.4), plugin-manager contracts (audit §6.5).
- Publish→consumer gaps (R-PUB).
- `IPagination<T>` (dead interface, audit §6.2): no edges anywhere;
  record as reviewed in the checkpoint (no model entry required).
- The 12 ruled-out false positives (audit §6.12) must NOT appear as
  findings: `IEntityBuilder`, `INopConnectionStringInfo`,
  `IAttributeFormatter` (generic only), `IInstallationService`
  (`Lazy<>`-wrapped), `ISummernoteHelper` (Razor `@inject`), and the
  model marker interfaces. If the tool flags any of them, that is a
  defect, not a finding.

A view may show unproven populations (counts, captions); it must never
render them as edges.

## Hard constraints (violating any one fails the stage)

- **H1 — No detection beyond this brief.** No new relation kinds
  except `Exposes`. No new component kinds beyond Controller / Service /
  Repository (Endpoint already exists). No Tier 2 (messaging, external
  integrations, configuration surface). **BackgroundWorker is OUT of
  F2** — deferred, although F0 §3.1 lists it as planned for F2: no
  ground truth for it exists in the audit, and the foundations F2
  scope names only controllers, services, and repositories. Do not
  implement it; the deferral is recorded here, not a driver decision.
- **H2 — Existing tests pass unmodified.** All 731 existing tests pass
  (box #3 terms). On box #4 expect 663 pass + 68 skipped (external-corpus
  tests; box #4 does not hold the corpora) — record the skip count in
  every checkpoint. Do not weaken, skip, delete, or "fix" an existing
  test. New tests are additive and must pass. If a test expectation
  contradicts this brief, STOP and record it.
- **H3 — Output additive-only, additions recorded.** Existing JSON
  sections keep their names and fields byte-for-byte. New model content
  appears only in additive sections/fields and in the `relations`
  section, whose per-corpus growth must be enumerated in the checkpoint
  with the rule behind each new relation kind/instance. Markdown, HTML,
  console, SARIF, summary outputs are byte-identical on fixtures except
  intended, recorded additions. `schemaVersion` stays 1. MCP answers
  identical except additive model content. `--diff` / `--strict`
  behavior unchanged (strict exit codes preserved).
- **H4 — Frozen question set.** Unmoved except by intended, recorded
  additions. `FrozenQuestionSet.cs` untouched. Any movement must be
  enumerated in the checkpoint with the reason for each.
- **H5 — No release changes.** No version bump, no CHANGELOG/ROADMAP
  edits, no README edits, no new CLI flags, no new PackageReferences,
  no `.csproj` diff.
- **H6 — Architecture rules hold.** The 4-project one-way dependency
  chain and one-type-per-file rule stay intact
  (`ArchitectureBoundaryTests` must pass).
- **H7 — Wraps rendering gap: preserved, not fixed.** Do NOT change
  sequence/flow rendering. The gap stays documented exactly as F1 left
  it. No silent correction.
- **H8 — Determinism.** Repeated scans of unchanged source with a fixed
  `--generated-at` produce identical output bytes — fixtures and
  nopCommerce, all formats, both modes.
- **H9 — Driver never commits, never pushes.** Leave all work
  uncommitted in the working tree. Mariam verifies each checkpoint
  independently (including a 6-corpus A/B on box #3) and commits only
  after verification passes.
- **H10 — Corpus read-only.** See Corpus section. Pinned tag/commit
  only; no re-pinning.
- **H11 — No corpus or class-name special-casing.** Every rule must be
  general: no `if nopCommerce`, no hardcoded type names in production
  code. Tests may name audit types; production code must not. A rule
  that only works on nopCommerce is not a rule.

## Environment

- .NET SDK 8.0.427 at `~/.dotnet`. Every dotnet command runs with
  `DOTNET_ROOT=$HOME/.dotnet` and `PATH=$HOME/.dotnet:$PATH` exported.
- Test command: `dotnet test CqrsLens.sln -c Release --nologo`.
- Baseline binary for before/after comparison: create a clean worktree
  with `git worktree add /tmp/cqrslens-f1base 3fcbcba`, build it once,
  use it read-only for baseline outputs, delete the worktree when done.
  Never modify it.
- Corpus: `/data/workspace/corpora/nopcommerce`, pinned as above.
  Verify the pin (`git rev-parse HEAD` → `e7a52b91…`, `git describe
  --tags` → `release-4.90.0`, `git status` clean) before the first scan
  and after the last run.
- Box #4 does NOT hold the six frozen corpora: the driver's A/B scope
  is the in-repo fixtures + nopCommerce. The 6-corpus A/B and the full
  perf A/B are Mariam's verification on box #3, not the driver's — but
  a smoke regression on fixtures or nopCommerce is a stop-and-report.
- Both scanner modes (text + Roslyn) must produce the same model on
  fixtures. Any mode divergence on nopCommerce must be enumerated with
  its cause; divergence attributable to F2 rules is a CP3 gate failure.

## CP1 — Inventory + baselines

1. **Reproduce the F1 baseline.** Run the F1 baseline binary
   (worktree) on the pinned corpus, text mode, JSON, pinned
   `--generated-at 2026-01-01T00:00:00Z`, and reproduce the audit
   baseline exactly: 9,644,562 bytes, sha256 `b2825eb8…`, 35 projects,
   3,234 relations, 0/0/0/0/0/0. If it does not reproduce, STOP: the
   corpus or toolchain differs from the audit, and nothing else in
   this brief can be trusted until that is resolved.
2. **Establish the Roslyn F1 baseline.** No Roslyn-mode baseline
   exists for this corpus (audit §7.2). Run the F1 binary in Roslyn
   mode (same pin, same `--generated-at`, 1 warmup + 3 timed runs)
   and record medians, output size, sha, and relation counts. This
   becomes the Roslyn regression line for CP3.
3. **Record test-registration behavior** per R-DI (how the current
   index treats the 246 `BaseNopTest.cs` mirror registrations).
4. **Implement role detection + inventory** (R-CTRL, R-SVC, R-REPO)
   and the `Exposes` relation (R-EXP) for already-detected endpoints.
   Components enter the spine with evidenced role sets; identities per
   F0 §2; no consumer/output changes beyond additive content.
5. **Fixture tests** proving each role rule, positive and negative:
   controller base vs attribute-only; service via injection+calls vs
   via registration alone; repository via implementation+resolution
   vs name alone (must NOT get the role); settings exclusion;
   framework-type exclusion; one symbol, two roles, one component;
   namesake identities distinct.
6. **Inventory reconciliation** against the audit: controllers /
   services / repositories inventoried on the pinned corpus, with
   every delta vs the audit's file counts (133 / 210 / 3) explained
   in the checkpoint. Report the counts; do not force them to match
   file counts — role evidence decides, and the reconciliation
   explains the difference.

Suite green (731 existing + new). Write `CHECKPOINT_F2_CP1.md` in the
repo root: baseline-reproduction proof, Roslyn baseline numbers,
test-registration behavior, inventory counts + reconciliation, files
changed (`git status --short` + `git diff --stat`), test result,
decisions, open issues.

## CP2 — Relations + evidence rules

Implement R-CALL, R-DI, R-DATA, R-PUB, R-UNPROVEN, and the severable
R-ENDPOINT item. Document the exact additive JSON shape (field names,
nesting, ordering) in the checkpoint. `schemaVersion` stays 1.

- **Fixture tests for every rule**, including: ordered-Via chains and
  the no-transitive-edges rule; open-generic + transitive + namesake
  resolution; each of the 8 ambiguity shapes (including the
  same-implementation collapse and the RfqMessageService
  registered-vs-implementer distinction); convention/locator/reflection
  cases staying unproven; data-edge rule (injected+called → edge,
  injected-only → no edge, zero-repo service → no edge); publish-site
  Calls edges with event-type evidence and zero `Publishes`; commented-
  out code producing nothing.
- **nopCommerce reconciliation.** Run the F2 binary on the pinned
  corpus and reconcile the model against audit Tables A/B and §6 with a
  script whose commands and outputs are recorded verbatim in the
  checkpoint. The reconciliation must cover every assertion in CP3
  gate 6. Reconciliation scripts live in `/tmp` (ephemeral); the
  checkpoint is the durable record. Do NOT add suite tests depending
  on the box #4-only corpus path.
- **Per-corpus relations deltas** (fixtures + nopCommerce) with rule
  attribution for every new relation kind/instance class. Any
  non-`relations` JSON section change, and any md/html/agent/summary/
  sarif change on fixtures, must be enumerated as an intended addition
  with its reason — or it is a defect.

Suite green. Write `CHECKPOINT_F2_CP2.md`: the JSON shape doc, the
reconciliation commands + outputs, the deltas table, files changed,
test result, decisions (including the R-ENDPOINT severability outcome),
open issues.

## CP3 — Gates

Prove each gate with commands and outputs recorded in the checkpoint.
Gates 1–6 run on box #4; gate 7's six-corpus leg and gate 8's full
A/B are Mariam's verification — the driver evidences the fixture +
nopCommerce legs and records everything needed to re-run them.

1. **Legacy parity.** Full suite green; frozen question set exact
   except enumerated, recorded additions (H4).
2. **Identity uniqueness.** Tests + a corpus-scan assertion: no two
   components share a ComponentId; namesake components (public vs
   admin factories; the two `IWidgetModelFactory` interfaces) are
   distinct identities.
3. **Evidence completeness.** Every new relation on every fixture and
   on nopCommerce has a valid rule and ≥1 evidence occurrence (assert
   in tests + in the reconciliation script).
4. **No invented certainty.** All 8 ambiguous registrations, all
   locator/reflection/convention sites, and all publish→consumer gaps
   are unproven entries, never edges; inferred facts (including
   conventional-routing endpoints, if implemented) keep their strength
   label in every view; 0 requests / 0 notifications / 0 DbContexts
   hold on nopCommerce.
5. **Deterministic output.** Two runs with fixed `--generated-at`
   produce identical bytes on all fixtures and nopCommerce, all
   formats, both modes.
6. **Ground-truth reconciliation.** Every assertion below passes
   against the pinned corpus (audit references in brackets):
   - All 5 slice controllers inventoried; all 100 Table A actions'
     call sites reconciled at union level (edges + occurrence lines
     match Table A, with every non-edge call site explained by rule)
     [Table A].
   - All 56 invoked interfaces resolve to ≥1 implementation; the
     resolution matches Table B (implementation + ctor line) [Table B].
   - Data edges: `ProductService` entity set ⊆ its 20 injected repos;
     `OrderService` ⊆ its 9; `CategoryService` ⊆ its 4;
     `ShoppingCartService` ⊆ its 1; `OrderProcessingService` ZERO
     data edges; `CheckoutModelFactory` ZERO data edges; no factory
     has a data edge [Table B].
   - One full chain demonstrated end to end with file:line at each
     hop (controller → factory → service → repository) [Tables A/B].
   - All 8 §4.1 ambiguity cases surface as ambiguous with the
     correct candidate sets (including the `RfqMessageService`
     2-registered / 3-implementer distinction and the `ThumbService`
     same-type collapse) [§4.1, §6.7].
   - `ISearchProvider`, `IAdminMenuPlugin` unimplemented (no edges);
     `IPagination<T>` dead (no edges); none of the 12 §6.12 ruled-out
     items flagged [§6.1, §6.2, §6.12].
   - Both in-slice locator sites (`ProductService.cs:1739,1774`)
     recorded as unproven; no `ResolvesTo` edge from them [§6.3].
   - All 15 slice publish calls are `Calls` edges with event-type
     evidence; zero `Publishes` relations; consumers unproven [§6.11].
   - Validators stay 105; endpoints reconcile per R-ENDPOINT (100
     slice actions if implemented, 0 if severed — either way the
     number is explained, not assumed).
7. **Regression.** Fixtures + nopCommerce: every non-`relations` JSON
   section byte-identical to the F1 baseline binary; `relations`
   deltas enumerated with rule attribution; md/html/agent/summary/
   sarif identical except recorded additions. nopCommerce F1-baseline
   output reproducible from the clean worktree (sha `b2825eb8…`).
   MCP stdio probe: identical except additive model content. `--diff`
   / `--strict` outputs identical in behavior; strict exit codes
   preserved. H7 preserved (`RequestSequenceBuilder` flattening
   untouched). Zero new warnings (normalized, deduped warning-set
   comparison vs the F1 tree; `CqrsLens.Core` stays 0 warnings).
8. **Performance.** Wall-clock `dotnet <dll> <corpus> --format <fmt>`
   including process startup, 1 warmup + 3 timed runs, median, same
   machine/session, pinned `--generated-at`. Bound: **no measured
   cell above +10%** vs the F1 baseline binary (text/json, text/md,
   roslyn/json — fixtures + nopCommerce); **aim nopCommerce text
   ≤ +5%** (frozen line: 3,688 ms text/json). A single-session
   breach triggers a second session (5-run median); judgment is on
   median-of-sessions, and small-corpus session swing (±5pp, the
   F1-perf lesson) is noise unless it reproduces across sessions
   AND a same-session comparison confirms a regression attributable
   to F2. In-process profiling is never a substitute for wall-clock
   medians. Record output sizes + relation counts per run: growth
   is expected and must be visible, not hidden.
9. **Scope clean.** `git diff -- '*.csproj' CHANGELOG.md README.md
   ROADMAP.md Directory.Build.props
   tests/CqrsLens.Tests/FrozenQuestionSet.cs` is empty; no added
   `<PackageReference>` lines; no corpus/class-name special-casing
   (state how this was checked); corpus `git status` still clean.

Write `CHECKPOINT_F2_CP3.md`: gate-by-gate evidence with the exact
commands run and their outputs, full test result, timing tables,
the complete `git status` / diff stat of the working tree, open
issues. Then STOP for verification.

## Checkpoint format

Every checkpoint file goes in the repo root, named exactly
`CHECKPOINT_F2_CP<n>.md`, and contains: status (done / blocked), files
changed (paste `git status --short` + `git diff --stat`), tests run with
results, gate evidence where applicable, decisions made, open issues,
and the exact commands to reproduce every claim. Do not delete or
overwrite a previous checkpoint file. Write the checkpoint, then
continue to the next CP in the same session, except after CP3, where
you STOP. If the session is interrupted, resume from the last written
checkpoint — re-read it first.

## On blockers

If any hard constraint cannot be met, if a test expectation contradicts
this brief, or if F0, the audit, and this brief disagree: STOP, write
the checkpoint with the blocker described precisely (what, where, what
you tried), and do not work around it. Do not weaken a gate. Do not
invent an interpretation of F0 or the audit. The ONLY severable item
is R-ENDPOINT (conventional-routing detection), handled per its own
rule above; everything else blocks.

## What success looks like

Three checkpoint files, a green suite (731 existing + additive new
tests), controllers/services/repositories inventoried with evidenced
roles, the Table A/B + §6 ground truth reconciled, ambiguity and
unproven cases surfaced rather than silently resolved, regression and
perf gates evidenced on fixtures + nopCommerce, and an uncommitted
working tree on `foundations/f2-controllers` that Mariam can verify
and commit.
