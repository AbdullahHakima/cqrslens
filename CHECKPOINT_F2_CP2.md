# CHECKPOINT F2 CP2 — Spine Endpoints, Call/Data Edges, Unproven Populations, & Corpus Reconciliation

- **Milestone:** Foundation 2 — Checkpoint 2 (`BRIEF_F2.md` CP2)
- **Branch:** `foundations/f2-controllers`
- **Base Commit (`HEAD`):** `3fcbcba` (uncommitted working tree; never committed or pushed per H9)
- **Target Corpus:** `nopCommerce` `release-4.90.0` (`e7a52b9199f9c7651839e92826df47f668f3c767`, read-only clean) + all 5 in-repo fixtures (`GoldenCorpus`, `Adversarial`, `DiffPair/base`, `DiffPair/head`, `StrictFailure`)
- **Status:** **PASS** (Verified against `F2_CORPUS_AUDIT.md`, F1 baseline at `3fcbcba`, both `text` and `roslyn` modes, and the full test suite)

---

## 1. Summary & Architectural Decisions

CP2 implements the full F2 spine graph over the CP1 component/role inventory: conventional & attribute-routed controller endpoints (`SpineEndpoints` + `Exposes`), DI & concrete component-to-method call chains (`Calls`), repository data-access edges (`TouchesData`), and explicit non-speculative uncertainty tracking (`Unproven`), while enforcing the user-approved test-project exclusion rule in `ServiceResolutionIndexBuilder`.

### 1.1 User Decision: General Test-Project Exclusion Rule (`ServiceResolutionIndexBuilder`)

- **Decision:** Exclude test projects from the DI service-resolution index as a general rule using existing project layer classification, never hardcoding corpus or project names.
- **Exact Rule Implemented** ([ServiceResolutionIndexBuilder.cs:25-48](file:///data/workspace/cqrslens/src/CqrsLens.Analysis/Resolution/ServiceResolutionIndexBuilder.cs#L25-L48)):
  1. Collect all `result.Projects` where `string.Equals(project.Layer, "Tests", StringComparison.OrdinalIgnoreCase)` into a case-insensitive `testProjects` set.
  2. Filter `parsedFiles` to `activeFiles` whose `f.File.ProjectName is null || !testProjects.Contains(f.File.ProjectName)`.
  3. Rebuild the shared `TypeHierarchy` over `activeFiles` when `testProjects.Count > 0` so declarations in `Tests` projects never enter the declaration candidate map or registration table.
- **Impact on nopCommerce (`Nop.Tests`):**
  - Eliminated test-double ambiguity (`TestPictureService`, `TestAuthenticationService`, `TestSmtpBuilder`, `TestTaskScheduler`, test `IConsumer<T>`, etc.), unblocking **+12** production `ResolvesTo` bindings and removing **-1** test-internal `ResolvesTo` binding (`Nop.Tests.Nop.Core.Tests.Infrastructure.TypeFinderTests.ISomeInterface -> SomeClass`).
  - Downstream in `ServiceCallEdgeResolver`, excluding those test doubles unblocked **+86** production `injectedMemberInvocation` calls (`IPictureService`: 48, `INopFileProvider`: 17, `ICustomerService`: 4, `ISettingService`: 3, `IAuthenticationService`: 2, `IProductAttributeParser`: 2, `IProductAttributeService`: 2, `AppSettings`: 1, `IWebHelper`: 1, `ILocalizationService`: 1, `ILogger`: 1, `IDownloadService`: 1, `IEmailAccountService`: 1, `ISmtpBuilder`: 1, `IUrlRecordService`: 1). Combined with **-22** `EventPublisher` publish edges reclassified to `eventPublishCallSite` (see §1.5), legacy `serviceCallEdges` and `injectedMemberInvocation` changed by `+64` net (`2,446 -> 2,510`).
- **Impact on In-Repo Fixtures:** Zero change (`0` `Tests`-layer projects affect production fixture contracts).

### 1.2 R-ENDPOINT Outcome: Non-Severed Controller Endpoint Exposure (`ConventionalControllerEndpointDiscovery`)

- **Outcome:** Non-severed. Both attribute-routed MVC actions and conventional MVC controller actions are discovered in [ConventionalControllerEndpointDiscovery.cs](file:///data/workspace/cqrslens/src/CqrsLens.Analysis/Slicing/Endpoints/ConventionalControllerEndpointDiscovery.cs) and linked via `RelationKind.Exposes` (`EvidenceRule.ControllerEndpointExposure`).
- **No Invented Certainty on HTTP Verbs or Routes:**
  - Actions with explicit `[HttpGet]`, `[HttpPost]`, `[AcceptVerbs(...)]`, etc. emit proven `httpMethod` and `verbStrength = "proven"`.
  - Un-attributed conventional MVC actions **never silently default to `"GET"`**; they record `httpMethod = "UNKNOWN"`, `verbStrength = "inferred"`, `routeStrength = "inferred"`, `inferred = true`, and conventional route template `/[controller]/[action]` (or `/<area>/[controller]/[action]` when `[Area("...")]` is present).
  - Preprocessor directives (`#region Methods (common)`, `#if`, `#pragma`, etc.) are skipped during brace/paren depth tracking so conventional actions following `#region (...)` headers (e.g. `CatalogController.NewProducts`, `CheckoutController.Index`, `CheckoutController.BillingAddress`) are discovered accurately (`100 / 100` Table A slice actions).
- **nopCommerce Counts:** `1,151` `spineEndpoints` + `1,151` `Exposes` edges (`556` `UNKNOWN`/inferred verb, `594` `POST`/proven verb, `1` `GET`/proven verb); legacy F1 `endpoints` remains omitted/empty (`0`). In the 100-action storefront slice: `51` `UNKNOWN`/`inferred` + `49` `POST`/`proven` = `100 / 100`.
- **GoldenCorpus Counts:** All `6` existing endpoints (2 MVC `OrdersController` + 4 Minimal API `MapGet`/`MapPost`/`MapDelete`) appear in `spineEndpoints` with `2` new `Exposes` edges (`OrdersController` -> `OrdersController.Archive`, `OrdersController.Get`); legacy `endpoints` stays untouched (`6`).

### 1.3 Option (a) Repository Data-Access Rule (`TouchesData` / `RepositoryDataAccess`)

- Per `BRIEF_F2.md`, `ComponentCallAndDataResolver` emits `RelationKind.TouchesData` (`EvidenceRule.RepositoryDataAccess`) from a `Component` (`Service`, `Controller`, `Repository`, etc.) to `Type` (`Entity`) along a 3-hop `via` chain `[IRepository<TEntity>, EntityRepository`1, Method]` **only when a method is statically invoked on an `IRepository<TEntity>`-typed member**.
- Pure property accesses without repository method invocation (e.g. LINQ `_productRepository.Table` queries in `CategoryService` / `VendorService`) and un-invoked `IRepository<T>` ctor injections produce no fabricated `TouchesData` edge.
- Every `TouchesData` edge on nopCommerce (`611` relations across the solution) maps to a genuine `IRepository<TEntity>` method call; `0` core/slice factories emit `TouchesData` (`CheckoutModelFactory`: `0`, `CatalogModelFactory`: `0`), and solution-wide only `Nop.Plugin.Misc.RFQ.Factories.AdminModelFactory` legitimately emits `TouchesData` (`Customer`) because it directly injects and calls `_customerRepository` (`IRepository<Customer>` at line 43).

### 1.4 Component Call Resolution (`ComponentMemberInvocation`) & Registered-Candidate Filtering

- `ComponentCallAndDataResolver` resolves injected service calls (closed-generic, open-generic such as ``AttributeService`2`` / ``EntityRepository`1`` / ``PluginManager`1``, partial classes, concrete service/setting helpers, and controller protected helpers) via `EvidenceRule.ComponentMemberInvocation` (`3,649` relations on nopCommerce), using `ServiceResolutionLookup` so interface-typed fields only resolve when unambiguously bound.
- In [AnalysisResult.cs](file:///data/workspace/cqrslens/src/CqrsLens.Core/Analysis/AnalysisResult.cs), `OnServiceResolutionAdded` filters ambiguous candidate lists to `FromRegistration == true` whenever at least one candidate has an explicit DI registration:
  - Example: `IWorkflowMessageService` has 3 in-tree implementations (`WorkflowMessageService`, `BrevoMessageService`, `RfqMessageService`), of which only 2 are registered in DI; `RfqMessageService` is excluded, leaving the exact 2 registered candidates (`WorkflowMessageService`, `BrevoMessageService`).
  - Example: `ICacheKeyService` & `IStaticCacheManager` retain their 5 registered cache managers and exclude unregistered `PerRequestCacheManager`.
  - Same-type duplicate registrations collapse cleanly (`IThumbService`: 3 distinct types across registrations including collapsed `ThumbService`; `IRedisConnectionWrapper`: 1 concrete type -> promoted to resolved `ResolvesTo`).
- Uninjected or unregistered marker/entity/plugin-item interfaces with no DI ambiguity impact are pruned from `ambiguousResolutions` via `UnprovenPopulations.PruneAmbiguousResolutions`, yielding zero false positives on all 12 §6.12 ruled-out items.

### 1.5 Event Publishing & Unproven Populations (`EventPublishCallSite` & `UnprovenPopulations`)

- `ComponentCallAndDataResolver` intercepts calls to `IEventPublisher` (`PublishAsync`, `EntityInsertedAsync`, `EntityUpdatedAsync`, `EntityDeletedAsync`, `PublishShipmentDeliveredAsync`, etc.) and emits `RelationKind.Calls` with `EvidenceRule.EventPublishCallSite` (`44` relations, `108` occurrences carrying `detail: "<EventType>"` on nopCommerce), `0` speculative `Publishes` edges, and `108` matching `UnprovenItemKind.UnresolvedEventConsumer` observations.
- Legacy `SpineQueries.BuildServiceCallEdges` excludes `EvidenceRule.EventPublishCallSite` so event-publish calls never leak into legacy `serviceCallEdges`. This accounts for the **-22** `EventPublisher.*` edge shift out of `injectedMemberInvocation` / `serviceCallEdges` into `eventPublishCallSite` (`44` relations across services, factories, and framework helpers).
- Production non-test files are scanned cleanly for `ServiceLocator` (`159`), `DynamicReflection` (`55`, including `FindClassesOfType`, `Activator`, `ResolveUnregistered`, plugin contract discovery), `ConventionRegistration` (`9` `FindClassesOfType` call sites across `NopEngine.cs`, `NopDbStartup.cs`, `ServiceCollectionExtensions.cs`, `NopStartup.cs`), and `UnimplementedInterface` (`2`: `ISearchProvider` via `SearchPluginManager.cs:11`, `IAdminMenuPlugin` via `AdminMenu.cs:31`).

---

## 2. Additive JSON Schema Extension (`schemaVersion: 1`)

All existing JSON sections remain **schema-identical** and **byte-identical** across all 5 fixtures (except the intended `nopCommerce` `serviceCallEdges` `+64` downstream change from `Layer == "Tests"` exclusion). Four additive structures were added, each omitted when empty:

1. **Top-level `"components"`** (`List<JsonSpineComponent>?`, omitted when empty):
   - Fields in declaration order: `"id"` (`string`), `"fullName"` (`string`), `"name"` (`string`), `"project"` (`string?`), `"location"` (`JsonLocation?`), `"roles"` (`List<JsonSpineRoleEvidence>` where each item has `"role"` and `"evidence": List<JsonSpineEvidence>`).
2. **Top-level `"spineEndpoints"`** (`List<JsonSpineEndpoint>?`, omitted when empty):
   - Fields in declaration order: `"id"`, `"name"`, `"httpMethod"`, `"route"`, `"verbStrength"`, `"routeStrength"`, `"inferred"` (`bool`), `"project"`, `"location"`, `"evidence"`.
3. **Top-level `"unproven"`** (`JsonUnprovenPopulations?`, omitted when `totalCount == 0`):
   - `"totalCount"` (`int`)
   - `"ambiguousResolutions"`: `List<JsonAmbiguousResolution>?` (`interfaceSignature`, `interfaceName`, `interfaceLocation`, `candidates`: `List<JsonServiceCandidate>` with `type`, `declaredInScan`, `fromRegistration`, `location`, `registrationLocation`)
   - `"observations"`: `List<JsonUnprovenDiagnostic>?` (`kind`, `code`, `subject`, `message`, `project`, `location`) — populated with `"serviceLocator"`, `"dynamicReflection"`, `"conventionRegistration"`, `"unimplementedInterface"`, `"unresolvedEventConsumer"`
   - Optional existing sub-lists when present (`"unresolvedDispatches"`, `"ambiguousLinks"`, `"unresolvedContracts"`, `"unestablishedBehaviors"`).
4. **Per-occurrence `"detail"`** (`string?`, omitted when null) inside `relations[].occurrences[]`:
   - Records structured call detail (e.g., published event type `"Nop.Core.Events.EntityDeletedEvent<T>"` on `eventPublishCallSite`).

New serialized enum values:
- `RelationKind`: `"exposes"` (`RelationKind.Exposes`)
- `ComponentRole`: `"controller"`, `"service"`, `"repository"`
- `EvidenceRule`: `"controllerEndpointExposure"`, `"componentMemberInvocation"`, `"repositoryDataAccess"`, `"eventPublishCallSite"`, plus `"controllerDiscovery"`, `"serviceDiscovery"`, `"repositoryDiscovery"`
- `UnprovenItemKind`: `"conventionRegistration"`, `"serviceLocator"`, `"dynamicReflection"`, `"unimplementedInterface"`, `"unresolvedEventConsumer"`

---

## 3. nopCommerce 4.90 Reconciliation & Verification

### 3.1 Scan Commands & Parity

```bash
DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH \
  dotnet src/CqrsLens.Cli/bin/Release/net8.0/cqrslens.dll \
  /data/workspace/corpora/nopcommerce --analysis text --format json \
  -o /tmp/f2check/latest/nop/nop_f2_text_1.json --no-banner --no-color \
  --generated-at 2026-01-01T00:00:00Z
```

- **Text SHA-256 (x2 deterministic):** `5bc6d4f9bf9fa33e90724a5126060223a4f774349a0e3fa74dd95cbc42c0125a` (`33,747,841` bytes)
- **Roslyn SHA-256 (x2 deterministic):** `378a7f42dad80b32d78e9064fd20d27ae0c224cd58fb112d13dcd7ffc5366b8b` (`33,747,843` bytes — 2-byte diff solely from `"text"` vs `"roslyn"` in `"scanner"`)
- **Strict Text == Roslyn parity:** `components: True`, `spineEndpoints: True`, `unproven: True`, `relations: True`, `serviceCallEdges: True`.
- **Legacy CQRS surfaces on nopCommerce:** `requests=0`, `notifications=0`, `behaviors=0`, `dbContexts=0`, `validators=105`, `checks=0`, `fileCount=3,464`, `projects=35` — all byte-identical to F1 baseline (`b2825eb8...`).
- **Component Inventory:** `516` unique `components` (`282` `service`, `1` `repository`, `128` `controller`, `105` `validator`), `0` duplicate IDs.

### 3.2 Table A Reconciliation (100 Storefront Slice Actions, 5 Controllers)

- **Endpoint & Exposure Coverage:** **100 / 100** Table A slice actions matched in `spineEndpoints` and **100 / 100** linked via `Exposes` edges (`51` `httpMethod="UNKNOWN", verbStrength="inferred", routeStrength="inferred"`, `49` `httpMethod="POST", verbStrength="proven", routeStrength="inferred"`):
  - `CatalogController.cs`: **21 / 21**
  - `ProductController.cs`: **13 / 13**
  - `CheckoutController.cs`: **29 / 29**
  - `OrderController.cs`: **12 / 12**
  - `ShoppingCartController.cs`: **25 / 25**
- **Dependency Call-Line Reconciliation:**
  - `763` direct Table A call lines + `279` helper-method call lines matched directly to `Calls` edge occurrences on the controller.
  - `135` cited non-edge lines classified cleanly: `129` Settings property/configuration reads (`*Settings.*`), `6` non-invocation property/argument references.
  - `4` injected call sites targeting ambiguous DI contracts (`ProductController.cs:479` & `ShoppingCartController.cs:1734` -> `IWorkflowMessageService`; `ShoppingCartController.cs:952,954` -> `IStaticCacheManager`) intentionally omitted from `Calls` edges and recorded in `unproven.ambiguousResolutions` (Gate 4: no invented certainty).
  - `2` valid method calls inside C# interpolated string literals (`CatalogController.cs:344` `_localizationService.GetLocalizedAsync` inside `$"{await ...}"`, `CheckoutController.cs:2080` `_webHelper.GetStoreLocation` inside `$"{...}"`) are stripped by string-literal masking in text mode and noted in §6 Known Limitations.

### 3.3 Table B Reconciliation (56 Invoked Interfaces) & `TouchesData`

- **Interface Resolution:** **56 / 56** Table B interfaces accounted for: **54** resolved to `ResolvesTo` edges and **2** intentionally held in `unproven.ambiguousResolutions` (`IStaticCacheManager`, `IWorkflowMessageService`), `0` missing.
- **`TouchesData` Subset Verification:**
  - `ProductService` (`Nop.Services/Catalog/ProductService.cs`): `10` `TouchesData` entities (strict subset of `20` injected `IRepository<T>` params; remaining `10` are `.Table` property reads or un-invoked in-tree).
  - `OrderService` (`Nop.Services/Orders/OrderService.cs`): `5` `TouchesData` entities (strict subset of `9` injected `IRepository<T>`).
  - `CategoryService` (`Nop.Services/Catalog/CategoryService.cs`): `3` `TouchesData` entities (strict subset of `4`; `_productRepository` uses `.Table` only at line 612).
  - `ShoppingCartService` (`Nop.Services/Orders/ShoppingCartService.cs`): `1 / 1` (`ShoppingCartItem`).
  - `OrderProcessingService` (`Nop.Services/Orders/OrderProcessingService.cs`): `0 / 0` (orchestrator).
  - `CheckoutModelFactory`, `CatalogModelFactory`, and all storefront/admin factories: `0 / 0`. Solution-wide only `Nop.Plugin.Misc.RFQ.Factories.AdminModelFactory` has `TouchesData` (`Customer` via injected `_customerRepository` at line 43). *(Note: `VendorService` ctor lines 18–31 actually inject 4 repositories including `VendorNote` at line 31; F2 emits `Vendor` and `VendorNote` and omits `.Table`-only `Customer` & `Product`.)*
- **End-to-End 4-Hop Chain Proof (`CatalogController.Category`):**
  1. `Exposes`: `CatalogController` -> endpoint `CatalogController.Category` @ `src/Presentation/Nop.Web/Controllers/CatalogController.cs:113` (`controllerEndpointExposure`, `inferred`)
  2. `Calls` + `ResolvesTo`: `CatalogController` -> `CatalogModelFactory.PrepareCategoryModelAsync` @ `CatalogController.cs:137` via `ICatalogModelFactory` -> `CatalogModelFactory` (`CatalogModelFactory.cs:30`)
  3. `Calls` + `ResolvesTo`: `CatalogModelFactory` -> `CategoryService.GetAllCategoriesAsync` @ `CatalogModelFactory.cs:493` via `ICategoryService` -> `CategoryService` (`CategoryService.cs:18`)
  4. `TouchesData`: `CategoryService` -> `Nop.Core.Domain.Catalog.Category` @ `CategoryService.cs:204` (`repositoryDataAccess`, `resolved`)

### 3.4 §4.1 Ambiguities & §6 Negative Cases

- **§4.1 (8 Interfaces):** `ICacheKeyService` (5 reg), `ILocker` (2 reg), `IStaticCacheManager` (5 reg), `INopDataProvider` (3 decl), `IThumbService` (3 distinct types across registrations, collapsing duplicate `ThumbService`), `IWorkflowMessageService` (2 reg: `WorkflowMessageService` + `BrevoMessageService`; unregistered `RfqMessageService` excluded), `INopAssetHelper` (2 reg) remain in `unproven.ambiguousResolutions`; `IRedisConnectionWrapper` collapses cleanly to `RedisConnectionWrapper` (`ResolvesTo`, `serviceConfirmedBinding` + `serviceDeclarationCandidate`).
- **§6 Negative Cases:** `UnimplementedInterface` = `2` (`ISearchProvider` @ `SearchPluginManager.cs:11`, `IAdminMenuPlugin` @ `AdminMenu.cs:31`); `IPagination<T>` = `0` relations / `0` unproven (dead); `ProductService` service locator = `lines 1739, 1774`; `ConventionRegistration` = `9`; `DynamicReflection` = `55`; `ServiceLocator` = `159`; `UnresolvedEventConsumer` = `108` (including `18` across slice services, e.g. `OrderPlacedEvent`, `OrderPaidEvent`, `OrderRefundedEvent`, `ClearShoppingCartEvent`, `ShipmentDeliveredEvent`); **0 `Publishes` edges**; **0 hits** in `ambiguousResolutions` or `observations` for all §6.12 ruled-out marker/entity/settings interfaces (`IAclSupported`, `IDiscountSupported`, `IStoreMappingSupported`, `ISoftDeletedEntity`, `ISlugSupported`, `BaseEntity`, `ISettings`, `IPagedList`, `IRepository`).

---

## 4. Complete `relations` & Non-`relations` Deltas Across All 6 Corpora

### 4.1 In-Repo Fixtures (5 Corpora — Text & Roslyn)

On all 5 fixtures, **shared non-`relations` JSON keys (`projects`, `requests`, `handlers`, `validators`, `behaviors`, `dbContexts`, `checks`, `diagnostics`, `notifications`, `endpoints`, `flowEdges`, `resolvedEdges`, `serviceCallEdges`, `dispatchCallSites`, `unresolvedDispatches`) AND `.md`, `.html`, `.agent.md`, `.summary.md`, `.sarif`, `--diff` (`.md`/`.json`), and `--strict` exit codes are 100% byte-identical between F1 and F2**:

| Corpus | F1 Relations | F2 Relations | Exact `relations` Delta | Generic-Arity `` `N `` Name Renderings | Non-`relations` JSON & Format Diffs |
|---|---:|---:|---|---|---|
| `GoldenCorpus` | 25 | 27 | **+2 `exposes`** (`controllerEndpointExposure`, `proven`): `OrdersController` -> `OrdersController.Archive` (`OrdersController.cs:10`) and `OrdersController.Get` (`OrdersController.cs:13`). `0` removed, `0` modified. Identical in `text` & `roslyn`. | Unchanged (`2` F1 edges preserved verbatim): ``IRequestHandlerFactory`1<CreateOrderCommand>`` -> `NotAHandler`; ``ICommand`1<bool>`` -> `ArchiveOrderCommand`. | `0` diffs on shared keys; `.md`/`.html`/`.agent.md`/`.summary.md`/`.sarif` byte-identical; additive `components=21`, `spineEndpoints=6`, `unproven=2`. |
| `Adversarial` | 6 | 6 | **None** (`0` added, `0` removed, `0` modified in `text` & `roslyn`). | Unchanged (`1` F1 edge preserved verbatim): ``IAppHandler`1<ArchiveWidgetCommand>`` -> `ArchiveWidgetCommandHandler`. | `0` diffs on shared keys; all 5 formats byte-identical; additive `components=9`, `unproven=1`. |
| `DiffPair/base` | 1 | 1 | **None** (`0` added, `0` removed, `0` modified in `text` & `roslyn`). | None present. | `0` diffs on shared keys; all formats + `--diff` `.md`/`.json` byte-identical; additive `components=2`. |
| `DiffPair/head` | 0 | 0 | **None** (`0` added, `0` removed, `0` modified in `text` & `roslyn`). | None present. | `0` diffs on shared keys; all formats + `--diff` `.md`/`.json` byte-identical; additive `components=1`. |
| `StrictFailure` | 0 | 0 | **None** (`0` added, `0` removed, `0` modified in `text` & `roslyn`). | None present. | `0` diffs on shared keys; all formats & exit code `4` identical; additive `components=1`. |

### 4.2 nopCommerce 4.90 (`3,234 -> 8,764` Relations; `6,830 -> 17,422` Occurrences)

**By Kind & Rule (F1 vs F2, identical in `text` & `roslyn`):**

| Kind | Rule(s) | F1 Count | F2 Count | Delta | Notes |
|---|---|---:|---:|---:|---|
| `references` | `projectReference` | 12 | 12 | `0` | Unchanged |
| `resolvesTo` | `serviceConfirmedBinding` | 5 | 6 | `+1` | Unblocked + confirmation updates (see below) |
| `resolvesTo` | `serviceConfirmedBinding`, `serviceDeclarationCandidate` | 225 | 233 | `+8` | Unblocked + 2 rule upgrades (`IRedisConnectionWrapper`, `IProductModelFactory`) |
| `resolvesTo` | `serviceDeclarationCandidate` | 540 | 545 | `+5` | Unblocked plugin/engine/consumer candidates minus `TypeFinderTests` |
| `resolvesTo` | `serviceRegistrationBinding` | 6 | 3 | `-3` | 2 upgraded + `ConcurrentTrie` replaced by confirmed component |
| **Subtotal `resolvesTo`** | | **776** | **787** | **+11** | `-1` test, `+12` production unblocked, `7` generic name upgrades |
| `calls` | `injectedMemberInvocation` | 2,446 | 2,510 | `+64` | `-22` `EventPublisher` reclassified + `+86` unblocked via `Layer == "Tests"` exclusion |
| `calls` | `componentMemberInvocation` | 0 | 3,649 | `+3,649` | CP2 component/factory/open-generic/concrete member calls |
| `calls` | `eventPublishCallSite` | 0 | 44 | `+44` | `108` publish occurrences (`detail` = event type), replaces `-22` legacy |
| **Subtotal `calls`** | | **2,446** | **6,203** | **+3,757** | Plus participant upgrade `External` -> `Component` across legacy calls |
| `touchesData` | `repositoryDataAccess` | 0 | 611 | `+611` | Option (a) `IRepository<T>` method calls (`Component` -> `Type`) |
| `exposes` | `controllerEndpointExposure` | 0 | 1,151 | `+1,151` | Controller -> `spineEndpoint` (`556` `UNKNOWN`/inferred, `594` `POST`, `1` `GET`) |
| **Total Relations** | | **3,234** | **8,764** | **+5,530** | Occurrences: `6,830 -> 17,422` (`+10,592`) |

**Complete `resolvesTo` & Generic-Arity Name Rendering Delta on nopCommerce (`776 -> 787`):**
1. **Removed `-1` test-project edge** (via `Layer == "Tests"` exclusion):
   - `Nop.Tests.Nop.Core.Tests.Infrastructure.TypeFinderTests.ISomeInterface -> Nop.Tests.Nop.Core.Tests.Infrastructure.TypeFinderTests.SomeClass` (`serviceDeclarationCandidate`).
2. **Added `+12` unblocked production edges** (via `Layer == "Tests"` exclusion):
   - `IConventionSet -> Nop.Data.Migrations.NopConventionSet` (`serviceConfirmedBinding`)
   - `Nop.Core.Infrastructure.IEngine -> Nop.Core.Infrastructure.NopEngine` (`serviceDeclarationCandidate`)
   - `Nop.Data.IDataProviderManager -> Nop.Data.DataProviderManager` (`serviceConfirmedBinding`, `serviceDeclarationCandidate`)
   - `Nop.Services.Authentication.IAuthenticationService -> Nop.Services.Authentication.CookieAuthenticationService` (`serviceConfirmedBinding`, `serviceDeclarationCandidate`)
   - `Nop.Services.Directory.IExchangeRateProvider -> Nop.Plugin.ExchangeRate.EcbExchange.EcbExchangeRateProvider` (`serviceDeclarationCandidate`)
   - `Nop.Services.Discounts.IDiscountRequirementRule -> Nop.Plugin.DiscountRules.CustomerRoles.CustomerRoleDiscountRequirementRule` (`serviceDeclarationCandidate`)
   - ``Nop.Services.Events.IConsumer`1<EntityInsertedEvent<NewsLetterSubscription>>`` -> `Nop.Services.Messages.Caching.NewsLetterSubscriptionCacheEventConsumer` (`serviceDeclarationCandidate`)
   - ``Nop.Services.Events.IConsumer`1<EntityUpdatedEvent<LocaleStringResource>>`` -> `Nop.Services.Localization.Caching.LocaleStringResourceCacheEventConsumer` (`serviceDeclarationCandidate`)
   - `Nop.Services.Media.IPictureService -> Nop.Services.Media.PictureService` (`serviceConfirmedBinding`, `serviceDeclarationCandidate`)
   - `Nop.Services.Messages.ISmtpBuilder -> Nop.Services.Messages.SmtpBuilder` (`serviceConfirmedBinding`, `serviceDeclarationCandidate`)
   - `Nop.Services.ScheduleTasks.ITaskScheduler -> Nop.Services.ScheduleTasks.TaskScheduler` (`serviceConfirmedBinding`, `serviceDeclarationCandidate`)
   - `Nop.Services.Shipping.Pickup.IPickupPointProvider -> Nop.Plugin.Pickup.PickupInStore.PickupInStoreProvider` (`serviceDeclarationCandidate`)
3. **Participant upgrades & rule changes on retained pairs (`768`):**
   - `238` `resolvesTo` targets (and all `2,446` legacy `calls` endpoints) upgraded from `ParticipantKind.External` (`External\x1f...`) to `ParticipantKind.Component` (`Component\x1f<proj>::...` / `Method\x1f<proj>::...`).
   - `2` `resolvesTo` pairs gained declaration confirmation (`serviceRegistrationBinding` -> `serviceConfirmedBinding`, `serviceDeclarationCandidate`): `IRedisConnectionWrapper -> RedisConnectionWrapper` and `IProductModelFactory -> ProductModelFactory`.
4. **Exact Generic-Arity Name Renderings (`Type` vs ``Type`N``):**
   - **7 generic concrete `resolvesTo` targets** shifted from un-suffixed F1 `External` names to CRL `` `N `` `Component` names:
     - ``Lazy`1`` -> `Nop.Web.Framework.Infrastructure.LazyInstance` (becomes ``LazyInstance`1``)
     - ``Nop.Core.Infrastructure.IConcurrentCollection`1`` -> `Nop.Core.Infrastructure.ConcurrentTrie` (becomes ``ConcurrentTrie`1``)
     - ``Nop.Data.IRepository`1`` -> `Nop.Data.EntityRepository` (becomes ``EntityRepository`1``)
     - ``Nop.Services.Attributes.IAttributeFormatter`2`` -> `Nop.Services.Attributes.AttributeFormatter` (becomes ``AttributeFormatter`2``)
     - ``Nop.Services.Attributes.IAttributeParser`2`` -> `Nop.Services.Attributes.AttributeParser` (becomes ``AttributeParser`2``)
     - ``Nop.Services.Attributes.IAttributeService`2`` -> `Nop.Services.Attributes.AttributeService` (becomes ``AttributeService`2``)
     - ``Nop.Services.Plugins.IPluginManager`1`` -> `Nop.Services.Plugins.PluginManager` (becomes ``PluginManager`1``)
   - **`resolvesTo.from` generic-arity strings:** `538 -> 540` (`+2` unblocked ``IConsumer`1<...>`` entries listed above).
   - **`calls` / `touchesData` generic-arity strings:** `5` generic `from` components (``EntityRepository`1``, ``AttributeFormatter`2``, ``AttributeParser`2``, ``AttributeService`2``, ``PluginManager`1``) and `41` generic `Method` targets (``ConcurrentTrie`1.*``, ``EntityRepository`1.*``, ``AttributeService`2.*``, etc.), all consistently rendered with CRL `` `N `` arity.
5. **Other Formats on nopCommerce:** `.md` (`11,844` B), `.summary.md` (`327` B), `.sarif` (`878` B) remain byte-identical between F1 and F2.

---

## 5. Files Modified / Created & Test Suite Verification

- **Modified (`24` files across `src/`, `0` files in `tests/` or protected files):**
  - `src/CqrsLens.Core/`: `Analysis/AnalysisResult.cs`, `Architecture/EndpointInfo.cs`, `Spine/ArchitectureSpine.cs`, `Spine/ComponentRole.cs`, `Spine/EvidenceOccurrence.cs`, `Spine/EvidenceRule.cs`, `Spine/RelationKind.cs`, `Spine/SpineQueries.cs`, `Spine/SpineRelation.cs`, `Spine/UnprovenItemKind.cs`, `Spine/UnprovenPopulations.cs`
  - `src/CqrsLens.Analysis/`: `Analyzer.cs`, `Resolution/ServiceCallEdgeResolver.cs`, `Resolution/ServiceRegistration.cs`, `Resolution/ServiceRegistrationParser.cs`, `Resolution/ServiceResolutionIndexBuilder.cs`, `Slicing/Endpoints/ControllerEndpointDiscovery.cs`, `Slicing/ReceiverTypeMapBuilder.cs`, `Text/CSharpSyntax.cs`
  - `src/CqrsLens.Reporting/`: `Json/DirectSpineRelationsConverter.cs`, `Json/JsonReportDocument.cs`, `Json/JsonReportDocumentFactory.cs`, `Json/JsonSpineEvidence.cs`, `Json/JsonSpineNames.cs`
- **New Source & Test Files (F2 CP1 + CP2):**
  - `src/CqrsLens.Analysis/Roles/` (`ComponentRoleDiscovery.cs`, `ControllerRoleRules.cs`, `ScannedComponentEntry.cs`, `ScannedTypeCatalog.cs`, `ServiceAndRepositoryRoleRules.cs`)
  - `src/CqrsLens.Analysis/Slicing/Endpoints/ConventionalControllerEndpointDiscovery.cs`
  - `src/CqrsLens.Analysis/Resolution/` (`ComponentCallAndDataResolver.cs`, `ExplicitServiceRegistration.cs`, `ExtensionPublishInfo.cs`, `MemberInvocationHit.cs`, `MemberInvocationScanner.cs`, `ResolvedServiceTarget.cs`, `ServiceResolutionLookup.cs`, `SourceLineIndex.cs`)
  - `src/CqrsLens.Reporting/Json/` (`JsonAmbiguousResolution.cs`, `JsonServiceCandidate.cs`, `JsonSpineComponent.cs`, `JsonSpineEndpoint.cs`, `JsonSpineRoleEvidence.cs`, `JsonUnestablishedBehavior.cs`, `JsonUnprovenDiagnostic.cs`, `JsonUnprovenPopulations.cs`)
  - `tests/CqrsLens.Tests/` (`F2RoleInventoryTests.cs` [5 tests], `F2RelationAndResolutionTests.cs` [10 tests])
- **Test Execution:** `dotnet test CqrsLens.sln -c Release --nologo` -> **693 passed, 68 skipped (external-corpus gated tests), 0 failed**; **0 new compiler warnings** (`CqrsLens.Core`: `0` warnings).

---

## 6. Known Limitations & Open Items

1. **Interpolated-string method invocations in Text scanner:** In C# interpolated strings (`$"{await _localizationService.GetLocalizedAsync(...)}"` at `CatalogController.cs:344` and `$"{_webHelper.GetStoreLocation()}"` at `CheckoutController.cs:2080`), string masking replaces literal interiors before regex member-invocation matching, omitting those 2 call occurrences in Table A (`CatalogController` still retains `LocalizationService` localization edges via lines 360–361, and `CheckoutController` still retains `WebHelper.GetStoreLocation` via `ConfirmOrder:1324`). Deferred cleanly (no ad-hoc patch).
2. **`TouchesData` excludes pure `.Table` property reads:** By Option (a) design, un-invoked `IRepository<T>` ctor parameters and `.Table` LINQ property accesses without a repository method call do not emit `TouchesData`.
3. **One-time cold-start JIT on 1-file fixtures (`~280ms` subprocesses):** Covered and quantified alongside Gate 8 in `CHECKPOINT_F2_CP3.md`.
