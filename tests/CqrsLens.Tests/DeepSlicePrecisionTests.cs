using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// Precision (false-positive) regression tests for the deep-slice analyzer and the agent/MCP
/// outputs. The recall tests in <see cref="DeepSliceAndAgentTests"/> prove that expected facts
/// appear; these prove that plausible-looking but wrong facts do NOT appear, because AI agents
/// consuming the MCP tools repeat whatever the tools assert.
/// </summary>
public class DeepSlicePrecisionTests
{
    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "cqrslens-precision-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public void Write(string relativePath, string contents)
        {
            var full = System.IO.Path.Combine(Path, relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
            File.WriteAllText(full, contents);
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* best effort */ }
        }
    }

    private const string Stubs = """
        namespace MediatR
        {
            public interface IRequest { }
            public interface IRequest<out TResponse> { }
            public interface IRequestHandler<in TRequest> where TRequest : IRequest { }
            public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse> { }
            public interface INotification { }
            public interface INotificationHandler<in TNotification> where TNotification : INotification { }
        }
        namespace Microsoft.EntityFrameworkCore
        {
            public class DbContext { }
            public class DbSet<TEntity> where TEntity : class { }
        }
        """;

    private static CqrsRequest Request(AnalysisResult result, string name)
        => Assert.Single(result.Requests, r => r.Name == name);

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void NotificationCreation_WithWhitespaceAfterNew_IsDetected(AnalysisMode mode)
    {
        using var dir = new TempDir();
        dir.Write("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        dir.Write("App.cs", Stubs + """
            namespace App
            {
                using MediatR;

                public class OrderPlaced : INotification { }
                public record PlaceOrder : IRequest;

                public class PlaceOrderHandler : IRequestHandler<PlaceOrder>
                {
                    public void Handle(PlaceOrder request)
                    {
                        var evt = new
                            OrderPlaced();
                        Publish(evt);
                    }

                    private void Publish(INotification evt) { }
                }
            }
            """);

        var result = Analyzer.Analyze(dir.Path, mode);

        Assert.Contains("App.OrderPlaced", Request(result, "PlaceOrder").PublishedNotifications);
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void DomainEvents_AreAttributedToTheInvokedMember_NotToConstructionOrSiblingEntities(AnalysisMode mode)
    {
        using var dir = new TempDir();
        dir.Write("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        dir.Write("App.cs", Stubs + """
            namespace App
            {
                using MediatR;
                using Microsoft.EntityFrameworkCore;

                public class OrderUpdatedEvent : INotification { }
                public class CustomerUpdatedEvent : INotification { }

                public class Order
                {
                    public int Id { get; set; }
                    public void Update() { Raise(new OrderUpdatedEvent()); }
                    private void Raise(INotification e) { }
                }

                public class Customer
                {
                    public int Id { get; set; }
                    public void Update() { Raise(new CustomerUpdatedEvent()); }
                    private void Raise(INotification e) { }
                }

                public class ShopDb : DbContext
                {
                    public DbSet<Order> Orders { get; set; } = null!;
                    public DbSet<Customer> Customers { get; set; } = null!;
                }

                // Reads a customer, but only ever mutates the order.
                public record UpdateOrderCommand(int OrderId, int CustomerId) : IRequest;
                public class UpdateOrderCommandHandler(ShopDb db) : IRequestHandler<UpdateOrderCommand>
                {
                    public void Handle(UpdateOrderCommand request)
                    {
                        var customer = db.Customers.Find(request.CustomerId);
                        var order = db.Orders.Find(request.OrderId);
                        order!.Update();
                    }
                }

                // Constructs a Customer but never calls a member that raises an event.
                public record PreviewCustomerQuery(int Id) : IRequest<int>;
                public class PreviewCustomerQueryHandler : IRequestHandler<PreviewCustomerQuery, int>
                {
                    public int Handle(PreviewCustomerQuery request)
                    {
                        var blank = new Customer();
                        return blank.Id;
                    }
                }

                // `Set<T>()` on a non-DbContext must not type `order` as an entity.
                public class Bag { public T Set<T>() where T : new() => new T(); }

                public record ProbeBagQuery : IRequest;
                public class ProbeBagQueryHandler(Bag bag) : IRequestHandler<ProbeBagQuery>
                {
                    public void Handle(ProbeBagQuery request)
                    {
                        var order = bag.Set<Order>();
                        order.Update();
                    }
                }
            }
            """);

        var result = Analyzer.Analyze(dir.Path, mode);

        var update = Request(result, "UpdateOrderCommand");
        Assert.Contains("App.OrderUpdatedEvent", update.PublishedNotifications);
        Assert.DoesNotContain("App.CustomerUpdatedEvent", update.PublishedNotifications);

        var preview = Request(result, "PreviewCustomerQuery");
        Assert.Empty(preview.PublishedNotifications);

        Assert.Empty(Request(result, "ProbeBagQuery").PublishedNotifications);
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void PropertySetterEvents_AreDetectedThroughDbSetInferredLocals(AnalysisMode mode)
    {
        // Jason Taylor CleanArchitecture shape: `var entity = await _context.TodoItems.FindAsync(..)`
        // followed by `entity.Done = request.Done;`, where the Done setter raises the event.
        using var dir = new TempDir();
        dir.Write("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        dir.Write("App.cs", Stubs + """
            namespace App
            {
                using MediatR;
                using Microsoft.EntityFrameworkCore;

                public class TodoItemCompletedEvent : INotification { }

                public class TodoItem
                {
                    private bool _done;
                    public int Id { get; set; }
                    public bool Done
                    {
                        get => _done;
                        set
                        {
                            if (value && !_done)
                                AddDomainEvent(new TodoItemCompletedEvent());
                            _done = value;
                        }
                    }
                    private void AddDomainEvent(INotification e) { }
                }

                public interface IApplicationDbContext
                {
                    DbSet<TodoItem> TodoItems { get; }
                }

                public record UpdateTodoItemCommand(int Id, bool Done) : IRequest;
                public class UpdateTodoItemCommandHandler : IRequestHandler<UpdateTodoItemCommand>
                {
                    private readonly IApplicationDbContext _context;
                    public UpdateTodoItemCommandHandler(IApplicationDbContext context) => _context = context;

                    public async System.Threading.Tasks.Task Handle(UpdateTodoItemCommand request)
                    {
                        var entity = await _context.TodoItems.FindAsync(request.Id);
                        entity.Done = request.Done;
                    }
                }
            }
            """);

        var result = Analyzer.Analyze(dir.Path, mode);

        var update = Request(result, "UpdateTodoItemCommand");
        Assert.Contains("App.TodoItemCompletedEvent", update.PublishedNotifications);
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void DbSetTouches_RequireADbContextReceiver_NotAnyMemberWithTheSameName(AnalysisMode mode)
    {
        using var dir = new TempDir();
        dir.Write("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        dir.Write("App.cs", Stubs + """
            namespace App
            {
                using System.Collections.Generic;
                using MediatR;
                using Microsoft.EntityFrameworkCore;

                public class Order { public int Id { get; set; } }
                public class ShopDb : DbContext { public DbSet<Order> Orders { get; set; } = null!; }

                // `request.Orders` is a payload property that merely shares the DbSet's name.
                public record CountOrdersQuery(List<int> Orders) : IRequest<int>;
                public class CountOrdersQueryHandler : IRequestHandler<CountOrdersQuery, int>
                {
                    public int Handle(CountOrdersQuery request) => request.Orders.Count;
                }

                public record ListOrdersQuery : IRequest<int>;
                public class ListOrdersQueryHandler(ShopDb db) : IRequestHandler<ListOrdersQuery, int>
                {
                    public int Handle(ListOrdersQuery request) => db.Orders.GetHashCode();
                }

                // A DTO reader that merely sees `request.Orders` must not be registered as a
                // repository for Order in the class-scan pass.
                public class OrderReader
                {
                    public int Count(CountOrdersQuery request) => request.Orders.Count;
                }

                public record ReaderOrdersQuery : IRequest<int>;
                public class ReaderOrdersQueryHandler(OrderReader reader) : IRequestHandler<ReaderOrdersQuery, int>
                {
                    public int Handle(ReaderOrdersQuery request) => reader.Count(new CountOrdersQuery([]));
                }
            }
            """);

        var result = Analyzer.Analyze(dir.Path, mode);

        Assert.Empty(Request(result, "CountOrdersQuery").TouchedEntities);
        Assert.Contains("Orders (Order)", Request(result, "ListOrdersQuery").TouchedEntities);
        Assert.Empty(Request(result, "ReaderOrdersQuery").TouchedEntities);
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void GenericRepositoryTypeParameters_AreNeverReportedAsEntities(AnalysisMode mode)
    {
        // ardalis CleanArchitecture shape: `EfRepository<T> : IRepository<T>`.
        using var dir = new TempDir();
        dir.Write("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        dir.Write("App.cs", Stubs + """
            namespace App
            {
                using MediatR;
                using Microsoft.EntityFrameworkCore;

                public class Contributor { public int Id { get; set; } }
                public class Project { public int Id { get; set; } }
                public class AppDbContext : DbContext
                {
                    public DbSet<Contributor> Contributors { get; set; } = null!;
                    public DbSet<Project> Projects { get; set; } = null!;
                }

                public interface IRepository<T> where T : class { }
                public interface IReadRepository<T> where T : class { }
                public class EfRepository<T>(AppDbContext dbContext) : IRepository<T>, IReadRepository<T> where T : class
                {
                    public object All() => dbContext.Set<T>();
                }

                // A concrete, non-generic repository must not leak its entity to every IRepository<X>.
                public class ProjectRepository : IRepository<Project> { }

                public record GetContributorQuery(int Id) : IRequest<int>;
                public class GetContributorHandler(IReadRepository<Contributor> repository) : IRequestHandler<GetContributorQuery, int>
                {
                    public int Handle(GetContributorQuery request) => request.Id;
                }

                public record DeleteContributorCommand(int Id) : IRequest;
                public class DeleteContributorHandler(IRepository<Contributor> repository) : IRequestHandler<DeleteContributorCommand>
                {
                    public void Handle(DeleteContributorCommand request) { }
                }
            }
            """);

        var result = Analyzer.Analyze(dir.Path, mode);

        foreach (var name in new[] { "GetContributorQuery", "DeleteContributorCommand" })
        {
            var touched = Request(result, name).TouchedEntities;
            Assert.Equal(["Contributors (Contributor)"], touched);
        }

        Assert.DoesNotContain(result.Handlers.SelectMany(h => h.TouchedEntities), e => e == "T");
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void DbSetLabels_SupersedeEntityOnlyFallbacks(AnalysisMode mode)
    {
        // `Order` first enters the map as a navigation-only entity of ShopDb (fallback label
        // "Order"), then IAppDbContext declares the real DbSet label "Orders (Order)".
        // The weaker fallback must not make the stronger, source-declared label ambiguous.
        using var dir = new TempDir();
        dir.Write("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        dir.Write("App.cs", Stubs + """
            namespace App
            {
                using System.Collections.Generic;
                using MediatR;
                using Microsoft.EntityFrameworkCore;

                public class Customer
                {
                    public int Id { get; set; }
                    public List<Order> Orders { get; set; } = [];
                }

                public class Order
                {
                    public int Id { get; set; }
                    public int CustomerId { get; set; }
                }

                public class ShopDb : DbContext
                {
                    public DbSet<Customer> Customers { get; set; } = null!;
                }

                public interface IAppDbContext
                {
                    DbSet<Order> Orders { get; }
                }

                public record ShipOrderCommand(int Id) : IRequest;
                public class ShipOrderCommandHandler(IAppDbContext db) : IRequestHandler<ShipOrderCommand>
                {
                    public void Handle(ShipOrderCommand request) => _ = db.Orders;
                }
            }
            """);

        var result = Analyzer.Analyze(dir.Path, mode);

        Assert.Equal(["Orders (Order)"], Request(result, "ShipOrderCommand").TouchedEntities);
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void AmbiguousLocalTypes_DoNotBorrowSiblingScopeEvents(AnalysisMode mode)
    {
        // `item` is a Bar in one block and a Foo in a sibling block. Keeping the first type
        // would attribute item.Update() to Bar's event even though the call is on a Foo.
        using var dir = new TempDir();
        dir.Write("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        dir.Write("App.cs", Stubs + """
            namespace App
            {
                using MediatR;

                public class FooUpdatedEvent : INotification { }
                public class BarUpdatedEvent : INotification { }

                public class Foo
                {
                    public void Update() => Raise(new FooUpdatedEvent());
                    private void Raise(INotification e) { }
                }

                public class Bar
                {
                    public void Update() => Raise(new BarUpdatedEvent());
                    private void Raise(INotification e) { }
                }

                public record TouchFooCommand : IRequest;
                public class TouchFooCommandHandler : IRequestHandler<TouchFooCommand>
                {
                    public void Handle(TouchFooCommand request)
                    {
                        { var item = new Bar(); item.GetHashCode(); }
                        { var item = new Foo(); item.Update(); }
                    }
                }
            }
            """);

        var result = Analyzer.Analyze(dir.Path, mode);

        Assert.DoesNotContain("App.BarUpdatedEvent", Request(result, "TouchFooCommand").PublishedNotifications);
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void SameNamedEntityTypes_DoNotMergeDomainEventsAcrossModules(AnalysisMode mode)
    {
        // Sales.Order and Billing.Order are different declarations. `_order.Update()` on a
        // Sales.Order must not publish the event raised by Billing.Order.Update.
        using var dir = new TempDir();
        dir.Write("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        dir.Write("App.cs", Stubs + """
            namespace Sales
            {
                using MediatR;
                public class SalesOrderUpdatedEvent : INotification { }
                public class Order
                {
                    public void Update() => Raise(new SalesOrderUpdatedEvent());
                    private void Raise(INotification e) { }
                }
            }
            namespace Billing
            {
                using MediatR;
                public class BillingOrderUpdatedEvent : INotification { }
                public class Order
                {
                    public void Update() => Raise(new BillingOrderUpdatedEvent());
                    private void Raise(INotification e) { }
                }
            }
            namespace App
            {
                using MediatR;
                public record UpdateOrderCommand : IRequest;
                public class UpdateOrderCommandHandler : IRequestHandler<UpdateOrderCommand>
                {
                    private readonly Sales.Order _order = new();
                    public void Handle(UpdateOrderCommand request) => _order.Update();
                }
            }
            """);

        var result = Analyzer.Analyze(dir.Path, mode);

        Assert.DoesNotContain(
            "Billing.BillingOrderUpdatedEvent",
            Request(result, "UpdateOrderCommand").PublishedNotifications);
    }

    [Fact]
    public void AgentTrace_DoesNotBorrowAnotherModulesEndpoint_ForSameNamedRequests()
    {
        // kgrzybek modular-monolith shape: two modules each declare AcceptProposalCommand,
        // only the Administration one is exposed over HTTP.
        using var dir = new TempDir();
        dir.Write("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        dir.Write("Stubs.cs", Stubs + """
            namespace Microsoft.AspNetCore.Mvc
            {
                public class ControllerBase { }
                public class RouteAttribute : System.Attribute { public RouteAttribute(string t) { } }
                public class HttpPatchAttribute : System.Attribute { public HttpPatchAttribute(string t) { } }
            }
            """);
        dir.Write("Administration/Accept.cs", """
            namespace Modules.Administration.Proposals
            {
                using MediatR;
                public record AcceptProposalCommand(int Id) : IRequest;
                public class AcceptProposalCommandHandler : IRequestHandler<AcceptProposalCommand> { }
            }
            """);
        dir.Write("Meetings/Accept.cs", """
            namespace Modules.Meetings.Proposals
            {
                using MediatR;
                public record AcceptProposalCommand(int Id) : IRequest;
                public class AcceptProposalCommandHandler : IRequestHandler<AcceptProposalCommand> { }
            }
            """);
        dir.Write("Api/ProposalsController.cs", """
            namespace Api
            {
                using Microsoft.AspNetCore.Mvc;
                using Modules.Administration.Proposals;

                [Route("api/administration/proposals")]
                public class ProposalsController : ControllerBase
                {
                    [HttpPatch("{id}/accept")]
                    public object Accept(int id) => new AcceptProposalCommand(id);
                }
            }
            """);

        var result = Analyzer.Analyze(dir.Path, AnalysisMode.Text);
        var report = AgentReportGenerator.Generate(result, "AcceptProposal");

        var meetingsSection = report[report.IndexOf("Modules.Meetings.Proposals.AcceptProposalCommand", StringComparison.Ordinal)..];
        var nextSlice = meetingsSection.IndexOf("### ", 1, StringComparison.Ordinal);
        if (nextSlice > 0) meetingsSection = meetingsSection[..nextSlice];
        var adminSection = report[report.IndexOf("Modules.Administration.Proposals.AcceptProposalCommand", StringComparison.Ordinal)..];

        Assert.Contains("PATCH /api/administration/proposals/{id}/accept", adminSection);
        Assert.DoesNotContain("Entrypoints", meetingsSection);
        Assert.DoesNotContain("/api/administration", meetingsSection);
    }

    [Fact]
    public void AgentDna_DoesNotClaimAMediatorLibraryThatWasNotDetected()
    {
        using var dir = new TempDir();
        dir.Write("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        dir.Write("App.cs", """
            namespace App
            {
                public interface ICommand { }
                public interface ICommandHandler<T> where T : ICommand { }
                public record PingCommand : ICommand;
                public class PingCommandHandler : ICommandHandler<PingCommand> { }
            }
            """);

        var result = Analyzer.Analyze(dir.Path, AnalysisMode.Text, new AnalysisOptions
        {
            Markers = new HashSet<string>(StringComparer.Ordinal) { "ICommand" },
            HandlerMarkers = new HashSet<string>(StringComparer.Ordinal) { "ICommandHandler" },
        });
        var dna = AgentReportGenerator.GenerateDnaAndIndex(result);

        Assert.DoesNotContain("MediatR", dna);
        Assert.Contains("not detected", dna);
    }

    [Fact]
    public void McpServer_ContentLengthFraming_CountsUtf8BytesForNonAsciiPayloads()
    {
        using var dir = new TempDir();
        dir.Write("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        // The Arabic query makes the byte count larger than the char count. Reading
        // Content-Length as chars would swallow the start of the second message.
        var first = """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"trace_slice","arguments":{"query":"طلب"}}}""";
        var second = """{"jsonrpc":"2.0","id":2,"method":"ping"}""";
        var framed = new StringBuilder()
            .Append($"Content-Length: {Encoding.UTF8.GetByteCount(first)}\r\n\r\n{first}")
            .Append($"Content-Length: {Encoding.UTF8.GetByteCount(second)}\r\n\r\n{second}")
            .ToString();

        using var stdin = new StringReader(framed);
        using var stdout = new StringWriter();
        McpServer.Run(new CliOptions { Path = dir.Path, Mcp = true }, stdin, stdout, TextWriter.Null);

        var ids = stdout.ToString()
            .Split("Content-Length:", StringSplitOptions.RemoveEmptyEntries)
            .Select(chunk => JsonNode.Parse(chunk[(chunk.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4)..])!["id"]!.GetValue<int>())
            .ToList();
        Assert.Equal([1, 2], ids);
    }

    [Fact]
    public void AgentTrace_RanksExactNamesAndCapsTheDefaultResultSet()
    {
        using var dir = new TempDir();
        var result = new AnalysisResult { RootPath = dir.Path };
        foreach (var name in new[]
                 {
                     "CreateOrderCommand", "Order", "OrderItem", "GetOrderQuery",
                     "UpdateOrderCommand", "CancelOrderCommand", "ShipOrderCommand",
                 })
        {
            result.Requests.Add(new CqrsRequest { Name = name, Namespace = "App" });
        }

        var report = AgentReportGenerator.Generate(result, "Order");
        var firstHeading = report
            .Split('\n')
            .First(line => line.StartsWith("### ", StringComparison.Ordinal))
            .TrimEnd();

        // Exact name beats the `OrderItem` prefix, which beats every substring match.
        Assert.Equal("### [Request] `Order` → `void`", firstHeading);
        Assert.Contains("showing 5 of 7", report);
        Assert.Contains("+2 more slice(s)", report);
        Assert.DoesNotContain("`ShipOrderCommand`", report);

        var limited = AgentReportGenerator.Generate(result, "Order", maxSlices: 2);
        Assert.Contains("showing 2 of 7", limited);
        Assert.Contains("`OrderItem`", limited);
        Assert.DoesNotContain("`CreateOrderCommand`", limited);
    }

    [Fact]
    public void AgentTrace_CapsLargePayloadAndHandlerLists()
    {
        using var dir = new TempDir();
        var result = new AnalysisResult { RootPath = dir.Path };

        var request = new CqrsRequest
        {
            Name = "GetThingQuery",
            Namespace = "App",
            ResponseType = "Thing",
        };
        for (var i = 0; i < 25; i++)
            request.Properties.Add(new RequestPropertyInfo($"P{i}", "string"));

        var handler = new HandlerInfo
        {
            Name = "GetThingQueryHandler",
            Namespace = "App",
            RequestName = request.Name,
        };
        for (var i = 0; i < 20; i++)
            handler.Dependencies.Add($"Dependency{i}");
        for (var i = 0; i < 20; i++)
            handler.TouchedEntities.Add($"Sets{i} (Entity{i})");
        request.Handlers.Add(handler.FullName);
        result.Requests.Add(request);
        result.Handlers.Add(handler);

        var report = AgentReportGenerator.Generate(result, "GetThingQuery");

        Assert.Contains("+5 more", report); // payload capped at 20
        Assert.Contains("+8 more", report); // dependencies and DbSets capped at 12
        Assert.DoesNotContain("Dependency19", report);
    }

    [Fact]
    public void AgentDna_DefaultOmitsSliceIndexAndIncludeIndexRestoresIt()
    {
        using var dir = new TempDir();
        var result = new AnalysisResult { RootPath = dir.Path };
        result.Requests.Add(new CqrsRequest { Name = "PingCommand", Namespace = "App" });

        var compact = AgentReportGenerator.GenerateDnaAndIndex(result);
        Assert.Contains("Index omitted", compact);
        Assert.DoesNotContain("- **[Command] `PingCommand`**", compact);

        var indexed = AgentReportGenerator.GenerateDnaAndIndex(result, includeIndex: true);
        Assert.Contains("- **[Command] `PingCommand`**", indexed);
        Assert.DoesNotContain("Index omitted", indexed);
    }

    [Fact]
    public void McpDna_DefaultIsCompactAndIncludeIndexOptsIn()
    {
        using var dir = new TempDir();
        dir.Write("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        dir.Write("App.cs", Stubs + """
            namespace App
            {
                public record PingCommand : MediatR.IRequest;
            }
            """);

        var options = new CliOptions { Path = dir.Path, Mcp = true };

        string Call(string arguments)
        {
            var response = JsonNode.Parse(McpServer.ProcessMessage(
                $$"""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"get_architecture_dna","arguments":{{arguments}} } }""",
                options)!)!;
            Assert.False(response["result"]!["isError"]!.GetValue<bool>());
            return response["result"]!["content"]![0]!["text"]!.GetValue<string>();
        }

        var compact = Call("{}");
        Assert.Contains("Index omitted", compact);
        Assert.DoesNotContain("PingCommand", compact);

        var indexed = Call("""{"includeIndex":true}""");
        Assert.Contains("- **[Command] `PingCommand`**", indexed);
        Assert.DoesNotContain("Index omitted", indexed);
    }

    [Fact]
    public void AgentEntityImpact_ExactMatchDoesNotIncludeSubstringEntities()
    {
        using var dir = new TempDir();
        var result = new AnalysisResult { RootPath = dir.Path };

        var order = new CqrsRequest { Name = "CreateOrderCommand", Namespace = "App" };
        order.TouchedEntities.Add("Orders (Order)");
        var orderItem = new CqrsRequest { Name = "CreateOrderItemCommand", Namespace = "App" };
        orderItem.TouchedEntities.Add("OrderItems (OrderItem)");
        var purchaseOrder = new CqrsRequest { Name = "GetPurchaseOrderQuery", Namespace = "App", ResponseType = "PurchaseOrder" };
        purchaseOrder.TouchedEntities.Add("PurchaseOrders (PurchaseOrder)");
        result.Requests.AddRange([order, orderItem, purchaseOrder]);

        var exact = AgentReportGenerator.GenerateEntityImpact(result, "Order");
        Assert.Contains("`CreateOrderCommand`", exact);
        Assert.DoesNotContain("`CreateOrderItemCommand`", exact);
        Assert.DoesNotContain("`GetPurchaseOrderQuery`", exact);

        var contains = AgentReportGenerator.GenerateEntityImpact(result, "Order", containsFilter: true);
        Assert.Contains("`CreateOrderItemCommand`", contains);
        Assert.Contains("`GetPurchaseOrderQuery`", contains);
    }

    [Fact]
    public void AgentEntityImpact_ShowsOnlyTheRequestedEntitySchema()
    {
        using var dir = new TempDir();
        var result = new AnalysisResult { RootPath = dir.Path };

        var context = new DbContextInfo { Name = "ShopDb" };
        context.DbSets.Add(new DbSetInfo("Order", "Orders"));
        context.DbSets.Add(new DbSetInfo("Customer", "Customers"));
        context.Entities.Add(new EntityInfo { Name = "Order", Namespace = "App" });
        context.Entities.Add(new EntityInfo { Name = "Customer", Namespace = "App" });
        result.DbContexts.Add(context);

        // Matches Order via its response type, but also touches Customer.
        var request = new CqrsRequest { Name = "GetOrderQuery", Namespace = "App", ResponseType = "Order" };
        request.TouchedEntities.Add("Customers (Customer)");
        result.Requests.Add(request);

        var report = AgentReportGenerator.GenerateEntityImpact(result, "Order");
        var schema = report[..report.IndexOf("## Impacted Vertical Slices", StringComparison.Ordinal)];

        Assert.Contains("Entity `Order`", schema);
        Assert.DoesNotContain("Entity `Customer`", schema);
        Assert.DoesNotContain("Customers", schema);
    }

    [Fact]
    public void AgentTrace_MarksConventionGuessedEndpointRoutesAsInferred()
    {
        using var dir = new TempDir();
        dir.Write("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        dir.Write("App.cs", Stubs + """
            namespace App
            {
                using MediatR;

                public record CreateTodoItemCommand(string Title) : IRequest;

                public class TodoEndpoints
                {
                    public void Map(IEndpointRouteBuilder groupBuilder)
                    {
                        groupBuilder.MapPost(CreateTodoItem);
                    }

                    private static System.Threading.Tasks.Task CreateTodoItem(CreateTodoItemCommand command)
                        => System.Threading.Tasks.Task.CompletedTask;
                }
            }
            """);

        var result = Analyzer.Analyze(dir.Path, AnalysisMode.Text);
        var report = AgentReportGenerator.Generate(result, "CreateTodoItemCommand");

        // The `/api/TodoEndpoints` prefix comes from the EndpointGroup class-name convention,
        // not from source, so it must be flagged rather than presented as a fact.
        Assert.Contains("`POST /api/TodoEndpoints` _(inferred)_", report);
        Assert.Contains(
            "POST /api/TodoEndpoints _(inferred)_",
            Request(result, "CreateTodoItemCommand").Entrypoints);
    }

    [Fact]
    public void AgentTrace_DoesNotInventUnresolvedFastEndpointRouteConstants()
    {
        using var dir = new TempDir();
        dir.Write("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        dir.Write("App.cs", Stubs + """
            namespace App
            {
                using MediatR;

                public record GetContributorRequest(int Id) : IRequest<int>;

                public class GetContributorEndpoint : Endpoint<GetContributorRequest>
                {
                    public override void Configure()
                    {
                        Get(GetContributorRequest.Route);
                    }
                }
            }
            """);

        var result = Analyzer.Analyze(dir.Path, AnalysisMode.Text);
        var report = AgentReportGenerator.Generate(result, "GetContributorRequest");

        Assert.Contains("`GET (GetContributorEndpoint)` _(inferred)_", report);
        Assert.DoesNotContain("GetContributorRequest.Route", report);
    }

    [Fact]
    public void McpServer_RejectsNonStringRequestFieldsWithoutThrowing()
    {
        var options = new CliOptions
        {
            Path = Path.Combine(Path.GetTempPath(), "cqrslens-missing-" + Guid.NewGuid().ToString("N")),
            Mcp = true,
        };

        var badMethod = JsonNode.Parse(McpServer.ProcessMessage(
            """{"jsonrpc":"2.0","id":1,"method":42}""", options)!)!;
        Assert.Equal(-32600, badMethod["error"]!["code"]!.GetValue<int>());

        var badTool = JsonNode.Parse(McpServer.ProcessMessage(
            """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":42,"arguments":{}}}""", options)!)!;
        Assert.Equal(-32602, badTool["error"]!["code"]!.GetValue<int>());

        var badVersion = JsonNode.Parse(McpServer.ProcessMessage(
            """{"jsonrpc":"2.0","id":3,"method":"initialize","params":{"protocolVersion":42}}""", options)!)!;
        Assert.Equal("2024-11-05", badVersion["result"]!["protocolVersion"]!.GetValue<string>());
    }

    [Fact]
    public void McpServer_ValidatesToolArguments_BeforeScanning()
    {
        // A missing path would fail the scan; argument validation must win so the agent
        // gets the actionable error instead of an unrelated one.
        var options = new CliOptions { Path = Path.Combine(Path.GetTempPath(), "cqrslens-missing-" + Guid.NewGuid().ToString("N")), Mcp = true };

        var unknown = JsonNode.Parse(McpServer.ProcessMessage(
            """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"nope","arguments":{}}}""", options)!)!;
        Assert.Equal(-32602, unknown["error"]!["code"]!.GetValue<int>());

        var missingQuery = JsonNode.Parse(McpServer.ProcessMessage(
            """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"trace_slice","arguments":{}}}""", options)!)!;
        Assert.True(missingQuery["result"]!["isError"]!.GetValue<bool>());
        Assert.Contains("'query'", missingQuery["result"]!["content"]![0]!["text"]!.GetValue<string>());

        var badLimit = JsonNode.Parse(McpServer.ProcessMessage(
            """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"trace_slice","arguments":{"query":"Order","limit":99}}}""", options)!)!;
        var badLimitText = badLimit["result"]!["content"]![0]!["text"]!.GetValue<string>();
        Assert.True(badLimit["result"]!["isError"]!.GetValue<bool>());
        Assert.Contains("limit", badLimitText);
        Assert.DoesNotContain("path not found", badLimitText);
    }
}
