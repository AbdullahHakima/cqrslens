# CHECKPOINT_F2_CP1 — Foundation 2 CP1: Role & Contract Spine Inventory

**Status**: CP1 implementation & verification COMPLETE (`text` + `roslyn`).  
**STOP Trigger Fired (CP1 → CP2 boundary per `BRIEF_F2.md` R-DI, lines 278–284)**: **STOPPING before CP2** to obtain an explicit user decision on test-project exclusion in `ServiceResolutionIndexBuilder` (`BaseNopTest.cs` in `Nop.Tests` creates false ambiguity on slice interfaces `IPictureService` and `IAuthenticationService`, plus 4 other registered interfaces and 4 declared-only test doubles).

---

## 1. F1 Baseline Reproduction (Clean Baseline Worktree `/tmp/cqrslens-f1base` @ `3fcbcba`)

Target corpus: `/data/workspace/corpora/nopcommerce` @ `e7a52b9199f9c7651839e92826df47f668f3c767` (`git status --short` clean).

| Mode | Warmup (ms) | Run 1 (ms) | Run 2 (ms) | Run 3 (ms) | Median (ms) | JSON Bytes | SHA-256 |
|---|---:|---:|---:|---:|---:|---:|---|
| `text` | 4464.5 | 3542.2 | 3424.3 | 3460.1 | **3460.1** | 9,644,562 | `b2825eb86ab15a59fb48430fa1eac7562637d5e383ae5b48351ef515f400b2c3` |
| `roslyn` | 9893.0 | 10127.2 | 9553.8 | 9524.1 | **9553.8** | 9,644,564 | `e57ffe0ba849b421540eee870407a43d054ca9cae512bcf8f7f4b7756cbcd966` |

- Baseline counts (identical between `text` and `roslyn`; only `"scanner"` differs):
  - `35` projects, `3,464` discovered & analyzed files (`0` skipped)
  - `requests=0`, `handlers=0`, `validators=105`, `behaviors=0`, `dbContexts=0`, `notifications=0`, `notificationHandlers=0`, `endpoints=0`, `errors=0`, `warnings=0`, `knownFindings=0`, `diagnostics=0`
  - `relations=3,234` (`references=12`, `resolvesTo=776`, `calls=2,446`), total `occurrences=6,830`

### CP1 Post-Implementation Timing & JSON Delta (`nopCommerce`)

- `text` benchmark (warmup + 3 runs): `[3371, 3661, 3745]` ms, median **`3661 ms`** (+200.9 ms vs F1 baseline median, ~+5.8%).
- Top-level JSON structure is unchanged at CP1 (additive `components` / `unproven` JSON sections are deferred to CP2).
- `relations` count stays `3,234` (`references=12`, `resolvesTo=776`, `calls=2,446`); `245` `resolvesTo` target references promoted automatically via `ArchitectureSpine.PromoteReferencesToComponent` from `"external"` to `"component"` (`531` remain `"external"`).
- `nop-text.json` vs `nop-roslyn.json` `relations` arrays are **100% equal** (`len = 3,234`, `equal = True`).

---

## 2. CP1 Test-Project Registration Investigation (`R-DI` STOP Trigger)

`ServiceResolutionIndexBuilder` currently scans all projects including `Nop.Tests` (`Layer == "Tests"`), which produces `926` `ServiceResolutions` entries with `150` `IsAmbiguous = true`.

`src/Tests/Nop.Tests/BaseNopTest.cs` contains `254` `.Add*` lines (`209` two-argument `services.Add*<I, Impl>()` calls):
- ~200 registrations identically mirror production registrations and deduplicate cleanly.
- **6 interfaces registered in `BaseNopTest.cs` introduce `Test*` stubs that create false ambiguity (`IsAmbiguous = true`, preventing production resolution)**:
  1. **`Nop.Services.Media.IPictureService`** — **Table B slice interface**: `PictureService` (`NopStartup.cs:293`) vs `TestPictureService` (`BaseNopTest.cs:417`)
  2. **`Nop.Services.Authentication.IAuthenticationService`** — **slice controller dependency**: `CookieAuthenticationService` (`NopStartup.cs:210`) vs `TestAuthenticationService` (`BaseNopTest.cs:365`)
  3. **`Nop.Services.Messages.ISmtpBuilder`**: `SmtpBuilder` (`NopStartup.cs`) vs `TestSmtpBuilder` (`BaseNopTest.cs:350`)
  4. **`Nop.Services.ScheduleTasks.ITaskScheduler`**: `TaskScheduler` (`NopStartup.cs`) vs `TestTaskScheduler` (`BaseNopTest.cs:468`)
  5. **`Nop.Data.IDataProviderManager`**: `DataProviderManager` (`NopDbStartup.cs:58`) vs `TestDataProviderManager` (`BaseNopTest.cs:256`)
  6. **`IConventionSet`**: `NopConventionSet` (`NopDbStartup.cs:46`) vs `NopTestConventionSet` (`BaseNopTest.cs:451`)
- In addition, **4 declared-only (`fromRegistration=false`) interfaces** have `Nop.Tests` test doubles colliding in declarations: `IEngine` (`NopEngine` vs `NopTestEngine`), `IExchangeRateProvider` (`EcbExchangeRateProvider` vs `TestExchangeRateProvider`), `IDiscountRequirementRule` (`CustomerRoleDiscountRequirementRule` vs `TestDiscountRequirementRule`), and `IPickupPointProvider` (`PickupInStoreProvider` vs `PickupPointTestProvider`). (Plus `IScheduleTask`, `IShippingRateComputationMethod`, `IPermissionConfigManager`, `IPlugin` already have multiple production implementations.)

**Empirical CP1 Impact**: Because `IPictureService` and `IAuthenticationService` are marked `IsAmbiguous = true`, neither `PictureService` nor `CookieAuthenticationService` receives `ComponentRole.Service` through those interfaces at CP1, and `BaseNopTest.cs:514` (`services.AddTransient<ProductModelFactoryTests.ProductModelFactoryForTest>()`) causes `1` `Nop.Tests` class (`ProductModelFactoryForTest`) to enter as a `Service` component. Per `BRIEF_F2.md` R-DI, **no unprompted test-exclusion rule was added**; execution stops here after CP1 to ask how test projects should be treated.

---

## 3. CP1 Role Design & Corpus Inventory (`nopCommerce`)

### Design Highlights (Approved R-REPO Option (a))
- **R-CTRL** (`ControllerRoleRules`): Concrete (non-`abstract`, non-`static`), non-`[NonController]` classes inheriting transitively from ASP.NET `Controller` / `ControllerBase` / `ApiController`, OR carrying `[ApiController]` / `[Controller]` / `[Route]` class attributes or `[Http*]` / `[Route]` / `[AcceptVerbs]` action attributes inside the class body. Never matched by `*Controller` filename/classname alone.
- **R-SVC** (`ServiceAndRepositoryRoleRules`): Concrete classes appearing as the implementation target of:
  1. Unambiguous `ServiceResolutions` (`FromRegistration`, non-repository contract, `DeclaredInScan`)
  2. Explicit DI concrete/typed-client/Autofac-self registrations (`ServiceRegistrationParser.ParseExplicitRegistrations`: `AddScoped<T>` / `AddTransient<T>` / `AddSingleton<T>` / `AddHttpClient<...>` / `typeof(T)` / factory-lambda `new T(...)` / Autofac `RegisterType<T>()`)
  3. `ServiceCallEdges.To` via non-repository contracts
  - Excludes `abstract`/`static`, `DbContext`, `Settings`/`Config` (`*Settings`/`*Config` suffix or transitive `ISettings`/`IConfig`/`IOptions<T>`/`IOptionsSnapshot<T>`/`IOptionsMonitor<T>`), and external/framework types.
- **R-REPO (Option (a))** (`ServiceAndRepositoryRoleRules`): Concrete classes that both implement a repository contract (`I[A-Z]*Repository`) and resolve unambiguously (`!IsAmbiguous && Implementation != null && DeclaredInScan`) in `ServiceResolutions`. Using/calling `I*Repository` never confers `Service` on its own; dual-role classes merge onto one `ComponentId`.
- **R-EXP** (`ControllerRoleRules.ApplyExposes`): Links `ComponentRole.Controller` → `EndpointNode` via `RelationKind.Exposes` (`EvidenceRule.ControllerEndpointExposure`) for detected controller endpoints carrying `ControllerId`.

### CP1 nopCommerce Reconciliation (`Text == Roslyn`)

| Role / Metric | Text | Roslyn | Parity | Notes |
|---|---:|---:|---|---|
| `ComponentRole.Controller` | **128** | **128** | True | Exact `133 *Controller.cs` − `5` abstract bases (`91` Presentation + `42` Plugins) |
| `ComponentRole.Service` | **279** | **279** | True | `278` Prod + `1` Tests (`ProductModelFactoryForTest`); `108` from `*Service.cs` + `171` non-`*Service.cs` |
| `ComponentRole.Repository` | **1** | **1** | True | `Nop.Data.EntityRepository\`1` (from `3 *Repositor*.cs`) |
| `ComponentRole.Validator` | **105** | **105** | True | Unchanged F1 baseline |
| `RelationKind.Exposes` | **0** | **0** | True | Expected at CP1 (`Endpoints = 0`; F2 endpoint discovery is CP2) |
| Multi-role components | **0** | **0** | True | Tested separately on fixtures |
| Duplicate `ComponentId`s | **0** | **0** | True | Verified across all `497` spine components (incl. `105` validators) |
| Settings/Config/Framework leaks | **0** | **0** | True | `ILogger`, `IHttpContextAccessor`, `*Settings`, `*Config` = `0` |

#### Detailed File-by-File Reconciliation
- **`133 *Controller.cs`** = **`128`** concrete `Controller` components + **`5`** excluded `abstract` base classes:
  - `src/Presentation/Nop.Web.Framework/Controllers/BaseController.cs`
  - `src/Presentation/Nop.Web.Framework/Controllers/BasePaymentController.cs`
  - `src/Presentation/Nop.Web.Framework/Controllers/BasePluginController.cs`
  - `src/Presentation/Nop.Web/Areas/Admin/Controllers/BaseAdminController.cs`
  - `src/Presentation/Nop.Web/Controllers/BasePublicController.cs`
  - `0` controllers outside `*Controller.cs`.
- **`210 *Service.cs`** = **`108`** `ComponentRole.Service` classes + **`96`** interface-only `I*Service.cs` + **`1`** `abstract` (`CacheKeyService.cs`) + **`1`** `enum` (`Nop.Plugin.Shipping.UPS/Domain/DeliveryService.cs`) + **`4`** concrete `*Service` classes currently blocked by `IsAmbiguous = true` in `ServiceResolutions`:
  - `CookieAuthenticationService` (`IAuthenticationService` vs `BaseNopTest.cs` `TestAuthenticationService`)
  - `PictureService` (`IPictureService` vs `BaseNopTest.cs` `TestPictureService`)
  - `WorkflowMessageService` & `BrevoMessageService` (`IWorkflowMessageService` conflicted between `NopStartup.cs` and `Nop.Plugin.Misc.Brevo/Infrastructure/NopStartup.cs`)
  - Plus **`171`** `Service` components from non-`*Service.cs` files: `*Factory` (`92`), Other non-`*Service` (`30`, e.g., `MemoryCacheLocker`, `ConcurrentTrie`, `WebAppTypeFinder`, `NopMySql5TypeMap`, `NopGeneratorAccessor`), `*Manager` (`22`), `*HttpClient` (`13`), `*Helper` (`8`), `*Provider` (`5`), `Nop.Tests` doubles (`1`: `ProductModelFactoryForTest`).
- **`3 *Repositor*.cs`** = **`1`** `ComponentRole.Repository` (`Nop.Data.EntityRepository\`1` at `src/Libraries/Nop.Data/EntityRepository.cs:15`) + `IRepository.cs` (interface) + `EntityRepositoryTests.cs` (`Nop.Tests`).

### `GoldenCorpus` `Exposes` Verification
- `text` and `roslyn` both emit `2` identical `RelationKind.Exposes` relations (`OrdersController.cs:10:5#0` and `OrdersController.cs:13:5#0`) from `Component Golden.Api/Golden.Api.csproj::Golden.Api.Controllers.OrdersController`.

---

## 4. Test Suite, Build Health & Diff Inventory

- **Full test suite** (`dotnet test CqrsLens.sln -c Release --no-build --nologo`):
  - **Passed: `673`**, **Skipped: `68`** (pre-existing corpus tests), **Failed: `0`**, **Total: `741`** (includes `10/10` new `F2RoleInventoryTests` covering both `Text` and `Roslyn`).
- **Build warnings**:
  - `CqrsLens.Core`: **0 warnings**.
  - Solution-wide: **0 new warnings** in created/modified source or test files (only pre-existing `CS9057` analyzer/compiler version notice and pre-existing `Reporting`/`Cli`/`Tests` warnings).
- **Modified / Created Files (`git status --short`)**:
  - Modified (`9` files, `+351 / -47`):
    - [ComponentRole.cs](file:///data/workspace/cqrslens/src/CqrsLens.Core/Spine/ComponentRole.cs)
    - [RelationKind.cs](file:///data/workspace/cqrslens/src/CqrsLens.Core/Spine/RelationKind.cs)
    - [EvidenceRule.cs](file:///data/workspace/cqrslens/src/CqrsLens.Core/Spine/EvidenceRule.cs)
    - [EndpointInfo.cs](file:///data/workspace/cqrslens/src/CqrsLens.Core/Architecture/EndpointInfo.cs)
    - [JsonSpineNames.cs](file:///data/workspace/cqrslens/src/CqrsLens.Reporting/Json/JsonSpineNames.cs)
    - [ControllerEndpointDiscovery.cs](file:///data/workspace/cqrslens/src/CqrsLens.Analysis/Slicing/Endpoints/ControllerEndpointDiscovery.cs)
    - [CSharpSyntax.cs](file:///data/workspace/cqrslens/src/CqrsLens.Analysis/Text/CSharpSyntax.cs)
    - [ServiceRegistrationParser.cs](file:///data/workspace/cqrslens/src/CqrsLens.Analysis/Resolution/ServiceRegistrationParser.cs)
    - [Analyzer.cs](file:///data/workspace/cqrslens/src/CqrsLens.Analysis/Analyzer.cs)
  - Created (`7` files):
    - [ExplicitServiceRegistration.cs](file:///data/workspace/cqrslens/src/CqrsLens.Analysis/Resolution/ExplicitServiceRegistration.cs)
    - [ScannedComponentEntry.cs](file:///data/workspace/cqrslens/src/CqrsLens.Analysis/Roles/ScannedComponentEntry.cs)
    - [ScannedTypeCatalog.cs](file:///data/workspace/cqrslens/src/CqrsLens.Analysis/Roles/ScannedTypeCatalog.cs)
    - [ControllerRoleRules.cs](file:///data/workspace/cqrslens/src/CqrsLens.Analysis/Roles/ControllerRoleRules.cs)
    - [ServiceAndRepositoryRoleRules.cs](file:///data/workspace/cqrslens/src/CqrsLens.Analysis/Roles/ServiceAndRepositoryRoleRules.cs)
    - [ComponentRoleDiscovery.cs](file:///data/workspace/cqrslens/src/CqrsLens.Analysis/Roles/ComponentRoleDiscovery.cs)
    - [F2RoleInventoryTests.cs](file:///data/workspace/cqrslens/tests/CqrsLens.Tests/F2RoleInventoryTests.cs)

---

## 5. Reproduction Commands

```bash
DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet build CqrsLens.sln -c Release --nologo
DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet test CqrsLens.sln -c Release --no-build --nologo

DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet src/CqrsLens.Cli/bin/Release/net8.0/cqrslens.dll \
  /data/workspace/corpora/nopcommerce --analysis text --format json --generated-at 2026-11-15T00:00:00Z --no-banner -o /tmp/f2-cp1-out/nop-text.json

DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH dotnet src/CqrsLens.Cli/bin/Release/net8.0/cqrslens.dll \
  /data/workspace/corpora/nopcommerce --analysis roslyn --format json --generated-at 2026-11-15T00:00:00Z --no-banner -o /tmp/f2-cp1-out/nop-roslyn.json
```
