# F2 corpus audit — nopCommerce 4.90 (Catalog + Checkout slice)

- **Date:** 2026-11-15 (evening, Africa/Cairo)
- **Auditor:** Mariam (subagent), on Instabox box #4
- **Decision basis:** `F2_Corpus_Pick_2026-11-15.md` (Abdullah approved nopCommerce 4.90,
  hand-audit on the Catalog + Checkout slice, Linq2DB caveat option (a): repository access
  is the data evidence for this corpus). Smartstore kept as runner-up.
- **Charter:** `CqrsLens_Modeling_Foundations.md` (R1 proof discipline),
  `CqrsLens_Metamodel_F0.md` §10 (negative cases required).

## Verdict: ADMITTED WITH NOTES

The corpus is clean for F2: zero MediatR/mediator surface anywhere (confirmed both by
grep and by the F1 tool reporting 0 requests/handlers/notifications), the slice shape
matches the brief's expectations with the corrections noted below, and the F1 baseline
runs deterministically. The notes that matter for the F2 brief:

1. **Pinned to `release-4.90.0`, not a later 4.90.x patch** — patch tags 4.90.1–4.90.8
   exist on the remote; see §1.
2. **The brief's `DependencyRegistrar` claim is outdated** — 4.90 uses `INopStartup`
   discovery + explicit MS-DI `Add*` calls; see §4.
3. **Factories intermediate most controller→service chains**, and
   `OrderProcessingService` holds **zero** repositories (pure orchestrator); see §5.
4. **Eight ambiguous/duplicate registrations** and a rich set of **indirect,
   reflection- and config-resolved** edges that no static resolver can prove; see §§4, 6.
5. **nopCommerce has its own pub/sub** (`IEventPublisher`/`IConsumer<T>`), resolved by
   reflection — relevant to F2's Publishes evidence rule; see §§2, 6.

No finding contradicts the pick. Nothing was silently switched; no code was changed in
the CqrsLens repo (build only; `git status` verified clean-apart-from-docs afterwards,
§7).

---

## 1. Pin: tag + commit

| Item | Value |
|---|---|
| Repository | `https://github.com/nopSolutions/nopCommerce.git` |
| Location on box #4 | `/data/workspace/corpora/nopcommerce` (fresh clone; no prior copy existed) |
| Clone command | `git clone --depth 1 --branch release-4.90.0 https://github.com/nopSolutions/nopCommerce.git nopcommerce` |
| Tag | `release-4.90.0` |
| Commit | `e7a52b9199f9c7651839e92826df47f668f3c767` |
| Annotated tag object | `a26e19910fb6bb80833a31ec2cbfbaa7854ee777` |
| Working tree | 129M, `git status` clean (verified again after the baseline run) |

**Caveat — patch tags.** The remote also carries `release-4.90.1` through
`release-4.90.8`. `release-4.90.0` was pinned as the literal "4.90" stable release named
in the decision. If the parent prefers the latest 4.90 patch, re-pin and re-run §7; all
file:line evidence below is against `e7a52b91`.

## 2. MediatR grep — zero in core, zero everywhere

Command (box #4 has no ripgrep; `grep -r` used throughout):

```
grep -rniE "mediatr|imediator|irequest|inotification" . --exclude-dir=.git
```

**201 hits, all in `.cs` files, zero for MediatR / Mediator / IMediator.** Disposition:

| Hits | Pattern | Disposition |
|---|---|---|
| 160 | `INotificationService` | nopCommerce's own admin-toast notification service (`src/Libraries/Nop.Services/Messages/INotificationService.cs`). Not MediatR's `INotification`. |
| 47 | `iRequest` substring | Inside Zettle / PayPalCommerce plugin `*ApiRequest` / `*Request` DTO names (`ProductApiRequest` 12, `IApiRequest` 10, `ApiRequest` 7, `PusherApiRequest` / `OAuthApiRequest` / `InventoryApiRequest` / `ImageApiRequest` 4 each, `SecureApiRequest` 2). Substring matches only. |
| 1 | `IRequestCultureFeature` | ASP.NET Core localization feature, `src/Presentation/Nop.Web.Framework/WebWorkContext.cs:168`. |

Corroborating zero-results: zero `using MediatR` directives, zero MediatR
`PackageReference` in any `.csproj`, zero MassTransit / Wolverine / NServiceBus /
Brighter references, zero MediatR hits in `src/Tests`.

**Observation — nopCommerce's own pub/sub (not MediatR).** `IEventPublisher` /
`IConsumer<T>` (`src/Libraries/Nop.Core/Events/IEventPublisher.cs:6`; implementation
`src/Libraries/Nop.Services/Events/EventPublisher.cs`, consumers resolved via
`EngineContext.Current.ResolveAll<IConsumer<TEvent>>()` at lines 23 and 60). F2's
Publishes evidence rule must treat this as a distinct, reflection-resolved mechanism —
see §6. The F1 baseline independently confirms the zero-mediator picture (§7: 0
requests, 0 handlers, 0 notifications).

## 3. Counts

Method: `find` by filename, excluding `.git`. (Solution file lists 38 `Project(` lines;
that count includes solution folders, so the `*.csproj` count below is authoritative.)

| Item | Count | Notes |
|---|---|---|
| `*.csproj` | **35** | 34 under `src/` + 1 build tool (`src/Build/src/ClearPluginAssemblies`). The F1 run reports the same 35 projects (§7). |
| `*Controller.cs` | **133** | 91 Presentation, 42 Plugins. |
| `*Service.cs` files | **210** | Of which 99 are `I*Service.cs` interfaces → 111 implementation files by name. |
| `*Repositor*.cs` | **3** | `src/Libraries/Nop.Data/IRepository.cs`, `EntityRepository.cs`, + 1 test file. Generic-repository shape: services inject `IRepository<TEntity>`. |
| `.cs` files total | **3,464** | The F1 run reports the same 3,464 files scanned (§7). |

Framework / data layer:

- **Target framework `net9.0`** for all projects (`src/Directory.Build.props`);
  `global.json` pins SDK 9.0.100.
- **No EF Core**: zero EntityFramework package references; exactly one `DbContext` /
  `DbSet` mention in all of `src`, a comment at `NopEngine.cs:44`.
- Data layer is **linq2db 5.4.1** (`PackageReference Include="linq2db"`).
  `IRepository<TEntity> where TEntity : BaseEntity` confirmed
  (`src/Libraries/Nop.Data/IRepository.cs`). This is the accepted Linq2DB caveat
  (option (a)): repository access is the data evidence for this corpus.
- `Nop.Services` has `Catalog/` and `Orders/` folders as the brief expected.

## 4. DI registrations

**Correction to the decision brief: there is no `DependencyRegistrar` in 4.90.**
Zero `DependencyRegistrar` references anywhere in the tree (Autofac-era pattern, gone).
The actual mechanism:

- `NopEngine.ConfigureServices` (`src/Libraries/Nop.Core/Infrastructure/NopEngine.cs:105-122`)
  discovers all `INopStartup` implementations via
  `ITypeFinder.FindClassesOfType<INopStartup>()`, orders them by `Order`, instantiates
  via `Activator.CreateInstance`, and calls `ConfigureServices`. `INopStartup` is defined
  at `src/Libraries/Nop.Core/Infrastructure/INopStartup.cs:10`.
- Registrations themselves are explicit MS-DI `AddScoped` / `AddTransient` /
  `AddSingleton` calls inside the `INopStartup` implementations.

**547 `Add*` call sites in 21 files.** Breakdown:

| File | Sites | Notes |
|---|---|---|
| `src/Presentation/Nop.Web.Framework/Infrastructure/NopStartup.cs` | 159 | Core registrations incl. the cache config switch (§4.1) |
| `src/Presentation/Nop.Web/Infrastructure/NopStartup.cs` | 88 | Incl. factory registrations, namespace-qualified (§5) |
| `src/Libraries/Nop.Data/NopDbStartup.cs` | 10 | Incl. open-generic `IRepository<>` |
| `ServiceCollectionExtensions.cs` | 7 | Incl. `INopAssetHelper` branch (§4.1) |
| `NopEngine.cs` | 2 | |
| Plugin startups | ~30 | Across plugin `*NopStartup.cs` files |
| `src/Tests/Nop.Tests/BaseNopTest.cs` | 246 | **Test-only mirror registrations — exclude from production analysis** |

Open-generic registrations: `IRepository<>`→`EntityRepository<>`
(`NopDbStartup.cs:63`); `IConcurrentCollection<>` (`NopStartup.cs:95`);
`IAttributeService<,>`, `IAttributeParser<,>`, `IAttributeFormatter<,>`,
`IPluginManager<>` (`NopStartup.cs:250-259`); `Lazy<>`→`LazyInstance<>`
(`NopStartup.cs:326`, only when Autofac is not used).

Convention-based registrations (reflection, not `Add*` literals) — see also §6:

- **Settings:** `NopStartup.cs:276-286` — `FindClassesOfType(typeof(ISettings), false)`
  + factory lambda resolving via `ISettingService.LoadSettingAsync`.
- **Event consumers:** `NopStartup.cs:311-319` — `FindClassesOfType(typeof(IConsumer<>))`
  + per-interface registration via reflection over `FindInterfaces`.
- **`Lazy<>`:** `NopStartup.cs:326` — explains `Lazy<IInstallationService>` injection
  (`InstallController.cs:29,44`).

**Autofac alternate path exists but is not the default:** `Autofac.Extensions.DependencyInjection`
10.0.0 is referenced, a `UseAutofac` flag lives in `CommonConfig`, and `Program.cs`
branches on it. Default runs are MS-DI. F2 analysis should assume the MS-DI path and
note the alternate container as out of scope unless the brief says otherwise.

### 4.1 Ambiguous / duplicate registrations (production code, 8 interfaces)

Found by a corrected duplicate scan (an earlier scan undercounted due to a filter bug
matching only counts of exactly 2 or ≥10; the scan below is authoritative).

1. **`IStaticCacheManager` ×5 and `ICacheKeyService` ×5** — config switch in
   `NopStartup.cs:100-131` (Memory / SqlServer / Redis / RedisSynchronizedMemory
   branches + else). Mutually exclusive at runtime, 5 static candidates; lifetimes
   differ (Scoped vs Singleton). The same implementation classes serve both interfaces.
2. **`IThumbService` ×3** — core `ThumbService` (`NopStartup.cs:290`) + AzureBlob and
   CloudflareImages plugin factory lambdas with settings-conditional returns
   (`AzureBlob/.../PluginNopStartup.cs:20-35`, `CloudflareImages/.../PluginNopStartup.cs:22-36`).
3. **`ThumbService` (concrete) ×2** — both plugins register the core concrete type
   transiently so their factories can resolve/wrap it; same type, harmless duplicate.
4. **`IWorkflowMessageService` ×2** — core `WorkflowMessageService`
   (`NopStartup.cs:192`) + Brevo plugin `BrevoMessageService`
   (`Brevo/.../NopStartup.cs:30`). Genuine plugin-override ambiguity. (A third
   implementer, `RfqMessageService`, is registered only as its concrete type — §6.7.)
5. **`IRedisConnectionWrapper` ×2** — same implementation in two mutually exclusive
   switch branches.
6. **`INopAssetHelper` ×2** — early-return branch vs fall-through in
   `ServiceCollectionExtensions.cs:413,431` (bundling disabled vs enabled).
7. **`ILocker` ×2** — if/else on `distributedCacheConfig.Enabled`
   (`NopStartup.cs:125,129`), different implementations, config-conditional.

## 5. Ground-truth table — Catalog + Checkout slice

**Slice (confirmed against the real tree).** Five public (storefront) controllers in
`src/Presentation/Nop.Web/Controllers/`, **100 actions total**:

| Controller | Lines | Actions | Helpers (protected) |
|---|---|---|---|
| `CatalogController.cs` | 624 | 21 | 3 (`CheckCategory/Manufacturer/VendorAvailabilityAsync`, lines 562/588/614) |
| `ProductController.cs` | 585 | 13 | 0 |
| `CheckoutController.cs` | 2171 | 29 | 10 (lines 134–1445) |
| `OrderController.cs` | 309 | 12 | 0 (`CancelOrder` at line 218 is `public async` without `virtual`, hence 12 not 11) |
| `ShoppingCartController.cs` | 1916 | 25 | 4 (lines 173/298/337/442; `GetProductToCartDetailsAsync` at 337 is protected, not an action) |

Admin controllers in the same verticals exist (`Areas/Admin/Controllers/`: Category,
Product, Order, ShoppingCart, Manufacturer, ProductAttribute, ProductReview,
CheckoutAttribute) but are **outside** the hand-audit slice; the tool still scans them.

**Extraction method.** Script on box #4 (`/tmp/f2-extract.py`): member ranges by
4-space-indent member declarations; per action, all `_<field>.` call sites mapped to
injected types via `protected readonly` field declarations; `//`-comment lines excluded
(this mattered: `_pictureService` appeared only in commented-out code at
`CatalogController.cs:365,368`); helper-method calls recorded with one level of
attribution. All fields resolved, zero unmapped. Validated by reading source:
`ProductDetails` (`ProductController.cs:129`), `Category` (`CatalogController.cs:113`),
`Details` (`OrderController.cs:174`) match the extraction exactly. Repeated line numbers
in the tables (e.g. `@ 129, 129`) mean two call sites on the same line.

**69 distinct injected types** are invoked across the 100 actions (callsites include
one level of helper attribution; counts are call-site occurrences, not distinct methods):

```
114 callsites / 76 actions  IWorkContext
 97 callsites / 37 actions  ILocalizationService
 93 callsites / 49 actions  ICustomerService
 90 callsites / 47 actions  IShoppingCartService
 84 callsites / 59 actions  IStoreContext
 79 callsites / 26 actions  OrderSettings
 67 callsites / 31 actions  IGenericAttributeService
 57 callsites /  7 actions  IAttributeParser<CheckoutAttribute, CheckoutAttributeValue>
 32 callsites / 21 actions  ICheckoutModelFactory
 31 callsites / 21 actions  IProductService
 29 callsites / 15 actions  IOrderProcessingService
 27 callsites / 14 actions  IPermissionService
 25 callsites / 15 actions  CatalogSettings
 19 callsites / 17 actions  ICatalogModelFactory
 18 callsites / 12 actions  IWebHelper
 16 callsites / 12 actions  ShippingSettings
 16 callsites /  4 actions  IProductAttributeParser
 16 callsites / 10 actions  IAddressService
 16 callsites /  8 actions  IAttributeService<CheckoutAttribute, CheckoutAttributeValue>
 15 callsites /  8 actions  ShoppingCartSettings
 15 callsites / 13 actions  IOrderService
 14 callsites / 14 actions  IShoppingCartModelFactory
 13 callsites /  9 actions  IProductModelFactory
 12 callsites /  6 actions  IAttributeParser<AddressAttribute, AddressAttributeValue>
 12 callsites / 12 actions  ILogger
 11 callsites /  9 actions  IDownloadService
 10 callsites /  5 actions  CaptchaSettings
  8 callsites /  6 actions  ICustomerActivityService
  8 callsites /  4 actions  INotificationService
  8 callsites /  8 actions  IOrderModelFactory
  8 callsites /  6 actions  ICustomWishlistService
  7 callsites /  5 actions  IShippingService
  6 callsites /  6 actions  IAclService
  6 callsites /  6 actions  IStoreMappingService
  6 callsites /  5 actions  INopUrlHelper
  6 callsites /  2 actions  IProductReviewService
  6 callsites /  5 actions  AddressSettings
  6 callsites /  6 actions  PaymentSettings
  6 callsites /  3 actions  IProductAttributeService
  6 callsites /  2 actions  INopFileProvider
  5 callsites /  5 actions  IPaymentPluginManager
  5 callsites /  4 actions  IPaymentService
  4 callsites /  4 actions  ICompareProductsService
  4 callsites /  2 actions  TaxSettings
  3 callsites /  3 actions  ICategoryService
  3 callsites /  3 actions  IVendorService
  3 callsites /  2 actions  CustomerSettings
  3 callsites /  3 actions  ITaxService
  3 callsites /  3 actions  RewardPointsSettings
  3 callsites /  1 actions  IPictureService
  3 callsites /  2 actions  IDiscountService
  3 callsites /  2 actions  IGiftCardService
  2 callsites /  2 actions  IManufacturerService
  2 callsites /  2 actions  IProductTagService
  2 callsites /  2 actions  MediaSettings
  2 callsites /  2 actions  IRecentlyViewedProductsService
  2 callsites /  2 actions  IWorkflowMessageService
  2 callsites /  2 actions  IHtmlFormatter
  2 callsites /  2 actions  ICountryService
  2 callsites /  1 actions  IPriceFormatter
  2 callsites /  1 actions  IStaticCacheManager
  1 callsites /  1 actions  VendorSettings
  1 callsites /  1 actions  IFilterLevelValueService
  1 callsites /  1 actions  FilterLevelSettings
  1 callsites /  1 actions  IFilterLevelValueModelFactory
  1 callsites /  1 actions  IAddressModelFactory
  1 callsites /  1 actions  IPdfService
  1 callsites /  1 actions  IShipmentService
  1 callsites /  1 actions  ICurrencyService
```

**Table B method.** Script on box #4 (`/tmp/f2-tableb.py`): for each of the 56
`I*` interfaces invoked in Table A, the implementation was found by class-declaration
search (base list mentions the interface, transitive through inheritance) and its ctor
params parsed. All 56 resolved to ≥1 implementation. Core-service ctor parsing was
verified by reading the sources.

**Key Table B findings (read the code, do not guess):**

| Service | Implementation (ctor line) | `IRepository<T>` injections | Character |
|---|---|---|---|
| `IProductService` | `ProductService` (`Nop.Services/Catalog/ProductService.cs`, ctor 70) | **20** (lines 78–97) | Widest data surface in the slice |
| `ICategoryService` | `CategoryService` (ctor 38) | 4 (lines 41–44) | |
| `IOrderService` | `OrderService` (ctor 41) | **9** (lines 43–51) | |
| `IShoppingCartService` | `ShoppingCartService` (ctor 72) | 1 (`IRepository<ShoppingCartItem>`, line 91) | |
| `IOrderProcessingService` | `OrderProcessingService` (ctor 96) | **0** | Pure orchestrator over 41 other services; checkout data access flows through `OrderService` / `ShoppingCartService` / `ProductService` |

**Factory-chain finding.** Factories hold **no repositories**; they hold services. The
chain is controller → factory → service → repository. E.g. `CheckoutModelFactory`
(ctor line 64) holds 22 service-typed deps including `IOrderProcessingService`,
`IShoppingCartService`, `IPaymentService`, `IShippingService`, `ITaxService` — and no
repository. A resolver that only follows controller→service edges will miss most of
the slice's data flow.

**Namesake warning.** Public vs admin factory namesakes share simple names in
different namespaces (`Nop.Web.Factories` vs `Nop.Web.Areas.Admin.Factories`).
Registrations are namespace-qualified (e.g. `Factories.ICatalogModelFactory` at
`Nop.Web/Infrastructure/NopStartup.cs:93-94, 100-101, 103, 106, 108, 111`; admin
factories at lines 25–88). Slice controllers resolve factory interfaces to the
**public** `Nop.Web.Factories` implementations (verified via `using` directives).
There are also **two distinct `IWidgetModelFactory` interfaces** (Admin
`Areas/Admin/Factories/IWidgetModelFactory.cs:8` and Framework
`Nop.Web.Framework/Factories/IWidgetModelFactory.cs:8`), both registered — a genuine
same-name/different-namespace case.

**Cache-manager inheritance (for the resolvesTo picture).** 5 concrete implementations
via inheritance: `SynchronizedMemoryCacheManager : MemoryCacheManager`;
`MemoryDistributedCacheManager` / `RedisCacheManager` / `MsSqlServerCacheManager :
DistributedCacheManager` (abstract) `: CacheKeyService, IStaticCacheManager`;
`MemoryCacheManager : CacheKeyService, IStaticCacheManager`.

The full tables follow.

## Table A — controller actions: direct dependency calls (100 actions, 5 controllers)

Method: mechanical extraction from the controller sources (member ranges by declaration line; `_<field>.` occurrences per action, comment lines excluded; field types from `readonly` declarations), validated by reading sample actions against the source (ProductDetails, Category, Details matched exactly). Helper ranges are the `protected` methods listed per controller; calls made inside a helper are shown under that helper, attributed to the calling action.

### CatalogController.cs (21 actions)

| Action | Line | Route / verb attrs | Direct calls: Type (field) @ lines | Via helpers |
|---|---|---|---|---|
| Category | src/Presentation/Nop.Web/Controllers/CatalogController.cs:113 | - | ICategoryService (_categoryService) @ 115; IStoreContext (_storeContext) @ 120; IGenericAttributeService (_genericAttributeService) @ 123; IWorkContext (_workContext) @ 123; IWebHelper (_webHelper) @ 125; IPermissionService (_permissionService) @ 129, 129; ICustomerActivityService (_customerActivityService) @ 133; ILocalizationService (_localizationService) @ 134; ICatalogModelFactory (_catalogModelFactory) @ 137, 140 | CheckCategoryAvailabilityAsync (:562, called @117) -> IAclService (_aclService) @ 576; IStoreMappingService (_storeMappingService) @ 578; IPermissionService (_permissionService) @ 581, 581 |
| GetCategoryProducts | src/Presentation/Nop.Web/Controllers/CatalogController.cs:145 | [HttpPost] | ICategoryService (_categoryService) @ 147; ICatalogModelFactory (_catalogModelFactory) @ 152 | CheckCategoryAvailabilityAsync (:562, called @149) -> IAclService (_aclService) @ 576; IStoreMappingService (_storeMappingService) @ 578; IPermissionService (_permissionService) @ 581, 581 |
| Manufacturer | src/Presentation/Nop.Web/Controllers/CatalogController.cs:161 | - | IManufacturerService (_manufacturerService) @ 163; IStoreContext (_storeContext) @ 168; IGenericAttributeService (_genericAttributeService) @ 171; IWorkContext (_workContext) @ 171; IWebHelper (_webHelper) @ 173; IPermissionService (_permissionService) @ 177, 177; ICustomerActivityService (_customerActivityService) @ 181; ILocalizationService (_localizationService) @ 182; ICatalogModelFactory (_catalogModelFactory) @ 185, 188 | CheckManufacturerAvailabilityAsync (:588, called @165) -> IAclService (_aclService) @ 602; IStoreMappingService (_storeMappingService) @ 604; IPermissionService (_permissionService) @ 607, 607 |
| GetManufacturerProducts | src/Presentation/Nop.Web/Controllers/CatalogController.cs:194 | [HttpPost] | IManufacturerService (_manufacturerService) @ 196; ICatalogModelFactory (_catalogModelFactory) @ 201 | CheckManufacturerAvailabilityAsync (:588, called @198) -> IAclService (_aclService) @ 602; IStoreMappingService (_storeMappingService) @ 604; IPermissionService (_permissionService) @ 607, 607 |
| ManufacturerAll | src/Presentation/Nop.Web/Controllers/CatalogController.cs:206 | - | ICatalogModelFactory (_catalogModelFactory) @ 208 | - |
| Vendor | src/Presentation/Nop.Web/Controllers/CatalogController.cs:217 | - | IVendorService (_vendorService) @ 219; IStoreContext (_storeContext) @ 224; IGenericAttributeService (_genericAttributeService) @ 227; IWorkContext (_workContext) @ 227; IWebHelper (_webHelper) @ 229; IPermissionService (_permissionService) @ 233, 233; ICatalogModelFactory (_catalogModelFactory) @ 237 | CheckVendorAvailabilityAsync (:614, called @221) -> no field calls |
| GetVendorProducts | src/Presentation/Nop.Web/Controllers/CatalogController.cs:243 | [HttpPost] | IVendorService (_vendorService) @ 245; ICatalogModelFactory (_catalogModelFactory) @ 250 | CheckVendorAvailabilityAsync (:614, called @247) -> no field calls |
| VendorReviews | src/Presentation/Nop.Web/Controllers/CatalogController.cs:255 | - | IVendorService (_vendorService) @ 257; ICatalogModelFactory (_catalogModelFactory) @ 262 | CheckVendorAvailabilityAsync (:614, called @259) -> no field calls |
| VendorAll | src/Presentation/Nop.Web/Controllers/CatalogController.cs:267 | - | VendorSettings (_vendorSettings) @ 270; ICatalogModelFactory (_catalogModelFactory) @ 273 | - |
| ProductsByTag | src/Presentation/Nop.Web/Controllers/CatalogController.cs:281 | - | IProductTagService (_productTagService) @ 283; ICatalogModelFactory (_catalogModelFactory) @ 287 | - |
| GetTagProducts | src/Presentation/Nop.Web/Controllers/CatalogController.cs:293 | [HttpPost] | IProductTagService (_productTagService) @ 295; ICatalogModelFactory (_catalogModelFactory) @ 299 | - |
| ProductTagsAll | src/Presentation/Nop.Web/Controllers/CatalogController.cs:304 | - | ICatalogModelFactory (_catalogModelFactory) @ 306 | - |
| NewProducts | src/Presentation/Nop.Web/Controllers/CatalogController.cs:315 | - | CatalogSettings (_catalogSettings) @ 317; ICatalogModelFactory (_catalogModelFactory) @ 322 | - |
| GetNewProducts | src/Presentation/Nop.Web/Controllers/CatalogController.cs:329 | [HttpPost] | CatalogSettings (_catalogSettings) @ 331; ICatalogModelFactory (_catalogModelFactory) @ 334 | - |
| NewProductsRss | src/Presentation/Nop.Web/Controllers/CatalogController.cs:340 | [CheckLanguageSeoCode(ignore: true)] | IStoreContext (_storeContext) @ 342; ILocalizationService (_localizationService) @ 344, 360, 361; IWebHelper (_webHelper) @ 346, 350, 359, 374; CatalogSettings (_catalogSettings) @ 349; IProductService (_productService) @ 355; INopUrlHelper (_nopUrlHelper) @ 359 | - |
| Search | src/Presentation/Nop.Web/Controllers/CatalogController.cs:381 | - | IStoreContext (_storeContext) @ 383; IGenericAttributeService (_genericAttributeService) @ 386; IWorkContext (_workContext) @ 386; IWebHelper (_webHelper) @ 388; ICatalogModelFactory (_catalogModelFactory) @ 394 | - |
| SearchTermAutoComplete | src/Presentation/Nop.Web/Controllers/CatalogController.cs:400 | [CheckLanguageSeoCode(ignore: true)] | CatalogSettings (_catalogSettings) @ 407, 411, 412, 427, 429; IStoreContext (_storeContext) @ 413; ICategoryService (_categoryService) @ 417; IProductService (_productService) @ 419; IWorkContext (_workContext) @ 423; IProductModelFactory (_productModelFactory) @ 429; MediaSettings (_mediaSettings) @ 429; INopUrlHelper (_nopUrlHelper) @ 434 | - |
| SearchProducts | src/Presentation/Nop.Web/Controllers/CatalogController.cs:443 | [HttpPost] | ICatalogModelFactory (_catalogModelFactory) @ 448 | - |
| GetFilterLevelValues | src/Presentation/Nop.Web/Controllers/CatalogController.cs:461 | [CheckLanguageSeoCode(ignore: true)] | IFilterLevelValueService (_filterLevelValueService) @ 463; ILocalizationService (_localizationService) @ 466 | - |
| SearchByFilterLevelValues | src/Presentation/Nop.Web/Controllers/CatalogController.cs:526 | - | FilterLevelSettings (_filterLevelSettings) @ 528; IStoreContext (_storeContext) @ 531; IGenericAttributeService (_genericAttributeService) @ 534; IWorkContext (_workContext) @ 534; IWebHelper (_webHelper) @ 536; IFilterLevelValueModelFactory (_filterLevelValueModelFactory) @ 542 | - |
| SearchProductsByFilterLevelValues | src/Presentation/Nop.Web/Controllers/CatalogController.cs:548 | [HttpPost] | ICatalogModelFactory (_catalogModelFactory) @ 553 | - |

Helpers in CatalogController.cs:

- `CheckCategoryAvailabilityAsync` (src/Presentation/Nop.Web/Controllers/CatalogController.cs:562): IAclService (_aclService) @ 576; IStoreMappingService (_storeMappingService) @ 578; IPermissionService (_permissionService) @ 581, 581
- `CheckManufacturerAvailabilityAsync` (src/Presentation/Nop.Web/Controllers/CatalogController.cs:588): IAclService (_aclService) @ 602; IStoreMappingService (_storeMappingService) @ 604; IPermissionService (_permissionService) @ 607, 607
- `CheckVendorAvailabilityAsync` (src/Presentation/Nop.Web/Controllers/CatalogController.cs:614): no field calls

### ProductController.cs (13 actions)

| Action | Line | Route / verb attrs | Direct calls: Type (field) @ lines | Via helpers |
|---|---|---|---|---|
| ProductDetails | src/Presentation/Nop.Web/Controllers/ProductController.cs:129 | - | IProductService (_productService) @ 131, 143, 154; CatalogSettings (_catalogSettings) @ 137; IAclService (_aclService) @ 139; IStoreMappingService (_storeMappingService) @ 141; IPermissionService (_permissionService) @ 146, 146, 184, 185; INopUrlHelper (_nopUrlHelper) @ 158, 166; ShoppingCartSettings (_shoppingCartSettings) @ 164; IStoreContext (_storeContext) @ 167; IShoppingCartService (_shoppingCartService) @ 168; IWorkContext (_workContext) @ 168, 188; IRecentlyViewedProductsService (_recentlyViewedProductsService) @ 181; ICustomerActivityService (_customerActivityService) @ 196; ILocalizationService (_localizationService) @ 197; IProductModelFactory (_productModelFactory) @ 200, 202 | - |
| EstimateShipping | src/Presentation/Nop.Web/Controllers/ProductController.cs:208 | [HttpPost] | ShippingSettings (_shippingSettings) @ 215, 218; ILocalizationService (_localizationService) @ 216, 219, 222, 234; IProductService (_productService) @ 231; IStoreContext (_storeContext) @ 242; IWorkContext (_workContext) @ 243; IProductAttributeParser (_productAttributeParser) @ 256, 259, 262, 265; IShoppingCartModelFactory (_shoppingCartModelFactory) @ 269 | - |
| GetProductCombinations | src/Presentation/Nop.Web/Controllers/ProductController.cs:276 | [CheckLanguageSeoCode(ignore: true)] | IProductService (_productService) @ 278; IProductModelFactory (_productModelFactory) @ 282 | - |
| RecentlyViewedProducts | src/Presentation/Nop.Web/Controllers/ProductController.cs:290 | - | CatalogSettings (_catalogSettings) @ 292, 295; IRecentlyViewedProductsService (_recentlyViewedProductsService) @ 295; IProductModelFactory (_productModelFactory) @ 298 | - |
| ProductReviewsAdd | src/Presentation/Nop.Web/Controllers/ProductController.cs:309 | [HttpPost, ActionName("ProductReviews")] [ValidateCaptcha] | CaptchaSettings (_captchaSettings) @ 312, 312; ILocalizationService (_localizationService) @ 313, 364, 366; IProductService (_productService) @ 315; IStoreContext (_storeContext) @ 316; IProductReviewService (_productReviewService) @ 319, 322, 357; CatalogSettings (_catalogSettings) @ 319, 331, 333; IWorkContext (_workContext) @ 334; IProductModelFactory (_productModelFactory) @ 359, 377, 379; INotificationService (_notificationService) @ 364, 366; INopUrlHelper (_nopUrlHelper) @ 368 | - |
| SetProductReviewHelpfulness | src/Presentation/Nop.Web/Controllers/ProductController.cs:385 | [HttpPost] | IProductReviewService (_productReviewService) @ 387, 411, 414; IWorkContext (_workContext) @ 389; ICustomerService (_customerService) @ 390; CatalogSettings (_catalogSettings) @ 390; ILocalizationService (_localizationService) @ 394, 405, 418 | - |
| CustomerProductReviews | src/Presentation/Nop.Web/Controllers/ProductController.cs:424 | - | ICustomerService (_customerService) @ 426; IWorkContext (_workContext) @ 426; CatalogSettings (_catalogSettings) @ 429; IProductModelFactory (_productModelFactory) @ 434 | - |
| ProductEmailAFriend | src/Presentation/Nop.Web/Controllers/ProductController.cs:443 | - | IProductService (_productService) @ 445; CatalogSettings (_catalogSettings) @ 446; IProductModelFactory (_productModelFactory) @ 450 | - |
| ProductEmailAFriendSend | src/Presentation/Nop.Web/Controllers/ProductController.cs:457 | [HttpPost, ActionName("ProductEmailAFriend")] [FormValueRequired("send-email")] [ValidateCaptcha] | IProductService (_productService) @ 459; CatalogSettings (_catalogSettings) @ 460, 471; CaptchaSettings (_captchaSettings) @ 464, 464; ILocalizationService (_localizationService) @ 466, 473, 486; IWorkContext (_workContext) @ 470, 480; ICustomerService (_customerService) @ 471; IWorkflowMessageService (_workflowMessageService) @ 479; IHtmlFormatter (_htmlFormatter) @ 482; IProductModelFactory (_productModelFactory) @ 484, 492 | - |
| AddProductToCompareList | src/Presentation/Nop.Web/Controllers/ProductController.cs:501 | [HttpPost] | IProductService (_productService) @ 503; CatalogSettings (_catalogSettings) @ 511; ICompareProductsService (_compareProductsService) @ 518; ICustomerActivityService (_customerActivityService) @ 521; ILocalizationService (_localizationService) @ 522, 527 | - |
| RemoveProductFromCompareList | src/Presentation/Nop.Web/Controllers/ProductController.cs:533 | - | IProductService (_productService) @ 535; CatalogSettings (_catalogSettings) @ 539; ICompareProductsService (_compareProductsService) @ 542 | - |
| CompareProducts | src/Presentation/Nop.Web/Controllers/ProductController.cs:547 | - | CatalogSettings (_catalogSettings) @ 549, 554, 555; ICompareProductsService (_compareProductsService) @ 558; IAclService (_aclService) @ 560; IStoreMappingService (_storeMappingService) @ 560; IProductService (_productService) @ 562; IProductModelFactory (_productModelFactory) @ 565 | - |
| ClearCompareList | src/Presentation/Nop.Web/Controllers/ProductController.cs:575 | - | CatalogSettings (_catalogSettings) @ 577; ICompareProductsService (_compareProductsService) @ 580 | - |

### CheckoutController.cs (29 actions)

| Action | Line | Route / verb attrs | Direct calls: Type (field) @ lines | Via helpers |
|---|---|---|---|---|
| Index | src/Presentation/Nop.Web/Controllers/CheckoutController.cs:316 | - | OrderSettings (_orderSettings) @ 319, 333, 382; IWorkContext (_workContext) @ 322; IStoreContext (_storeContext) @ 323; IShoppingCartService (_shoppingCartService) @ 324, 359, 367; CustomerSettings (_customerSettings) @ 331; IProductService (_productService) @ 331, 365; ICustomerService (_customerService) @ 333, 354; IGenericAttributeService (_genericAttributeService) @ 357 | - |
| Completed | src/Presentation/Nop.Web/Controllers/CheckoutController.cs:388 | - | IWorkContext (_workContext) @ 391; ICustomerService (_customerService) @ 392; OrderSettings (_orderSettings) @ 392, 414; IOrderService (_orderService) @ 399, 404; IStoreContext (_storeContext) @ 403; ICheckoutModelFactory (_checkoutModelFactory) @ 420 | - |
| GetAddressById | src/Presentation/Nop.Web/Controllers/CheckoutController.cs:428 | - | IWorkContext (_workContext) @ 430; ICustomerService (_customerService) @ 435; IAddressModelFactory (_addressModelFactory) @ 441 | - |
| SaveEditBillingAddress | src/Presentation/Nop.Web/Controllers/CheckoutController.cs:464 | - | ICustomerService (_customerService) @ 469; ICheckoutModelFactory (_checkoutModelFactory) @ 475 | EditAddressAsync (:242, called @466) -> IWorkContext (_workContext) @ 254, 280; IStoreContext (_storeContext) @ 255; IShoppingCartService (_shoppingCartService) @ 256; ICustomerService (_customerService) @ 261; IAttributeParser<AddressAttribute, AddressAttributeValue> (_addressAttributeParser) @ 265, 266; IAddressService (_addressService) @ 274; ILogger (_logger) @ 280 |
| DeleteEditBillingAddress | src/Presentation/Nop.Web/Controllers/CheckoutController.cs:495 | - | ICheckoutModelFactory (_checkoutModelFactory) @ 503 | DeleteAddressAsync (:285, called @497) -> IWorkContext (_workContext) @ 289, 307; IStoreContext (_storeContext) @ 290; IShoppingCartService (_shoppingCartService) @ 291; ICustomerService (_customerService) @ 295, 298, 299; IAddressService (_addressService) @ 300; ILogger (_logger) @ 307 |
| DeleteEditShippingAddress | src/Presentation/Nop.Web/Controllers/CheckoutController.cs:521 | - | ICheckoutModelFactory (_checkoutModelFactory) @ 529 | DeleteAddressAsync (:285, called @523) -> IWorkContext (_workContext) @ 289, 307; IStoreContext (_storeContext) @ 290; IShoppingCartService (_shoppingCartService) @ 291; ICustomerService (_customerService) @ 295, 298, 299; IAddressService (_addressService) @ 300; ILogger (_logger) @ 307 |
| SaveEditShippingAddress | src/Presentation/Nop.Web/Controllers/CheckoutController.cs:548 | - | ICustomerService (_customerService) @ 553; ICheckoutModelFactory (_checkoutModelFactory) @ 562 | EditAddressAsync (:242, called @550) -> IWorkContext (_workContext) @ 254, 280; IStoreContext (_storeContext) @ 255; IShoppingCartService (_shoppingCartService) @ 256; ICustomerService (_customerService) @ 261; IAttributeParser<AddressAttribute, AddressAttributeValue> (_addressAttributeParser) @ 265, 266; IAddressService (_addressService) @ 274; ILogger (_logger) @ 280 |
| BillingAddress | src/Presentation/Nop.Web/Controllers/CheckoutController.cs:580 | - | OrderSettings (_orderSettings) @ 583, 593, 596, 604; IWorkContext (_workContext) @ 586; IStoreContext (_storeContext) @ 587; IShoppingCartService (_shoppingCartService) @ 588; ICustomerService (_customerService) @ 596; ICheckoutModelFactory (_checkoutModelFactory) @ 601 | - |
| SelectBillingAddress | src/Presentation/Nop.Web/Controllers/CheckoutController.cs:620 | - | OrderSettings (_orderSettings) @ 623; IWorkContext (_workContext) @ 626; ICustomerService (_customerService) @ 627, 633, 644; IStoreContext (_storeContext) @ 635; IShoppingCartService (_shoppingCartService) @ 636, 641; AddressSettings (_addressSettings) @ 640; ICountryService (_countryService) @ 640; ShippingSettings (_shippingSettings) @ 641; IGenericAttributeService (_genericAttributeService) @ 646, 647 | - |
| NewBillingAddress | src/Presentation/Nop.Web/Controllers/CheckoutController.cs:657 | [HttpPost, ActionName("BillingAddress")] [FormValueRequired("nextstep")] | OrderSettings (_orderSettings) @ 660, 670, 673; IWorkContext (_workContext) @ 663; IStoreContext (_storeContext) @ 664; IShoppingCartService (_shoppingCartService) @ 665, 726; ICustomerService (_customerService) @ 673, 676, 696, 718, 723, 729; TaxSettings (_taxSettings) @ 676, 676; IAttributeParser<AddressAttribute, AddressAttributeValue> (_addressAttributeParser) @ 684, 685; IAddressService (_addressService) @ 696, 716; ShippingSettings (_shippingSettings) @ 726; IGenericAttributeService (_genericAttributeService) @ 732, 733; ICheckoutModelFactory (_checkoutModelFactory) @ 743 | SaveCustomerVatNumberAsync (:226, called @678) -> ITaxService (_taxService) @ 228; ICustomerService (_customerService) @ 231; ILocalizationService (_localizationService) @ 235, 236 |
| ShippingAddress | src/Presentation/Nop.Web/Controllers/CheckoutController.cs:750 | - | OrderSettings (_orderSettings) @ 753, 763, 766; IWorkContext (_workContext) @ 756; IStoreContext (_storeContext) @ 757; IShoppingCartService (_shoppingCartService) @ 758, 769; ICustomerService (_customerService) @ 766; ICheckoutModelFactory (_checkoutModelFactory) @ 774 | - |
| SelectShippingAddress | src/Presentation/Nop.Web/Controllers/CheckoutController.cs:779 | - | OrderSettings (_orderSettings) @ 782; IWorkContext (_workContext) @ 785; ICustomerService (_customerService) @ 786, 792; ShippingSettings (_shippingSettings) @ 794; IStoreContext (_storeContext) @ 796; IGenericAttributeService (_genericAttributeService) @ 798 | - |
| NewShippingAddress | src/Presentation/Nop.Web/Controllers/CheckoutController.cs:806 | [HttpPost, ActionName("ShippingAddress")] [FormValueRequired("nextstep")] | OrderSettings (_orderSettings) @ 809, 819, 822, 829; IWorkContext (_workContext) @ 812; IStoreContext (_storeContext) @ 813; IShoppingCartService (_shoppingCartService) @ 814, 825; ICustomerService (_customerService) @ 822, 857, 877, 881; ShippingSettings (_shippingSettings) @ 829; IGenericAttributeService (_genericAttributeService) @ 841; IAttributeParser<AddressAttribute, AddressAttributeValue> (_addressAttributeParser) @ 845, 846; IAddressService (_addressService) @ 857, 875; ICheckoutModelFactory (_checkoutModelFactory) @ 887 | ParsePickupInStore (:157, called @831) -> no field calls; ParsePickupOptionAsync (:176, called @834) -> IWorkContext (_workContext) @ 180; IStoreContext (_storeContext) @ 181; IAddressService (_addressService) @ 183; IShippingService (_shippingService) @ 186; SavePickupOptionAsync (:197, called @835) -> ILocalizationService (_localizationService) @ 200, 201; IWorkContext (_workContext) @ 211; IStoreContext (_storeContext) @ 212; IGenericAttributeService (_genericAttributeService) @ 213, 214 |
| ShippingMethod | src/Presentation/Nop.Web/Controllers/CheckoutController.cs:894 | - | OrderSettings (_orderSettings) @ 897, 907, 910, 920; IWorkContext (_workContext) @ 900; IStoreContext (_storeContext) @ 901; IShoppingCartService (_shoppingCartService) @ 902, 913; ICustomerService (_customerService) @ 910, 929; IGenericAttributeService (_genericAttributeService) @ 915, 935; ICheckoutModelFactory (_checkoutModelFactory) @ 929; ShippingSettings (_shippingSettings) @ 931 | - |
| SelectShippingMethod | src/Presentation/Nop.Web/Controllers/CheckoutController.cs:948 | [HttpPost, ActionName("ShippingMethod")] [FormValueRequired("nextstep")] | OrderSettings (_orderSettings) @ 951, 961, 964, 975; IWorkContext (_workContext) @ 954; IStoreContext (_storeContext) @ 955; IShoppingCartService (_shoppingCartService) @ 956, 967; ICustomerService (_customerService) @ 964, 1006; IGenericAttributeService (_genericAttributeService) @ 969, 987, 1001, 1022; ShippingSettings (_shippingSettings) @ 975; IShippingService (_shippingService) @ 1006 | ParsePickupInStore (:157, called @977) -> no field calls; ParsePickupOptionAsync (:176, called @980) -> IWorkContext (_workContext) @ 180; IStoreContext (_storeContext) @ 181; IAddressService (_addressService) @ 183; IShippingService (_shippingService) @ 186; SavePickupOptionAsync (:197, called @981) -> ILocalizationService (_localizationService) @ 200, 201; IWorkContext (_workContext) @ 211; IStoreContext (_storeContext) @ 212; IGenericAttributeService (_genericAttributeService) @ 213, 214 |
| PaymentMethod | src/Presentation/Nop.Web/Controllers/CheckoutController.cs:1027 | - | OrderSettings (_orderSettings) @ 1030, 1040, 1043; IWorkContext (_workContext) @ 1033; IStoreContext (_storeContext) @ 1034; IShoppingCartService (_shoppingCartService) @ 1035; ICustomerService (_customerService) @ 1043, 1060; IOrderProcessingService (_orderProcessingService) @ 1048; IGenericAttributeService (_genericAttributeService) @ 1051, 1072; AddressSettings (_addressSettings) @ 1058; ICheckoutModelFactory (_checkoutModelFactory) @ 1064; PaymentSettings (_paymentSettings) @ 1066 | - |
| SelectPaymentMethod | src/Presentation/Nop.Web/Controllers/CheckoutController.cs:1084 | [HttpPost, ActionName("PaymentMethod")] [FormValueRequired("nextstep")] | OrderSettings (_orderSettings) @ 1087, 1097, 1100; IWorkContext (_workContext) @ 1090; IStoreContext (_storeContext) @ 1091; IShoppingCartService (_shoppingCartService) @ 1092; ICustomerService (_customerService) @ 1100; RewardPointsSettings (_rewardPointsSettings) @ 1104; IGenericAttributeService (_genericAttributeService) @ 1106, 1115, 1127; IOrderProcessingService (_orderProcessingService) @ 1112; IPaymentPluginManager (_paymentPluginManager) @ 1123 | - |
| PaymentInfo | src/Presentation/Nop.Web/Controllers/CheckoutController.cs:1133 | - | OrderSettings (_orderSettings) @ 1136, 1146, 1149; IWorkContext (_workContext) @ 1139; IStoreContext (_storeContext) @ 1140; IShoppingCartService (_shoppingCartService) @ 1141; ICustomerService (_customerService) @ 1149; IOrderProcessingService (_orderProcessingService) @ 1153, 1172; IGenericAttributeService (_genericAttributeService) @ 1160; PaymentSettings (_paymentSettings) @ 1169; ICheckoutModelFactory (_checkoutModelFactory) @ 1178 | - |
| EnterPaymentInfo | src/Presentation/Nop.Web/Controllers/CheckoutController.cs:1184 | [HttpPost, ActionName("PaymentInfo")] [FormValueRequired("nextstep")] | OrderSettings (_orderSettings) @ 1187, 1197, 1200; IWorkContext (_workContext) @ 1190; IStoreContext (_storeContext) @ 1191; IShoppingCartService (_shoppingCartService) @ 1192; ICustomerService (_customerService) @ 1200; IOrderProcessingService (_orderProcessingService) @ 1204, 1223; IGenericAttributeService (_genericAttributeService) @ 1211; ICheckoutModelFactory (_checkoutModelFactory) @ 1229 | - |
| Confirm | src/Presentation/Nop.Web/Controllers/CheckoutController.cs:1233 | - | OrderSettings (_orderSettings) @ 1236, 1246, 1249; IWorkContext (_workContext) @ 1239; IStoreContext (_storeContext) @ 1240; IShoppingCartService (_shoppingCartService) @ 1241; ICustomerService (_customerService) @ 1249; ICheckoutModelFactory (_checkoutModelFactory) @ 1253 | - |
| ConfirmOrder | src/Presentation/Nop.Web/Controllers/CheckoutController.cs:1259 | [ValidateCaptcha] [HttpPost, ActionName("Confirm")] | OrderSettings (_orderSettings) @ 1262, 1272, 1275; IWorkContext (_workContext) @ 1265; IStoreContext (_storeContext) @ 1266; IShoppingCartService (_shoppingCartService) @ 1267; ICustomerService (_customerService) @ 1275, 1281; ICheckoutModelFactory (_checkoutModelFactory) @ 1279; CaptchaSettings (_captchaSettings) @ 1282, 1282; ILocalizationService (_localizationService) @ 1287, 1295; IOrderProcessingService (_orderProcessingService) @ 1298, 1302, 1312, 1313, 1316; IGenericAttributeService (_genericAttributeService) @ 1310; IPaymentService (_paymentService) @ 1322; IWebHelper (_webHelper) @ 1324, 1324; ILogger (_logger) @ 1338 | IsMinimumOrderPlacementIntervalValidAsync (:134, called @1294) -> OrderSettings (_orderSettings) @ 137, 149; IStoreContext (_storeContext) @ 140; IOrderService (_orderService) @ 142 |
| OnePageCheckout | src/Presentation/Nop.Web/Controllers/CheckoutController.cs:1478 | - | OrderSettings (_orderSettings) @ 1481, 1491, 1494; IWorkContext (_workContext) @ 1484; IStoreContext (_storeContext) @ 1485; IShoppingCartService (_shoppingCartService) @ 1486; ICustomerService (_customerService) @ 1494; ICheckoutModelFactory (_checkoutModelFactory) @ 1497 | - |
| OpcSaveBilling | src/Presentation/Nop.Web/Controllers/CheckoutController.cs:1502 | [HttpPost] | OrderSettings (_orderSettings) @ 1507, 1517, 1520; ILocalizationService (_localizationService) @ 1508, 1529; IWorkContext (_workContext) @ 1510, 1652; IStoreContext (_storeContext) @ 1511; IShoppingCartService (_shoppingCartService) @ 1512, 1610; ICustomerService (_customerService) @ 1520, 1528, 1532, 1536, 1579, 1602, 1607, 1613, 1621; TaxSettings (_taxSettings) @ 1536, 1536; IAttributeParser<AddressAttribute, AddressAttributeValue> (_addressAttributeParser) @ 1547, 1548; ICheckoutModelFactory (_checkoutModelFactory) @ 1559, 1631; IAddressService (_addressService) @ 1579, 1600; AddressSettings (_addressSettings) @ 1616; ICountryService (_countryService) @ 1616; ShippingSettings (_shippingSettings) @ 1617; IGenericAttributeService (_genericAttributeService) @ 1623, 1624, 1645; ILogger (_logger) @ 1652 | OpcLoadStepAfterShippingAddress (:1350, called @1626) -> IWorkContext (_workContext) @ 1352; ICheckoutModelFactory (_checkoutModelFactory) @ 1353; ICustomerService (_customerService) @ 1353; ShippingSettings (_shippingSettings) @ 1354; IStoreContext (_storeContext) @ 1357; IGenericAttributeService (_genericAttributeService) @ 1359; OpcLoadStepAfterShippingMethod (:1379, called @1648) -> IWorkContext (_workContext) @ 1383; IStoreContext (_storeContext) @ 1384; IOrderProcessingService (_orderProcessingService) @ 1385; AddressSettings (_addressSettings) @ 1390; ICustomerService (_customerService) @ 1392; ICheckoutModelFactory (_checkoutModelFactory) @ 1396, 1433; PaymentSettings (_paymentSettings) @ 1398; IGenericAttributeService (_genericAttributeService) @ 1405, 1430; IPaymentPluginManager (_paymentPluginManager) @ 1411; SaveCustomerVatNumberAsync (:226, called @1538) -> ITaxService (_taxService) @ 228; ICustomerService (_customerService) @ 231; ILocalizationService (_localizationService) @ 235, 236 |
| OpcSaveShipping | src/Presentation/Nop.Web/Controllers/CheckoutController.cs:1658 | [HttpPost] | OrderSettings (_orderSettings) @ 1663, 1673, 1676, 1683; ILocalizationService (_localizationService) @ 1664, 1704; IWorkContext (_workContext) @ 1666, 1773; IStoreContext (_storeContext) @ 1667; IShoppingCartService (_shoppingCartService) @ 1668, 1679; ICustomerService (_customerService) @ 1676, 1703, 1707, 1746, 1761, 1766; ShippingSettings (_shippingSettings) @ 1683; IGenericAttributeService (_genericAttributeService) @ 1695; IAttributeParser<AddressAttribute, AddressAttributeValue> (_addressAttributeParser) @ 1715, 1716; ICheckoutModelFactory (_checkoutModelFactory) @ 1727; IAddressService (_addressService) @ 1746, 1759; ILogger (_logger) @ 1773 | OpcLoadStepAfterShippingAddress (:1350, called @1769) -> IWorkContext (_workContext) @ 1352; ICheckoutModelFactory (_checkoutModelFactory) @ 1353; ICustomerService (_customerService) @ 1353; ShippingSettings (_shippingSettings) @ 1354; IStoreContext (_storeContext) @ 1357; IGenericAttributeService (_genericAttributeService) @ 1359; OpcLoadStepAfterShippingMethod (:1379, called @1691) -> IWorkContext (_workContext) @ 1383; IStoreContext (_storeContext) @ 1384; IOrderProcessingService (_orderProcessingService) @ 1385; AddressSettings (_addressSettings) @ 1390; ICustomerService (_customerService) @ 1392; ICheckoutModelFactory (_checkoutModelFactory) @ 1396, 1433; PaymentSettings (_paymentSettings) @ 1398; IGenericAttributeService (_genericAttributeService) @ 1405, 1430; IPaymentPluginManager (_paymentPluginManager) @ 1411; ParsePickupInStore (:157, called @1685) -> no field calls; ParsePickupOptionAsync (:176, called @1688) -> IWorkContext (_workContext) @ 180; IStoreContext (_storeContext) @ 181; IAddressService (_addressService) @ 183; IShippingService (_shippingService) @ 186; SavePickupOptionAsync (:197, called @1689) -> ILocalizationService (_localizationService) @ 200, 201; IWorkContext (_workContext) @ 211; IStoreContext (_storeContext) @ 212; IGenericAttributeService (_genericAttributeService) @ 213, 214 |
| OpcSaveShippingMethod | src/Presentation/Nop.Web/Controllers/CheckoutController.cs:1779 | [HttpPost] | OrderSettings (_orderSettings) @ 1784, 1794, 1797, 1804; ILocalizationService (_localizationService) @ 1785; IWorkContext (_workContext) @ 1787, 1856; IStoreContext (_storeContext) @ 1788; IShoppingCartService (_shoppingCartService) @ 1789, 1800; ICustomerService (_customerService) @ 1797, 1835; ShippingSettings (_shippingSettings) @ 1804; IGenericAttributeService (_genericAttributeService) @ 1816, 1830, 1849; IShippingService (_shippingService) @ 1835; ILogger (_logger) @ 1856 | OpcLoadStepAfterShippingMethod (:1379, called @1812, 1852) -> IWorkContext (_workContext) @ 1383; IStoreContext (_storeContext) @ 1384; IOrderProcessingService (_orderProcessingService) @ 1385; AddressSettings (_addressSettings) @ 1390; ICustomerService (_customerService) @ 1392; ICheckoutModelFactory (_checkoutModelFactory) @ 1396, 1433; PaymentSettings (_paymentSettings) @ 1398; IGenericAttributeService (_genericAttributeService) @ 1405, 1430; IPaymentPluginManager (_paymentPluginManager) @ 1411; ParsePickupInStore (:157, called @1806) -> no field calls; ParsePickupOptionAsync (:176, called @1809) -> IWorkContext (_workContext) @ 180; IStoreContext (_storeContext) @ 181; IAddressService (_addressService) @ 183; IShippingService (_shippingService) @ 186; SavePickupOptionAsync (:197, called @1810) -> ILocalizationService (_localizationService) @ 200, 201; IWorkContext (_workContext) @ 211; IStoreContext (_storeContext) @ 212; IGenericAttributeService (_genericAttributeService) @ 213, 214 |
| OpcSavePaymentMethod | src/Presentation/Nop.Web/Controllers/CheckoutController.cs:1862 | [HttpPost] | OrderSettings (_orderSettings) @ 1867, 1877, 1880; ILocalizationService (_localizationService) @ 1868; IWorkContext (_workContext) @ 1870, 1928; IStoreContext (_storeContext) @ 1871; IShoppingCartService (_shoppingCartService) @ 1872; ICustomerService (_customerService) @ 1880; RewardPointsSettings (_rewardPointsSettings) @ 1888; IGenericAttributeService (_genericAttributeService) @ 1890, 1900, 1921; IOrderProcessingService (_orderProcessingService) @ 1896; ICheckoutModelFactory (_checkoutModelFactory) @ 1903; IPaymentPluginManager (_paymentPluginManager) @ 1917; ILogger (_logger) @ 1928 | OpcLoadStepAfterPaymentMethod (:1445, called @1924) -> PaymentSettings (_paymentSettings) @ 1448; IOrderProcessingService (_orderProcessingService) @ 1451; ICheckoutModelFactory (_checkoutModelFactory) @ 1453, 1466 |
| OpcSavePaymentInfo | src/Presentation/Nop.Web/Controllers/CheckoutController.cs:1935 | [HttpPost] [IgnoreAntiforgeryToken] | OrderSettings (_orderSettings) @ 1940, 1950, 1953; ILocalizationService (_localizationService) @ 1941; IWorkContext (_workContext) @ 1943, 1994; IStoreContext (_storeContext) @ 1944; IShoppingCartService (_shoppingCartService) @ 1945; ICustomerService (_customerService) @ 1953; IGenericAttributeService (_genericAttributeService) @ 1956; IOrderProcessingService (_orderProcessingService) @ 1967; ICheckoutModelFactory (_checkoutModelFactory) @ 1969, 1982; ILogger (_logger) @ 1994 | - |
| OpcConfirmOrder | src/Presentation/Nop.Web/Controllers/CheckoutController.cs:2001 | [ValidateCaptcha] [HttpPost] | IWorkContext (_workContext) @ 2005, 2110; ICustomerService (_customerService) @ 2007, 2031; CaptchaSettings (_captchaSettings) @ 2008, 2008; OrderSettings (_orderSettings) @ 2019, 2028, 2031; ILocalizationService (_localizationService) @ 2020, 2036, 2095; IStoreContext (_storeContext) @ 2022; IShoppingCartService (_shoppingCartService) @ 2023; IOrderProcessingService (_orderProcessingService) @ 2039, 2043, 2055, 2056, 2059; IGenericAttributeService (_genericAttributeService) @ 2053; IWebHelper (_webHelper) @ 2080; IPaymentService (_paymentService) @ 2084; ILogger (_logger) @ 2110 | IsMinimumOrderPlacementIntervalValidAsync (:134, called @2035) -> OrderSettings (_orderSettings) @ 137, 149; IStoreContext (_storeContext) @ 140; IOrderService (_orderService) @ 142 |
| OpcCompleteRedirectionPayment | src/Presentation/Nop.Web/Controllers/CheckoutController.cs:2115 | - | OrderSettings (_orderSettings) @ 2120, 2124; IWorkContext (_workContext) @ 2123, 2166; ICustomerService (_customerService) @ 2124; IStoreContext (_storeContext) @ 2128; IOrderService (_orderService) @ 2129; IPaymentService (_paymentService) @ 2152; IWebHelper (_webHelper) @ 2154, 2154; ILogger (_logger) @ 2166 | - |

Helpers in CheckoutController.cs:

- `IsMinimumOrderPlacementIntervalValidAsync` (src/Presentation/Nop.Web/Controllers/CheckoutController.cs:134): OrderSettings (_orderSettings) @ 137, 149; IStoreContext (_storeContext) @ 140; IOrderService (_orderService) @ 142
- `ParsePickupInStore` (src/Presentation/Nop.Web/Controllers/CheckoutController.cs:157): no field calls
- `ParsePickupOptionAsync` (src/Presentation/Nop.Web/Controllers/CheckoutController.cs:176): IWorkContext (_workContext) @ 180; IStoreContext (_storeContext) @ 181; IAddressService (_addressService) @ 183; IShippingService (_shippingService) @ 186
- `SavePickupOptionAsync` (src/Presentation/Nop.Web/Controllers/CheckoutController.cs:197): ILocalizationService (_localizationService) @ 200, 201; IWorkContext (_workContext) @ 211; IStoreContext (_storeContext) @ 212; IGenericAttributeService (_genericAttributeService) @ 213, 214
- `SaveCustomerVatNumberAsync` (src/Presentation/Nop.Web/Controllers/CheckoutController.cs:226): ITaxService (_taxService) @ 228; ICustomerService (_customerService) @ 231; ILocalizationService (_localizationService) @ 235, 236
- `EditAddressAsync` (src/Presentation/Nop.Web/Controllers/CheckoutController.cs:242): IWorkContext (_workContext) @ 254, 280; IStoreContext (_storeContext) @ 255; IShoppingCartService (_shoppingCartService) @ 256; ICustomerService (_customerService) @ 261; IAttributeParser<AddressAttribute, AddressAttributeValue> (_addressAttributeParser) @ 265, 266; IAddressService (_addressService) @ 274; ILogger (_logger) @ 280
- `DeleteAddressAsync` (src/Presentation/Nop.Web/Controllers/CheckoutController.cs:285): IWorkContext (_workContext) @ 289, 307; IStoreContext (_storeContext) @ 290; IShoppingCartService (_shoppingCartService) @ 291; ICustomerService (_customerService) @ 295, 298, 299; IAddressService (_addressService) @ 300; ILogger (_logger) @ 307
- `OpcLoadStepAfterShippingAddress` (src/Presentation/Nop.Web/Controllers/CheckoutController.cs:1350): IWorkContext (_workContext) @ 1352; ICheckoutModelFactory (_checkoutModelFactory) @ 1353; ICustomerService (_customerService) @ 1353; ShippingSettings (_shippingSettings) @ 1354; IStoreContext (_storeContext) @ 1357; IGenericAttributeService (_genericAttributeService) @ 1359
- `OpcLoadStepAfterShippingMethod` (src/Presentation/Nop.Web/Controllers/CheckoutController.cs:1379): IWorkContext (_workContext) @ 1383; IStoreContext (_storeContext) @ 1384; IOrderProcessingService (_orderProcessingService) @ 1385; AddressSettings (_addressSettings) @ 1390; ICustomerService (_customerService) @ 1392; ICheckoutModelFactory (_checkoutModelFactory) @ 1396, 1433; PaymentSettings (_paymentSettings) @ 1398; IGenericAttributeService (_genericAttributeService) @ 1405, 1430; IPaymentPluginManager (_paymentPluginManager) @ 1411
- `OpcLoadStepAfterPaymentMethod` (src/Presentation/Nop.Web/Controllers/CheckoutController.cs:1445): PaymentSettings (_paymentSettings) @ 1448; IOrderProcessingService (_orderProcessingService) @ 1451; ICheckoutModelFactory (_checkoutModelFactory) @ 1453, 1466

### OrderController.cs (12 actions)

| Action | Line | Route / verb attrs | Direct calls: Type (field) @ lines | Via helpers |
|---|---|---|---|---|
| CustomerOrders | src/Presentation/Nop.Web/Controllers/OrderController.cs:76 | - | ICustomerService (_customerService) @ 78; IWorkContext (_workContext) @ 78; IOrderModelFactory (_orderModelFactory) @ 81 | - |
| CustomerRecurringPayments | src/Presentation/Nop.Web/Controllers/OrderController.cs:86 | - | ICustomerService (_customerService) @ 88; IWorkContext (_workContext) @ 88; IOrderModelFactory (_orderModelFactory) @ 91 | - |
| CancelRecurringPayment | src/Presentation/Nop.Web/Controllers/OrderController.cs:98 | [HttpPost, ActionName("CustomerRecurringPayments")] [FormValueRequired(FormValueRequirement.StartsWith, "cancelRecurringPayment")] | IWorkContext (_workContext) @ 100; ICustomerService (_customerService) @ 101; IOrderService (_orderService) @ 110; IOrderProcessingService (_orderProcessingService) @ 116, 118; IOrderModelFactory (_orderModelFactory) @ 120 | - |
| RetryLastRecurringPayment | src/Presentation/Nop.Web/Controllers/OrderController.cs:132 | [HttpPost, ActionName("CustomerRecurringPayments")] [FormValueRequired(FormValueRequirement.StartsWith, "retryLastPayment")] | IWorkContext (_workContext) @ 134; ICustomerService (_customerService) @ 135; IOrderService (_orderService) @ 146; IOrderProcessingService (_orderProcessingService) @ 150, 153; IOrderModelFactory (_orderModelFactory) @ 154 | - |
| CustomerRewardPoints | src/Presentation/Nop.Web/Controllers/OrderController.cs:161 | - | ICustomerService (_customerService) @ 163; IWorkContext (_workContext) @ 163; RewardPointsSettings (_rewardPointsSettings) @ 166; IOrderModelFactory (_orderModelFactory) @ 169 | - |
| Details | src/Presentation/Nop.Web/Controllers/OrderController.cs:174 | - | IOrderService (_orderService) @ 176; IWorkContext (_workContext) @ 177; IOrderModelFactory (_orderModelFactory) @ 182 | - |
| PrintOrderDetails | src/Presentation/Nop.Web/Controllers/OrderController.cs:187 | - | IOrderService (_orderService) @ 189; IWorkContext (_workContext) @ 190; IOrderModelFactory (_orderModelFactory) @ 194 | - |
| GetPdfInvoice | src/Presentation/Nop.Web/Controllers/OrderController.cs:202 | [CheckLanguageSeoCode(ignore: true)] | IOrderService (_orderService) @ 204; IWorkContext (_workContext) @ 205, 212; IPdfService (_pdfService) @ 212; ILocalizationService (_localizationService) @ 215 | - |
| CancelOrder | src/Presentation/Nop.Web/Controllers/OrderController.cs:218 | - | OrderSettings (_orderSettings) @ 220; IOrderService (_orderService) @ 223, 239; IWorkContext (_workContext) @ 224; IOrderProcessingService (_orderProcessingService) @ 230, 231; INotificationService (_notificationService) @ 235, 240; ILocalizationService (_localizationService) @ 235, 240 | - |
| ReOrder | src/Presentation/Nop.Web/Controllers/OrderController.cs:246 | - | IOrderService (_orderService) @ 248; IWorkContext (_workContext) @ 249; IOrderProcessingService (_orderProcessingService) @ 253; INotificationService (_notificationService) @ 256; ILocalizationService (_localizationService) @ 256 | - |
| RePostPayment | src/Presentation/Nop.Web/Controllers/OrderController.cs:265 | [FormValueRequired("repost-payment")] | IOrderService (_orderService) @ 267; IWorkContext (_workContext) @ 268; IPaymentService (_paymentService) @ 272, 279; IWebHelper (_webHelper) @ 281, 281 | - |
| ShipmentDetails | src/Presentation/Nop.Web/Controllers/OrderController.cs:293 | - | IShipmentService (_shipmentService) @ 295; IOrderService (_orderService) @ 299; IWorkContext (_workContext) @ 300; IOrderModelFactory (_orderModelFactory) @ 305 | - |

### ShoppingCartController.cs (25 actions)

| Action | Line | Route / verb attrs | Direct calls: Type (field) @ lines | Via helpers |
|---|---|---|---|---|
| SelectShippingOption | src/Presentation/Nop.Web/Controllers/ShoppingCartController.cs:466 | [HttpPost] | ShippingSettings (_shippingSettings) @ 472; ILocalizationService (_localizationService) @ 473, 476, 524; IWorkContext (_workContext) @ 485; IStoreContext (_storeContext) @ 486; IShoppingCartService (_shoppingCartService) @ 487; IGenericAttributeService (_genericAttributeService) @ 498, 534, 538; IShippingService (_shippingService) @ 511 | ParseAndSaveCheckoutAttributesAsync (:173, called @489) -> IShoppingCartService (_shoppingCartService) @ 180; IStoreContext (_storeContext) @ 181; IAttributeService<CheckoutAttribute, CheckoutAttributeValue> (_checkoutAttributeService) @ 182, 223; IAttributeParser<CheckoutAttribute, CheckoutAttributeValue> (_checkoutAttributeParser) @ 198, 213, 229, 242, 264, 275, 289, 291; IDownloadService (_downloadService) @ 272; IGenericAttributeService (_genericAttributeService) @ 295; IWorkContext (_workContext) @ 295 |
| AddProductToCart_Catalog | src/Presentation/Nop.Web/Controllers/ShoppingCartController.cs:553 | [HttpPost] | IProductService (_productService) @ 558, 593; INopUrlHelper (_nopUrlHelper) @ 567; IProductAttributeService (_productAttributeService) @ 601, 611; IProductAttributeParser (_productAttributeParser) @ 617; IWorkContext (_workContext) @ 626; IStoreContext (_storeContext) @ 627; IShoppingCartService (_shoppingCartService) @ 628, 629, 648, 682, 728; ICustomerActivityService (_customerActivityService) @ 669, 715; ILocalizationService (_localizationService) @ 670, 684, 696, 705, 716, 730, 740; ShoppingCartSettings (_shoppingCartSettings) @ 672, 718, 733; ICustomWishlistService (_customWishlistService) @ 687; ICustomerService (_customerService) @ 688 | - |
| AddProductToCart_Details | src/Presentation/Nop.Web/Controllers/ShoppingCartController.cs:751 | [HttpPost] | IProductService (_productService) @ 753; ShoppingCartSettings (_shoppingCartSettings) @ 782; IStoreContext (_storeContext) @ 784; IShoppingCartService (_shoppingCartService) @ 786; IWorkContext (_workContext) @ 786; IProductAttributeParser (_productAttributeParser) @ 812, 815, 818, 821 | GetProductToCartDetailsAsync (:337, called @830) -> IWorkContext (_workContext) @ 352; IStoreContext (_storeContext) @ 353; ICustomerActivityService (_customerActivityService) @ 360, 407; ILocalizationService (_localizationService) @ 361, 376, 388, 397, 408, 423, 433; ShoppingCartSettings (_shoppingCartSettings) @ 363, 410, 426; IShoppingCartService (_shoppingCartService) @ 373, 420; ICustomWishlistService (_customWishlistService) @ 379; ICustomerService (_customerService) @ 380; SaveItemAsync (:298, called @827) -> IWorkContext (_workContext) @ 302; IStoreContext (_storeContext) @ 303; IShoppingCartService (_shoppingCartService) @ 307, 314, 316, 326, 332 |
| ProductDetails_AttributeChange | src/Presentation/Nop.Web/Controllers/ShoppingCartController.cs:836 | [HttpPost] | IProductService (_productService) @ 839, 855, 856, 857, 873, 903, 978; IProductAttributeParser (_productAttributeParser) @ 844, 851, 860, 913, 932, 943, 976; IPermissionService (_permissionService) @ 884; IStoreContext (_storeContext) @ 886, 953; IWorkContext (_workContext) @ 887, 897; IShoppingCartService (_shoppingCartService) @ 890; ITaxService (_taxService) @ 896; ICurrencyService (_currencyService) @ 897; IPriceFormatter (_priceFormatter) @ 898, 899; IProductAttributeService (_productAttributeService) @ 910, 935, 944; IStaticCacheManager (_staticCacheManager) @ 952, 954; IWebHelper (_webHelper) @ 953; IPictureService (_pictureService) @ 956, 959, 960; MediaSettings (_mediaSettings) @ 960 | - |
| CheckoutAttributeChange | src/Presentation/Nop.Web/Controllers/ShoppingCartController.cs:1002 | [HttpPost] | IWorkContext (_workContext) @ 1004; IStoreContext (_storeContext) @ 1005; IShoppingCartService (_shoppingCartService) @ 1006, 1016; IGenericAttributeService (_genericAttributeService) @ 1010; IAttributeService<CheckoutAttribute, CheckoutAttributeValue> (_checkoutAttributeService) @ 1017; IAttributeParser<CheckoutAttribute, CheckoutAttributeValue> (_checkoutAttributeParser) @ 1020 | ParseAndSaveCheckoutAttributesAsync (:173, called @1009) -> IShoppingCartService (_shoppingCartService) @ 180; IStoreContext (_storeContext) @ 181; IAttributeService<CheckoutAttribute, CheckoutAttributeValue> (_checkoutAttributeService) @ 182, 223; IAttributeParser<CheckoutAttribute, CheckoutAttributeValue> (_checkoutAttributeParser) @ 198, 213, 229, 242, 264, 275, 289, 291; IDownloadService (_downloadService) @ 272; IGenericAttributeService (_genericAttributeService) @ 295; IWorkContext (_workContext) @ 295 |
| UploadFileProductAttribute | src/Presentation/Nop.Web/Controllers/ShoppingCartController.cs:1045 | [HttpPost] [IgnoreAntiforgeryToken] | IProductAttributeService (_productAttributeService) @ 1047; IDownloadService (_downloadService) @ 1068, 1110; INopFileProvider (_fileProvider) @ 1073, 1077, 1106; ILocalizationService (_localizationService) @ 1092, 1117 | - |
| UploadFileCheckoutAttribute | src/Presentation/Nop.Web/Controllers/ShoppingCartController.cs:1125 | [HttpPost] [IgnoreAntiforgeryToken] | IAttributeService<CheckoutAttribute, CheckoutAttributeValue> (_checkoutAttributeService) @ 1127; IDownloadService (_downloadService) @ 1148, 1190; INopFileProvider (_fileProvider) @ 1153, 1157, 1186; ILocalizationService (_localizationService) @ 1172, 1197 | - |
| Cart | src/Presentation/Nop.Web/Controllers/ShoppingCartController.cs:1203 | - | IPermissionService (_permissionService) @ 1205; IStoreContext (_storeContext) @ 1208; IShoppingCartService (_shoppingCartService) @ 1209; IWorkContext (_workContext) @ 1209; IShoppingCartModelFactory (_shoppingCartModelFactory) @ 1211 | - |
| UpdateCart | src/Presentation/Nop.Web/Controllers/ShoppingCartController.cs:1217 | [HttpPost, ActionName("Cart")] [FormValueRequired("updatecart")] | IPermissionService (_permissionService) @ 1219; IWorkContext (_workContext) @ 1222; IStoreContext (_storeContext) @ 1223; IShoppingCartService (_shoppingCartService) @ 1224, 1258, 1264; IProductService (_productService) @ 1232; IShoppingCartModelFactory (_shoppingCartModelFactory) @ 1271 | ParseAndSaveCheckoutAttributesAsync (:173, called @1267) -> IShoppingCartService (_shoppingCartService) @ 180; IStoreContext (_storeContext) @ 181; IAttributeService<CheckoutAttribute, CheckoutAttributeValue> (_checkoutAttributeService) @ 182, 223; IAttributeParser<CheckoutAttribute, CheckoutAttributeValue> (_checkoutAttributeParser) @ 198, 213, 229, 242, 264, 275, 289, 291; IDownloadService (_downloadService) @ 272; IGenericAttributeService (_genericAttributeService) @ 295; IWorkContext (_workContext) @ 295 |
| ContinueShopping | src/Presentation/Nop.Web/Controllers/ShoppingCartController.cs:1287 | [HttpPost, ActionName("Cart")] [FormValueRequired("continueshopping")] | IStoreContext (_storeContext) @ 1289; IGenericAttributeService (_genericAttributeService) @ 1290; IWorkContext (_workContext) @ 1290 | - |
| StartCheckout | src/Presentation/Nop.Web/Controllers/ShoppingCartController.cs:1300 | [HttpPost, ActionName("Cart")] [FormValueRequired("checkout")] | IWorkContext (_workContext) @ 1302; IStoreContext (_storeContext) @ 1303; IShoppingCartService (_shoppingCartService) @ 1304, 1312; IGenericAttributeService (_genericAttributeService) @ 1310; IShoppingCartModelFactory (_shoppingCartModelFactory) @ 1317; OrderSettings (_orderSettings) @ 1321, 1331; CustomerSettings (_customerSettings) @ 1322, 1329; ICustomerService (_customerService) @ 1324; IProductService (_productService) @ 1329 | ParseAndSaveCheckoutAttributesAsync (:173, called @1307) -> IShoppingCartService (_shoppingCartService) @ 180; IStoreContext (_storeContext) @ 181; IAttributeService<CheckoutAttribute, CheckoutAttributeValue> (_checkoutAttributeService) @ 182, 223; IAttributeParser<CheckoutAttribute, CheckoutAttributeValue> (_checkoutAttributeParser) @ 198, 213, 229, 242, 264, 275, 289, 291; IDownloadService (_downloadService) @ 272; IGenericAttributeService (_genericAttributeService) @ 295; IWorkContext (_workContext) @ 295 |
| ApplyDiscountCoupon | src/Presentation/Nop.Web/Controllers/ShoppingCartController.cs:1342 | [HttpPost, ActionName("Cart")] [FormValueRequired("applydiscountcouponcode")] | IWorkContext (_workContext) @ 1349; IStoreContext (_storeContext) @ 1350; IShoppingCartService (_shoppingCartService) @ 1351; IDiscountService (_discountService) @ 1360, 1368; ICustomerService (_customerService) @ 1377; ILocalizationService (_localizationService) @ 1378, 1388, 1393, 1397; IShoppingCartModelFactory (_shoppingCartModelFactory) @ 1399 | ParseAndSaveCheckoutAttributesAsync (:173, called @1354) -> IShoppingCartService (_shoppingCartService) @ 180; IStoreContext (_storeContext) @ 181; IAttributeService<CheckoutAttribute, CheckoutAttributeValue> (_checkoutAttributeService) @ 182, 223; IAttributeParser<CheckoutAttribute, CheckoutAttributeValue> (_checkoutAttributeParser) @ 198, 213, 229, 242, 264, 275, 289, 291; IDownloadService (_downloadService) @ 272; IGenericAttributeService (_genericAttributeService) @ 295; IWorkContext (_workContext) @ 295 |
| ApplyGiftCard | src/Presentation/Nop.Web/Controllers/ShoppingCartController.cs:1406 | [HttpPost, ActionName("Cart")] [FormValueRequired("applygiftcardcouponcode")] | IWorkContext (_workContext) @ 1413; IStoreContext (_storeContext) @ 1414; IShoppingCartService (_shoppingCartService) @ 1415; ICustomerService (_customerService) @ 1426; ILocalizationService (_localizationService) @ 1427; IShoppingCartModelFactory (_shoppingCartModelFactory) @ 1436 | GetGiftCardValidationErrorAsync (:442, called @1422) -> ILocalizationService (_localizationService) @ 445, 448, 453, 456; IShoppingCartService (_shoppingCartService) @ 447; IGiftCardService (_giftCardService) @ 450, 452; IProductService (_productService) @ 455; ParseAndSaveCheckoutAttributesAsync (:173, called @1418) -> IShoppingCartService (_shoppingCartService) @ 180; IStoreContext (_storeContext) @ 181; IAttributeService<CheckoutAttribute, CheckoutAttributeValue> (_checkoutAttributeService) @ 182, 223; IAttributeParser<CheckoutAttribute, CheckoutAttributeValue> (_checkoutAttributeParser) @ 198, 213, 229, 242, 264, 275, 289, 291; IDownloadService (_downloadService) @ 272; IGenericAttributeService (_genericAttributeService) @ 295; IWorkContext (_workContext) @ 295 |
| GetEstimateShipping | src/Presentation/Nop.Web/Controllers/ShoppingCartController.cs:1441 | [HttpPost] | ShippingSettings (_shippingSettings) @ 1448, 1451; ILocalizationService (_localizationService) @ 1449, 1452, 1455; IStoreContext (_storeContext) @ 1464; IShoppingCartService (_shoppingCartService) @ 1465; IWorkContext (_workContext) @ 1465; IShoppingCartModelFactory (_shoppingCartModelFactory) @ 1469 | ParseAndSaveCheckoutAttributesAsync (:173, called @1467) -> IShoppingCartService (_shoppingCartService) @ 180; IStoreContext (_storeContext) @ 181; IAttributeService<CheckoutAttribute, CheckoutAttributeValue> (_checkoutAttributeService) @ 182, 223; IAttributeParser<CheckoutAttribute, CheckoutAttributeValue> (_checkoutAttributeParser) @ 198, 213, 229, 242, 264, 275, 289, 291; IDownloadService (_downloadService) @ 272; IGenericAttributeService (_genericAttributeService) @ 295; IWorkContext (_workContext) @ 295 |
| RemoveDiscountCoupon | src/Presentation/Nop.Web/Controllers/ShoppingCartController.cs:1476 | [HttpPost, ActionName("Cart")] [FormValueRequired(FormValueRequirement.StartsWith, "removediscount-")] | IDiscountService (_discountService) @ 1485; IWorkContext (_workContext) @ 1486; ICustomerService (_customerService) @ 1488; IStoreContext (_storeContext) @ 1490; IShoppingCartService (_shoppingCartService) @ 1491; IShoppingCartModelFactory (_shoppingCartModelFactory) @ 1493 | - |
| RemoveGiftCardCode | src/Presentation/Nop.Web/Controllers/ShoppingCartController.cs:1499 | [HttpPost, ActionName("Cart")] [FormValueRequired(FormValueRequirement.StartsWith, "removegiftcard-")] | IGiftCardService (_giftCardService) @ 1508; IWorkContext (_workContext) @ 1509; ICustomerService (_customerService) @ 1511; IStoreContext (_storeContext) @ 1513; IShoppingCartService (_shoppingCartService) @ 1514; IShoppingCartModelFactory (_shoppingCartModelFactory) @ 1516 | - |
| Wishlist | src/Presentation/Nop.Web/Controllers/ShoppingCartController.cs:1524 | - | IPermissionService (_permissionService) @ 1526; ICustomerService (_customerService) @ 1530; IWorkContext (_workContext) @ 1531; IStoreContext (_storeContext) @ 1535; IShoppingCartService (_shoppingCartService) @ 1536; IShoppingCartModelFactory (_shoppingCartModelFactory) @ 1539 | - |
| UpdateWishlist | src/Presentation/Nop.Web/Controllers/ShoppingCartController.cs:1545 | [HttpPost, ActionName("Wishlist")] [FormValueRequired("updatecart")] | IPermissionService (_permissionService) @ 1547; IWorkContext (_workContext) @ 1550; IStoreContext (_storeContext) @ 1551; IShoppingCartService (_shoppingCartService) @ 1552, 1566, 1574, 1587; IShoppingCartModelFactory (_shoppingCartModelFactory) @ 1589 | - |
| AddItemsToCartFromWishlist | src/Presentation/Nop.Web/Controllers/ShoppingCartController.cs:1609 | [HttpPost, ActionName("Wishlist")] [FormValueRequired("addtocartbutton")] | IPermissionService (_permissionService) @ 1611, 1614; IWorkContext (_workContext) @ 1617; ICustomerService (_customerService) @ 1619; IStoreContext (_storeContext) @ 1624; IShoppingCartService (_shoppingCartService) @ 1625, 1638, 1650, 1679; IProductService (_productService) @ 1636; ShoppingCartSettings (_shoppingCartSettings) @ 1645; INotificationService (_notificationService) @ 1663, 1670, 1676; ILocalizationService (_localizationService) @ 1663, 1670, 1676; IShoppingCartModelFactory (_shoppingCartModelFactory) @ 1682 | - |
| EmailWishlist | src/Presentation/Nop.Web/Controllers/ShoppingCartController.cs:1686 | - | IPermissionService (_permissionService) @ 1688; ShoppingCartSettings (_shoppingCartSettings) @ 1688; IStoreContext (_storeContext) @ 1691; IShoppingCartService (_shoppingCartService) @ 1692; IWorkContext (_workContext) @ 1692; IShoppingCartModelFactory (_shoppingCartModelFactory) @ 1698 | - |
| EmailWishlistSend | src/Presentation/Nop.Web/Controllers/ShoppingCartController.cs:1705 | [HttpPost, ActionName("EmailWishlist")] [FormValueRequired("send-email")] [ValidateCaptcha] | IPermissionService (_permissionService) @ 1707; ShoppingCartSettings (_shoppingCartSettings) @ 1707, 1724; IWorkContext (_workContext) @ 1710, 1735; IStoreContext (_storeContext) @ 1711; IShoppingCartService (_shoppingCartService) @ 1712; CaptchaSettings (_captchaSettings) @ 1718, 1718; ILocalizationService (_localizationService) @ 1720, 1726, 1742; ICustomerService (_customerService) @ 1724; IWebHelper (_webHelper) @ 1732; IWorkflowMessageService (_workflowMessageService) @ 1734; IHtmlFormatter (_htmlFormatter) @ 1738; IShoppingCartModelFactory (_shoppingCartModelFactory) @ 1748 | - |
| AddWishlist | src/Presentation/Nop.Web/Controllers/ShoppingCartController.cs:1754 | [HttpPost] | IWorkContext (_workContext) @ 1756; ICustomerService (_customerService) @ 1757; ILocalizationService (_localizationService) @ 1764, 1773, 1785; ShoppingCartSettings (_shoppingCartSettings) @ 1768, 1779; ICustomWishlistService (_customWishlistService) @ 1778, 1797; IProductService (_productService) @ 1799 | - |
| MoveProductToCustomWishlist | src/Presentation/Nop.Web/Controllers/ShoppingCartController.cs:1813 | [HttpPost] | IWorkContext (_workContext) @ 1815; ICustomerService (_customerService) @ 1816; ILocalizationService (_localizationService) @ 1823, 1832, 1851; ShoppingCartSettings (_shoppingCartSettings) @ 1827; IStoreContext (_storeContext) @ 1836; IShoppingCartService (_shoppingCartService) @ 1837, 1842; IProductService (_productService) @ 1839; ICustomWishlistService (_customWishlistService) @ 1846 | - |
| MoveToCustomWishlist | src/Presentation/Nop.Web/Controllers/ShoppingCartController.cs:1856 | [HttpPost] | IWorkContext (_workContext) @ 1858; ICustomWishlistService (_customWishlistService) @ 1862; ILocalizationService (_localizationService) @ 1868; IShoppingCartService (_shoppingCartService) @ 1871, 1875 | - |
| DeleteWishlist | src/Presentation/Nop.Web/Controllers/ShoppingCartController.cs:1885 | [HttpPost] | IWorkContext (_workContext) @ 1887; ICustomWishlistService (_customWishlistService) @ 1888, 1908; ILocalizationService (_localizationService) @ 1895; IStoreContext (_storeContext) @ 1900; IShoppingCartService (_shoppingCartService) @ 1901, 1904 | - |

Helpers in ShoppingCartController.cs:

- `ParseAndSaveCheckoutAttributesAsync` (src/Presentation/Nop.Web/Controllers/ShoppingCartController.cs:173): IShoppingCartService (_shoppingCartService) @ 180; IStoreContext (_storeContext) @ 181; IAttributeService<CheckoutAttribute, CheckoutAttributeValue> (_checkoutAttributeService) @ 182, 223; IAttributeParser<CheckoutAttribute, CheckoutAttributeValue> (_checkoutAttributeParser) @ 198, 213, 229, 242, 264, 275, 289, 291; IDownloadService (_downloadService) @ 272; IGenericAttributeService (_genericAttributeService) @ 295; IWorkContext (_workContext) @ 295
- `SaveItemAsync` (src/Presentation/Nop.Web/Controllers/ShoppingCartController.cs:298): IWorkContext (_workContext) @ 302; IStoreContext (_storeContext) @ 303; IShoppingCartService (_shoppingCartService) @ 307, 314, 316, 326, 332
- `GetProductToCartDetailsAsync` (src/Presentation/Nop.Web/Controllers/ShoppingCartController.cs:337): IWorkContext (_workContext) @ 352; IStoreContext (_storeContext) @ 353; ICustomerActivityService (_customerActivityService) @ 360, 407; ILocalizationService (_localizationService) @ 361, 376, 388, 397, 408, 423, 433; ShoppingCartSettings (_shoppingCartSettings) @ 363, 410, 426; IShoppingCartService (_shoppingCartService) @ 373, 420; ICustomWishlistService (_customWishlistService) @ 379; ICustomerService (_customerService) @ 380
- `GetGiftCardValidationErrorAsync` (src/Presentation/Nop.Web/Controllers/ShoppingCartController.cs:442): ILocalizationService (_localizationService) @ 445, 448, 453, 456; IShoppingCartService (_shoppingCartService) @ 447; IGiftCardService (_giftCardService) @ 450, 452; IProductService (_productService) @ 455

## Table B — invoked interfaces: implementation + IRepository injections

Method: implementation found by class-declaration search (base list mentions the interface, transitive through inheritance); ctor params parsed from the implementation file. `REPO` lines are `IRepository<T>` ctor params with their line numbers. Other service-typed ctor params are listed by type name only (verifiable at the ctor line). `*Settings` and other concrete config params are omitted here. Two same-named factory interfaces exist in different namespaces (public `Nop.Web.Factories` vs `Nop.Web.Areas.Admin.Factories`); both implementations are listed — the slice controllers use the public ones (see §5 note).

### IAclService
- Impl: `AclService` — src/Libraries/Nop.Services/Security/AclService.cs:14
  - Ctor: line 29
  - REPO: `IRepository<AclRecord>` (aclRecordRepository) @ line 32
  - Other service-typed deps: `ICustomerService`, `INopDataProvider`, `IStaticCacheManager`

### IAddressModelFactory
- Impl: `AddressModelFactory` — src/Presentation/Nop.Web/Areas/Admin/Factories/AddressModelFactory.cs:10
  - Ctor: line 22
  - REPO: none
  - Other service-typed deps: `IAttributeFormatter<AddressAttribute, AddressAttributeValue>`
- Impl: `AddressModelFactory` — src/Presentation/Nop.Web/Factories/AddressModelFactory.cs:18
  - Ctor: line 36
  - REPO: none
  - Other service-typed deps: `IAddressService`, `IAttributeFormatter<AddressAttribute, AddressAttributeValue>`, `IAttributeParser<AddressAttribute, AddressAttributeValue>`, `IAttributeService<AddressAttribute, AddressAttributeValue>`, `ICountryService`, `ILocalizationService`, `IStateProvinceService`

### IAddressService
- Impl: `AddressService` — src/Libraries/Nop.Services/Common/AddressService.cs:14
  - Ctor: line 30
  - REPO: `IRepository<Address>` (addressRepository) @ line 35
  - Other service-typed deps: `IAttributeParser<AddressAttribute, AddressAttributeValue>`, `IAttributeService<AddressAttribute, AddressAttributeValue>`, `ICountryService`, `ILocalizationService`

### IAttributeParser<AddressAttribute, AddressAttributeValue>
- Impl: `AttributeParser` — src/Libraries/Nop.Services/Attributes/AttributeParser.cs:15
  - Ctor: line 32
  - REPO: none

### IAttributeParser<CheckoutAttribute, CheckoutAttributeValue>
- Impl: `AttributeParser` — src/Libraries/Nop.Services/Attributes/AttributeParser.cs:15
  - Ctor: line 32
  - REPO: none

### IAttributeService<CheckoutAttribute, CheckoutAttributeValue>
- Impl: `AttributeService` — src/Libraries/Nop.Services/Attributes/AttributeService.cs:12
  - Ctor: line 26
  - REPO: `IRepository<TAttributeValue>` (attributeValueRepository) @ line 27

### ICatalogModelFactory
- Impl: `CatalogModelFactory` — src/Presentation/Nop.Web/Factories/CatalogModelFactory.cs:30
  - Ctor: line 72
  - REPO: none
  - Other service-typed deps: `ICategoryService`, `ICategoryTemplateService`, `ICurrencyService`, `ICustomerService`, `IEventPublisher`, `IFilterLevelValueService`, `IGenericAttributeService`, `IHttpContextAccessor`, `IJsonLdModelFactory`, `ILocalizationService`, `IManufacturerService`, `IManufacturerTemplateService`, `INopUrlHelper`, `IPictureService`, `IProductModelFactory`, `IProductReviewService`, `IProductService`, `IProductTagService`, `ISearchTermService`, `ISpecificationAttributeService`, `IStaticCacheManager`, `IStoreContext`, `IUrlRecordService`, `IVendorService`, `IWebHelper`, `IWorkContext`

### ICategoryService
- Impl: `CategoryService` — src/Libraries/Nop.Services/Catalog/CategoryService.cs:18
  - Ctor: line 38
  - REPO: `IRepository<Category>` (categoryRepository) @ line 41
  - REPO: `IRepository<DiscountCategoryMapping>` (discountCategoryMappingRepository) @ line 42
  - REPO: `IRepository<Product>` (productRepository) @ line 43
  - REPO: `IRepository<ProductCategory>` (productCategoryRepository) @ line 44
  - Other service-typed deps: `ICustomerService`, `ILocalizationService`, `IStaticCacheManager`, `IStoreContext`, `IStoreMappingService`

### ICheckoutModelFactory
- Impl: `CheckoutModelFactory` — src/Presentation/Nop.Web/Factories/CheckoutModelFactory.cs:25
  - Ctor: line 64
  - REPO: none
  - Other service-typed deps: `IAddressModelFactory`, `IAddressService`, `ICountryService`, `ICurrencyService`, `ICustomerService`, `IGenericAttributeService`, `ILocalizationService`, `IOrderProcessingService`, `IOrderTotalCalculationService`, `IPaymentPluginManager`, `IPaymentService`, `IPickupPluginManager`, `IPriceFormatter`, `IRewardPointService`, `IShippingPluginManager`, `IShippingService`, `IShoppingCartService`, `IStateProvinceService`, `IStoreContext`, `IStoreMappingService`, `ITaxService`, `IWorkContext`

### ICompareProductsService
- Impl: `CompareProductsService` — src/Libraries/Nop.Services/Catalog/CompareProductsService.cs:12
  - Ctor: line 27
  - REPO: none
  - Other service-typed deps: `IHttpContextAccessor`, `IProductService`

### ICountryService
- Impl: `CountryService` — src/Libraries/Nop.Services/Directory/CountryService.cs:14
  - Ctor: line 28
  - REPO: `IRepository<Country>` (countryRepository) @ line 31
  - Other service-typed deps: `IStaticCacheManager`, `ILocalizationService`, `IStoreContext`

### ICurrencyService
- Impl: `CurrencyService` — src/Libraries/Nop.Services/Directory/CurrencyService.cs:11
  - Ctor: line 24
  - REPO: `IRepository<Currency>` (currencyRepository) @ line 26
  - Other service-typed deps: `IExchangeRatePluginManager`

### ICustomWishlistService
- Impl: `CustomWishlistService` — src/Libraries/Nop.Services/Orders/CustomWishlistService.cs:9
  - Ctor: line 20
  - REPO: none

### ICustomerActivityService
- Impl: `CustomerActivityService` — src/Libraries/Nop.Services/Logging/CustomerActivityService.cs:11
  - Ctor: line 25
  - REPO: `IRepository<ActivityLog>` (activityLogRepository) @ line 26
  - REPO: `IRepository<ActivityLogType>` (activityLogTypeRepository) @ line 27
  - Other service-typed deps: `IWebHelper`

### ICustomerService
- Impl: `CustomerService` — src/Libraries/Nop.Services/Customers/CustomerService.cs:25
  - Ctor: line 59
  - REPO: `IRepository<Address>` (customerAddressRepository) @ line 63
  - REPO: `IRepository<BlogComment>` (blogCommentRepository) @ line 64
  - REPO: `IRepository<Customer>` (customerRepository) @ line 65
  - REPO: `IRepository<CustomerAddressMapping>` (customerAddressMappingRepository) @ line 66
  - REPO: `IRepository<CustomerCustomerRoleMapping>` (customerCustomerRoleMappingRepository) @ line 67
  - REPO: `IRepository<CustomerPassword>` (customerPasswordRepository) @ line 68
  - REPO: `IRepository<CustomerRole>` (customerRoleRepository) @ line 69
  - REPO: `IRepository<ForumPost>` (forumPostRepository) @ line 70
  - REPO: `IRepository<ForumTopic>` (forumTopicRepository) @ line 71
  - REPO: `IRepository<GenericAttribute>` (gaRepository) @ line 72
  - REPO: `IRepository<NewsComment>` (newsCommentRepository) @ line 73
  - REPO: `IRepository<Order>` (orderRepository) @ line 74
  - REPO: `IRepository<ProductReview>` (productReviewRepository) @ line 75
  - REPO: `IRepository<ProductReviewHelpfulness>` (productReviewHelpfulnessRepository) @ line 76
  - REPO: `IRepository<PollVotingRecord>` (pollVotingRecordRepository) @ line 77
  - REPO: `IRepository<ShoppingCartItem>` (shoppingCartRepository) @ line 78
  - Other service-typed deps: `IEventPublisher`, `IGenericAttributeService`, `INopDataProvider`, `IShortTermCacheManager`, `IStaticCacheManager`, `IStoreContext`

### IDiscountService
- Impl: `DiscountService` — src/Libraries/Nop.Services/Discounts/DiscountService.cs:18
  - Ctor: line 38
  - REPO: `IRepository<Discount>` (discountRepository) @ line 42
  - REPO: `IRepository<DiscountRequirement>` (discountRequirementRepository) @ line 43
  - REPO: `IRepository<DiscountUsageHistory>` (discountUsageHistoryRepository) @ line 44
  - REPO: `IRepository<Order>` (orderRepository) @ line 45
  - Other service-typed deps: `IDiscountPluginManager`, `ILocalizationService`, `IProductService`, `IShortTermCacheManager`, `IStaticCacheManager`

### IDownloadService
- Impl: `DownloadService` — src/Libraries/Nop.Services/Media/DownloadService.cs:10
  - Ctor: line 20
  - REPO: none

### IFilterLevelValueModelFactory
- Impl: `FilterLevelValueModelFactory` — src/Presentation/Nop.Web/Areas/Admin/Factories/FilterLevelValueModelFactory.cs:17
  - Ctor: line 31
  - REPO: none
  - Other service-typed deps: `IFilterLevelValueService`, `ILocalizationService`, `IProductService`
- Impl: `FilterLevelValueModelFactory` — src/Presentation/Nop.Web/Factories/FilterLevelValueModelFactory.cs:10
  - Ctor: line 22
  - REPO: none
  - Other service-typed deps: `IFilterLevelValueService`

### IFilterLevelValueService
- Impl: `FilterLevelValueService` — src/Libraries/Nop.Services/FilterLevels/FilterLevelValueService.cs:15
  - Ctor: line 32
  - REPO: `IRepository<FilterLevelValue>` (filterLevelValueRepository) @ line 34
  - REPO: `IRepository<FilterLevelValueProductMapping>` (filterLevelValueProductMappingRepository) @ line 35
  - REPO: `IRepository<LocalizedProperty>` (localizedPropertyRepository) @ line 36
  - REPO: `IRepository<Product>` (productRepository) @ line 37
  - Other service-typed deps: `IAclService`, `IStoreMappingService`

### IGenericAttributeService
- Impl: `GenericAttributeService` — src/Libraries/Nop.Services/Common/GenericAttributeService.cs:11
  - Ctor: line 23
  - REPO: none
  - Other service-typed deps: `IShortTermCacheManager`

### IGiftCardService
- Impl: `GiftCardService` — src/Libraries/Nop.Services/Orders/GiftCardService.cs:13
  - Ctor: line 26
  - REPO: `IRepository<GiftCard>` (giftCardRepository) @ line 28
  - REPO: `IRepository<GiftCardUsageHistory>` (giftCardUsageHistoryRepository) @ line 29
  - Other service-typed deps: `IEventPublisher`

### IHtmlFormatter
- Impl: `HtmlFormatter` — src/Libraries/Nop.Services/Html/HtmlFormatter.cs:9
  - Ctor: line 19
  - REPO: none

### ILocalizationService
- Impl: `LocalizationService` — src/Libraries/Nop.Services/Localization/LocalizationService.cs:21
  - Ctor: line 38
  - REPO: `IRepository<LocaleStringResource>` (lsrRepository) @ line 41
  - Other service-typed deps: `ILocalizedEntityService`, `ILogger`, `ISettingService`, `IStaticCacheManager`, `IWorkContext`

### ILogger
- Impl: `DefaultLogger` — src/Libraries/Nop.Services/Logging/DefaultLogger.cs:12
  - Ctor: line 26
  - REPO: `IRepository<Log>` (logRepository) @ line 28

### IManufacturerService
- Impl: `ManufacturerService` — src/Libraries/Nop.Services/Catalog/ManufacturerService.cs:17
  - Ctor: line 39
  - REPO: `IRepository<DiscountManufacturerMapping>` (discountManufacturerMappingRepository) @ line 43
  - REPO: `IRepository<Manufacturer>` (manufacturerRepository) @ line 44
  - REPO: `IRepository<Product>` (productRepository) @ line 45
  - REPO: `IRepository<ProductManufacturer>` (productManufacturerRepository) @ line 46
  - REPO: `IRepository<ProductCategory>` (productCategoryRepository) @ line 47
  - Other service-typed deps: `IAclService`, `ICategoryService`, `ICustomerService`, `IStaticCacheManager`, `IStoreContext`, `IStoreMappingService`

### INopFileProvider
- Impl: `NopFileProvider` — src/Libraries/Nop.Core/Infrastructure/NopFileProvider.cs:12
  - Ctor: line 20
  - REPO: none

### INopUrlHelper
- Impl: `NopUrlHelper` — src/Presentation/Nop.Web.Framework/Mvc/Routing/NopUrlHelper.cs:21
  - Ctor: line 38
  - REPO: none
  - Other service-typed deps: `IActionContextAccessor`, `ICategoryService`, `IManufacturerService`, `IStoreContext`, `ITopicService`, `IUrlHelperFactory`

### INotificationService
- Impl: `NotificationService` — src/Libraries/Nop.Services/Messages/NotificationService.cs:12
  - Ctor: line 25
  - REPO: none
  - Other service-typed deps: `ILogger`, `ITempDataDictionaryFactory`

### IOrderModelFactory
- Impl: `OrderModelFactory` — src/Presentation/Nop.Web/Areas/Admin/Factories/OrderModelFactory.cs:47
  - Ctor: line 101
  - REPO: none
  - Other service-typed deps: `IActionContextAccessor`, `IAddressModelFactory`, `IAddressService`, `IAffiliateService`, `IBaseAdminModelFactory`, `ICountryService`, `ICurrencyService`, `ICustomerService`, `IDateTimeHelper`, `IDiscountService`, `IDownloadService`, `IEncryptionService`, `IGiftCardService`, `ILocalizationService`, `IMeasureService`, `IOrderProcessingService`, `IOrderReportService`, `IOrderService`, `IPaymentPluginManager`, `IPictureService`, `IPriceCalculationService`, `IPriceFormatter`, `IProductAttributeService`, `IProductService`, `IReturnRequestService`, `IRewardPointService`, `ISettingService`, `IShipmentService`, `IStateProvinceService`, `IStoreService`, `ITaxService`, `IUrlHelperFactory`, `IVendorService`, `IWarehouseService`, `IWorkContext`, `IUrlRecordService`
- Impl: `OrderModelFactory` — src/Presentation/Nop.Web/Factories/OrderModelFactory.cs:38
  - Ctor: line 82
  - REPO: none
  - Other service-typed deps: `IAddressModelFactory`, `IAddressService`, `ICountryService`, `ICurrencyService`, `ICustomerService`, `IDateTimeHelper`, `IGiftCardService`, `ILocalizationService`, `IOrderProcessingService`, `IOrderService`, `IOrderTotalCalculationService`, `IPaymentPluginManager`, `IPaymentService`, `IPictureService`, `IPriceFormatter`, `IProductService`, `IRewardPointService`, `IShipmentService`, `IShortTermCacheManager`, `IStateProvinceService`, `IStaticCacheManager`, `IStoreContext`, `IUrlRecordService`, `IVendorService`, `IWebHelper`, `IWorkContext`

### IOrderProcessingService
- Impl: `OrderProcessingService` — src/Libraries/Nop.Services/Orders/OrderProcessingService.cs:39
  - Ctor: line 96
  - REPO: none
  - Other service-typed deps: `IAddressService`, `IAffiliateService`, `ICheckoutAttributeFormatter`, `ICountryService`, `ICurrencyService`, `ICustomerActivityService`, `ICustomerService`, `ICustomNumberFormatter`, `IDiscountService`, `IEncryptionService`, `IEventPublisher`, `IGenericAttributeService`, `IGiftCardService`, `ILanguageService`, `ILocalizationService`, `ILogger`, `IOrderService`, `IOrderTotalCalculationService`, `IPaymentPluginManager`, `IPaymentService`, `IPdfService`, `IPriceCalculationService`, `IPriceFormatter`, `IProductAttributeFormatter`, `IProductAttributeParser`, `IProductService`, `IReturnRequestService`, `IRewardPointService`, `IShipmentService`, `IShippingService`, `IShoppingCartService`, `IStateProvinceService`, `IStaticCacheManager`, `IStoreContext`, `IStoreMappingService`, `IStoreService`, `ITaxService`, `IVendorService`, `IWebHelper`, `IWorkContext`, `IWorkflowMessageService`

### IOrderService
- Impl: `OrderService` — src/Libraries/Nop.Services/Orders/OrderService.cs:19
  - Ctor: line 41
  - REPO: `IRepository<Address>` (addressRepository) @ line 43
  - REPO: `IRepository<Customer>` (customerRepository) @ line 44
  - REPO: `IRepository<Order>` (orderRepository) @ line 45
  - REPO: `IRepository<OrderItem>` (orderItemRepository) @ line 46
  - REPO: `IRepository<OrderNote>` (orderNoteRepository) @ line 47
  - REPO: `IRepository<Product>` (productRepository) @ line 48
  - REPO: `IRepository<ProductWarehouseInventory>` (productWarehouseInventoryRepository) @ line 49
  - REPO: `IRepository<RecurringPayment>` (recurringPaymentRepository) @ line 50
  - REPO: `IRepository<RecurringPaymentHistory>` (recurringPaymentHistoryRepository) @ line 51
  - Other service-typed deps: `IProductService`

### IPaymentPluginManager
- Impl: `PaymentPluginManager` — src/Libraries/Nop.Services/Payments/PaymentPluginManager.cs:12
  - Ctor: line 23
  - REPO: none
  - Other service-typed deps: `IPluginService`, `ISettingService`

### IPaymentService
- Impl: `PaymentService` — src/Libraries/Nop.Services/Payments/PaymentService.cs:12
  - Ctor: line 26
  - REPO: none
  - Other service-typed deps: `IPaymentPluginManager`, `IPriceCalculationService`

### IPdfService
- Impl: `PdfService` — src/Libraries/Nop.Services/Common/PdfService.cs:37
  - Ctor: line 78
  - REPO: none
  - Other service-typed deps: `IAddressService`, `IAttributeFormatter<AddressAttribute, AddressAttributeValue>`, `ICountryService`, `ICurrencyService`, `IDateTimeHelper`, `IGiftCardService`, `IHtmlFormatter`, `ILanguageService`, `ILocalizationService`, `IMeasureService`, `INopFileProvider`, `IOrderService`, `IPaymentPluginManager`, `IPaymentService`, `IPictureService`, `IPriceFormatter`, `IProductService`, `IRewardPointService`, `ISettingService`, `IShipmentService`, `IStateProvinceService`, `IStoreContext`, `IStoreService`, `IThumbService`, `IVendorService`, `IWorkContext`

### IPermissionService
- Impl: `PermissionService` — src/Libraries/Nop.Services/Security/PermissionService.cs:15
  - Ctor: line 32
  - REPO: `IRepository<CustomerRole>` (customerRoleRepository) @ line 34
  - REPO: `IRepository<PermissionRecord>` (permissionRecordRepository) @ line 35
  - REPO: `IRepository<PermissionRecordCustomerRoleMapping>` (permissionRecordCustomerRoleMappingRepository) @ line 36
  - Other service-typed deps: `ILocalizationService`, `IStaticCacheManager`, `ITypeFinder`

### IPictureService
- Impl: `PictureService` — src/Libraries/Nop.Services/Media/PictureService.cs:21
  - Ctor: line 44
  - REPO: `IRepository<Picture>` (pictureRepository) @ line 50
  - REPO: `IRepository<PictureBinary>` (pictureBinaryRepository) @ line 51
  - REPO: `IRepository<ProductPicture>` (productPictureRepository) @ line 52
  - Other service-typed deps: `IHttpContextAccessor`, `ILogger`, `INopFileProvider`, `IProductAttributeParser`, `IProductAttributeService`, `ISettingService`, `IThumbService`, `IUrlRecordService`, `IWebHelper`

### IPriceFormatter
- Impl: `PriceFormatter` — src/Libraries/Nop.Services/Catalog/PriceFormatter.cs:14
  - Ctor: line 30
  - REPO: none
  - Other service-typed deps: `ICurrencyService`, `ILocalizationService`, `IMeasureService`, `IPriceCalculationService`, `IWorkContext`

### IProductAttributeParser
- Impl: `ProductAttributeParser` — src/Libraries/Nop.Services/Catalog/ProductAttributeParser.cs:17
  - Ctor: line 34
  - REPO: `IRepository<ProductAttributeValue>` (productAttributeValueRepository) @ line 38
  - Other service-typed deps: `IDownloadService`, `ILocalizationService`, `IProductAttributeService`

### IProductAttributeService
- Impl: `ProductAttributeService` — src/Libraries/Nop.Services/Catalog/ProductAttributeService.cs:12
  - Ctor: line 32
  - REPO: `IRepository<PredefinedProductAttributeValue>` (predefinedProductAttributeValueRepository) @ line 33
  - REPO: `IRepository<Product>` (productRepository) @ line 34
  - REPO: `IRepository<ProductAttribute>` (productAttributeRepository) @ line 35
  - REPO: `IRepository<ProductAttributeCombination>` (productAttributeCombinationRepository) @ line 36
  - REPO: `IRepository<ProductAttributeCombinationPicture>` (productAttributeCombinationPictureRepository) @ line 37
  - REPO: `IRepository<ProductAttributeMapping>` (productAttributeMappingRepository) @ line 38
  - REPO: `IRepository<ProductAttributeValue>` (productAttributeValueRepository) @ line 39
  - REPO: `IRepository<ProductAttributeValuePicture>` (productAttributeValuePictureRepository) @ line 40
  - REPO: `IRepository<ProductPicture>` (productPictureRepository) @ line 41

### IProductModelFactory
- Impl: `ProductModelFactory` — src/Presentation/Nop.Web/Areas/Admin/Factories/ProductModelFactory.cs:41
  - Ctor: line 91
  - REPO: none
  - Other service-typed deps: `IAddressService`, `IBaseAdminModelFactory`, `ICategoryService`, `ICurrencyService`, `ICustomerService`, `IDateTimeHelper`, `IDiscountService`, `IDiscountSupportedModelFactory`, `IFilterLevelValueService`, `ILanguageService`, `ILocalizationService`, `ILocalizedModelFactory`, `IManufacturerService`, `IMeasureService`, `IOrderService`, `IPictureService`, `IPriceFormatter`, `IProductAttributeFormatter`, `IProductAttributeParser`, `IProductAttributeService`, `IProductService`, `IProductTagService`, `IProductTemplateService`, `ISettingModelFactory`, `ISettingService`, `IShipmentService`, `IShoppingCartService`, `ISpecificationAttributeService`, `IStoreMappingSupportedModelFactory`, `IStoreContext`, `IStoreService`, `IUrlRecordService`, `IVideoService`, `IWarehouseService`, `IWorkContext`
- Impl: `ProductModelFactory` — src/Presentation/Nop.Web/Factories/ProductModelFactory.cs:42
  - Ctor: line 94
  - REPO: none
  - Other service-typed deps: `ICategoryService`, `ICurrencyService`, `ICustomerService`, `ICustomWishlistService`, `IDateRangeService`, `IDateTimeHelper`, `IDownloadService`, `IGenericAttributeService`, `IJsonLdModelFactory`, `ILocalizationService`, `IManufacturerService`, `IPermissionService`, `IPictureService`, `IPriceCalculationService`, `IPriceFormatter`, `IProductAttributeParser`, `IProductAttributeService`, `IProductReviewService`, `IProductService`, `IProductTagService`, `IProductTemplateService`, `IReviewTypeService`, `IShoppingCartService`, `ISpecificationAttributeService`, `IStaticCacheManager`, `IStoreContext`, `IStoreService`, `IShoppingCartModelFactory`, `ITaxService`, `IUrlRecordService`, `IVendorService`, `IVideoService`, `IWebHelper`, `IWorkContext`

### IProductReviewService
- Impl: `ProductReviewService` — src/Libraries/Nop.Services/Catalog/ProductReviewService.cs:20
  - Ctor: line 45
  - REPO: `IRepository<Product>` (productRepository) @ line 53
  - REPO: `IRepository<ProductReview>` (productReviewRepository) @ line 54
  - REPO: `IRepository<ProductReviewHelpfulness>` (productReviewHelpfulnessRepository) @ line 55
  - Other service-typed deps: `IAclService`, `ICustomerActivityService`, `ICustomerService`, `IEventPublisher`, `ILocalizationService`, `IOrderService`, `IProductService`, `IReviewTypeService`, `IStoreMappingService`, `IWorkContext`, `IWorkflowMessageService`

### IProductService
- Impl: `ProductService` — src/Libraries/Nop.Services/Catalog/ProductService.cs:26
  - Ctor: line 70
  - REPO: `IRepository<Category>` (categoryRepository) @ line 78
  - REPO: `IRepository<CrossSellProduct>` (crossSellProductRepository) @ line 79
  - REPO: `IRepository<DiscountProductMapping>` (discountProductMappingRepository) @ line 80
  - REPO: `IRepository<LocalizedProperty>` (localizedPropertyRepository) @ line 81
  - REPO: `IRepository<Manufacturer>` (manufacturerRepository) @ line 82
  - REPO: `IRepository<Product>` (productRepository) @ line 83
  - REPO: `IRepository<ProductAttributeCombination>` (productAttributeCombinationRepository) @ line 84
  - REPO: `IRepository<ProductAttributeMapping>` (productAttributeMappingRepository) @ line 85
  - REPO: `IRepository<ProductCategory>` (productCategoryRepository) @ line 86
  - REPO: `IRepository<ProductManufacturer>` (productManufacturerRepository) @ line 87
  - REPO: `IRepository<ProductPicture>` (productPictureRepository) @ line 88
  - REPO: `IRepository<ProductProductTagMapping>` (productTagMappingRepository) @ line 89
  - REPO: `IRepository<ProductSpecificationAttribute>` (productSpecificationAttributeRepository) @ line 90
  - REPO: `IRepository<ProductTag>` (productTagRepository) @ line 91
  - REPO: `IRepository<ProductVideo>` (productVideoRepository) @ line 92
  - REPO: `IRepository<ProductWarehouseInventory>` (productWarehouseInventoryRepository) @ line 93
  - REPO: `IRepository<RelatedProduct>` (relatedProductRepository) @ line 94
  - REPO: `IRepository<Shipment>` (shipmentRepository) @ line 95
  - REPO: `IRepository<StockQuantityHistory>` (stockQuantityHistoryRepository) @ line 96
  - REPO: `IRepository<TierPrice>` (tierPriceRepository) @ line 97
  - Other service-typed deps: `IAclService`, `ICustomerService`, `IDateRangeService`, `ILanguageService`, `ILocalizationService`, `IProductAttributeParser`, `IProductAttributeService`, `ISearchPluginManager`, `IStaticCacheManager`, `IVendorService`, `IStoreMappingService`, `IWorkContext`

### IProductTagService
- Impl: `ProductTagService` — src/Libraries/Nop.Services/Catalog/ProductTagService.cs:15
  - Ctor: line 33
  - REPO: `IRepository<Product>` (productRepository) @ line 36
  - REPO: `IRepository<ProductProductTagMapping>` (productProductTagMappingRepository) @ line 37
  - REPO: `IRepository<ProductTag>` (productTagRepository) @ line 38
  - Other service-typed deps: `IAclService`, `ICustomerService`, `IStaticCacheManager`, `IStoreMappingService`, `IUrlRecordService`

### IRecentlyViewedProductsService
- Impl: `RecentlyViewedProductsService` — src/Libraries/Nop.Services/Catalog/RecentlyViewedProductsService.cs:12
  - Ctor: line 27
  - REPO: none
  - Other service-typed deps: `IHttpContextAccessor`, `IProductService`

### IShipmentService
- Impl: `ShipmentService` — src/Libraries/Nop.Services/Shipping/ShipmentService.cs:15
  - Ctor: line 32
  - REPO: `IRepository<Address>` (addressRepository) @ line 33
  - REPO: `IRepository<Order>` (orderRepository) @ line 34
  - REPO: `IRepository<OrderItem>` (orderItemRepository) @ line 35
  - REPO: `IRepository<Product>` (productRepository) @ line 36
  - REPO: `IRepository<Shipment>` (shipmentRepository) @ line 37
  - REPO: `IRepository<ShipmentItem>` (siRepository) @ line 38

### IShippingService
- Impl: `ShippingService` — src/Libraries/Nop.Services/Shipping/ShippingService.cs:21
  - Ctor: line 47
  - REPO: none
  - Other service-typed deps: `IAttributeParser<CheckoutAttribute, CheckoutAttributeValue>`, `ICountryService`, `ICustomerService`, `IGenericAttributeService`, `ILocalizationService`, `ILogger`, `IPickupPluginManager`, `IPriceCalculationService`, `IProductAttributeParser`, `IProductService`, `IShippingPluginManager`, `IStateProvinceService`, `IStoreContext`, `IWarehouseService`

### IShoppingCartModelFactory
- Impl: `ShoppingCartModelFactory` — src/Presentation/Nop.Web/Areas/Admin/Factories/ShoppingCartModelFactory.cs:23
  - Ctor: line 45
  - REPO: none
  - Other service-typed deps: `IBaseAdminModelFactory`, `ICountryService`, `ICustomerService`, `ICustomWishlistService`, `IDateTimeHelper`, `ILocalizationService`, `IPriceFormatter`, `IProductAttributeFormatter`, `IProductService`, `IShoppingCartService`, `IStoreService`
- Impl: `ShoppingCartModelFactory` — src/Presentation/Nop.Web/Factories/ShoppingCartModelFactory.cs:42
  - Ctor: line 101
  - REPO: none
  - Other service-typed deps: `IAddressModelFactory`, `IAddressService`, `IAttributeParser<CheckoutAttribute, CheckoutAttributeValue>`, `IAttributeService<CheckoutAttribute, CheckoutAttributeValue>`, `ICheckoutAttributeFormatter`, `ICountryService`, `ICurrencyService`, `ICustomerService`, `ICustomWishlistService`, `IDateTimeHelper`, `IDiscountService`, `IDownloadService`, `IGenericAttributeService`, `IGiftCardService`, `IHttpContextAccessor`, `ILocalizationService`, `IOrderProcessingService`, `IOrderTotalCalculationService`, `IPaymentPluginManager`, `IPaymentService`, `IPermissionService`, `IPictureService`, `IPriceFormatter`, `IProductAttributeFormatter`, `IProductService`, `IShippingService`, `IShoppingCartService`, `IShortTermCacheManager`, `IStateProvinceService`, `IStaticCacheManager`, `IStoreContext`, `IStoreMappingService`, `ITaxService`, `IUrlRecordService`, `IVendorService`, `IWebHelper`, `IWorkContext`

### IShoppingCartService
- Impl: `ShoppingCartService` — src/Libraries/Nop.Services/Orders/ShoppingCartService.cs:32
  - Ctor: line 72
  - REPO: `IRepository<ShoppingCartItem>` (sciRepository) @ line 91
  - Other service-typed deps: `IAclService`, `IActionContextAccessor`, `IAttributeParser<CheckoutAttribute, CheckoutAttributeValue>`, `IAttributeService<CheckoutAttribute, CheckoutAttributeValue>`, `ICurrencyService`, `ICustomerService`, `IDateRangeService`, `IDateTimeHelper`, `IEventPublisher`, `IGenericAttributeService`, `IGiftCardService`, `ILocalizationService`, `IPermissionService`, `IPriceCalculationService`, `IPriceFormatter`, `IProductAttributeParser`, `IProductAttributeService`, `IProductService`, `IShippingService`, `IShortTermCacheManager`, `IStaticCacheManager`, `IStoreContext`, `IStoreService`, `IStoreMappingService`, `IUrlHelperFactory`, `IUrlRecordService`, `IWorkContext`

### IStaticCacheManager
- Impl: `MemoryCacheManager` — src/Libraries/Nop.Core/Caching/MemoryCacheManager.cs:13
  - Ctor: line 33
  - REPO: none
- Impl: `DistributedCacheManager` — src/Libraries/Nop.Core/Caching/DistributedCacheManager.cs:12
  - Ctor: line 32 (protected; abstract class — parser only matched public ctors)
  - REPO: none
  - Other service-typed deps: `IDistributedCache`, `ICacheKeyManager`, `IConcurrentCollection<object>`

### IStoreContext
- Impl: `WebStoreContext` — src/Presentation/Nop.Web.Framework/WebStoreContext.cs:16
  - Ctor: line 39
  - REPO: `IRepository<Store>` (storeRepository) @ line 41
  - Other service-typed deps: `IHttpContextAccessor`

### IStoreMappingService
- Impl: `StoreMappingService` — src/Libraries/Nop.Services/Stores/StoreMappingService.cs:12
  - Ctor: line 27
  - REPO: `IRepository<StoreMapping>` (storeMappingRepository) @ line 29
  - Other service-typed deps: `INopDataProvider`, `IStaticCacheManager`, `IStoreContext`

### ITaxService
- Impl: `TaxService` — src/Libraries/Nop.Services/Tax/TaxService.cs:21
  - Ctor: line 47
  - REPO: none
  - Other service-typed deps: `IAddressService`, `ICheckVatService`, `ICountryService`, `ICustomerService`, `IEventPublisher`, `IGenericAttributeService`, `IGeoLookupService`, `ILogger`, `IStateProvinceService`, `IStoreContext`, `ITaxPluginManager`, `IWebHelper`, `IWorkContext`

### IVendorService
- Impl: `VendorService` — src/Libraries/Nop.Services/Vendors/VendorService.cs:13
  - Ctor: line 27
  - REPO: `IRepository<Customer>` (customerRepository) @ line 28
  - REPO: `IRepository<Product>` (productRepository) @ line 29
  - REPO: `IRepository<Vendor>` (vendorRepository) @ line 30

### IWebHelper
- Impl: `WebHelper` — src/Libraries/Nop.Core/WebHelper.cs:19
  - Ctor: line 33
  - REPO: none
  - Other service-typed deps: `IHostApplicationLifetime`, `IHttpContextAccessor`, `IUrlHelperFactory`

### IWorkContext
- Impl: `WebWorkContext` — src/Presentation/Nop.Web.Framework/WebWorkContext.cs:27
  - Ctor: line 58
  - REPO: none
  - Other service-typed deps: `IAuthenticationService`, `ICurrencyService`, `ICustomerService`, `IEventPublisher`, `IGenericAttributeService`, `IHttpContextAccessor`, `ILanguageService`, `IStoreContext`, `IUserAgentHelper`, `IVendorService`, `IWebHelper`

### IWorkflowMessageService
- Impl: `WorkflowMessageService` — src/Libraries/Nop.Services/Messages/WorkflowMessageService.cs:27
  - Ctor: line 54
  - REPO: none
  - Other service-typed deps: `IAddressService`, `IAffiliateService`, `ICustomerService`, `IEmailAccountService`, `IEventPublisher`, `ILanguageService`, `ILocalizationService`, `IMessageTemplateService`, `IMessageTokenProvider`, `IOrderService`, `IProductService`, `IQueuedEmailService`, `IStoreContext`, `IStoreService`, `ITokenizer`


## 6. Negative cases (F0 §10)

Inventory method: script on box #4 (`/tmp/f2-negatives.py`) inventoried 290 interfaces
and cross-checked implementations (transitive), injection sites, and registrations. The
script has known blind spots (it misses `record` implementations, `Lazy<T>`-wrapped
injections, namespace-qualified registrations, and some generic/inherited declarations),
so **every finding below was individually re-verified by grep/reading** unless stated.
Findings are grouped by mechanism; each carries file:line.

### 6.1 Interfaces with no implementation in-tree (genuine)

- **`ISearchProvider`** (`src/Libraries/Nop.Services/Catalog/ISearchProvider.cs:8`).
  Only `SearchPluginManager : PluginManager<ISearchProvider>` + type checks reference
  it. `LuceneSearchProvider : BasePlugin, IMiscPlugin` does **not** implement it. Plugin
  extension point with no in-tree implementer.
- **`IAdminMenuPlugin`**. Only `IPluginManager<IAdminMenuPlugin>` in `AdminMenu.cs:31,49`
  references it. Plugin extension point with no in-tree implementer.

### 6.2 Declared but never referenced (dead interface)

- **`IPagination<T>`** (`src/Presentation/Nop.Web.Framework/UI/Paging/IPageableModel.cs:53`,
  `: IPageableModel`). The sole `IPagination<` reference in all of `src` is its own
  declaration line. `IPagedList<T> : IList<T>` does not extend it, and
  `PagedList<T> : List<T>, IPagedList<T>` does not implement it. Genuinely dead in this
  checkout.

### 6.3 Service-locator resolution (indirect, unprovable statically)

- `IRoutePublisher` via `EngineContext.Current.Resolve<IRoutePublisher>()`
  (`ApplicationBuilderExtensions.cs:475`).
- `IWorkflowMessageService` via `EngineContext.Current.Resolve<IWorkflowMessageService>()`
  **inside the slice** — `ProductService.cs:1739,1774` (back-in-stock notification
  paths). Doubly unprovable: the resolved interface is also the one with the Brevo
  plugin override (§4.1.4).
- `ILogger` via `EngineContext.Current.Resolve<ILogger>()` in `EventPublisher` catch
  blocks (`EventPublisher.cs`, both `Publish`/`PublishAsync`).
- `IConsumer<TEvent>` via `EngineContext.Current.ResolveAll<IConsumer<TEvent>>()`
  (`EventPublisher.cs:23,60`); `ResolveAll` delegates to
  `GetServices(typeof(T))` (`NopEngine.cs:186-189`).
- `IDataProviderManager` via `serviceProvider.GetRequiredService<IDataProviderManager>()`
  inside `NopDbStartup.cs:60`.
- Settings factory lambda uses `GetRequiredService<IStoreContext>` /
  `GetRequiredService<ISettingService>` (`NopStartup.cs:276-286`).

### 6.4 Reflection discovery (FindClassesOfType / Activator / ResolveUnregistered)

Complete call-site list for `FindClassesOfType` in production code:

| Discovered contract | Site |
|---|---|
| `IConfig` | `ServiceCollectionExtensions.cs:74` |
| `IExternalAuthenticationRegistrar` | `ServiceCollectionExtensions.cs:288` |
| `ISettings` | `NopStartup.cs:276` (settings registration, §4) |
| `IConsumer<>` | `NopStartup.cs:311` (consumer registration, §4) |
| `IStartupTask` | `NopEngine.cs:40` |
| `IOrderedMapperProfile` | `NopEngine.cs:62` (AutoMapper discovery) |
| `INopStartup` | `NopEngine.cs:112,146` (DI bootstrap itself, §4) |

- `INopStartup` instances created via `Activator.CreateInstance`
  (`NopEngine.cs:112-122`).
- `IPermissionConfigManager`: `FindClassesOfType<IPermissionConfigManager>()` +
  `Activator.CreateInstance` (`PermissionService.cs:399-400`).
- `INameCompatibility`: `FindClassesOfType<INameCompatibility>()` +
  `EngineContext.Current.ResolveUnregistered(type)`
  (`NameCompatibilityManager.cs:32-35`); `ResolveUnregistered` activates unregistered
  types via reflection (`NopEngine.cs:196`).

### 6.5 Plugin-manager-resolved contracts (reflection, not DI)

Resolved through `PluginManager<T>` / the various `*PluginManager` classes, not through
constructor injection: `IPaymentMethod`, `IShippingRateComputationMethod`,
`IPickupPointProvider`, `IExchangeRateProvider`, `IWidgetPlugin`,
`IExternalAuthenticationMethod`, `IMultiFactorAuthenticationMethod`,
`IShipmentTracker`, `IScheduleTask`, `IPlugin`, `ISearchProvider` (§6.1).

`INopDataProvider` (3 implementations: MySql / MsSql / PostgreSql; 21 injection-site
files) is resolved via the `DataProviderManager` factory — see §6.6. `ISettings`
implementations are resolved via the settings/options pattern (§4), not per-type DI
registration.

### 6.6 Direct construction (statically visible `new`, runtime selection unprovable)

- **`IArtificialIntelligenceHttpClientHelper`** — 3 implementations (`DeepSeek` /
  `Gemini` / `ChatGptHttpClientHelper`), **no DI registration**, instantiated with
  `new` in a provider-enum switch (`ArtificialIntelligenceHttpClient.cs:33-35`). A DI
  resolver must not claim DI edges here; the `new` calls are statically provable
  direct-construction evidence.
- **`DataProviderManager.GetDataProvider`** — static factory switch with `new`
  (`DataProviderManager.cs:21-31`), selection driven by `Singleton<DataConfig>.Instance.DataProvider`
  (`DataProviderManager.cs:43-49`). The three `new` calls are visible; which provider
  runs is config-driven and unprovable statically.

### 6.7 Implementer resolved only as its concrete type

- **`RfqMessageService : WorkflowMessageService`**
  (`src/Plugins/Nop.Plugin.Misc.RFQ/Services/RfqMessageService.cs:23`) is registered as
  its concrete type (`PluginNopStartup.cs:21`, `AddScoped<RfqMessageService>()`) and
  injected as the concrete type (`RfqAdminController.cs:41,59`;
  `RfqService.cs:39,60`). It is **not** registered as `IWorkflowMessageService`. An
  interface→implementation resolver that enumerates all implementers would over-report
  the `IWorkflowMessageService` candidates (2 registered, 3 implementers).

### 6.8 Static singleton holder (outside DI)

- **`IPluginsInfo`** via `Singleton<IPluginsInfo>.Instance`
  (`PluginService.cs:55`; `ApplicationPartManagerExtensions.cs:60-63`, getter + setter).
  Set from outside DI; no constructor injection.

### 6.9 Delegate indirection

- `predicate?.Invoke(shipment)` — a `Func<>` predicate invocation, method parameter
  (`OrderService.cs:88`). Not reflection, but the callee is unprovable statically.
- `Func<ICacheKeyService, CacheKey>` cache-key factory parameters throughout
  `IRepository` (`IRepository.cs:26,37,49,62,75,85,98`) and `EntityRepository.cs:141`.

### 6.10 Config-conditional registrations

The 8 ambiguous registrations in §4.1 (cache switch, plugin overrides, bundling
branch, distributed-cache branch) — static analysis sees multiple candidates; runtime
selection is config/settings-driven.

### 6.11 Publish edges: event type known, consumers not

15 `_eventPublisher.Publish*` calls across the slice files (controllers + core
services), e.g. `OrderPlacedEvent` (`OrderProcessingService.cs:1590,1960`),
`OrderPaidEvent` (`:1097`), `OrderStatusChangedEvent` (`:1010`),
`ShoppingCartItemMovedToOrderItemEvent` (`:1313`). The published event types are
statically known; the consumers are reflection-resolved (§6.3/§6.4), so publish→consumer
edges are not statically provable. Slice files contain **no** other locator /
reflection / `dynamic` usage: the only hits in the 15 slice files (5 controllers, 5
public factories, 5 core services) were the two `Resolve<IWorkflowMessageService>`
calls (§6.3) and the `predicate?.Invoke` (§6.9).

### 6.12 Ruled out — checked, not findings

- `IEntityBuilder` — 138 `NopEntityBuilder<T>` subclasses.
- `INopConnectionStringInfo` — implemented by the `InstallModel` record (script blind
  spot: records).
- Model marker interfaces — implemented by records/classes: `ILocalizedModel` (36
  impl-decls), `ISettingsModel` (36), `IMetaTagsSupportedModel` (19),
  `IStoreMappingSupportedModel` (12), `IAclSupportedModel` (10), `IPluginModel` (8),
  `IConfigModel` (8), `ITranslationSupportedModel` (3), `IDiscountSupportedModel` (2),
  `IRouteValues` (2), `IPageableModel` / `IPagedModel` / `IPagingRequestModel` (1 each).
- `IAttributeFormatter` — only the generic `IAttributeFormatter<TAttribute,
  TAttributeValue>` exists (open-generic registered, §4); no non-generic twin.
- `IInstallationService` — injected as `Lazy<IInstallationService>`
  (`InstallController.cs:29,44`); `Lazy<>` is registered (§4).
- `ISummernoteHelper` — used via `@inject` in Razor view
  `Areas/Admin/Views/Shared/EditorTemplates/RichEditor.cshtml:3`.
- **Unused-service pass (focused):** all 167 `I*Service` / `I*Factory` interfaces have
  at least one reference outside their declaration / implementation / registration
  files and outside tests (name-mention pass). An injection-site-only pass (readonly
  fields + ctor-param patterns) found exactly one interface with zero direct injection
  sites: `ICacheKeyService` — benign, because it is a base contract inherited by
  `IStaticCacheManager` / `IShortTermCacheManager` and appears only as a
  `Func<ICacheKeyService, CacheKey>` delegate parameter (§6.9); consumers inject
  `IStaticCacheManager`. No implemented-but-unused service/factory interface exists
  in this corpus by either measure. 39 interfaces have exactly one injection site.

## 7. F1 baseline run

### 7.1 Build (box #4, read-only apart from build output)

```
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH DOTNET_CLI_TELEMETRY_OPTOUT=1
cd /data/workspace/cqrslens   # branch foundations/f1-spine @ 3fcbcba
dotnet build src/CqrsLens.Cli/CqrsLens.Cli.csproj -c Release --nologo -v q
```

Result: **0 errors, 10 warnings** (CLI-only build; pre-existing style warnings),
46.83 s. DLL: `src/CqrsLens.Cli/bin/Release/net8.0/cqrslens.dll`
(178,176 bytes). Tool reports `toolVersion: 1.1.0`, `schemaVersion: 1`.
.NET 8.0.427 at `~/.dotnet`. Note: the corpus targets net9.0 while the tool runs on
.NET 8 — text mode scans source only, no corpus compilation is involved, and no issue
was observed.

### 7.2 Runs (text mode, JSON, pinned `--generated-at`)

```
DLL=/data/workspace/cqrslens/src/CqrsLens.Cli/bin/Release/net8.0/cqrslens.dll
CORPUS=/data/workspace/corpora/nopcommerce
GEN="2026-01-01T00:00:00Z"
dotnet "$DLL" "$CORPUS" --format json -o <out> --no-banner --no-color --generated-at "$GEN"
```

One warmup + 3 timed runs, wall clock including process startup (same convention as
the F1 perf verification). Outputs in `/tmp/f2-nop-baseline/` on box #4.

| Run | Exit | Wall time (ms) | Output bytes | SHA-256 |
|---|---|---|---|---|
| warmup | 0 | — | 9,644,562 | `b2825eb8…` (same) |
| run1 | 0 | 3,732 | 9,644,562 | `b2825eb86ab15a59fb48430fa1eac7562637d5e383ae5b48351ef515f400b2c3` |
| run2 | 0 | 3,649 | 9,644,562 | `b2825eb86ab15a59fb48430fa1eac7562637d5e383ae5b48351ef515f400b2c3` |
| run3 | 0 | 3,688 | 9,644,562 | `b2825eb86ab15a59fb48430fa1eac7562637d5e383ae5b48351ef515f400b2c3` |

**Median: 3,688 ms. All 3 outputs byte-identical — the baseline is deterministic.**
stderr empty on all runs. (Roslyn mode was not run: the checklist asked for one
baseline run; text mode is the frozen baseline. A Roslyn-mode baseline can be added
later if the F2 brief needs it.)

### 7.3 Baseline numbers (from `run1.json`)

| Metric | Value |
|---|---|
| Projects | **35** (matches the `*.csproj` count, §3) |
| Files scanned (`fileCount` / `discoveredFileCount`) | **3,464** (matches the `.cs` count, §3) |
| Requests / handlers | **0 / 0** |
| Notifications / notification handlers | **0 / 0** |
| Behaviors | **0** |
| DbContexts | **0** (no EF Core — §3) |
| Endpoints | **0** |
| Validators | **105** (FluentValidation `*Validator` classes, e.g. `RequirementModelValidator` in `Nop.Plugin.DiscountRules.CustomerRoles.Validators` — the tool's validator detection fires on them) |
| `serviceCallEdges` | **2,446** |
| Relations | **3,234** — `calls` 2,446, `resolvesTo` 776, `references` 12 |
| Evidence entries (total across relations) | 6,830 |
| Checks / diagnostics / errors / warnings | 0 / 0 / 0 / 0 |
| Skipped files | 0 |
| `generatedAt` in output | `2026-01-01T00:00:00.0000000Z` (pinned) |
| `scanner` | `text` |

The zero-mediator result is itself a finding: on this corpus the F1 tool's entire
CQRS surface is empty, and the model consists of project references, DI resolutions,
and service-call edges. That is exactly the surface F2's widening work must build on.

### 7.4 Repo-integrity verification after the run

- CqrsLens repo `/data/workspace/cqrslens`: HEAD still `3fcbcba`
  ("Add the architecture spine…"), `git status --porcelain` shows **only** the 7
  pre-existing untracked doc files (`BRIEF_F1.md`, `BRIEF_F1_PERF.md`,
  `CHECKPOINT_F1_CP1.md`, `CHECKPOINT_F1_CP2.md`, `CHECKPOINT_F1_CP3.md`,
  `CHECKPOINT_F1_PERF.md`, `F0_METAMODEL_v2.1.md`). `bin/` and `obj/` are gitignored.
  No modifications, no commits, no pushes.
- Corpus `/data/workspace/corpora/nopcommerce`: `git status` clean, HEAD
  `e7a52b91`, `git describe --tags` → `release-4.90.0`.

## 8. Caveats and open items

1. **Patch-tag choice (§1):** pinned to `release-4.90.0`; `4.90.1`–`4.90.8` exist. Amend
   before the F2 brief if the latest patch is wanted.
2. **Brief correction (§4):** `DependencyRegistrar` does not exist in 4.90; the F2
   brief must describe the `INopStartup` mechanism.
3. **Text-mode baseline only (§7.2):** no Roslyn-mode baseline was run.
4. **Extraction scope (§5):** Table A covers direct field calls + one level of helper
   attribution. It does not trace transitive calls inside services (that is Table B's
   ctor evidence + the tool's own `calls` edges). Call-site lines are occurrences of
   `_<field>.`, not resolved method symbols.
5. **Open micro-items:** none remaining. Every item opened during the audit
   (`RfqMessageService` registration, `IPagination<T>`, the unused-service pass, the
   `FindClassesOfType` / locator site lists, the ProductService 20-repo and OrderService
   9-repo recounts) was closed with source evidence above.

## Appendix — audit artifacts on box #4

- Corpus: `/data/workspace/corpora/nopcommerce`
- Scripts: `/tmp/f2-extract.py`, `/tmp/f2-tableb.py`, `/tmp/f2-negatives.py`,
  `/tmp/f2-render.py`
- Raw data: `/tmp/f2-actions.json`, `/tmp/f2-tableb.json`, `/tmp/f2-negatives.json`,
  `/tmp/f2-mediatr-hits.txt`, `/tmp/f2-dupes.txt`, `/tmp/f2-dupes2.txt`,
  `/tmp/f2-tables.md` (sha256 `cd353590807d8aceb2cd43c7b15042aacb83a0391b64a559356a4f3224b0be2a`)
- Baseline outputs: `/tmp/f2-nop-baseline/{warmup,run1,run2,run3}.json` (+ stdout/stderr)
- Local copies of the three extraction scripts: `/tmp/f2-extract.py`,
  `/tmp/f2-tableb.py`, `/tmp/f2-negatives.py` on this VM.
