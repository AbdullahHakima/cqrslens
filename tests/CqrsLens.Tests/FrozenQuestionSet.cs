namespace CqrsLens.Tests;

/// <summary>The four corpora the frozen question set is measured against.</summary>
internal enum FrozenCorpus
{
    Northwind,
    Eshop,
    Modular,
    Abp,
}

/// <summary>
/// One frozen claim plus the verdict the real scan of its corpus must produce. The expected
/// verdict is derived from the actual Stage 3 close counts (see <see cref="Derivation"/>), not
/// from a hand-built model.
/// </summary>
internal sealed record FrozenClaim(
    string Id,
    FrozenCorpus Corpus,
    string Subject,
    string Relation,
    string? Object,
    ClaimVerdict ExpectedVerdict,
    string ExpectedMarker,
    string Derivation);

/// <summary>
/// One frozen A2 <c>check_architecture</c> query plus the total and page the real scan must return.
/// Expected totals are the measured rule census of the frozen scans (see <see cref="Derivation"/>).
/// </summary>
internal sealed record FrozenCheckQuery(
    string Id,
    FrozenCorpus Corpus,
    IReadOnlyList<string> Rules,
    string? Severity,
    string? Target,
    int Limit,
    int ExpectedTotal,
    int ExpectedShown,
    IReadOnlyList<string> ExpectedMarkers,
    string Derivation);

/// <summary>
/// One frozen A2 <c>trace_slice</c> structured-data query plus the values the real scan must hold.
/// The markers are matched against the serialized JSON the tool returns.
/// </summary>
internal sealed record FrozenSliceQuery(
    string Id,
    FrozenCorpus Corpus,
    string Query,
    int Limit,
    int ExpectedTotalMatched,
    IReadOnlyList<string> ExpectedMarkers,
    IReadOnlyList<string> AbsentMarkers,
    string Derivation);

/// <summary>
/// One frozen A3 <c>trace_request_impact</c> query plus the values the real scan must hold. The
/// markers are matched against the rendered impact trace.
/// </summary>
internal sealed record FrozenRequestImpactQuery(
    string Id,
    FrozenCorpus Corpus,
    string Request,
    bool Contains,
    int Limit,
    IReadOnlyList<string> ExpectedMarkers,
    IReadOnlyList<string> AbsentMarkers,
    string Derivation);

/// <summary>
/// One frozen A3 grouped <c>check_architecture</c> query plus the totals the real scan must return.
/// </summary>
internal sealed record FrozenGroupingQuery(
    string Id,
    FrozenCorpus Corpus,
    IReadOnlyList<string> Rules,
    string? Severity,
    string? Target,
    int Limit,
    int ExpectedMatching,
    int ExpectedGroups,
    int ExpectedShownGroups,
    IReadOnlyList<string> ExpectedMarkers,
    string Derivation);

/// <summary>One corpus whose grouped totals must reconcile exactly with its unfiltered count.</summary>
internal sealed record FrozenGroupingReconciliation(
    FrozenCorpus Corpus,
    int ExpectedTotal,
    string Derivation);

/// <summary>
/// One frozen A4 <c>diff_architecture</c> query: a head scan and a base operand (a base folder or a
/// snapshot of the base folder) plus the answer lines the real diff must produce. The expected
/// values are read from the actual runs of the frozen corpora / the in-repo fixture pair.
/// </summary>
internal sealed record FrozenDiffQuery(
    string Id,
    FrozenCorpus? HeadCorpus,
    FrozenCorpus? BaseCorpus,
    bool FixturePair,
    bool BaseFromSnapshot,
    IReadOnlyList<string> ExpectedMarkers,
    IReadOnlyList<string> AbsentMarkers,
    string Derivation);

/// <summary>
/// The Phase 6 Stage 4 frozen question set: fixed claims over northwind, eshop (Ordering),
/// modular-monolith and ABP, with the expected verdict of each claim derived from the frozen
/// scan counts of that corpus. Brief A2 extends it with frozen queries that exercise the filtered
/// <c>check_architecture</c> view and the structured <c>trace_slice</c> form. It is the stage's
/// success-test harness.
/// </summary>
internal static class FrozenQuestionSet
{
    internal const string DefaultCorpusRoot = "/data/workspace/cqrslens-corpus";
    internal const string DefaultAbpRoot = "/data/workspace/abp-corpus/abp";

    /// <summary>Frozen Stage 3 close counts, cited by the derivations below.</summary>
    internal const string FrozenCountsNote =
        "northwind 19/19/4/2/2 (requests/handlers/validators/behaviors/DbContexts) 0E/8W; " +
        "eshop 8/8/4/3/1 0/15; modular 105/105/5/0/4 0/205; ABP 0/0/13/0/80 0/4. " +
        "All four corpora hold 0 handlerToDbContext flow edges; northwind/eshop hold 4 unresolved " +
        "dispatch call sites each, modular 26, ABP 0.";

    internal static string Root(FrozenCorpus corpus) => corpus switch
    {
        FrozenCorpus.Abp => Environment.GetEnvironmentVariable("CQRSLENS_ABP_ROOT") ?? DefaultAbpRoot,
        _ => Path.Combine(
            Environment.GetEnvironmentVariable("CQRSLENS_CORPUS_ROOT") ?? DefaultCorpusRoot,
            corpus switch
            {
                FrozenCorpus.Northwind => "northwindtraders",
                FrozenCorpus.Eshop => "eshop",
                FrozenCorpus.Modular => "modular-monolith",
                _ => "",
            }),
    };

    internal static IReadOnlyList<FrozenClaim> All { get; } = new[]
    {
        // ---- coverage: supported existence -------------------------------------------------
        new FrozenClaim(
            "NW-01-supported-handler", FrozenCorpus.Northwind,
            "CreateCustomerCommand", "has handler",
            "Northwind.Application.Customers.Commands.CreateCustomer.CreateCustomerCommand.Handler",
            ClaimVerdict.Supported, "lists handler",
            "Request 1 of the 19 northwind requests; its Handlers set holds exactly that full name " +
            "(request at CreateCustomerCommand.cs:9, nested handler at :33). 19/19 requests are handled."),

        // ---- coverage: supported absence (a command with no validator) ---------------------
        new FrozenClaim(
            "NW-02-supported-absence-validator", FrozenCorpus.Northwind,
            "CreateProductCommand", "has no validator", null,
            ClaimVerdict.Supported, "lists no validators",
            "Northwind has 4 validators total (CreateCustomer/DeleteCustomer/UpdateCustomer/GetCustomerDetail); " +
            "CreateProductCommand.Validators is empty, so the absence is a proved model fact."),

        // ---- coverage: not supported (the scan proves the opposite) ------------------------
        new FrozenClaim(
            "NW-03-not-supported-wrong-request", FrozenCorpus.Northwind,
            "CreateCustomerCommand", "has validator", "DeleteCustomerCommandValidator",
            ClaimVerdict.NotSupported, "lists validators",
            "The named validator exists but targets DeleteCustomerCommand. CreateCustomerCommand.Validators " +
            "holds only CreateCustomerCommandValidator, so the claim is refuted."),

        new FrozenClaim(
            "NW-04-not-supported-absence-refuted", FrozenCorpus.Northwind,
            "CreateCustomerCommand", "has no validator", null,
            ClaimVerdict.NotSupported, "lists validators",
            "CreateCustomerCommand.Validators holds CreateCustomerCommandValidator, so the absence claim is refuted."),

        // ---- supported relations ----------------------------------------------------------
        new FrozenClaim(
            "NW-05-supported-publishes", FrozenCorpus.Northwind,
            "CreateCustomerCommand", "publishes", "CustomerCreated",
            ClaimVerdict.Supported, "publishes notification",
            "Northwind inventories 1 notification (CustomerCreated); CreateCustomerCommand.PublishedNotifications " +
            "holds it (published by its handler at CreateCustomerCommand.cs:64)."),

        new FrozenClaim(
            "NW-06-supported-handled-notification", FrozenCorpus.Northwind,
            "CustomerCreatedHandler", "handled notification", "CustomerCreated",
            ClaimVerdict.Supported, "handles",
            "Northwind inventories 1 notification handler; CustomerCreatedHandler.NotificationName = CustomerCreated."),

        new FrozenClaim(
            "NW-07-supported-depends-on", FrozenCorpus.Northwind,
            "CreateProductCommandHandler", "depends on", "INorthwindDbContext",
            ClaimVerdict.Supported, "dependencies",
            "Stage 3 H1 census: 19/19 northwind handlers inject INorthwindDbContext; CreateProductCommandHandler's " +
            "dependencies = [INorthwindDbContext]."),

        new FrozenClaim(
            "NW-08-supported-layer", FrozenCorpus.Northwind,
            "Application", "belongs to layer", "Application",
            ClaimVerdict.Supported, "layer:",
            "Project `Application` is classified layer `Application` (name convention)."),

        new FrozenClaim(
            "NW-09-supported-dispatched-by", FrozenCorpus.Northwind,
            "GetCustomersListQuery", "dispatched by", "CustomersController",
            ClaimVerdict.Supported, "dispatch site",
            "A resolved dispatch call site targets GetCustomersListQuery from CustomersController " +
            "(CustomersController.cs:19). Northwind has 16 resolved and 4 unresolved dispatch sites."),

        // ---- coverage: out-of-vocabulary -> not determinable -------------------------------
        new FrozenClaim(
            "NW-10-out-of-vocabulary-order", FrozenCorpus.Northwind,
            "CreateCustomerCommand", "executes before", "DeleteCategoryCommand",
            ClaimVerdict.NotDeterminable, "not in the verify_claim vocabulary",
            "Execution order is not a recorded model fact; the relation is outside the vocabulary by construction."),

        // ---- not determinable: model limitation (handler -> DbContext) ---------------------
        new FrozenClaim(
            "NW-11-uses-dbcontext-outside-model", FrozenCorpus.Northwind,
            "CreateProductCommandHandler", "uses DbContext", "NorthwindDbContext",
            ClaimVerdict.NotDeterminable, "interface/repository",
            "All corpora have 0 handlerToDbContext edges. The handler depends on INorthwindDbContext (an interface); " +
            "the interface -> concrete NorthwindDbContext hop is not modelled (Stage 3 H1), so neither use nor non-use is proved."),

        // ---- coverage: unresolved dispatch -> not determinable -----------------------------
        new FrozenClaim(
            "NW-12-unresolved-dispatch", FrozenCorpus.Northwind,
            "CreateCustomerCommand", "dispatched by", "CustomersController",
            ClaimVerdict.NotDeterminable, "unresolved dispatch",
            "No resolved dispatch call site targets CreateCustomerCommand from CustomersController; CustomersController.cs:39 " +
            "holds an unresolved Mediator.Send(command) (runtime-typed send). 4 unresolved dispatches in northwind."),

        // ---- subject absent from a complete scan -> not supported --------------------------
        new FrozenClaim(
            "NW-13-subject-absent", FrozenCorpus.Northwind,
            "FooCommand", "has handler", null,
            ClaimVerdict.NotSupported, "absent from the scanned model",
            "No request named FooCommand exists among the 19 inventoried requests; the scan is complete (0 error diagnostics)."),

        // ---- eshop ------------------------------------------------------------------------
        new FrozenClaim(
            "ES-01-supported-validator", FrozenCorpus.Eshop,
            "CreateOrderCommand", "has validator", "CreateOrderCommandValidator",
            ClaimVerdict.Supported, "lists validator",
            "eshop has 4 validators; CreateOrderCommand.Validators holds CreateOrderCommandValidator."),

        new FrozenClaim(
            "ES-02-supported-absence-validator", FrozenCorpus.Eshop,
            "CreateOrderDraftCommand", "has no validator", null,
            ClaimVerdict.Supported, "lists no validators",
            "Of the 8 eshop requests, CreateOrderDraftCommand has an empty Validators set (Stage 3 H1: 7/8 handlers " +
            "inject a repository, 1 handler has no dependencies, none names a validator)."),

        new FrozenClaim(
            "ES-03-not-supported-validator", FrozenCorpus.Eshop,
            "CreateOrderDraftCommand", "has validator", "CreateOrderCommandValidator",
            ClaimVerdict.NotSupported, "lists validators",
            "CreateOrderCommandValidator targets CreateOrderCommand, not CreateOrderDraftCommand; the latter's " +
            "Validators set is empty."),

        new FrozenClaim(
            "ES-04-supported-dispatched-by", FrozenCorpus.Eshop,
            "SetPaidOrderStatusCommand", "dispatched by", "OrderPaymentSucceededIntegrationEventHandler",
            ClaimVerdict.Supported, "dispatch site",
            "A resolved dispatch call site targets SetPaidOrderStatusCommand from " +
            "OrderPaymentSucceededIntegrationEventHandler.cs:21."),

        new FrozenClaim(
            "ES-05-unresolved-dispatch", FrozenCorpus.Eshop,
            "CreateOrderDraftCommand", "dispatched by", "OrdersApi",
            ClaimVerdict.NotDeterminable, "unresolved dispatch",
            "No resolved dispatch call site targets CreateOrderDraftCommand; OrdersApi.cs:115 holds an unresolved " +
            "Mediator.Send(command) (runtime-typed send). 4 unresolved dispatches in eshop."),

        new FrozenClaim(
            "ES-06-supported-handled-notification", FrozenCorpus.Eshop,
            "OrderCancelledDomainEventHandler", "handled notification", "OrderCancelledDomainEvent",
            ClaimVerdict.Supported, "handles",
            "eshop inventories 7 domain notifications and 7 notification handlers; OrderCancelledDomainEventHandler " +
            "handles OrderCancelledDomainEvent."),

        // ---- modular-monolith -------------------------------------------------------------
        new FrozenClaim(
            "MD-01-supported-validator", FrozenCorpus.Modular,
            "AddMeetingCommentCommand", "has validator", "AddMeetingCommentCommandValidator",
            ClaimVerdict.Supported, "lists validator",
            "modular has 5 validators over 105 requests; AddMeetingCommentCommand.Validators holds " +
            "AddMeetingCommentCommandValidator (unique simple name)."),

        new FrozenClaim(
            "MD-02-supported-validator-fullname", FrozenCorpus.Modular,
            "CompanyName.MyMeetings.Modules.Meetings.Application.MeetingGroupProposals.AcceptMeetingGroupProposal.AcceptMeetingGroupProposalCommand",
            "has validator",
            "CompanyName.MyMeetings.Modules.Meetings.Application.MeetingGroupProposals.AcceptMeetingGroupProposal.AcceptMeetingGroupProposalCommandValidator",
            ClaimVerdict.Supported, "lists validator",
            "Resolved by full name to the Meetings module request (the Administration module has a same-simple-named " +
            "request with no validator)."),

        new FrozenClaim(
            "MD-03-ambiguous-subject", FrozenCorpus.Modular,
            "AcceptMeetingGroupProposalCommand", "has validator", "AcceptMeetingGroupProposalCommandValidator",
            ClaimVerdict.NotDeterminable, "matches more than one",
            "Two of the 105 requests share the simple name AcceptMeetingGroupProposalCommand (Administration + Meetings " +
            "modules), so the bare name cannot be resolved to one fact."),

        new FrozenClaim(
            "MD-04-supported-absence-validator", FrozenCorpus.Modular,
            "ActivatePriceListItemCommand", "has no validator", null,
            ClaimVerdict.Supported, "lists no validators",
            "Only 5 validators exist for 105 requests; ActivatePriceListItemCommand.Validators is empty."),

        new FrozenClaim(
            "MD-05-supported-dispatched-by", FrozenCorpus.Modular,
            "MeetingGroupProposalAcceptedIntegrationEvent", "dispatched by",
            "MeetingGroupProposalAcceptedNotificationHandler",
            ClaimVerdict.Supported, "dispatch site",
            "A resolved dispatch call site targets the integration-event notification from " +
            "MeetingGroupProposalAcceptedNotificationHandler.cs:18."),

        // ---- ABP negative control and package-opaque / ambiguity --------------------------
        new FrozenClaim(
            "ABP-01-negative-control-mediator", FrozenCorpus.Abp,
            "PlaceOrderCommand", "has handler", "PlaceOrderCommandHandler",
            ClaimVerdict.NotSupported, "absent from the scanned model",
            "ABP has 0 requests and 0 handlers (no MediatR convention in .cs). The mediator claim is refuted by a " +
            "complete scan, not merely unresolved."),

        new FrozenClaim(
            "ABP-02-package-opaque", FrozenCorpus.Abp,
            "AbpSqliteCommand", "has handler", "AbpSqliteCommandHandler",
            ClaimVerdict.NotDeterminable, "package-opaque wrapper",
            "The scan reports an UnresolvedContract diagnostic for AbpSqliteCommand: it looks like a request but its " +
            "base chain references SqliteCommand, which has no source declaration. Discovery stopped, so absence is not a proof."),

        new FrozenClaim(
            "ABP-03-ambiguous-project", FrozenCorpus.Abp,
            "MyCompanyName.MyProjectName", "belongs to layer", "Application",
            ClaimVerdict.NotDeterminable, "matches more than one",
            "Three ABP project shapes share the identical name MyCompanyName.MyProjectName; the project cannot be " +
            "resolved uniquely."),

        new FrozenClaim(
            "ABP-04-supported-layer", FrozenCorpus.Abp,
            "Volo.Abp.Auditing.Tests", "belongs to layer", "Tests",
            ClaimVerdict.Supported, "layer:",
            "Project Volo.Abp.Auditing.Tests is classified layer Tests."),
    };

    private static readonly Dictionary<string, FrozenClaim> ById =
        All.ToDictionary(c => c.Id, StringComparer.Ordinal);

    internal static FrozenClaim Get(string id) => ById[id];

    // ---------------------------------------------------------- A2: filtered check_architecture

    /// <summary>
    /// Frozen filtered-check queries. The expected totals are the measured rule census of the frozen
    /// scans: northwind 8W = CommandWithoutValidator 8; eshop 15W = CommandWithoutValidator 5 +
    /// UnpublishedNotification 7 + UnreachableRequest 3; modular 205W = CommandWithoutValidator 71 +
    /// OrphanNotification 62 + UnpublishedNotification 18 + UnreachableRequest 54; ABP 4W =
    /// AmbiguousProjectName 3 + ApplicationLayerViolation 1 (0E; ABP's additional diagnostics are not
    /// findings).
    /// </summary>
    internal static IReadOnlyList<FrozenCheckQuery> CheckQueries { get; } = new[]
    {
        new FrozenCheckQuery(
            "A2-CHK-01-rule-alone", FrozenCorpus.Modular,
            Rules: ["CommandWithoutValidator"], Severity: null, Target: null, Limit: 0,
            ExpectedTotal: 71, ExpectedShown: 71,
            ExpectedMarkers: ["rules=`CommandWithoutValidator`", "AcceptMeetingGroupProposalCommand"],
            Derivation: "modular's 205 warnings are CommandWithoutValidator 71 + OrphanNotification 62 + " +
                        "UnpublishedNotification 18 + UnreachableRequest 54 (measured rule census of the scan)."),

        new FrozenCheckQuery(
            "A2-CHK-02-severity-alone", FrozenCorpus.Modular,
            Rules: [], Severity: "warning", Target: null, Limit: 0,
            ExpectedTotal: 205, ExpectedShown: 205,
            ExpectedMarkers: ["severity=`warning`", "[WARN]"],
            Derivation: "modular's frozen count is 0E/205W; filtering to warnings keeps every finding."),

        new FrozenCheckQuery(
            "A2-CHK-03-paged-rule", FrozenCorpus.Modular,
            Rules: ["UnpublishedNotification"], Severity: null, Target: null, Limit: 10,
            ExpectedTotal: 18, ExpectedShown: 10,
            ExpectedMarkers: ["`UnpublishedNotification`", "**Showing**: 10 (limit 10)", "_+8 more matching finding(s)"],
            Derivation: "modular holds 18 UnpublishedNotification findings; page size 10 leaves 8 counted, not printed."),

        new FrozenCheckQuery(
            "A2-CHK-04-eshop-rule", FrozenCorpus.Eshop,
            Rules: ["UnpublishedNotification"], Severity: null, Target: null, Limit: 0,
            ExpectedTotal: 7, ExpectedShown: 7,
            ExpectedMarkers: ["UnpublishedNotification", "OrderCancelledDomainEvent"],
            Derivation: "eshop's 15 warnings are CommandWithoutValidator 5 + UnpublishedNotification 7 + " +
                        "UnreachableRequest 3."),

        new FrozenCheckQuery(
            "A2-CHK-05-composed-rule-target", FrozenCorpus.Northwind,
            Rules: ["CommandWithoutValidator"], Severity: null, Target: "Products", Limit: 0,
            ExpectedTotal: 3, ExpectedShown: 3,
            ExpectedMarkers: ["target=`Products`", "CreateProductCommand", "DeleteProductCommand", "UpdateProductCommand"],
            Derivation: "northwind's 8 warnings are all CommandWithoutValidator; 3 of them target a Products command."),

        new FrozenCheckQuery(
            "A2-CHK-06-abp-no-errors", FrozenCorpus.Abp,
            Rules: [], Severity: "error", Target: null, Limit: 0,
            ExpectedTotal: 0, ExpectedShown: 0,
            ExpectedMarkers: ["No findings match the filters"],
            Derivation: "ABP's frozen count is 0E/4W, so the error filter selects nothing."),

        new FrozenCheckQuery(
            "A2-CHK-07-two-rules-are-or", FrozenCorpus.Modular,
            Rules: ["CommandWithoutValidator", "OrphanNotification"], Severity: null, Target: null, Limit: 0,
            ExpectedTotal: 133, ExpectedShown: 133,
            ExpectedMarkers: ["CommandWithoutValidator", "OrphanNotification"],
            Derivation: "71 CommandWithoutValidator + 62 OrphanNotification = 133; rules are an OR within the dimension."),

        new FrozenCheckQuery(
            "A2-CHK-08-composed-target-substring", FrozenCorpus.Modular,
            Rules: ["OrphanNotification"], Severity: null, Target: "Meetings.Domain", Limit: 0,
            ExpectedTotal: 31, ExpectedShown: 31,
            ExpectedMarkers: ["target=`Meetings.Domain`", "Meetings.Domain"],
            Derivation: "31 of modular's 62 OrphanNotification findings have Meetings.Domain in their target."),

        new FrozenCheckQuery(
            "A2-CHK-09-abp-rule", FrozenCorpus.Abp,
            Rules: ["AmbiguousProjectName"], Severity: null, Target: null, Limit: 0,
            ExpectedTotal: 3, ExpectedShown: 3,
            ExpectedMarkers: ["AmbiguousProjectName"],
            Derivation: "ABP's 4 warnings are AmbiguousProjectName 3 + ApplicationLayerViolation 1."),

        new FrozenCheckQuery(
            "A2-CHK-10-corpus-scale-paging", FrozenCorpus.Modular,
            Rules: [], Severity: "warning", Target: null, Limit: 50,
            ExpectedTotal: 205, ExpectedShown: 50,
            ExpectedMarkers: ["**Showing**: 50 (limit 50)", "_+155 more matching finding(s)"],
            Derivation: "modular's frozen warning set is 205 findings; a page size of 50 prints 50 and " +
                        "counts the remaining 155 without printing them."),
    };

    // ------------------------------------------------------------ A2: structured trace_slice

    /// <summary>
    /// Frozen structured-slice queries. The expected values are read from the actual scans of the
    /// frozen corpora; markers are matched against the compact JSON the tool returns.
    /// </summary>
    internal static IReadOnlyList<FrozenSliceQuery> SliceQueries { get; } = new[]
    {
        new FrozenSliceQuery(
            "A2-SLICE-01-northwind-behaviors-and-notification", FrozenCorpus.Northwind,
            Query: "CreateCustomerCommand", Limit: 1, ExpectedTotalMatched: 1,
            ExpectedMarkers:
            [
                "\"name\":\"CreateCustomerCommand\"",
                "\"name\":\"CreateCustomerCommandValidator\"",
                "\"name\":\"CustomerCreated\"",
                "\"name\":\"CustomerCreatedHandler\"",
                "\"kind\":\"endpointToRequest\"",
                "\"name\":\"RequestPerformanceBehaviour\"",
                "\"name\":\"RequestValidationBehavior\"",
                "registered order",
            ],
            AbsentMarkers: ["\"kind\":\"handlerToDbContext\""],
            Derivation: "northwind's 2 behaviors register at DependencyInjection.cs:15-16 in order " +
                        "RequestPerformanceBehaviour (1), RequestValidationBehavior (2). CreateCustomerCommand has " +
                        "1 handler, 1 validator, publishes CustomerCreated (handled by CustomerCreatedHandler), and " +
                        "has 1 resolved endpoint edge. All corpora hold 0 handlerToDbContext edges."),

        new FrozenSliceQuery(
            "A2-SLICE-02-eshop-behaviors-no-endpoint-edges", FrozenCorpus.Eshop,
            Query: "CreateOrderCommand", Limit: 1, ExpectedTotalMatched: 1,
            ExpectedMarkers:
            [
                "\"name\":\"CreateOrderCommand\"",
                "\"name\":\"CreateOrderCommandHandler\"",
                "\"name\":\"LoggingBehavior\"",
                "\"name\":\"ValidatorBehavior\"",
                "\"name\":\"TransactionBehavior\"",
                "\"registeredOrder\":1",
                "\"registeredOrder\":3",
            ],
            AbsentMarkers: ["\"kind\":\"endpointToRequest\""],
            Derivation: "eshop resolves 0 endpoint→request edges (its OrdersApi.Send is runtime-typed). Its 3 " +
                        "behaviors register at Extensions.cs:39-41 in order Logging (1), Validator (2), Transaction (3)."),

        new FrozenSliceQuery(
            "A2-SLICE-03-modular-validator-no-behaviors", FrozenCorpus.Modular,
            Query: "AddMeetingCommentCommand", Limit: 1, ExpectedTotalMatched: 1,
            ExpectedMarkers:
            [
                "\"name\":\"AddMeetingCommentCommand\"",
                "\"name\":\"AddMeetingCommentCommandValidator\"",
                "\"registered\":[]",
            ],
            AbsentMarkers: ["\"registeredOrder\""],
            Derivation: "modular holds 0 pipeline behaviors, so the registered list is empty; AddMeetingCommentCommand " +
                        "has validator AddMeetingCommentCommandValidator (MD-01)."),

        new FrozenSliceQuery(
            "A2-SLICE-04-northwind-absent-validator", FrozenCorpus.Northwind,
            Query: "CreateProductCommand", Limit: 1, ExpectedTotalMatched: 1,
            ExpectedMarkers:
            [
                "\"validators\":[]",
                "\"dependencies\":[\"INorthwindDbContext\"]",
            ],
            AbsentMarkers: ["\"name\":\"CreateProductCommandValidator\""],
            Derivation: "NW-02: CreateProductCommand has no validator, so the structured validators array is empty " +
                        "(absent, not completed). Its handler injects INorthwindDbContext (NW-07)."),
    };

    // ------------------------------------------------------- A3: request-level impact

    /// <summary>
    /// Frozen request-impact queries. The expected values are read from the actual scans of the
    /// frozen corpora; markers are matched against the rendered impact trace.
    /// </summary>
    internal static IReadOnlyList<FrozenRequestImpactQuery> RequestImpactQueries { get; } = new[]
    {
        new FrozenRequestImpactQuery(
            "A3-REQ-01-northwind-full-chain", FrozenCorpus.Northwind,
            Request: "CreateCustomerCommand", Contains: false, Limit: 0,
            ExpectedMarkers:
            [
                "# CqrsLens Request Impact Trace — `CreateCustomerCommand` (`northwindtraders`)",
                "## [Command] `CreateCustomerCommand` → `void`",
                "`CreateCustomerCommandValidator`",
                "`CustomerCreated`",
                "`CustomerCreatedHandler`",
                "`RequestPerformanceBehaviour`",
                "`RequestValidationBehavior`",
                "`POST /customers`",
                "endpointToRequest",
                "registered order",
                "### DbContext edges (0)",
            ],
            AbsentMarkers: [],
            Derivation: "northwind's CreateCustomerCommand has 1 handler (nested `Handler`), 1 validator " +
                        "(CreateCustomerCommandValidator), 2 pipeline behaviors (RequestPerformanceBehaviour, " +
                        "RequestValidationBehavior at DependencyInjection.cs:15-16), publishes CustomerCreated " +
                        "(handled by CustomerCreatedHandler), and 1 resolved endpoint→request edge from " +
                        "CustomersController.Create. All corpora hold 0 handlerToDbContext edges, so that section is empty."),

        new FrozenRequestImpactQuery(
            "A3-REQ-02-eshop-no-endpoint", FrozenCorpus.Eshop,
            Request: "CreateOrderCommand", Contains: false, Limit: 0,
            ExpectedMarkers:
            [
                "# CqrsLens Request Impact Trace — `CreateOrderCommand` (`eshop`)",
                "## [Command] `CreateOrderCommand` → `bool`",
                "`CreateOrderCommandHandler`",
                "`CreateOrderCommandValidator`",
                "`LoggingBehavior`",
                "`ValidatorBehavior`",
                "`TransactionBehavior`",
                "No endpoint dispatches this request in the scanned model.",
            ],
            AbsentMarkers: ["endpointToRequest"],
            Derivation: "eshop resolves 0 endpoints and 0 endpoint→request edges (its OrdersApi.Send is runtime-typed). " +
                        "CreateOrderCommand has 1 handler and 1 validator; its 3 behaviors register at " +
                        "Extensions.cs:39-41 in order Logging (1), Validator (2), Transaction (3)."),

        new FrozenRequestImpactQuery(
            "A3-REQ-03-unknown-name", FrozenCorpus.Northwind,
            Request: "FooCommand", Contains: false, Limit: 0,
            ExpectedMarkers:
            [
                "No request named `FooCommand` is present in the scanned model",
                "not completed or guessed",
                "Known requests:",
            ],
            AbsentMarkers: [],
            Derivation: "No request named FooCommand exists among northwind's 19 inventoried requests; the answer is " +
                        "a clean not-found answer, not an error and not a guess."),

        new FrozenRequestImpactQuery(
            "A3-REQ-04-modular-reverse-reach", FrozenCorpus.Modular,
            Request: "CompanyName.MyMeetings.Modules.Administration.Application.Members.CreateMember.CreateMemberCommand",
            Contains: false, Limit: 0,
            ExpectedMarkers:
            [
                "Reverse reach (1)",
                "notification handler `CompanyName.MyMeetings.Modules.Administration.Application.Members.NewUserRegisteredIntegrationEventHandler`",
                "records dispatching this request",
                "No endpoint dispatches this request in the scanned model.",
            ],
            AbsentMarkers: [],
            Derivation: "modular resolves no command dispatch call sites and no endpoints for this request, but the " +
                        "Administration NewUserRegisteredIntegrationEventHandler records dispatching this command in " +
                        "its DispatchedRequests list (matched by full name, so the same-simple-named Meetings command " +
                        "is not cross-linked)."),
    };

    // --------------------------------------------------- A3: per-module findings grouping

    /// <summary>
    /// Frozen grouped-check queries. The expected totals are the measured rule census of the frozen
    /// scans; markers are matched against the grouped answer.
    /// </summary>
    internal static IReadOnlyList<FrozenGroupingQuery> GroupingQueries { get; } = new[]
    {
        new FrozenGroupingQuery(
            "A3-GRP-01-modular-all", FrozenCorpus.Modular,
            Rules: [], Severity: null, Target: null, Limit: 0,
            ExpectedMatching: 205, ExpectedGroups: 18, ExpectedShownGroups: 18,
            ExpectedMarkers:
            [
                "- **Grouped by**: project",
                "- **Matching findings**: 205",
                "- **Projects**: 18",
                "- **Errors**: 0 · **Warnings**: 205",
                "**`CompanyName.MyMeetings.Modules.Meetings.Application`** — **47** finding(s): 0 error(s), 47 warning(s)",
                "  - `CommandWithoutValidator` × 27",
            ],
            Derivation: "modular's frozen 0E/205W set spans 18 projects; the largest is Meetings.Application with 47, " +
                        "27 of which are CommandWithoutValidator."),

        new FrozenGroupingQuery(
            "A3-GRP-02-northwind-all", FrozenCorpus.Northwind,
            Rules: [], Severity: null, Target: null, Limit: 0,
            ExpectedMatching: 8, ExpectedGroups: 1, ExpectedShownGroups: 1,
            ExpectedMarkers:
            [
                "- **Matching findings**: 8",
                "- **Projects**: 1",
                "**`Application`** — **8** finding(s): 0 error(s), 8 warning(s)",
                "  - `CommandWithoutValidator` × 8",
            ],
            Derivation: "northwind's frozen 0E/8W set is all CommandWithoutValidator in the single project Application."),

        new FrozenGroupingQuery(
            "A3-GRP-03-eshop-all", FrozenCorpus.Eshop,
            Rules: [], Severity: null, Target: null, Limit: 0,
            ExpectedMatching: 15, ExpectedGroups: 2, ExpectedShownGroups: 2,
            ExpectedMarkers:
            [
                "- **Matching findings**: 15",
                "- **Projects**: 2",
                "**`Ordering.API`** — **8** finding(s): 0 error(s), 8 warning(s)",
            ],
            Derivation: "eshop's frozen 0E/15W set spans Ordering.API (8) and Ordering.Domain (7)."),

        new FrozenGroupingQuery(
            "A3-GRP-04-abp-all", FrozenCorpus.Abp,
            Rules: [], Severity: null, Target: null, Limit: 0,
            ExpectedMatching: 4, ExpectedGroups: 4, ExpectedShownGroups: 4,
            ExpectedMarkers:
            [
                "- **Matching findings**: 4",
                "- **Projects**: 4",
                "`AmbiguousProjectName`",
            ],
            Derivation: "ABP's frozen 0E/4W set is one finding in each of 4 projects (AmbiguousProjectName 3 + " +
                        "ApplicationLayerViolation 1), so 4 projects of 1 each."),

        new FrozenGroupingQuery(
            "A3-GRP-05-modular-rule-filter", FrozenCorpus.Modular,
            Rules: ["CommandWithoutValidator"], Severity: null, Target: null, Limit: 0,
            ExpectedMatching: 71, ExpectedGroups: 10, ExpectedShownGroups: 10,
            ExpectedMarkers:
            [
                "- **Filters**: rules=`CommandWithoutValidator`",
                "- **Matching findings**: 71",
                "- **Projects**: 10",
            ],
            Derivation: "modular holds 71 CommandWithoutValidator findings across 10 projects; grouping under the rule " +
                        "filter groups only the matching findings and states the filter."),

        new FrozenGroupingQuery(
            "A3-GRP-06-modular-paged", FrozenCorpus.Modular,
            Rules: [], Severity: null, Target: null, Limit: 3,
            ExpectedMatching: 205, ExpectedGroups: 18, ExpectedShownGroups: 3,
            ExpectedMarkers:
            [
                "- **Projects**: 18 (showing 3, limit 3)",
                "_+15 more project(s)",
            ],
            Derivation: "modular's grouped view has 18 projects; a page size of 3 prints 3 and counts the remaining 15."),
    };

    /// <summary>
    /// The grouping reconciliation targets: the sum of the grouped per-project totals must equal the
    /// unfiltered finding count of each corpus (the frozen Stage 3 close counts).
    /// </summary>
    internal static IReadOnlyList<FrozenGroupingReconciliation> GroupingReconciliation { get; } = new[]
    {
        new FrozenGroupingReconciliation(FrozenCorpus.Northwind, 8,
            "northwind frozen count 0E/8W"),
        new FrozenGroupingReconciliation(FrozenCorpus.Eshop, 15,
            "eshop frozen count 0E/15W"),
        new FrozenGroupingReconciliation(FrozenCorpus.Modular, 205,
            "modular frozen count 0E/205W"),
        new FrozenGroupingReconciliation(FrozenCorpus.Abp, 4,
            "ABP frozen count 0E/4W"),
    };

    // ------------------------------------------------------- A4: diff_architecture

    /// <summary>
    /// Frozen diff queries: the in-repo fixture pair (one handler removed), one real corpus pair
    /// (northwind base -> eshop head), and a corpus against its own snapshot (empty delta). Every
    /// expected line is read from the actual run; the derivations cite the frozen Stage 3 counts.
    /// </summary>
    internal static IReadOnlyList<FrozenDiffQuery> DiffQueries { get; } = new[]
    {
        new FrozenDiffQuery(
            "A4-DIFF-01-fixture-handler-removed", HeadCorpus: null, BaseCorpus: null,
            FixturePair: true, BaseFromSnapshot: false,
            ExpectedMarkers:
            [
                "- **Changed**: yes",
                "- **Requests**: +0 added · -0 removed · =1 unchanged · ~1 modified",
                "- **Handlers**: +0 added · -1 removed · =0 unchanged",
                "- **Validators**: +0 added · -0 removed · =0 unchanged",
                "- **Pipeline behaviors**: +0 added · -0 removed · =0 unchanged",
                "- **DbContexts**: +0 added · -0 removed · =0 unchanged · ~0 modified",
                "- **Endpoints**: +0 added · -0 removed · =0 unchanged",
                "- **New findings**: 1",
                "- **Resolved findings**: 1",
                "**[ERROR] `MissingHandler`** target=`App.DoThingQuery`",
                "**[WARN] `UnreachableRequest`** target=`App.DoThingQuery`",
                "- - `App.DoThingQueryHandler` (request `DoThingQuery`)",
                "- ~ `DoThingQuery` · project `App` — -Handler: DoThingQueryHandler",
            ],
            AbsentMarkers: ["- + `", "### New findings (2)", "### Resolved findings (2)"],
            Derivation: "The fixture pair differs by exactly one handler: the head drops " +
                        "DoThingQueryHandler. Handler delta +0/-1; the request keeps its identity but loses " +
                        "its handler (~1 modified). The head request has no handler, so MissingHandler (error) " +
                        "appears, and the base's UnreachableRequest (warning: a handled request with no call site) " +
                        "resolves: one finding appears, one resolves."),

        new FrozenDiffQuery(
            "A4-DIFF-02-corpus-pair-northwind-eshop", HeadCorpus: FrozenCorpus.Eshop,
            BaseCorpus: FrozenCorpus.Northwind, FixturePair: false, BaseFromSnapshot: false,
            ExpectedMarkers:
            [
                "- **Changed**: yes",
                "- **Requests**: +8 added · -19 removed · =0 unchanged · ~0 modified",
                "- **Handlers**: +8 added · -19 removed · =0 unchanged",
                "- **Validators**: +4 added · -4 removed · =0 unchanged",
                "- **Pipeline behaviors**: +3 added · -2 removed · =0 unchanged",
                "- **DbContexts**: +1 added · -2 removed · =0 unchanged · ~0 modified",
                "- **Endpoints**: +0 added · -18 removed · =0 unchanged",
                "- **New findings**: 15",
                "- **Resolved findings**: 8",
                "CreateOrderDraftCommand",
                "Northwind.Application.Products.Commands.CreateProduct.CreateProductCommand",
            ],
            AbsentMarkers: [],
            Derivation: "Real scans: northwind 19/19/4/2/2 0E/8W vs eshop 8/8/4/3/1 0/15. The full-name " +
                        "identities share no request/handler/validator/behavior/DbContext, so eshop's counts " +
                        "are added and northwind's removed; northwind resolves 18 endpoints, eshop 0. No finding " +
                        "key overlaps, so all 15 eshop warnings are new and all 8 northwind warnings resolved."),

        new FrozenDiffQuery(
            "A4-DIFF-03-empty-delta-self-snapshot", HeadCorpus: FrozenCorpus.Northwind,
            BaseCorpus: FrozenCorpus.Northwind, FixturePair: false, BaseFromSnapshot: true,
            ExpectedMarkers:
            [
                "- **Changed**: no",
                "- **Requests**: +0 added · -0 removed · =19 unchanged · ~0 modified",
                "- **Handlers**: +0 added · -0 removed · =19 unchanged",
                "- **Validators**: +0 added · -0 removed · =4 unchanged",
                "- **Pipeline behaviors**: +0 added · -0 removed · =2 unchanged",
                "- **DbContexts**: +0 added · -0 removed · =2 unchanged · ~0 modified",
                "- **Endpoints**: +0 added · -0 removed · =18 unchanged",
                "- **New findings**: 0",
                "- **Resolved findings**: 0",
                "_No architecture delta: 0 added, 0 removed, 0 modified, 0 new, 0 resolved._",
            ],
            AbsentMarkers: ["- **Changed**: yes", "### New findings", "### Resolved findings"],
            Derivation: "northwind against a snapshot of itself: every identity is shared, so the change " +
                        "counts (added, removed, modified, new, resolved) are all zero; the unchanged counts " +
                        "restate the frozen 19/19/4/2/2 shape with 18 endpoints and 8 active warnings. The delta " +
                        "is explicitly empty, not a partial answer."),
    };

    private static readonly Dictionary<string, FrozenCheckQuery> CheckQueriesById =
        CheckQueries.ToDictionary(q => q.Id, StringComparer.Ordinal);

    private static readonly Dictionary<string, FrozenSliceQuery> SliceQueriesById =
        SliceQueries.ToDictionary(q => q.Id, StringComparer.Ordinal);

    private static readonly Dictionary<string, FrozenRequestImpactQuery> RequestImpactQueriesById =
        RequestImpactQueries.ToDictionary(q => q.Id, StringComparer.Ordinal);

    private static readonly Dictionary<string, FrozenGroupingQuery> GroupingQueriesById =
        GroupingQueries.ToDictionary(q => q.Id, StringComparer.Ordinal);

    private static readonly Dictionary<string, FrozenDiffQuery> DiffQueriesById =
        DiffQueries.ToDictionary(q => q.Id, StringComparer.Ordinal);

    internal static FrozenCheckQuery GetCheckQuery(string id) => CheckQueriesById[id];

    internal static FrozenSliceQuery GetSliceQuery(string id) => SliceQueriesById[id];

    internal static FrozenRequestImpactQuery GetRequestImpactQuery(string id) => RequestImpactQueriesById[id];

    internal static FrozenGroupingQuery GetGroupingQuery(string id) => GroupingQueriesById[id];

    internal static FrozenDiffQuery GetDiffQuery(string id) => DiffQueriesById[id];
}
