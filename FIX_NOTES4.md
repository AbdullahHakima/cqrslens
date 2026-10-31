# FIX NOTES — Phase 4 (graph depth: from inventory to flow)

## Summary

Phase 4 adds the flow layer the inventory lacked, without changing a single frozen
inventory count:

- **A — call-site tracking.** A text-based dispatch scanner resolves `Send` /
  `SendAsync` / `Publish` / `PublishAsync` / `InvokeAsync` call sites to their
  target type for `new X(...)`, generic `Send<X>(...)` / `InvokeAsync<X>(...)`,
  and variables whose declared type is resolvable from the enclosing class.
- **B — reachability warnings.** Two new checks, `UnreachableRequest` and
  `UnpublishedNotification`, at **warning severity only**. Unresolved sends are
  never guessed into an edge; they are counted and named in the finding text.
  Sends from test projects never count as production reachability.
- **C — solution-wide flow edges.** Five edge kinds are built and exposed in the
  JSON contract: endpoint → request, request → handler, handler → request,
  handler → notification, handler → DbContext (type level).

Baseline **359 passing → 368 passing** (9 new Phase 4 test cases, 0 pre-existing
failures). `--strict` still exits 0 everywhere it did. Northwind performance
guard holds (text ≤ 5 s, roslyn ≤ 15 s).

## Baseline / final `dotnet test`

```
before:  Passed! - Failed: 0, Passed: 359, Skipped: 0, Total: 359
after:   Passed! - Failed: 0, Passed: 368, Skipped: 0, Total: 368
```

## Edge model

`src/CqrsLens.Core/Architecture/` (one type per file, per the boundary test):

| Type | Meaning |
| --- | --- |
| `FlowEdgeKind` | `EndpointToRequest`, `RequestToHandler`, `HandlerToRequest`, `HandlerToNotification`, `HandlerToDbContext` |
| `FlowEdge` | `Kind`, `From`, `To`, `ProjectName`, `Location` — both ends are full type names |
| `DispatchCallSite` | a **resolved** dispatch: `TargetFullName`, `TargetKind` (request/notification), `SenderFullName`, `ProjectName`, `IsTestProject`, `Location` |
| `UnresolvedDispatch` | a dispatch-looking call whose target could not be resolved: `Method`, `ReceiverName`, `ProjectName`, `IsTestProject`, `Location` |
| `AnalysisResult` | new `FlowEdges`, `DispatchCallSites`, `UnresolvedDispatches`, internal `FlowAnalyzed` |

`FlowAnalyzer` (`src/CqrsLens.Analysis/Flow/FlowAnalyzer.cs`) runs after
`DeepSliceAnalyzer` (so endpoints, handlers and DbContexts are linked) and
before the architecture checks. It builds the shape indexes, drives
`DispatchCallSiteScanner` once per file, then adds the structural edges
(endpoint → request from `EndpointInfo`, request → handler from the inventory,
handler → DbContext from handler dependencies / masked body references).

The five edge kinds map to JSON as `endpointToRequest`, `requestToHandler`,
`handlerToRequest`, `handlerToNotification`, `handlerToDbContext`.

### JSON contract (additive; `schemaVersion` stays 1)

```json
"flowEdges": [
  { "kind": "endpointToRequest", "from": "OrdersController.Create",
    "to": "App.Features.CommandA", "project": "App", "location": { "file": "...", "line": 13 } }
],
"dispatchCallSites": [
  { "target": "App.Features.CommandB", "targetKind": "request",
    "sender": "App.Features.CommandAHandler", "project": "App",
    "testProject": false, "location": { "file": "...", "line": 20 } }
],
"unresolvedDispatches": [
  { "method": "Send", "receiver": "_mediator", "project": "App",
    "location": { "file": "...", "line": 87 } }
]
```

`summary` gains `flowEdges`, `dispatchCallSites`, `unresolvedDispatches`.
`JsonReportDocumentReader` defaults the three lists, so old diff files still
read. No diagram or reporting surface was added (out of scope); the existing
agent/HTML surfaces are unchanged and the new checks render through the generic
check writers.

## Problem A — dispatch shapes resolved

`DispatchCallSiteScanner` matches `.` + one of the five dispatch method names and
resolves the target type three ways; edges are created only when the target
resolves to an **inventoried** request/notification.

| Shape | Resolution | Example |
| --- | --- | --- |
| `Send(new X(...))` | `new` type in the first argument | `Mediator.Send(new GetCategoriesListQuery())` |
| `Send(x)` / `SendAsync(x)` | `x`'s declared type from `ReceiverTypeMapBuilder` (class body: fields, parameters, locals, `var x = new X(...)`; fails closed on sibling-scope conflicts) | `var command = new SetStockConfirmedOrderStatusCommand(...); await mediator.Send(command);` |
| `Send<X>(...)` / `InvokeAsync<X>(...)` | generic type argument | `bus.InvokeAsync<BusCommand>(...)` |
| `Publish(new X(...))` / `PublishAsync(new X(...))` | notification target | `_publisher.Publish(new CustomerCreated {...})` |

Receiver-agnostic: martinothamar `IMediator`, Wolverine `IMessageBus`, MediatR
`ISender`/`IPublisher` and application wrappers all work, because the edge is
anchored on the resolved target type, not on the variable name. A resolved
target that is a **concrete** type but not an inventoried request/notification
(an integration event, a DTO, the open-generic `IdentifiedCommand<T,R>` wrapper)
produces no edge and is **not** counted as unresolved — it is a resolved
dispatch to a non-CQRS type.

## Problem B — honest reachability

`FlowReachabilityChecks` (`src/CqrsLens.Analysis/Checks/FlowReachabilityChecks.cs`):

- `UnreachableRequest` (warning): a request with ≥ 1 handler and no **production**
  dispatch call site (a `DispatchCallSite` with `IsTestProject == false`, or an
  endpoint → request edge from a non-test project).
- `UnpublishedNotification` (warning): a notification with ≥ 1 handler and no
  production publisher.

Honesty rules implemented:

1. **No fabricated edges.** An edge requires a statically resolved target. A
   runtime-typed variable (`IRequest x; Send(x)`), an interface cast
   (`Send((IRequest)o)`), a type parameter, or an external type is recorded as an
   `UnresolvedDispatch`, never an edge.
2. **Unresolved accounting.** The finding message ends with
   *"N unresolved dispatch call(s) in the solution could not be resolved and are
   not counted as call sites."* The full population is in the JSON
   `unresolvedDispatches` array (method, receiver, project, location), so a
   shape with no resolved call site is never *silently* branded dead.
3. **Tests don't count.** `IsTestProject` is derived from `LayerClassifier`
   (`Layer == "Tests"`). A shape reached only from test projects is flagged, and
   the message adds *"Only test-project call site(s) were found."*
4. **Warning severity only.** No new error can be produced; no `--strict`
   outcome changes.

The checks run only when the flow pass actually ran (`AnalysisResult.FlowAnalyzed`),
so a hand-built model with no scan and no flow data is not treated as a solution
where nothing is dispatched.

## Problem C — edges built

| Edge | Source |
| --- | --- |
| endpoint → request | `EndpointInfo.TargetRequestFullName` (existing endpoint linking, now an edge and in JSON) |
| request → handler | the existing inventory edge, unchanged, now also emitted as a `FlowEdge` |
| handler → request | `Send`/`SendAsync`/`InvokeAsync` inside a request **or notification** handler |
| handler → notification | `Publish`/`PublishAsync` inside a handler |
| handler → DbContext (type level) | a handler whose constructor dependency or masked body references a discovered DbContext type |

Per-`DbSet` property-level edges are **not** implemented: the corpora handlers use
abstractions (`INorthwindDbContext`, repositories, `ISqlConnectionFactory`) rather
than concrete DbContext types, so a property-level edge would need receiver
inference beyond the type level and risk false positives. The type-level edge is
proven by a fixture (see below).

## Frozen inventory and counts (text; roslyn spot-checked)

`before` = Phase 3 committed scans; `after` = this phase. TEXT and ROSLYN produce
byte-identical requests/handlers/validators/notifications/edges/unresolved on all
four corpora.

| Corpus | Req | Han | Val | Beh | Ctx | Notif | NotifHan | Endp | Errors | Warnings |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| northwindtraders | 19→19 | 19→19 | 4→4 | 2→2 | 2→2 | 1→1 | 1→1 | 18→18 | 0→0 | 8→**8** |
| eShop Ordering | 8→8 | 8→8 | 4→4 | 3→3 | 1→1 | 7→7 | 7→7 | 0→0 | 0→0 | 5→**15** |
| eShopOnWeb | 2→2 | 2→2 | 0→0 | 0→0 | 2→2 | 0→0 | 0→0 | 2→2 | 0→0 | 0→**0** |
| modular-monolith | 105→105 | 105→105 | 5→5 | 0→0 | 4→4 | 86→86 | 30→30 | 51→51 | 0→0 | 133→**205** |
| ABP | 0→0 | 0→0 | 13→13 | 0→0 | 80→80 | 0→0 | 0→0 | 0→0 | 0→0 | 4→**4** |

modular-monolith scanned with `--markers ICommand,IQuery`; the others with
default markers. **Every inventory count and every error count is unchanged.**
The only warning deltas are the new reachability codes; nothing was removed or
re-worded (verified by set-differencing the check tuples before/after).

### Edges and dispatch call sites

| Corpus | endpointToRequest | requestToHandler | handlerToRequest | handlerToNotification | handlerToDbContext | resolved sites | unresolved |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| northwindtraders | 18 | 19 | 0 | 1 | 0 | 16 | 4 |
| eShop Ordering | 0 | 8 | 0 | 0 | 0 | 5 | 4 |
| eShopOnWeb | 2 | 2 | 0 | 0 | 0 | 2 | 0 |
| modular-monolith | 51 | 105 | 0 | 7 | 0 | 7 | 26 |
| ABP | 0 | 0 | 0 | 0 | 0 | 0 | 0 |

`handlerToRequest` is 0 on the sampled corpora because their chained requests are
dispatched only through the erasing abstractions below. Handler → DbContext is 0
for the same reason (handlers use context *interfaces*).

## New warnings itemized (with grep evidence)

No new warning fires on **northwindtraders**, **eShopOnWeb** or **ABP**.
ABP has 0 requests and 0 notifications, so the flow pass returns early there.

### eShop Ordering — 3 `UnreachableRequest` + 7 `UnpublishedNotification`

| Warning | Target | Hiding pattern / evidence |
| --- | --- | --- |
| UnreachableRequest | `CreateOrderCommand` | Idempotency wrapper: dispatched as `IdentifiedCommand<CreateOrderCommand, bool>`, not the concrete command. `grep -n "IdentifiedCommand<CreateOrderCommand" Ordering.API/Apis/OrdersApi.cs` → `146: var requestCreateOrder = new IdentifiedCommand<CreateOrderCommand, bool>(...)`. The inner send is in the open-generic decorator: `grep -n "Send(command" Ordering.API/Application/Commands/IdentifiedCommandHandler.cs` → `87: var result = await _mediator.Send(command, cancellationToken);` (`command` is `T`, dropped from the concrete inventory in Phase 2). |
| UnreachableRequest | `ShipOrderCommand` | Same wrapper: `OrdersApi.cs:61` `new IdentifiedCommand<ShipOrderCommand, bool>(command, requestId)`. |
| UnreachableRequest | `CreateOrderDraftCommand` | `OrdersApi.cs:115` `return await services.Mediator.Send(command);`; `command` is reused across sibling actions (`OrdersApi.cs:24,53,106`) so the class-wide receiver map conflicts and fails closed. |
| UnpublishedNotification | `BuyerAndPaymentMethodVerifiedDomainEvent`, `OrderCancelledDomainEvent`, `OrderShippedDomainEvent`, `OrderStartedDomainEvent`, `OrderStatusChangedToAwaitingValidationDomainEvent`, `OrderStatusChangedToPaidDomainEvent`, `OrderStatusChangedToStockConfirmedDomainEvent` (7) | Generic domain-event dispatcher publishes an `IDomainEvent` loop variable: `grep -n "mediator.Publish(domainEvent)" Ordering.Infrastructure/MediatorExtension.cs` → `19: await mediator.Publish(domainEvent);`. No concrete notification type is written at any publish site. |

### modular-monolith — 54 `UnreachableRequest` + 18 `UnpublishedNotification`

All 54 are dispatched through **type-erasing custom abstractions**, not through a
MediatR `Send` with a concrete argument:

- `CommandsExecutor.Execute(ICommand command)` / `Execute<TResult>(ICommand<TResult>)` forwards an interface-typed value to MediatR:
  `Modules/Meetings/Infrastructure/Configuration/Processing/CommandsExecutor.cs:14 await mediator.Send(command);`
  Called by Quartz jobs, e.g. `Modules/Payments/Infrastructure/Configuration/Processing/Inbox/ProcessInboxJob.cs:10 await CommandsExecutor.Execute(new ProcessInboxCommand());`
- Module facades `…Module.ExecuteCommandAsync/ExecuteQueryAsync(new X(...))`:
  `API/…/UserAccess/AuthenticatedUserController.cs:27 var user = await _userAccessModule.ExecuteQueryAsync(new GetAuthenticatedUserQuery());`
- Internal-command scheduler `_commandsScheduler.EnqueueAsync(new X(...))`:
  `Modules/Payments/Application/Subscriptions/CreateSubscription/SubscriptionCreatedEnqueueEmailConfirmationHandler.cs:29 await _commandsScheduler.EnqueueAsync(new SendSubscriptionCreationConfirmationEmailCommand(...));`
- A minority are constructed **only in test projects** (verifiably not production-reachable), e.g. `GetMeetingGroupProposalQuery` (`src/Tests/IntegrationTests/CreateMeetingGroup/CreateMeetingGroupTests.cs`), `CreatePayerCommand` / `GetPayerQuery` (`Modules/Payments/Tests/IntegrationTests/Payers/PayerTests.cs`).

Full list (54): Administration — `GetMeetingGroupProposalQuery`,
`RequestMeetingGroupProposalVerificationCommand`, `CreateMemberCommand`,
`GetMemberQuery`, `ProcessInboxCommand`, `ProcessInternalCommandsCommand`,
`ProcessOutboxCommand`; Meetings — `GetMeetingCommentingConfigurationQuery`,
`GetMeetingCommentLikersQuery`, `GetMeetingCommentsQuery`,
`AcceptMeetingGroupProposalCommand`, `GetMeetingGroupProposalQuery`,
`CreateNewMeetingGroupCommand`, `SendMeetingGroupCreatedEmailCommand`,
`SetMeetingGroupExpirationDateCommand`, `MarkMeetingAttendeeFeeAsPayedCommand`,
`SendMeetingAttendeeAddedEmailCommand`,
`ChangeSubscriptionExpirationDateForMemberCommand`, `CreateMemberCommand`,
`ProcessInboxCommand`, `ProcessInternalCommandsCommand`, `ProcessOutboxCommand`;
Payments — `CreateMeetingFeeCommand`, `GetMeetingFeesQuery`,
`MarkMeetingFeeAsPaidCommand`, `CreatePayerCommand`, `GetPayerQuery`,
`GetPriceListItemsQuery`, `CreateSubscriptionCommand`, `ExpireSubscriptionCommand`,
`ExpireSubscriptionPaymentCommand`, `ExpireSubscriptionPaymentsCommand`,
`ExpireSubscriptionsCommand`, `GetSubscriptionDetailsQuery`,
`GetSubscriptionPaymentsQuery`, `RenewSubscriptionCommand`,
`SendSubscriptionCreationConfirmationEmailCommand`,
`SendSubscriptionRenewalConfirmationEmailCommand`, `ProcessInboxCommand`,
`ProcessInternalCommandsCommand`, `ProcessOutboxCommand`; Registrations —
`GetUserRegistrationQuery`, `SendUserRegistrationConfirmationEmailCommand`,
`ProcessInboxCommand`, `ProcessInternalCommandsCommand`, `ProcessOutboxCommand`;
UserAccess — `AuthenticateCommand`, `GetUserPermissionsQuery`,
`AddAdminUserCommand`, `CreateUserCommand`, `GetUserQuery`, `ProcessInboxCommand`,
`ProcessInternalCommandsCommand`, `ProcessOutboxCommand`.

All 18 `UnpublishedNotification` are published through generic dispatchers that
erase the event type:

- `BuildingBlocks/Infrastructure/DomainEventsDispatching/DomainEventsDispatcher.cs:63 await _mediator.Publish(domainEvent);` (`IDomainEvent` loop).
- `Modules/*/Infrastructure/Configuration/Processing/Outbox/ProcessOutboxCommandHandler.cs:60/62 await this._mediator.Publish(@event, cancellationToken);` (deserialized `IDomainEventNotification`).
- `Modules/*/Infrastructure/Configuration/Processing/Inbox/ProcessInboxCommandHandler.cs:50/51 await _mediator.Publish((INotification)request, cancellationToken);` (cast).

Full list (18): `MeetingGroupProposalAcceptedNotification` (Administration),
`MeetingCommentLikedNotification`, `MeetingCommentUnlikedNotification`,
`MeetingGroupProposalAcceptedNotification`, `MeetingGroupProposedNotification`,
`MeetingGroupCreatedNotification`, `MeetingAttendeeAddedNotification`,
`MemberSubscriptionExpirationDateChangedNotification`, `MemberCreatedNotification`,
`MeetingCreatedDomainEvent`, `MeetingFeePaidNotification`,
`MeetingFeePaymentPaidNotification`, `SubscriptionCreatedNotification`,
`SubscriptionPaymentPaidNotification`, `SubscriptionRenewalPaymentPaidNotification`,
`SubscriptionRenewedNotification`, `UserRegistrationConfirmedNotification`,
`NewUserRegisteredNotification`.

The modular findings carry the unresolved note (26 unresolved `.Send`/`.Publish`
sites including the `CommandsExecutor`/`DomainEventsDispatcher` sends), so the
type-erased population that could reach them is acknowledged in the message and
enumerated in JSON.

## Sampled call edges (grep-verifiable)

### northwindtraders (12)

| Kind | from → to | file:line | source line |
| --- | --- | --- | --- |
| dispatch (endpointToRequest) | `CategoriesController` → `DeleteCategoryCommand` | `Src/WebUI/Controllers/CategoriesController.cs:36` | `await Mediator.Send(new DeleteCategoryCommand { Id = id });` |
| dispatch (endpointToRequest) | `CategoriesController` → `GetCategoriesListQuery` | `CategoriesController.cs:18` | `return Ok(await Mediator.Send(new GetCategoriesListQuery()));` |
| dispatch | `CustomersController` → `DeleteCustomerCommand` | `CustomersController.cs:59` | `await Mediator.Send(new DeleteCustomerCommand { Id = id });` |
| dispatch | `CustomersController` → `GetCustomersListQuery` | `CustomersController.cs:19` | `var vm = await Mediator.Send(new GetCustomersListQuery());` |
| dispatch | `EmployeesController` → `DeleteEmployeeCommand` | `EmployeesController.cs:44` | `await Mediator.Send(new DeleteEmployeeCommand { Id = id });` |
| dispatch | `ProductsController` → `DeleteProductCommand` | `ProductsController.cs:58` | `await Mediator.Send(new DeleteProductCommand { Id = id });` |
| dispatch | `ProductsController` → `GetProductDetailQuery` | `ProductsController.cs:30` | `var vm = await Mediator.Send(new GetProductDetailQuery { Id = id });` |
| dispatch | `Program` → `SeedSampleDataCommand` | `Src/WebUI/Program.cs:39` | `await mediator.Send(new SeedSampleDataCommand(), CancellationToken.None);` |
| handlerToNotification | `CreateCustomerCommand.Handler` → `CustomerCreated` | `Src/Application/Customers/Commands/CreateCustomer/CreateCustomerCommand.cs:64` | `await _mediator.Publish(new CustomerCreated { CustomerId = entity.CustomerId }, cancellationToken);` |
| endpointToRequest | `ProductsController.GetAll` → `GetProductsListQuery` | `ProductsController.cs:17` | `return Ok(await Mediator.Send(new GetProductsListQuery()));` |
| requestToHandler | `CreateCustomerCommand` → `CreateCustomerCommand.Handler` | `CreateCustomerCommand.cs:3` | `public class CreateCustomerCommand : IRequest` |
| endpointToRequest | `CustomersController.Create` → `CreateCustomerCommand` | `CustomersController.cs:34` | `var id = await Mediator.Send(command);` |

### eShop Ordering (13)

| Kind | from → to | file:line | source line |
| --- | --- | --- | --- |
| dispatch | `OrderPaymentFailedIntegrationEventHandler` → `CancelOrderCommand` | `Ordering.API/Application/IntegrationEvents/EventHandling/OrderPaymentFailedIntegrationEventHandler.cs:12` | `var command = new CancelOrderCommand(@event.OrderId);` |
| dispatch | `GracePeriodConfirmedIntegrationEventHandler` → `SetAwaitingValidationOrderStatusCommand` | `…/GracePeriodConfirmedIntegrationEventHandler.cs:19` | `var command = new SetAwaitingValidationOrderStatusCommand(@event.OrderId);` |
| dispatch | `OrderPaymentSucceededIntegrationEventHandler` → `SetPaidOrderStatusCommand` | `…/OrderPaymentSucceededIntegrationEventHandler.cs:12` | `var command = new SetPaidOrderStatusCommand(@event.OrderId);` |
| dispatch | `OrderStockConfirmedIntegrationEventHandler` → `SetStockConfirmedOrderStatusCommand` | `…/OrderStockConfirmedIntegrationEventHandler.cs:12` | `var command = new SetStockConfirmedOrderStatusCommand(@event.OrderId);` |
| dispatch | `OrderStockRejectedIntegrationEventHandler` → `SetStockRejectedOrderStatusCommand` | `…/OrderStockRejectedIntegrationEventHandler.cs:15` | `var command = new SetStockRejectedOrderStatusCommand(@event.OrderId, orderStockRejectedItems);` |
| requestToHandler | `CancelOrderCommand` → handler | `Ordering.API/Application/Commands/CancelOrderCommand.cs:3` | `public record CancelOrderCommand(int OrderNumber) : IRequest<bool>;` |
| requestToHandler | `CreateOrderCommand` → handler | `…/CreateOrderCommand.cs:16` | `public class CreateOrderCommand` |
| requestToHandler | `CreateOrderDraftCommand` → handler | `…/CreateOrderDraftCommand.cs:4` | `public record CreateOrderDraftCommand(string BuyerId, IEnumerable<BasketItem> Items) : IRequest<OrderDraftDTO>;` |
| requestToHandler | `SetAwaitingValidationOrderStatusCommand` → handler | `…/SetAwaitingValidationOrderStatusCommand.cs:3` | `public record SetAwaitingValidationOrderStatusCommand(int OrderNumber) : IRequest<bool>;` |
| requestToHandler | `SetPaidOrderStatusCommand` → handler | `…/SetPaidOrderStatusCommand.cs:3` | `public record SetPaidOrderStatusCommand(int OrderNumber) : IRequest<bool>;` |
| requestToHandler | `SetStockConfirmedOrderStatusCommand` → handler | `…/SetStockConfirmedOrderStatusCommand.cs:3` | `public record SetStockConfirmedOrderStatusCommand(int OrderNumber) : IRequest<bool>;` |
| requestToHandler | `SetStockRejectedOrderStatusCommand` → handler | `…/SetStockRejectedOrderStatusCommand.cs:3` | `public record SetStockRejectedOrderStatusCommand(int OrderNumber, List<int> OrderStockItems) : IRequest<bool>;` |
| requestToHandler | `ShipOrderCommand` → handler | `…/ShipOrderCommand.cs:3` | `public record ShipOrderCommand(int OrderNumber) : IRequest<bool>;` |

Ordering has 5 resolved **dispatch call** edges (the five integration-event
handlers); the remaining entries are the request → handler flow edges, because
the API layer's command sends are type-erased (see the itemized warnings above).
All 13 rows are grep-verifiable.

### modular-monolith (12)

| Kind | from → to | file:line | source line |
| --- | --- | --- | --- |
| handlerToNotification | `MeetingGroupProposalAcceptedNotificationHandler` → `MeetingGroupProposalAcceptedIntegrationEvent` | `Modules/Administration/Application/MeetingGroupProposals/AcceptMeetingGroupProposal/MeetingGroupProposalAcceptedNotificationHandler.cs:18` | `await _eventsBus.Publish(new MeetingGroupProposalAcceptedIntegrationEvent(` |
| handlerToNotification | `MeetingGroupProposedNotificationHandler` → `MeetingGroupProposedIntegrationEvent` | `Modules/Meetings/Application/MeetingGroupProposals/MeetingGroupProposedNotificationHandler.cs:18` | `await _eventsBus.Publish(new MeetingGroupProposedIntegrationEvent(` |
| handlerToNotification | `MeetingAttendeeAddedPublishEventNotificationHandler` → `MeetingAttendeeAddedIntegrationEvent` | `Modules/Meetings/Application/Meetings/SendMeetingAttendeeAddedEmail/MeetingAttendeeAddedPublishEventNotificationHandler.cs:18` | `await _eventsBus.Publish(new MeetingAttendeeAddedIntegrationEvent(` |
| handlerToNotification | `MeetingFeePaidNotificationHandler` → `MeetingFeePaidIntegrationEvent` | `Modules/Payments/Application/MeetingFees/MarkMeetingFeeAsPaid/MeetingFeePaidNotificationHandler.cs:27` | `await _eventsBus.Publish(new MeetingFeePaidIntegrationEvent(` |
| handlerToNotification | `SubscriptionCreatedNotificationHandler` → `SubscriptionExpirationDateChangedIntegrationEvent` | `Modules/Payments/Application/Subscriptions/CreateSubscription/SubscriptionCreatedNotificationHandler.cs:18` | `await _eventsBus.Publish(new SubscriptionExpirationDateChangedIntegrationEvent(` |
| handlerToNotification | `SubscriptionRenewedNotificationHandler` → `SubscriptionExpirationDateChangedIntegrationEvent` | `Modules/Payments/Application/Subscriptions/RenewSubscription/SubscriptionRenewedNotificationHandler.cs:18` | `await _eventsBus.Publish(new SubscriptionExpirationDateChangedIntegrationEvent(` |
| handlerToNotification | `NewUserRegisteredPublishEventHandler` → `NewUserRegisteredIntegrationEvent` | `Modules/Registrations/Application/UserRegistrations/RegisterNewUser/NewUserRegisteredPublishEventHandler.cs:18` | `await _eventsBus.Publish(new NewUserRegisteredIntegrationEvent(` |
| endpointToRequest | `AuthenticatedUserController.GetAuthenticatedUser` → `GetAuthenticatedUserQuery` | `API/CompanyName.MyMeetings.API/Modules/UserAccess/AuthenticatedUserController.cs:27` | `var user = await _userAccessModule.ExecuteQueryAsync(new GetAuthenticatedUserQuery());` |
| endpointToRequest | `CountriesController.GetAllCountries` → `GetAllCountriesQuery` | `API/…/Modules/Meetings/Countries/CountriesController.cs:25` | `new GetAllCountriesQuery());` |
| endpointToRequest | `MeetingCommentsController.AddComment` → `AddMeetingCommentCommand` | `API/…/Modules/Meetings/MeetingComments/MeetingCommentsController.cs:30` | `await _meetingModule.ExecuteCommandAsync(new AddMeetingCommentCommand(` |
| endpointToRequest | `MeetingGroupProposalsController.AcceptMeetingGroupProposal` → `AcceptMeetingGroupProposalCommand` | `API/…/Modules/Administration/MeetingGroupProposals/MeetingGroupProposalsController.cs:38` | `new AcceptMeetingGroupProposalCommand(meetingGroupProposalId));` |
| endpointToRequest | `MeetingFeePaymentsController.RegisterMeetingFeePayment` → `MarkMeetingFeePaymentAsPaidCommand` | `API/…/Modules/Payments/MeetingFees/MeetingFeePaymentsController.cs:37` | `await _meetingsModule.ExecuteCommandAsync(new MarkMeetingFeePaymentAsPaidCommand(meetingFeePaymentId));` |

## El-Hakim backend — first measurement (report only)

Scanned `--analysis text` on `/data/workspace/el-hakim` (8 projects, 63 requests,
63 handlers, 60 endpoints, 0 notifications, 0 errors, 38 warnings).

| Metric | Value |
| --- | ---: |
| Endpoints total | 60 |
| Endpoints with a resolved request edge | 60 (100%) |
| Requests with ≥ 1 production call site | 59 / 63 |
| Unresolved dispatch call sites | 17 |
| New `UnreachableRequest` | 4 |

Named dispatch patterns:

- **Controllers** use `_mediator.Send(command)` / `_sender.Send(command)` (17 unresolved). The parameter is a concrete command, but the name `command` (or `request`) is reused by sibling actions in the same controller (e.g. `PostsController` declares `CreatePostCommand command`, `UpdatePostCommand command`, `PublishPostCommand command`, …), so the class-wide receiver map fails closed. The same controllers also dispatch `_mediator.Send(new XQuery())` directly, which resolves.
- **Domain entity `Publish()`** (`post.Publish()`, 17 `.Publish(` sites) is not a mediator publish and is ignored; the receiver is not dispatch-like.
- **`HttpClient.SendAsync`** (13 sites) is not a mediator send and is ignored.
- The 4 unreachable requests are `CreatePostCommand`, `GetPostsQuery`, `CreateTagCommand`, `UpdateTagCommand` — each has a concrete `Send(command)`/`Send(request)` call site that is hidden only by sibling-parameter name reuse.

## Fixture evidence

`tests/CqrsLens.Tests/Phase4FlowTests.cs` (9 cases, all passing):

| Test | Asserts |
| --- | --- |
| `KnownFlow_ResolvesEdgesAndFlagsOnlyDeadShapes` (text, roslyn) | edges `endpoint→CommandA`, `CommandAHandler→EventE`, `CommandAHandler→CommandB`; `UnreachableRequest` on **CommandC only**; `UnpublishedNotification` on **EventF only**; none on A/B/E; JSON contains `flowEdges`/`endpointToRequest`/`handlerToRequest`/`dispatchCallSites` |
| `RuntimeTypedSend_FabricatesNoEdgeAndIsAccountedFor` (text, roslyn) | no `DispatchCallSite`/edge for `RuntimeCommand`; ≥ 2 `UnresolvedDispatch` (interface variable **and** interface cast); `UnreachableRequest` message names the unresolved population |
| `TestProjectSend_IsNotProductionReachability` (text, roslyn) | the `App.Tests` send is a `DispatchCallSite` with `IsTestProject == true`; `UnreachableRequest` fires with "test-project" in the message |
| `GenericAndInvokeAsyncDispatchShapes_AreResolved` (text, roslyn) | `bus.InvokeAsync<BusCommand>(...)` → request site; `bus.PublishAsync<BusEvent>(...)` → notification site; neither is flagged |
| `HandlerUsingDbContext_ProducesATypeLevelEdge` (text) | `handlerToDbContext` edge `SaveCommandHandler → AppDbContext` |

The two honesty fixtures and the known-flow fixture are the Phase 4 judge: they
prove no fabricated edge, the unresolved accounting, the test/production split,
and that only genuinely unsent shapes are flagged.

## Performance guard

northwindtraders full scan, this machine: **text 0.66 s** (target ≤ 5 s),
**roslyn 1.70 s** (target ≤ 15 s). Other corpora: Ordering 0.33 / 1.20 s,
eShopOnWeb 0.38 / 1.37 s, modular 0.70 / 2.25 s, ABP 4.76 s (text), El-Hakim
0.47 s (text). The flow pass reuses the Phase 1 long-line guard in
`ReceiverTypeMapBuilder` and only visits files whose text contains a dispatch
method name.

## Scope / non-regressions

- No repository or project-name special-casing: every rule is a function of
  resolved types, declared shapes and project layer (`Tests`).
- No fabricated edges; unresolved sends are counted and located, never guessed.
- No inventory or error count changed on any corpus; `--strict` exits 0 in both
  modes on Ordering and Northwind, and on modular-monolith and ABP.
- Pipeline-behavior ordering, the ABP roslyn crash, the `--format agent`
  wording, the layer display-name fallback and the benchmark runner are
  untouched (out of scope).

## Commits

Local only; never pushed.
