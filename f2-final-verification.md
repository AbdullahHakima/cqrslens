# F2 Final Verification — CqrsLens `foundations/f2-controllers` (CP2 + CP3 as one unit)

**Verifier:** Mariam (independent, box #3) — **Date:** 2026-11-16, completed ~03:30 EET
**Driver tree:** box #4, branch `foundations/f2-controllers` @ `3fcbcba`, final snapshot
`f2-final-snapshot.tgz` SHA256 `6c3b7d9a76f8830117987386fcb08d2290a7900327a9eaab159fce3114801bf3`
**Baseline:** F1 commit `3fcbcba`; corpus nopCommerce `release-4.90.0` (`e7a52b91`, clean)
**Scope:** verification only. No modifications to the box #4 driver tree, no commits, no pushes,
no driver relaunch. CP2-pure is not separately recoverable, so F2 verifies as ONE unit (like F1).

## Headline verdict: CONDITIONAL PASS

All 9 gates pass on their written scope. Three conditions — all record/acknowledgment items,
no code changes required:

1. **Gate 8, DiffPair/base text/json is marginal.** Median-of-4-sessions **+9.92%** — 0.08pp
   under the +10% bound. Median of the 3 formal protocol sessions alone is +10.60% (over).
   The cause is a consistent ~+25–33 ms fixed cold-start JIT cost on a ~260 ms 1-file process,
   not a scaling regression (nopCommerce median-of-sessions is ~0%). The user accepts this
   cell as within the brief's median-of-sessions rule, or requests a perf-fix pass for the
   fixed JIT cost.
2. **Record the 6-corpus non-relations deltas** (unrecorded by the driver, additive and
   correct): ardalis `requests`/`handlers` and eshop `notificationHandlers` gain bare entity
   names in `touchedEntities` (TouchesData projection); ABP `serviceCallEdges` 50→54,
   modular 184→177. See §Gate 7.
3. **Qualify the CP2 interpolated-string claim.** The 2 occurrences
   (CatalogController.cs:344, CheckoutController.cs:2080) are stripped in **both** modes,
   not text-only. Relation-level text==roslyn equality holds as written. See §8.

No gate fails. Nothing in this report blocks a commit once conditions 1–3 are acknowledged
in the commit/verification record.

---

## 0. Tree reconstruction — PASS

- Snapshot SHA256 verified `6c3b7d9a…`; extracted to `/tmp/f2final/` (patch 2,311 lines).
- Box #3 worktrees from clean `3fcbcba`: `/data/workspace/cqrslens-f2final-base` (baseline),
  `/data/workspace/cqrslens-f2final` (F2). Patch applied cleanly.
- **Exact match:** `git status --short` + `git diff --stat` = **24 modified files +1388/−202**;
  all new-file SHA256s match the snapshot. No mismatch.
- nopCommerce pin verified: `HEAD e7a52b91`, tag `release-4.90.0`, `git status` clean.

## 1. Suite — PASS (Gate 1)

Command (box #3, both worktrees):
`DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet test CqrsLens.sln -c Release --nologo`

| | Passed | Failed | Skipped | Total |
|---|---|---|---|---|
| **F2 tree (box #3)** | **761** | **0** | **0** | 761 |

- 761 total = 746 pre-existing (including the 68 corpus tests, which **ran, not skipped**)
  + 15 new F2 tests (`F2RoleInventoryTests` 5, `F2RelationAndResolutionTests` 10).
  Duration ~17 s.
- The driver reported 693 passed + 68 skipped on box #4: the 68 corpus tests are gated on
  corpus paths that do not exist on box #4, so they skip there. On box #3 the corpora exist,
  the 68 run and pass. This is an environment difference, not a discrepancy — and it makes
  this verification stronger (the corpus legs actually executed).
- Warnings (normalized, deduped): **F1 = 16, F2 = 16 — 0 new**; `CqrsLens.Core` **0 warnings**.
  (Raw warnings are pre-existing CS9057/CS8620/CA1865/CA1869/CS8714 in Reporting/Cli/Tests/Benchmarks.)
- Frozen question set: `FrozenQuestionSet.cs` diff empty (see Gate 9); suite green including
  all frozen-question tests.
- **CP1 findings disposition:** (a) CP1 checkpoint's "497 spine components" was wrong
  (actual 513) — CP3 reports **516**, verified here; (b) the 7 unenumerated open-generic
  renderings are now enumerated (CP3 §3: `LazyInstance`1`, `ConcurrentTrie`1`,
  `EntityRepository`1`, `AttributeFormatter`2`, `AttributeParser`2`, `AttributeService`2`,
  `PluginManager`1`); the generic-arity renames appear throughout the A/B deltas below;
  (c) the CP1 perf watch (nopCommerce text +9.8–19.7% across sessions) is resolved as session
  noise — median-of-sessions is ~0% (see Gate 8).

## 2. Baseline reproduction + nopCommerce — PASS (Gates 3, 4, 7)

F1 baseline from the clean `3fcbcba` worktree binary (pinned `--generated-at 2026-01-01T00:00:00Z`):

| Binary / mode | SHA-256 | Bytes | Relations |
|---|---|---|---|
| F1 text | `b2825eb8…` | 9,644,562 | 3,234 |
| F1 roslyn | `e57ffe0b…` | 9,644,564 | 3,234 |
| F2 text | `5bc6d4f9…` | 33,747,841 | **8,764** |
| F2 roslyn | `378a7f42…` | 33,747,843 | **8,764** |

- F1 baseline byte-identical to the driver's recorded SHAs. F2 relations **8,764** = driver claim.
- F2 text vs roslyn: **strictly equal** on components, spineEndpoints, unproven, relations,
  serviceCallEdges (same check as `compare_nop.py`).
- Every non-relations JSON section identical to baseline **except** the recorded
  `serviceCallEdges` change (**2,446 → 2,510**).

Relations by kind (text mode, F1 → F2):

| Kind | F1 | F2 |
|---|---|---|
| calls `componentMemberInvocation` | 0 | 3,649 |
| calls `eventPublishCallSite` | 0 | 44 |
| calls `injectedMemberInvocation` | 2,446 | 2,510 (+64) |
| exposes | 0 | 1,151 |
| references | 12 | 12 |
| resolvesTo | 776 | 787 (+11) |
| touchesData | 0 | 611 |
| **Total** | **3,234** | **8,764** |
| Occurrences | 6,830 | 17,422 |

Inventory: **516 components** (282 service, 1 repository, 128 controller, 105 validator),
0 duplicate ComponentIds, 0 multi-role; **1,151 endpoints**, 0 duplicate endpoint IDs;
verbs UNKNOWN 556 / POST 594 / GET 1; validators 105; requests/notifications/behaviors/
dbContexts 0; legacy endpoints 0. Unproven totalCount **340** (7 ambiguous + 333 observations:
9 conventionRegistration, 55 dynamicReflection, 159 serviceLocator, 2 unimplementedInterface,
108 unresolvedEventConsumer). Publishes 0; `eventPublishCallSite` 44 relations / 108 occurrences.

## 3. Six-corpus A/B — characterized (Gate 7, see condition 2)

Script `/tmp/f2final-verify/run_ab.py` + `compare_ab2.py` (multiset deltas, canonical relations),
box #3, both modes. Caveat (known): box #3's ardalis (53 base) and jasontaylor (131 base) trees
are not the F1-frozen VM trees (156/124) — the A/B on identical trees is valid, absolute counts
can't be cross-checked against the F1 VM legs for those two.

| Corpus | Base → F2 | Net | Non-relations diff |
|---|---|---|---|
| ABP | 3,624 → 3,750 | +126 | `serviceCallEdges` 50→54 |
| modular | 710 → 790 | +80 | `serviceCallEdges` 184→177 |
| northwind | 172 → 191 | +19 | none |
| eshop | 114 → 121 | +7 | `notificationHandlers` (touchedEntities +bare entity) |
| ardalis | 53 → 132 | +79 | `requests`, `handlers` (touchedEntities +bare entity) |
| jasontaylor | 131 → 131 | 0 | none |

Rule attribution (multiset, added − removed):
- **ABP (+126):** exposes +257, componentMemberInvocation +26, injectedMemberInvocation net +4,
  resolvesTo net **−161** (496 removed candidate edges to `External` implementations,
  335 added candidate edges to in-corpus `Component` implementations; occurrences 1,136→973).
  Cause: the new resolution index resolves generic candidates in-corpus instead of to external
  referenced assemblies. ABP has **no test projects**, so this is NOT the test-exclusion rule.
  It is a quality improvement (candidates now point at real in-corpus implementations), not a
  loss: 313 of the 496 removed interfaces still resolve in F2 (to a different, in-corpus target);
  confirmed-binding-level edges moved only −5 net (10 added vs 12 removed confirmedBinding).
- **modular (+80):** exposes +51, componentMemberInvocation +34, eventPublishCallSite +7,
  injectedMemberInvocation net −7, resolvesTo net −5 (renames).
- **northwind (+19):** exposes +19; dispatches 14 added / 14 removed (generic-arity renames).
- **eshop (+7):** touchesData +7 (`repositoryDataAccess`); resolvesTo + injectedMemberInvocation
  8/8 and 32/32 added/removed (renames).
- **ardalis (+79):** componentMemberInvocation +38, touchesData +19, exposes +22; renames.
- **jasontaylor (0):** 2 added / 2 removed (renames only).
- **text==roslyn** by (kind, from, to, rules): **True** for all 5 testable corpora × both binaries.
  (Full-canonical multisets differ only in occurrence-level detail, as expected.) ABP roslyn
  exits 5 on both binaries, no output — pre-existing defect, unchanged.

**Non-relations finding (condition 2):** the only non-relations differences on the 6 corpora are
(a) `serviceCallEdges` on ABP (50→54) and modular (184→177) — the same mechanism as the recorded
nopCommerce change (event-publish reclassification + unblocked calls); and (b) `touchedEntities`
gains inside `requests`/`handlers` (ardalis: +`"Order"` alongside `"Orders (Order)"`) and
`notificationHandlers` (eshop: +`"Buyer"` alongside `"Buyers (Buyer)"`). Same entries, same
identities — one extra bare entity name per affected entry, a direct projection of the new
TouchesData edges. Additive-only, correct, confined to a projection of chartered new relations —
but **not enumerated by the driver** (no final-tree 6-corpus table was published). Record it.

## 4. Fixtures — PASS (Gates 1, 7)

All 5 fixtures, both modes, all formats (json/md/html/agent/summary/sarif), `--diff`, `--strict`,
MCP stdio probe:

| Fixture | Base → F2 relations | Formats |
|---|---|---|
| GoldenCorpus | 25 → 27 | byte-identical |
| Adversarial | 6 → 6 | byte-identical |
| DiffPair/base | 1 → 1 | byte-identical |
| DiffPair/head | 0 → 0 | byte-identical |
| StrictFailure | 0 → 0 | byte-identical |

- GoldenCorpus +2 exposes exactly at `OrdersController.Archive`:10 and `.Get`:13 (driver claim).
- nopCommerce md/summary/sarif byte-identical; `--diff` md/json byte-identical; `--strict` exit
  codes 4 (StrictFailure) / 0 (GoldenCorpus) / 0 (nopCommerce); **MCP byte-identical**.
- Note: DiffPair/head and StrictFailure JSONs omit the `relations` key when empty (0 relations) —
  the driver's "0 → 0" claim holds; an earlier comparison-script crash (`KeyError: 'relations'`)
  was a verifier-script bug (missing `.get()` default), fixed and re-run; no tool issue.

## 5. Ground-truth reconciliation — PASS (Gate 6)

Script `/tmp/f2final-verify/reconcile2.py` (v2; fixes: endpoints by name, Table B REPO parser,
resolvesTo-only Rfq check, occurrence-location interpolated-string check). Output
`/tmp/f2final-verify/reconcile2.out`. All checks run against the transferred F2 nopCommerce
JSONs (`/tmp/f2final-verify/nop-f2-text.json`, `nop-f2-roslyn.json`) and the audit
`F2_corpus_audit_nopcommerce.md`; source lines confirmed by direct inspection of the pinned
corpus on box #3.

- **A. Endpoints 100/100.** By `Controller.Action` name (the tool records endpoint location at
  the first attribute line, e.g. GetCategoryProducts: Table A line 145, endpoint at 144
  `[HttpPost]` — an earlier line-based check that scored 46/100 was the verifier's error, not
  the tool's). All 100 slice actions have an endpoint **and** an `exposes` relation from their
  controller. Catalog 21, Product 13, Checkout 29, Order 12, ShoppingCart 25.
- **B. Call sites 1,042 matched** (763 direct + 279 helper, exactly the driver's numbers) of
  1,183 parsed pairs (888 direct + 295 helper). Unmatched 141, **all classified, 0 unexplained**:
  129 settings-property reads, 2 interpolated-string, 4 ambiguous-contract (held in unproven),
  6 property reads (`_webHelper.IsRequestBeingRedirected` / `.IsPostBeingDone` at
  CheckoutController:1324 ×2, :2154 ×2, OrderController:281 ×2 — source-verified, not call sites).
  **11 extra real calls** the tool found that Table A did not enumerate (CheckoutController
  339, 922, 1162, 1213, 1409, 1915, 1958, 2065, 2134; ShoppingCartController 632, 1250) — all
  inside Table A actions, all multi-line `await (await _paymentPluginManager…` /
  `_genericAttributeService` / `_shoppingCartService` chains the audit's regex missed. The tool
  is a **superset** of Table A here, not a subset.
- **C. Table B 56/56** — resolved 54, ambiguous-held 2 (`IStaticCacheManager`,
  `IWorkflowMessageService`), missing 0. (An earlier run reported repo sets empty because the
  Table B parser missed indented ` - REPO:` sub-bullets; fixed.)
- **D. TouchesData subsets exact** (repo sets now populated from Table B):
  ProductService 10/20, OrderService 5/9, CategoryService 3/4, ShoppingCartService 1/1,
  OrderProcessingService 0, CheckoutModelFactory 0, CatalogModelFactory 0 — all subsets hold.
  RFQ AdminModelFactory (factory-named component in the RFQ plugin) touches `['Customer']` only.
- **E. 4-hop chain, all True:** Exposes CatalogController→Category @113; Calls
  →CatalogModelFactory.PrepareCategoryModelAsync @137; Calls →CategoryService.GetAllCategoriesAsync
  @493; TouchesData CategoryService→Category @204.
- **F. §4.1:** candidate counts exact — ICacheKeyService 5, ILocker 2, IStaticCacheManager 5,
  INopDataProvider 3, IThumbService 3, IWorkflowMessageService 2, INopAssetHelper 2.
  `RfqMessageService` **not** a candidate for IWorkflowMessageService; **0 resolvesTo edges to
  RfqMessageService** (an earlier "2" was the verifier's unfiltered check — the 2 edges are
  `calls` to its methods from RfqAdminController and RfqService, i.e. direct method calls, not
  resolutions). IRedisConnectionWrapper → 1 resolvesTo to RedisConnectionWrapper.
- **G. §6 negatives:** locator observations at ProductService.cs:1739 and :1774 present;
  unimplemented interfaces exact (ISearchProvider @ SearchPluginManager.cs:11, IAdminMenuPlugin @
  AdminMenu.cs:31); all 12 §6.12 ruled-out items absent from unproven; IPagination in no relation;
  **18 slice publish calls** as `Calls` with event-type detail, 0 missing detail, 0 Publishes.
  **The audit §6.11 says 15 — the audit undercounted.** Source grep of the 15 slice files:
  15 `_eventPublisher.PublishAsync` + 3 specialized (`PublishShipmentDeliveredAsync`,
  `PublishShipmentReadyForPickupAsync`, `PublishShipmentSentAsync`) = **18**. The tool reports all
  18; the audit's 15 equals the plain-`PublishAsync` count (OrderProcessingService's 15 alone).
  Gate 6's "all 15 slice publish calls" condition holds for all 18.
- **H. Interpolated strings — see §8.**

## 6. Test-exclusion rule — PASS (general, effects confirmed)

- **Generality:** `LayerClassifier.LooksLikeTestProject` is a general rule — project-name segments
  (`Test`/`Tests`/`Specs`/`Specifications`, ends-with `Tests`) and directory segments
  (`test`/`tests`). `ServiceResolutionIndexBuilder.cs:28` excludes `project.Layer == "Tests"`
  from the resolution index; `ComponentCallAndDataResolver` / `ConventionalControllerEndpointDiscovery`
  honour `IsTestProject` throughout. **No `Nop`/`nopCommerce`/`Nop.Tests` literals in `src/`**
  (grep, zero hits).
- **Effects on nopCommerce (base → F2 JSONs):**
  - Exactly **1** test resolvesTo in the base:
    `Nop.Tests.Nop.Core.Tests.Infrastructure.TypeFinderTests.ISomeInterface → SomeClass`;
    **0** in F2. (−1 test edge ✓)
  - Production resolvesTo 776 → 787 = **+11 net = −1 test + 12 production** ✓.
  - `serviceCallEdges` 2,446 → 2,510 (**+64**): driver's +86 unblocked / −22 EventPublisher
    decomposition is arithmetically consistent (86 − 22 = 64). Verified directly:
    `injectedMemberInvocation` occurrences mentioning EventPublisher **63 → 0**;
    `eventPublishCallSite` occurrences **0 → 108** (44 relations). The R-PUB check lives in
    `ComponentCallAndDataResolver.cs:302` (direct `Publish`/`PublishAsync` + 1-hop extension map).

## 7. Interpolated-string tension — RESOLVED (condition 3)

- CP2 says 2 call occurrences are stripped by string-literal masking in **TEXT** mode
  (CatalogController.cs:344, CheckoutController.cs:2080). CP3 claims text==roslyn strictly equal
  on relations for nopCommerce.
- **Finding:** the occurrences are absent in **both** modes. Source-verified on the pinned corpus:
  - CatalogController.cs:344 — `$"{await _localizationService.GetLocalizedAsync(store, x => x.Name)}: New products"`.
  - CheckoutController.cs:2080 — `redirect = $"{_webHelper.GetStoreLocation()}checkout/OpcCompleteRedirectionPayment"`.
  - Occurrence windows 335–355 / 2070–2090 are **identical** in text and roslyn (exposes @339,
    calls @342/@346/@350/@355; @2084). The call *inside* each `$"..."` is missing in both modes;
    the neighbouring call on its own line (`GetStoreLocation` @346) is present in both.
- **So CP3's relation-equality claim holds as written** (both modes strip the same 2 occurrences,
  so the relations are identical — confirmed by `compare_nop.py` and by canonical equality of
  the via-relations), **but CP2's mode attribution is wrong**: the masking is mode-independent,
  not text-only. The 2 missing occurrences are a small, symmetric limitation (the relations
  themselves survive via other occurrences of the same methods). Qualify the CP2 claim; no
  gate impact.

## 8. Gate 8 performance — PASS on median-of-sessions (condition 1)

Protocol per the brief: wall-clock `dotnet <dll> <corpus> --format <fmt>` including process
startup, 1 warmup + 3 timed runs, median, same machine/session, pinned `--generated-at`.
Session 2 (5-run median) run for the breaching cells per the brief's two-session rule.
Scripts `/tmp/f2final-verify/perf.py` (sessions 1 and 2 on box #3), driver numbers from
`CHECKPOINT_F2_CP3.md` §4 (box #4) plus the driver's earlier sequential run (task-1000).

### Session medians (F2 vs F1, %)

| Cell | Driver s1 (box #4) | Driver early | My s1 (box #3, 3-run) | My s2 (box #3, 5-run) | **Median of sessions** |
|---|---|---|---|---|---|
| nop text/json | +1.71% (3,436.2→3,495.1) | −1.92% | −7.81% (3,315.5→3,056.6) | +12.74% (3,195.3→3,602.2) | **−0.11%** |
| nop text/md | −6.23% | — | −4.36% (3,243.8→3,102.5) | −9.64% (3,245.0→2,932.1) | **−6.23%** |
| nop roslyn/json | +0.29% | — | +1.53% (9,298.6→9,441.2) | +2.96% (9,080.0→9,349.1) | **+1.53%** |
| GoldenCorpus text/json | +7.57% | — | +7.03% (353.4→378.2) | +10.91% (375.1→416.0) | **+7.57%** |
| Adversarial text/json | +9.29% | — | +6.71% (313.8→334.8) | +5.72% (333.7→352.8) | **+6.71%** |
| DiffPair/base text/json | +10.60% (314.0→347.3) | +5.04% | +11.92% (260.0→291.0) | +9.24% (271.4→296.5) | **+9.92%** |
| DiffPair/head text/json | +3.52% | — | +10.12% (253.1→278.7) | +6.57% (266.2→283.7) | **+6.57%** |
| StrictFailure text/json | +13.21% (276.3→312.8) | +7.81% | +8.14% (264.1→285.6) | +8.06% (261.1→282.1) | **+8.10%** |

### Judgment

- **nopCommerce (the cells that matter):** median-of-sessions −0.11% / −6.23% / +1.53% — all
  well within the +10% bound and the +5% text aim. Every measured F2 absolute time is under
  the 3,688 ms frozen line (3,056.6–3,602.2 ms). The single-session +12.74% (my s2 text/json)
  is machine noise: F2's 5 timed runs in that session spread 3,094.9–3,955.9 ms while the base
  stayed tight (3,150.6–3,255.7 ms) — box #3 is a shared VM and the 33.7 MB JSON serialization
  is contention-sensitive. The median-of-sessions rule exists for exactly this.
- **Fixtures:** every fixture shows a consistent **~+21–33 ms** absolute F2 overhead
  (DiffPair/base +31.0/+25.1 ms; DiffPair/head +25.6/+17.5 ms; StrictFailure +21.5/+21.0 ms;
  GoldenCorpus +24.8/+40.9 ms; Adversarial +21.0/+19.1 ms) — a fixed cold-start JIT cost of the
  new resolver code (driver's `--profile`: ~14.5 ms internal stage delta), not a scaling cost.
  On ~250–350 ms processes (mostly `dotnet` startup) that fixed cost reads as +6–12%.
- **The marginal cell is DiffPair/base text/json:** median-of-4-sessions **+9.92%** (0.08pp under
  the bound); median of the 3 formal protocol sessions alone is +10.60% (over). It breached in
  2 of 4 sessions (+10.60%, +11.92%) and not in the other 2 (+5.04%, +9.24%) — i.e. the breach
  does not reproduce consistently, and the swing (+5.04% → +11.92%, ~7pp) is within the ±5pp
  small-corpus noise band the brief names. Per the brief's rule ("judgment is on
  median-of-sessions"), the cell passes — by a hair. This is condition 1: the user accepts the
  marginal cell, or requests a perf-fix pass for the fixed JIT cost. There is no scaling
  regression to fix on any real corpus.
- Output sizes + relation counts recorded per run in `perf-s1.log` / `perf-s2.log`
  (nopCommerce text/json 9,644,562 B → 33,747,841 B; relations 3,234 → 8,764 — growth visible,
  as the brief requires).

## 9. Determinism, H7, H5, schemaVersion — PASS (Gates 5, 7, 9)

- **Gate 5 determinism:** duplicate runs (F2 binary, pinned `--generated-at`), all 12 cells
  byte-identical. SHAs match the driver's CP3 gate-5 table exactly:
  GoldenCorpus text `83088b34…` / roslyn `ccc17f6e…`; Adversarial `4dcf76e8…` / `32b77182…`;
  DiffPair/base `f0f9e14c…` / `0a2b417a…`; DiffPair/head `a805798e…` / `7263b37c…`;
  StrictFailure `fdbc69d7…` / `92adc27b…`; nopCommerce text `5bc6d4f9…` / roslyn `378a7f42…`.
- **H7:** `RequestSequenceBuilder` diff empty. **H5:** `git diff -- '*.csproj' CHANGELOG.md README.md
  ROADMAP.md Directory.Build.props tests/CqrsLens.Tests/FrozenQuestionSet.cs` empty;
  **0** new `<PackageReference>` lines. **schemaVersion stays 1.**
- **Gate 9 scope:** 24 modified files +1388/−202 (matches CP3); new files as listed in CP3 §5;
  no corpus/class-name special-casing (no `Nop`/`nopCommerce` literals in `src/`); nopCommerce
  working tree clean at `release-4.90.0` (`e7a52b91`).

## Gate-by-gate summary

| Gate | Verdict | Evidence (one line) |
|---|---|---|
| 1 Legacy parity | **PASS** | Suite 761/761, 0 failed, 0 skipped (68 corpus tests ran); FrozenQuestionSet diff empty; CP1 findings addressed |
| 2 Identity uniqueness | **PASS** | 516 components, 0 dup IDs, 0 multi-role; 1,151 unique endpoints; AddressModelFactory ×2 distinct IDs; IWidgetModelFactory ×2 distinct node keys |
| 3 Evidence completeness | **PASS** | 0/8,764 relations missing rule/occurrences; 12 location-omitted (projectReference) |
| 4 No invented certainty | **PASS** | 0 requests/notifications/behaviors/dbContexts; 0 Publishes; unproven 340 (7 + 333); verbs 556 UNKNOWN / 594 POST / 1 GET |
| 5 Deterministic output | **PASS** | 12/12 duplicate runs byte-identical; SHAs match CP3 table |
| 6 Ground-truth reconciliation | **PASS** | Table A 100/100; call sites 1,042 matched, 141 all classified; Table B 54+2+0; TouchesData exact; 4-hop chain; §4.1 + §6 exact (18 publish calls, audit said 15 — audit undercounted) |
| 7 Regression | **PASS** (cond. 2) | Fixtures + nopCommerce non-relations byte-identical except recorded serviceCallEdges; formats/--diff/--strict/MCP identical; warnings 16=16, Core 0; H7 empty |
| 8 Performance | **PASS** (cond. 1) | Median-of-sessions: nop −0.11% / −6.23% / +1.53%; fixtures ≤ +9.92%; DiffPair/base marginal at +9.92% |
| 9 Scope clean | **PASS** | Scope diff empty; 0 new PackageReferences; no corpus literals in src/; corpus pinned + clean |

## Discrepancies vs the driver's checkpoints (all resolved, none a gate failure)

1. CP3 gate-2 wording: "ICatalogModelFactory (Nop.Web.Factories vs Admin)" — there is only one
   `ICatalogModelFactory` in the source (Nop.Web/Factories). The Admin-vs-public namesake pair is
   `AddressModelFactory` (verified: 2 distinct IDs). Checkpoint wording error, not a tool defect.
2. CP2 interpolated-string claim ("text-only stripping") — stripping is in both modes (§7).
3. Audit §6.11 "15 slice publish calls" — source has 18 (15 `PublishAsync` + 3 specialized).
   Tool correct; audit undercounted (§5G).
4. Driver published no final-tree 6-corpus table — the ABP resolvesTo −161, the ardalis/eshop
   `touchedEntities` additions, and the ABP/modular `serviceCallEdges` deltas are characterized
   here for the first time (§3, condition 2).
5. Driver's suite line (693 passed + 68 skipped) is the box #4 behaviour (corpora absent);
   on box #3 the 68 corpus tests run and pass (761/761).

## Commands & artifacts (reproducibility)

- Box #3 SSH: `ssh -p 2222 -o ProxyCommand="nc -X connect -x hatch-egress-proxy:3128 %h %p" -o UserKnownHostsFile=/home/hatch/.ssh/instabox_known_hosts ibssh_dohh36n4jsu3t6nscxfvxnmlli@ssh.instabox.sh`
- Worktrees (deleted after verification): `/data/workspace/cqrslens-f2final-base`,
  `/data/workspace/cqrslens-f2final` — removed via `git worktree remove --force` at the end;
  corpus re-verified clean (`HEAD e7a52b91`, tag `release-4.90.0`) and the main clone
  `/data/workspace/cqrslens` untouched at `bd49fc3` (pre-existing untracked files
  `.agents/`, `AGENTS.md`, `DESIGN.md`, `PRODUCT.md`, `site/` were already there, not mine).
- VM scratch: `/tmp/f2final/` (extracted snapshot), `/tmp/f2final-verify/` — scripts
  `compare_nop.py`, `run_ab.py`, `compare_ab2.py`, `reconcile2.py`, `perf.py`, `determinism.py`,
  `inspect_rfq_interp.py`; outputs `compare_nop.out`, `reconcile2.out`, `ab-compare2.txt`,
  `perf-s1.log`, `perf-s2.log`; transferred JSONs `nop-f2-text.json`, `nop-f2-roslyn.json`.
  Box #3 outputs in `/tmp/f2final-verify/ab/`, `/tmp/f2final-verify/out/`,
  `/tmp/f2final-verify/perf-s1/`, `/tmp/f2final-verify/perf-s2/`, `/tmp/f2final-verify/determinism/`.
- Note (verifier's own errors, corrected in-session, no tool impact): an early line-based
  endpoint check scored 46/100 (tool records endpoints at the attribute line, not the method
  line — fixed to name-based, 100/100); a comparison script crashed on the missing `relations`
  key for empty fixtures (fixed with `.get()`); a combined transfer+launch SSH chain left a
  0-byte perf script (transfer and launch must be separate calls — fixed, re-run).

## What this verification does NOT decide

Committing is the user's call after this verdict (F1-style: his explicit accept). No commit,
push, or driver relaunch was performed. The driver tree on box #4 was not modified.
