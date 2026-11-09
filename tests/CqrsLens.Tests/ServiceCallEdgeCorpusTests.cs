using System.Text.Json;
using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// Stage 2 CP2: the pilot service-call tables read back from the generated JSON on the real
/// corpora, plus text/Roslyn parity of the JSON section. A corpus that is not present on disk is
/// skipped rather than answered from a hand-built model.
/// </summary>
public sealed class ServiceCallEdgeCorpusTests
{
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    private const string EsCommands = "eShop.Ordering.API.Application.Commands.";
    private const string EsHandlers = "eShop.Ordering.API.Application.DomainEventHandlers.";
    private const string EsIntegration = "eShop.Ordering.API.Application.IntegrationEvents.";
    private const string OrderRepoInterface = "eShop.Ordering.Domain.AggregatesModel.OrderAggregate.IOrderRepository";
    private const string BuyerRepoInterface = "eShop.Ordering.Domain.AggregatesModel.BuyerAggregate.IBuyerRepository";
    private const string OrderRepository = "eShop.Ordering.Infrastructure.Repositories.OrderRepository";
    private const string BuyerRepository = "eShop.Ordering.Infrastructure.Repositories.BuyerRepository";
    private const string OrderingIntegrationEventService = EsIntegration + "OrderingIntegrationEventService";
    private const string OrderingIntegrationEventInterface = EsIntegration + "IOrderingIntegrationEventService";
    private const string EventLogInterface = "IIntegrationEventLogService";
    private const string EventLogService = "IntegrationEventLogService";

    /// <summary>The 13 hand-audited eShop edges (all present in the accepted 32).</summary>
    private static readonly HashSet<(string From, string Via, string Method, string To)> EshopPilot13 = new()
    {
        (EsCommands + "CancelOrderCommandHandler", OrderRepoInterface, "GetAsync", OrderRepository),
        (EsCommands + "CreateOrderCommandHandler", OrderRepoInterface, "Add", OrderRepository),
        (EsCommands + "CreateOrderCommandHandler", OrderingIntegrationEventInterface, "AddAndSaveEventAsync", OrderingIntegrationEventService),
        (EsCommands + "SetAwaitingValidationOrderStatusCommandHandler", OrderRepoInterface, "GetAsync", OrderRepository),
        (EsCommands + "SetPaidOrderStatusCommandHandler", OrderRepoInterface, "GetAsync", OrderRepository),
        (EsCommands + "SetStockConfirmedOrderStatusCommandHandler", OrderRepoInterface, "GetAsync", OrderRepository),
        (EsCommands + "SetStockRejectedOrderStatusCommandHandler", OrderRepoInterface, "GetAsync", OrderRepository),
        (EsCommands + "ShipOrderCommandHandler", OrderRepoInterface, "GetAsync", OrderRepository),
        (OrderingIntegrationEventService, EventLogInterface, "RetrieveEventLogsPendingToPublishAsync", EventLogService),
        (OrderingIntegrationEventService, EventLogInterface, "MarkEventAsInProgressAsync", EventLogService),
        (OrderingIntegrationEventService, EventLogInterface, "MarkEventAsPublishedAsync", EventLogService),
        (OrderingIntegrationEventService, EventLogInterface, "MarkEventAsFailedAsync", EventLogService),
        (OrderingIntegrationEventService, EventLogInterface, "SaveEventAsync", EventLogService),
    };

    /// <summary>The 19 edges contributed by the 7 domain-event notification handlers.</summary>
    private static readonly HashSet<(string From, string Via, string Method, string To)> EshopDomainEvent19 = BuildDomainEventEdges();

    private static readonly HashSet<(string From, string Via, string Method, string To)> NorthwindAudited3 = new()
    {
        ("Northwind.Application.Products.Queries.GetProductsFile.GetProductsFileQueryHandler",
            "Northwind.Application.Common.Interfaces.ICsvFileBuilder", "BuildProductsFile",
            "Northwind.Infrastructure.Files.CsvFileBuilder"),
        ("Northwind.Application.Customers.Commands.CreateCustomer.CustomerCreated.CustomerCreatedHandler",
            "Northwind.Application.Common.Interfaces.INotificationService", "SendAsync",
            "Northwind.Infrastructure.NotificationService"),
        ("Northwind.Application.Employees.Commands.DeleteEmployee.DeleteEmployeeCommand.DeleteEmployeeCommandHandler",
            "Northwind.Application.Common.Interfaces.IUserManager", "DeleteUserAsync",
            "Northwind.Infrastructure.Identity.UserManagerService"),
    };

    [SkippableFact]
    public void Eshop_ServiceCallEdges_MatchTheAcceptedTable()
    {
        var root = FrozenQuestionSet.Root(FrozenCorpus.Eshop);
        Skip.If(!Directory.Exists(root), $"corpus not found: {root}");

        var edges = ReadEdgesFromJson(root);
        var tuples = edges.Select(e => (e.From, e.Via, e.Method, e.To)).ToHashSet();

        // Ruling on CP1 §4: notification handlers are sources, so the 7 domain-event
        // handlers contribute 19 edges on top of the 13 hand-audited command/service edges.
        Assert.Equal(32, edges.Count);
        Assert.Empty(EshopPilot13.Except(tuples));
        Assert.Empty(EshopDomainEvent19.Except(tuples));
        Assert.Equal(EshopPilot13.Count + EshopDomainEvent19.Count, edges.Count);
        Assert.All(edges, e => Assert.Equal("serviceCall", e.Kind));
    }

    [SkippableFact]
    public void Northwind_ServiceCallEdges_MatchTheAuditedTable()
    {
        var root = FrozenQuestionSet.Root(FrozenCorpus.Northwind);
        Skip.If(!Directory.Exists(root), $"corpus not found: {root}");

        var edges = ReadEdgesFromJson(root);
        var tuples = edges.Select(e => (e.From, e.Via, e.Method, e.To)).ToHashSet();

        Assert.Equal(3, edges.Count);
        Assert.Empty(NorthwindAudited3.Except(tuples));
        Assert.Empty(tuples.Except(NorthwindAudited3));
    }

    [SkippableTheory]
    [InlineData("northwind")]
    [InlineData("eshop")]
    [InlineData("modular")]
    public void TextAndRoslyn_ServiceCallEdges_AreDeepEqualInTheJson(string corpus)
    {
        var root = FrozenQuestionSet.Root(corpus switch
        {
            "northwind" => FrozenCorpus.Northwind,
            "eshop" => FrozenCorpus.Eshop,
            _ => FrozenCorpus.Modular,
        });
        Skip.If(!Directory.Exists(root), $"corpus not found: {root}");

        var text = ReadEdgesFromJson(root, AnalysisMode.Text);
        var roslyn = ReadEdgesFromJson(root, AnalysisMode.Roslyn);

        static string Canonical(IEnumerable<JsonServiceCallEdge> edges) => string.Join(
            "\n",
            edges.Select(e => $"{e.From}|{e.Via}|{e.Method}|{e.To}|{e.Project}|{e.Location}")
                .OrderBy(line => line, StringComparer.Ordinal));

        Assert.Equal(Canonical(text), Canonical(roslyn));
    }

    private static List<JsonServiceCallEdge> ReadEdgesFromJson(
        string root,
        AnalysisMode mode = AnalysisMode.Text)
    {
        var result = Analyzer.Analyze(root, mode);
        var json = JsonReportGenerator.Generate(result);
        var document = JsonSerializer.Deserialize<JsonReportDocument>(json, ReadOptions)!;
        Assert.NotNull(document);
        Assert.Equal(1, document!.SchemaVersion);
        return document.ServiceCallEdges ?? new List<JsonServiceCallEdge>();
    }

    private static HashSet<(string, string, string, string)> BuildDomainEventEdges()
    {
        string[] classes =
        {
            "OrderCancelledDomainEventHandler",
            "OrderShippedDomainEventHandler",
            "OrderStatusChangedToAwaitingValidationDomainEventHandler",
            "OrderStatusChangedToPaidDomainEventHandler",
            "OrderStatusChangedToStockConfirmedDomainEventHandler",
        };

        var edges = new HashSet<(string, string, string, string)>();
        foreach (var cls in classes)
        {
            edges.Add((EsHandlers + cls, OrderRepoInterface, "GetAsync", OrderRepository));
            edges.Add((EsHandlers + cls, BuyerRepoInterface, "FindByIdAsync", BuyerRepository));
            edges.Add((EsHandlers + cls, OrderingIntegrationEventInterface, "AddAndSaveEventAsync", OrderingIntegrationEventService));
        }

        edges.Add((EsHandlers + "UpdateOrderWhenBuyerAndPaymentMethodVerifiedDomainEventHandler",
            OrderRepoInterface, "GetAsync", OrderRepository));
        edges.Add((EsHandlers + "ValidateOrAddBuyerAggregateWhenOrderStartedDomainEventHandler",
            BuyerRepoInterface, "FindAsync", BuyerRepository));
        edges.Add((EsHandlers + "ValidateOrAddBuyerAggregateWhenOrderStartedDomainEventHandler",
            BuyerRepoInterface, "Add", BuyerRepository));
        edges.Add((EsHandlers + "ValidateOrAddBuyerAggregateWhenOrderStartedDomainEventHandler",
            OrderingIntegrationEventInterface, "AddAndSaveEventAsync", OrderingIntegrationEventService));
        return edges;
    }
}
