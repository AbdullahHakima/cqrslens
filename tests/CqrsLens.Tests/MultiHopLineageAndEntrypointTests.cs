using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// Coverage for Phase A enhancements across both <see cref="AnalysisMode.Text"/> and
/// <see cref="AnalysisMode.Roslyn"/>:
/// 1. Multi-hop Service → Repository → DbContext resolution & transitive entity lineage.
/// 2. <c>IDbContextFactory&lt;T&gt;</c> and open-generic repository fallback + fail-closed ambiguity.
/// 3. <c>MapGroup</c> + route-first Minimal APIs with nested parens, Razor Pages <c>PageModel</c>,
///    and Blazor <c>.razor</c> / <c>.razor.cs</c> component entrypoints.
/// </summary>
public sealed class MultiHopLineageAndEntrypointTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "cqrslens-phase-a-" + Guid.NewGuid().ToString("N"));

    public MultiHopLineageAndEntrypointTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private void WriteFile(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string Contracts => """
        namespace MediatR
        {
            public interface IRequest<out TResponse> { }
            public interface IRequest { }
            public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse> { }
            public interface IRequestHandler<in TRequest> where TRequest : IRequest { }
            public interface IMediator { System.Threading.Tasks.Task Send(object request); System.Threading.Tasks.Task<T> Send<T>(IRequest<T> request); }
        }

        namespace Microsoft.EntityFrameworkCore
        {
            public class DbContext { }
            public class DbSet<TEntity> where TEntity : class { }
            public interface IDbContextFactory<TContext> where TContext : DbContext
            {
                TContext CreateDbContext();
            }
        }
        """;

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void MultiHopServiceRepositoryLineage_PropagatesResolvedEdgeEntitiesAndClaims(AnalysisMode mode)
    {
        WriteFile("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        WriteFile("Contracts.cs", Contracts);
        WriteFile("Persistence.cs", """
            using Microsoft.EntityFrameworkCore;

            namespace App.Persistence;

            public sealed class Order { public int Id { get; set; } }
            public sealed class AuditEntry { public int Id { get; set; } }

            public sealed class OrderingContext : DbContext
            {
                public DbSet<Order> Orders => null!;
                public DbSet<AuditEntry> AuditEntries => null!;
            }

            public interface IOrderRepository { void Save(); }
            public sealed class OrderRepository(OrderingContext db) : IOrderRepository
            {
                public void Save() { var _ = db.Orders; }
            }
            """);
        WriteFile("Services.cs", """
            using App.Persistence;

            namespace App.Services;

            public interface IOrderFulfillmentService { void Ship(); }
            public sealed class OrderFulfillmentService(IOrderRepository repo) : IOrderFulfillmentService
            {
                public void Ship() => repo.Save();
            }
            """);
        WriteFile("Handler.cs", """
            using MediatR;
            using App.Services;

            namespace App.Application;

            public sealed record ShipOrderCommand(int Id) : IRequest<bool>;
            public sealed record UnrelatedCommand : IRequest;

            public sealed class ShipOrderHandler(IOrderFulfillmentService svc) : IRequestHandler<ShipOrderCommand, bool>
            {
                public bool Handle(ShipOrderCommand cmd) { svc.Ship(); return true; }
            }

            public sealed class UnrelatedHandler(IUnknownExternal dep) : IRequestHandler<UnrelatedCommand> { }
            public interface IUnknownExternal { }
            """);

        var result = Analyzer.Analyze(_root, mode);

        var edge = Assert.Single(result.ResolvedEdges);
        Assert.Equal("App.Application.ShipOrderHandler", edge.From);
        Assert.Equal("App.Services.IOrderFulfillmentService", edge.Via);
        Assert.Equal(
            [
                "App.Services.IOrderFulfillmentService",
                "App.Services.OrderFulfillmentService",
                "App.Persistence.IOrderRepository",
                "App.Persistence.OrderRepository",
            ],
            edge.ViaChain);
        Assert.Equal("App.Persistence.OrderingContext", edge.To);

        var request = Assert.Single(result.Requests, r => r.Name == "ShipOrderCommand");
        Assert.Contains("Orders (Order)", request.TouchedEntities);

        var json = JsonReportGenerator.Generate(result);
        Assert.Contains("\"viaChain\"", json);
        Assert.Contains("OrderFulfillmentService", json);

        var flow = Assert.Single(DiagramBuilder.FlowDiagramSections(result));
        Assert.Contains("resolved via IOrderFulfillmentService → OrderFulfillmentService → IOrderRepository → OrderRepository", flow.Source);

        var sequence = Assert.Single(RequestSequenceBuilder.Build(result), s => s.RequestName == "ShipOrderCommand");
        Assert.Contains("uses Orders via IOrderFulfillmentService → OrderFulfillmentService → IOrderRepository → OrderRepository", sequence.Source);

        var impactTrace = AgentReportGenerator.GenerateRequestImpact(result, "ShipOrderCommand");
        Assert.Contains("App.Services.IOrderFulfillmentService -> App.Services.OrderFulfillmentService -> App.Persistence.IOrderRepository -> App.Persistence.OrderRepository", impactTrace);

        var claim = AgentClaimVerifier.Verify(result, "ShipOrderHandler", "uses DbContext", "OrderingContext");
        Assert.Equal(ClaimVerdict.Supported, claim.Verdict);
        Assert.Contains(claim.Evidence, e => e.Contains("OrderRepository", StringComparison.Ordinal));

        var refutedAbsence = AgentClaimVerifier.Verify(result, "ShipOrderHandler", "does not use DbContext", "OrderingContext");
        Assert.Equal(ClaimVerdict.NotSupported, refutedAbsence.Verdict);

        var unrelatedClaim = AgentClaimVerifier.Verify(result, "UnrelatedHandler", "does not use DbContext", "OrderingContext");
        Assert.Equal(ClaimVerdict.NotDeterminable, unrelatedClaim.Verdict);
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void DbContextFactoryAndOpenGenericRepository_ResolveUnambiguouslyAndFailClosedWhenAmbiguous(AnalysisMode mode)
    {
        WriteFile("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        WriteFile("Contracts.cs", Contracts);
        WriteFile("Persistence.cs", """
            using Microsoft.EntityFrameworkCore;

            namespace App.Data;

            public sealed class Product { public int Id { get; set; } }
            public sealed class AppDbContext : DbContext
            {
                public DbSet<Product> Products => null!;
            }

            public interface IRepository<T> where T : class { }
            public sealed class EfRepository<T>(IDbContextFactory<AppDbContext> factory) : IRepository<T> where T : class { }

            public interface IAmbiguousRepo { }
            public sealed class RepoOne(AppDbContext db) : IAmbiguousRepo { }
            public sealed class RepoTwo(AppDbContext db) : IAmbiguousRepo { }
            """);
        WriteFile("Setup.cs", """
            using App.Data;
            namespace App;
            public static class Setup
            {
                public static void Register(object services)
                {
                    services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
                }
            }
            """);
        WriteFile("Handlers.cs", """
            using MediatR;
            using App.Data;

            namespace App.Features;

            public sealed record GetProductQuery(int Id) : IRequest<Product>;
            public sealed class GetProductHandler(IRepository<Product> repo) : IRequestHandler<GetProductQuery, Product> { }

            public sealed record AmbiguousCommand : IRequest;
            public sealed class AmbiguousHandler(IAmbiguousRepo repo) : IRequestHandler<AmbiguousCommand> { }
            """);

        var result = Analyzer.Analyze(_root, mode);

        var edge = Assert.Single(result.ResolvedEdges);
        Assert.Equal("App.Features.GetProductHandler", edge.From);
        Assert.Equal("App.Data.AppDbContext", edge.To);
        Assert.Equal(
            ["App.Data.IRepository", "App.Data.EfRepository", "IDbContextFactory<AppDbContext>"],
            edge.ViaChain);

        Assert.DoesNotContain(result.ResolvedEdges, e => e.From == "App.Features.AmbiguousHandler");
        Assert.Contains("Products (Product)",
            Assert.Single(result.Requests, r => r.Name == "GetProductQuery").TouchedEntities);
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void ModernEntrypoints_MapGroupMethodGroup_RazorPageAndBlazorAreDiscovered(AnalysisMode mode)
    {
        WriteFile("Web.csproj", "<Project Sdk=\"Microsoft.NET.Sdk.Web\" />");
        WriteFile("Contracts.cs", Contracts + """
            namespace Microsoft.AspNetCore.Mvc.RazorPages { public class PageModel { } }
            namespace Microsoft.AspNetCore.Components { public class ComponentBase { } }
            namespace Microsoft.AspNetCore.Mvc
            {
                public sealed class FromHeaderAttribute : System.Attribute { public string? Name { get; set; } }
                public sealed class RouteAttribute(string template) : System.Attribute { }
            }
            """);
        WriteFile("Requests.cs", """
            using MediatR;
            namespace Web.Features.Orders;

            public sealed record CreateOrderCommand(string Customer) : IRequest<int>;

            public static class Edit
            {
                public sealed record Command(int Id, string Name) : IRequest<bool>;
            }

            public sealed record ViewDashboardQuery : IRequest<string>;
            """);
        WriteFile("OrdersApi.cs", """
            using MediatR;
            using Microsoft.AspNetCore.Mvc;
            using Web.Features.Orders;

            namespace Web.Apis;

            public static class OrdersApi
            {
                public static void MapOrdersApi(dynamic app)
                {
                    var api = app
                        .NewVersionedApi("Orders")
                        .MapGroup("api/orders")
                        .HasApiVersion(1.0);
                    api.MapPost("/", CreateOrderAsync);
                }

                public static async Task<int> CreateOrderAsync(
                    [FromHeader(Name = "x-requestid")] System.Guid requestId,
                    CreateOrderCommand command,
                    IMediator mediator)
                    => await mediator.Send(command);
            }
            """);
        WriteFile("Pages/Orders/Edit.cshtml", """
            @page "/orders/edit/{id:int}"
            @model Web.Pages.Orders.EditPageModel
            """);
        WriteFile("Pages/Orders/Edit.cshtml.cs", """
            using MediatR;
            using Microsoft.AspNetCore.Mvc.RazorPages;
            using Web.Features.Orders;

            namespace Web.Pages.Orders;

            public sealed class Edit : PageModel
            {
                public Web.Features.Orders.Edit.Command Data { get; set; } = null!;
                private readonly IMediator _mediator = null!;

                public async System.Threading.Tasks.Task OnPostAsync()
                {
                    await _mediator.Send(Data);
                }
            }
            """);
        WriteFile("Components/Pages/Dashboard.razor", """
            @page "/dashboard"
            @using MediatR
            @using Web.Features.Orders
            @inject IMediator Mediator
            <h3>Dashboard</h3>
            @code {
                protected override async Task OnInitializedAsync()
                {
                    await Mediator.Send(new ViewDashboardQuery());
                }
            }
            """);
        WriteFile("Components/Shared/Row.razor", """
            @using Web.Features.Orders
            @code {
                public ViewDashboardQuery? Unused { get; set; }
            }
            """);

        var result = Analyzer.Analyze(_root, mode);

        Assert.Contains(result.Endpoints, e =>
            e.HttpMethod == "POST" &&
            e.Route == "/api/orders" &&
            e.TargetRequestFullName == "Web.Features.Orders.CreateOrderCommand" &&
            !e.IsInferred);

        Assert.Contains(result.Endpoints, e =>
            e.HttpMethod == "POST" &&
            e.Route == "/orders/edit/{id:int}" &&
            e.TargetRequestFullName == "Web.Features.Orders.Edit.Command" &&
            !e.IsInferred);

        Assert.Contains(result.Endpoints, e =>
            e.HttpMethod == "BLAZOR" &&
            e.Route == "/dashboard" &&
            e.Name == "Dashboard" &&
            e.TargetRequestFullName == "Web.Features.Orders.ViewDashboardQuery" &&
            !e.IsInferred);

        Assert.DoesNotContain(result.Endpoints, e => e.Name == "Row");
    }
}
