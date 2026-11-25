using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace CqrsLens.Tests;

public sealed class SystemOverviewAndCoverageTests : IDisposable
{
    private readonly string _root;

    public SystemOverviewAndCoverageTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cqrslens-sys-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void SystemDiscovery_FlowsProfilesDomainEventsAndImpactPolish_WorkAcrossBackends(AnalysisMode mode)
    {
        CreateSystemWorkspace();

        var result = Analyzer.Analyze(_root, mode, new AnalysisOptions
        {
            GeneratedAt = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero),
        });

        // 1. Direct non-MediatR Minimal API + Controller + gRPC *Base + BackgroundService + IIntegrationEventHandler<T>
        Assert.Contains(result.SystemEntrypoints, e =>
            e.ProjectName == "Catalog.Api" &&
            e.Kind == SystemEntrypointKind.Http &&
            e.ExecutionStyle == SystemExecutionStyle.Direct &&
            e.Method == "GET" &&
            e.RouteOrTrigger == "/api/catalog/products" &&
            e.CalledServices.Any(s => s.Contains("CatalogService", StringComparison.Ordinal)) &&
            e.TouchedDbContexts.Any(c => c.Contains("CatalogDbContext", StringComparison.Ordinal)));

        Assert.Contains(result.SystemEntrypoints, e =>
            e.ProjectName == "Orders.Api" &&
            e.Kind == SystemEntrypointKind.Http &&
            e.ExecutionStyle == SystemExecutionStyle.Direct &&
            e.Method == "GET" &&
            e.RouteOrTrigger.Contains("api/admin/orders", StringComparison.OrdinalIgnoreCase) &&
            e.TouchedDbContexts.Any(c => c.Contains("OrdersDbContext", StringComparison.Ordinal)));

        Assert.Contains(result.SystemEntrypoints, e =>
            e.ProjectName == "Catalog.Api" &&
            e.Kind == SystemEntrypointKind.Grpc &&
            e.Method == "GRPC" &&
            e.RouteOrTrigger == "CatalogGrpcService.GetProduct" &&
            e.TouchedDbContexts.Any(c => c.Contains("CatalogDbContext", StringComparison.Ordinal)));

        Assert.Contains(result.SystemEntrypoints, e =>
            e.ProjectName == "Orders.Worker" &&
            e.Kind == SystemEntrypointKind.Worker &&
            e.ExecutionStyle == SystemExecutionStyle.Worker &&
            e.Method == "WORKER" &&
            e.RouteOrTrigger == "OrderOutboxWorker");

        Assert.Contains(result.SystemEntrypoints, e =>
            e.ProjectName == "Catalog.Api" &&
            e.Kind == SystemEntrypointKind.MessageConsumer &&
            e.ExecutionStyle == SystemExecutionStyle.EventConsumer &&
            e.Method == "CONSUME" &&
            e.RouteOrTrigger == "OrderSubmittedIntegrationEvent" &&
            e.TouchedDbContexts.Any(c => c.Contains("CatalogDbContext", StringComparison.Ordinal)));

        Assert.Contains(result.SystemEntrypoints, e =>
            e.ProjectName == "Shop.Web" &&
            e.Kind == SystemEntrypointKind.Blazor &&
            e.Method == "BLAZOR" &&
            e.RouteOrTrigger == "/catalog");

        // 2. Cross-service IntegrationEvent pub/sub + Aspire AddProject / WithReference linking
        Assert.Contains(result.IntegrationFlows, f =>
            f.Kind == IntegrationFlowKind.IntegrationEvent &&
            f.FromProject == "Orders.Api" &&
            f.ToProject == "Catalog.Api" &&
            (f.MessageType?.Contains("OrderSubmittedIntegrationEvent", StringComparison.Ordinal) == true ||
             f.Label.Contains("OrderSubmittedIntegrationEvent", StringComparison.Ordinal)));

        Assert.Contains(result.IntegrationFlows, f =>
            f.Kind == IntegrationFlowKind.AspireReference &&
            f.FromProject == "Orders.Api" &&
            f.ToProject == "Catalog.Api");

        Assert.Contains(result.IntegrationFlows, f =>
            f.Kind == IntegrationFlowKind.AspireReference &&
            f.FromProject == "Orders.Api" &&
            f.ToProject.Contains("PostgreSQL", StringComparison.Ordinal));

        // 3. ServiceProfileBuilder role inference & technology detection
        var ordersApi = Assert.Single(result.ServiceProfiles, p => p.ProjectName == "Orders.Api");
        Assert.Equal(ServiceRuntimeRole.Api, ordersApi.RuntimeRole);
        Assert.Contains("PostgreSQL", ordersApi.Technologies);
        Assert.Contains("Redis", ordersApi.Technologies);
        Assert.Contains("RabbitMQ", ordersApi.Technologies);
        Assert.Contains("EF Core", ordersApi.Technologies);

        var catalogApi = Assert.Single(result.ServiceProfiles, p => p.ProjectName == "Catalog.Api");
        Assert.Equal(ServiceRuntimeRole.Api, catalogApi.RuntimeRole);
        Assert.Contains("gRPC", catalogApi.Technologies);
        Assert.Contains("EF Core", catalogApi.Technologies);
        Assert.Contains("PostgreSQL", catalogApi.Technologies);

        var worker = Assert.Single(result.ServiceProfiles, p => p.ProjectName == "Orders.Worker");
        Assert.Equal(ServiceRuntimeRole.Worker, worker.RuntimeRole);
        Assert.Contains("RabbitMQ", worker.Technologies);
        Assert.Contains("Redis", worker.Technologies);

        var ui = Assert.Single(result.ServiceProfiles, p => p.ProjectName == "Shop.Web");
        Assert.Equal(ServiceRuntimeRole.Ui, ui.RuntimeRole);
        Assert.Contains("Blazor", ui.Technologies);

        var appHost = Assert.Single(result.ServiceProfiles, p => p.ProjectName == "Shop.AppHost");
        Assert.Equal(ServiceRuntimeRole.Orchestrator, appHost.RuntimeRole);
        Assert.Contains(".NET Aspire", appHost.Technologies);

        var contracts = Assert.Single(result.ServiceProfiles, p => p.ProjectName == "Shop.Contracts");
        Assert.Equal(ServiceRuntimeRole.Library, contracts.RuntimeRole);

        var tests = Assert.Single(result.ServiceProfiles, p => p.ProjectName == "Orders.Tests");
        Assert.Equal(ServiceRuntimeRole.Tests, tests.RuntimeRole);

        // 4. DDD entity AddDomainEvent(new ...) eliminates false UnpublishedNotification
        Assert.Contains(result.Notifications, n => n.Name == "OrderCreatedDomainEvent");
        Assert.DoesNotContain(result.Checks, c =>
            c.Code == "UnpublishedNotification" &&
            c.Target?.Contains("OrderCreatedDomainEvent", StringComparison.Ordinal) == true);

        // 5. ImpactDiagramBuilder clean endpoint label + deduplicated production-only UnresolvedDispatch blind spots
        var impact = ChangeImpactAnalyzer.Analyze(result, "CreateOrderCommand", "request");
        var impactDiagram = ImpactDiagramBuilder.BuildMermaid(impact);
        Assert.NotNull(impactDiagram);
        Assert.Contains("POST /api/orders", impactDiagram);
        Assert.DoesNotContain("CreateOrderAsync)", impactDiagram);

        var unresolvedSpots = impact.BlindSpots
            .Where(b => b.Code == "UnresolvedDispatch")
            .ToList();
        Assert.Contains(result.UnresolvedDispatches, u => u.IsTestProject && u.ReceiverName == "_testMediator");
        var prodDynamicSpot = Assert.Single(unresolvedSpots, b => b.Message.Contains("`_sender`", StringComparison.Ordinal));
        Assert.Equal("Orders.Api", prodDynamicSpot.ProjectName);
        Assert.DoesNotContain(unresolvedSpots, b =>
            b.ProjectName == "Orders.Tests" ||
            b.Message.Contains("`_testMediator`", StringComparison.Ordinal));

        // 6. --view overview, --focus, --exclude-tests Markdown + zero-JS SVG HTML + JSON + get_system_overview MCP
        var overviewMd = ReportGenerator.Generate(
            result,
            CqrsMapSplitMode.Count,
            ReportViewMode.Overview,
            focusService: "Orders.Api",
            excludeTests: true);
        Assert.Contains("## System at a Glance", overviewMd);
        Assert.Contains("### System Landscape", overviewMd);
        Assert.Contains("### Cross-Service Integration Flows", overviewMd);
        Assert.Contains("### System Entrypoints Catalog", overviewMd);
        Assert.Contains("Orders.Api", overviewMd);
        Assert.DoesNotContain("Orders.Tests", overviewMd);
        Assert.DoesNotContain("## Request Sequences", overviewMd);

        var overviewSvgHtml = HtmlReportGenerator.Generate(
            result,
            DiagramRenderMode.Svg,
            CqrsMapSplitMode.Count,
            ReportViewMode.Overview,
            focusService: null,
            excludeTests: true);
        Assert.Contains("<nav class=\"toc\"", overviewSvgHtml);
        Assert.Contains("<details", overviewSvgHtml);
        Assert.Contains("http-equiv=\"Content-Security-Policy\"", overviewSvgHtml);
        Assert.Contains("default-src 'none'; style-src 'unsafe-inline'; img-src data:;", overviewSvgHtml);
        Assert.DoesNotContain("<script", overviewSvgHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<svg ", overviewSvgHtml);

        var fullSvgHtml = HtmlReportGenerator.Generate(
            result,
            DiagramRenderMode.Svg,
            CqrsMapSplitMode.Count,
            ReportViewMode.Full,
            focusService: null,
            excludeTests: true);
        Assert.Contains("<nav class=\"toc\"", fullSvgHtml);
        Assert.Contains("<details", fullSvgHtml);
        Assert.DoesNotContain("<script", fullSvgHtml, StringComparison.OrdinalIgnoreCase);

        var json = JsonReportGenerator.Generate(result);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("systemEntrypoints", out var epJson));
        Assert.True(epJson.GetArrayLength() >= 6);
        Assert.True(doc.RootElement.TryGetProperty("integrationFlows", out var flowJson));
        Assert.True(flowJson.GetArrayLength() >= 2);
        Assert.True(doc.RootElement.TryGetProperty("serviceProfiles", out var profJson));
        Assert.Equal(7, profJson.GetArrayLength());

        var mcpOptions = new CliOptions
        {
            Path = _root,
            Mcp = true,
            AnalysisMode = mode,
            AnalysisModeSpecified = true,
        };
        var mcpReply = JsonNode.Parse(McpServer.ProcessMessage(
            """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"get_system_overview","arguments":{}}}""",
            mcpOptions)!)!["result"]!;
        Assert.False(mcpReply["isError"]!.GetValue<bool>());
        var mcpText = mcpReply["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("## Service Capability Inventory", mcpText);
        Assert.Contains("Orders.Api", mcpText);
        Assert.Contains("Catalog.Api", mcpText);
        Assert.Contains("Orders.Worker", mcpText);
        Assert.Contains("Shop.Web", mcpText);
        Assert.Contains("Shop.AppHost", mcpText);
        Assert.DoesNotContain("**`Orders.Tests`**", mcpText);
    }

    [Fact]
    public void Cli_EndToEnd_OverviewFocusExcludeTests_WritesZeroJsPortalHtml()
    {
        CreateSystemWorkspace();
        var outHtml = Path.Combine(_root, "overview.html");

        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var exitCode = CliRunner.Run(
            [
                _root,
                "--view", "overview",
                "--focus", "Orders.Api",
                "--exclude-tests",
                "--format", "html",
                "--render", "svg",
                "-o", outHtml,
            ],
            stdout,
            stderr);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.True(File.Exists(outHtml));
        var html = File.ReadAllText(outHtml);
        Assert.Contains("<nav class=\"toc\"", html);
        Assert.Contains("<details", html);
        Assert.Contains("Content-Security-Policy", html);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Orders.Api", html);
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void GrpcPrecision_ExcludesStaticExtensionMethodsAndViewModelOverrides(AnalysisMode mode)
    {
        WriteFile("Basket.Api/Basket.Api.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <ItemGroup>
                <PackageReference Include="Grpc.AspNetCore" Version="2.65.0" />
              </ItemGroup>
            </Project>
            """);
        WriteFile("Basket.Api/BasketGrpc.cs", """
            using System;
            using System.Threading.Tasks;
            using Grpc.Core;

            namespace Basket.Api;

            public sealed record BasketRequest(Guid CustomerId);
            public sealed record BasketReply(Guid CustomerId);

            public static class BasketProto
            {
                public abstract class BasketProtoBase
                {
                    public virtual Task<BasketReply> GetBasket(BasketRequest request, ServerCallContext context)
                        => Task.FromResult(new BasketReply(request.CustomerId));
                }
            }

            public sealed class BasketService : BasketProto.BasketProtoBase
            {
                public override Task<BasketReply> GetBasket(BasketRequest request, ServerCallContext context)
                    => Task.FromResult(new BasketReply(request.CustomerId));
            }

            public static class ServerCallContextIdentityExtensions
            {
                public static string? GetUserIdentity(this ServerCallContext context) => null;
            }
            """);

        WriteFile("ClientApp/ClientApp.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Grpc.Net.Client" Version="2.65.0" />
              </ItemGroup>
            </Project>
            """);
        WriteFile("ClientApp/CatalogItemViewModel.cs", """
            using System.Collections.Generic;

            namespace ClientApp;

            public abstract class ViewModelBase
            {
                public virtual void ApplyQueryAttributes(IDictionary<string, object> query) { }
            }

            public sealed class CatalogItemViewModel : ViewModelBase
            {
                public override void ApplyQueryAttributes(IDictionary<string, object> query) { }
            }
            """);

        var result = Analyzer.Analyze(_root, mode);
        var grpcEntrypoints = result.SystemEntrypoints
            .Where(e => e.Kind == SystemEntrypointKind.Grpc)
            .ToList();

        var only = Assert.Single(grpcEntrypoints);
        Assert.Equal("Basket.Api", only.ProjectName);
        Assert.Equal("BasketService.GetBasket", only.RouteOrTrigger);
        Assert.DoesNotContain(result.SystemEntrypoints, e =>
            e.Name.Contains("GetUserIdentity", StringComparison.Ordinal) ||
            e.Name.Contains("ApplyQueryAttributes", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void AspireAddDotnetProject_ResolvesDirectoryRelativeProjectReference(AnalysisMode mode)
    {
        WriteFile("Catalog.API/Catalog.API.csproj", "<Project Sdk=\"Microsoft.NET.Sdk.Web\"></Project>");
        WriteFile("Catalog.API/CatalogEndpoints.cs", """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;

            namespace Catalog.API;

            public static class CatalogEndpoints
            {
                public static void Map(WebApplication app)
                    => app.MapGet("/api/catalog", () => Results.Ok());
            }
            """);

        WriteFile("Ordering.API/Ordering.API.csproj", "<Project Sdk=\"Microsoft.NET.Sdk.Web\"></Project>");
        WriteFile("Ordering.API/OrderingEndpoints.cs", """
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;

            namespace Ordering.API;

            public static class OrderingEndpoints
            {
                public static void Map(WebApplication app)
                    => app.MapGet("/api/orders", () => Results.Ok());
            }
            """);

        WriteFile("eShop.AppHost/eShop.AppHost.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Aspire.Hosting.AppHost" Version="9.0.0" />
              </ItemGroup>
            </Project>
            """);
        WriteFile("eShop.AppHost/AppHost.cs", """
            using Aspire.Hosting;

            var builder = DistributedApplication.CreateBuilder(args);
            var catalogApi = builder.AddDotnetProject("catalog-api", "../Catalog.API");
            builder.AddDotnetProject("ordering-api", "../Ordering.API")
                .WithReference(catalogApi);
            """);

        var result = Analyzer.Analyze(_root, mode);

        Assert.Contains(result.IntegrationFlows, f =>
            f.Kind == IntegrationFlowKind.AspireReference &&
            f.FromProject == "Ordering.API" &&
            f.ToProject == "Catalog.API" &&
            f.Label == "catalog-api");
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void SystemLandscape_UiProjectWithBlazorRoutes_DisplaysNonZeroEndpointCount(AnalysisMode mode)
    {
        WriteFile("WebApp/WebApp.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <ItemGroup>
                <PackageReference Include="Microsoft.AspNetCore.Components.Web" Version="8.0.0" />
              </ItemGroup>
            </Project>
            """);
        WriteFile("WebApp/Components/Pages/Catalog.razor", """
            @page "/catalog"
            <h1>Catalog</h1>
            """);
        WriteFile("WebApp/Components/Pages/Cart.razor", """
            @page "/cart"
            <h1>Cart</h1>
            """);

        var result = Analyzer.Analyze(_root, mode);
        var landscape = DiagramBuilder.SystemLandscape(result);

        Assert.NotNull(landscape);
        Assert.Contains("WebApp · UI · 2 endpoints", landscape);
        Assert.DoesNotContain("WebApp · UI · 0 endpoints", landscape);

        var agentOverview = AgentSystemOverviewGenerator.Generate(result);
        Assert.Contains("UI routes 2", agentOverview);
    }

    private void WriteFile(string relativePath, string contents)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }

    private void CreateSystemWorkspace()
    {
        WriteFile("Shop.Contracts/Shop.Contracts.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        WriteFile("Shop.Contracts/Events.cs", """
            using System.Threading;
            using System.Threading.Tasks;

            namespace Shop.Contracts;

            public sealed record OrderSubmittedIntegrationEvent(Guid OrderId);

            public interface IEventBus
            {
                Task PublishAsync<T>(T integrationEvent, CancellationToken ct = default);
            }

            public interface IIntegrationEventHandler<in TEvent>
            {
                Task HandleAsync(TEvent integrationEvent, CancellationToken ct = default);
            }
            """);

        WriteFile("Orders.Api/Orders.Api.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <ItemGroup>
                <PackageReference Include="MediatR" Version="12.4.1" />
                <PackageReference Include="FluentValidation" Version="11.9.0" />
                <PackageReference Include="Microsoft.EntityFrameworkCore" Version="8.0.0" />
                <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="8.0.0" />
                <PackageReference Include="StackExchange.Redis" Version="2.8.0" />
                <PackageReference Include="RabbitMQ.Client" Version="6.8.1" />
                <ProjectReference Include="..\Shop.Contracts\Shop.Contracts.csproj" />
              </ItemGroup>
            </Project>
            """);

        WriteFile("Orders.Api/OrdersModule.cs", """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using MediatR;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Mvc;
            using Microsoft.EntityFrameworkCore;
            using Shop.Contracts;

            namespace Orders.Api;

            public sealed record OrderCreatedDomainEvent(Guid OrderId) : INotification;

            public sealed class Order
            {
                private readonly List<INotification> _domainEvents = new();
                public Guid Id { get; set; }
                public IReadOnlyList<INotification> DomainEvents => _domainEvents;

                public void AddDomainEvent(INotification domainEvent) => _domainEvents.Add(domainEvent);

                public static Order Create()
                {
                    var order = new Order { Id = Guid.NewGuid() };
                    order.AddDomainEvent(new OrderCreatedDomainEvent(order.Id));
                    return order;
                }
            }

            public sealed class OrdersDbContext : DbContext
            {
                public DbSet<Order> Orders => Set<Order>();
            }

            public sealed record CreateOrderCommand(decimal Total) : IRequest<Guid>;

            public sealed class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, Guid>
            {
                private readonly OrdersDbContext _db;
                private readonly IEventBus _eventBus;

                public CreateOrderCommandHandler(OrdersDbContext db, IEventBus eventBus)
                {
                    _db = db;
                    _eventBus = eventBus;
                }

                public async Task<Guid> Handle(CreateOrderCommand request, CancellationToken ct)
                {
                    var order = Order.Create();
                    _db.Orders.Add(order);
                    await _db.SaveChangesAsync(ct);
                    await _eventBus.PublishAsync(new OrderSubmittedIntegrationEvent(order.Id), ct);
                    return order.Id;
                }
            }

            public static class OrdersApi
            {
                public static void Map(WebApplication app)
                {
                    app.MapPost("/api/orders", CreateOrderAsync);
                }

                public static async Task<IResult> CreateOrderAsync(CreateOrderCommand command, ISender sender, CancellationToken ct)
                {
                    var id = await sender.Send(command, ct);
                    return Results.Ok(id);
                }
            }

            [ApiController]
            [Route("api/admin/orders")]
            public sealed class OrdersAdminController : ControllerBase
            {
                private readonly OrdersDbContext _db;
                private readonly ISender _sender;

                public OrdersAdminController(OrdersDbContext db, ISender sender)
                {
                    _db = db;
                    _sender = sender;
                }

                [HttpGet]
                public async Task<IActionResult> ListAsync(CancellationToken ct)
                {
                    var orders = await _db.Orders.ToListAsync(ct);
                    return Ok(orders);
                }

                [HttpPost("dynamic")]
                public async Task<IActionResult> DispatchDynamicAsync(object dynamicCommand, CancellationToken ct)
                {
                    await _sender.Send(dynamicCommand, ct);
                    await _sender.Send(dynamicCommand, ct);
                    return Ok();
                }
            }
            """);

        WriteFile("Catalog.Api/Catalog.Api.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <ItemGroup>
                <PackageReference Include="Microsoft.EntityFrameworkCore" Version="8.0.0" />
                <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="8.0.0" />
                <PackageReference Include="Grpc.AspNetCore" Version="2.65.0" />
                <ProjectReference Include="..\Shop.Contracts\Shop.Contracts.csproj" />
              </ItemGroup>
            </Project>
            """);

        WriteFile("Catalog.Api/CatalogModule.cs", """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Grpc.Core;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Microsoft.EntityFrameworkCore;
            using Shop.Contracts;

            namespace Catalog.Api;

            public sealed class Product { public Guid Id { get; set; } }

            public sealed class CatalogDbContext : DbContext
            {
                public DbSet<Product> Products => Set<Product>();
            }

            public sealed class CatalogService
            {
                private readonly CatalogDbContext _db;
                public CatalogService(CatalogDbContext db) => _db = db;
                public Task<List<Product>> GetProductsAsync() => _db.Products.ToListAsync();
            }

            public static class CatalogMinimalApi
            {
                public static void Map(WebApplication app)
                {
                    app.MapGet("/api/catalog/products", async (CatalogService svc) =>
                        Results.Ok(await svc.GetProductsAsync()));
                }
            }

            public sealed record ProductRequest(Guid Id);
            public sealed record ProductReply(Guid Id);

            public static class CatalogProto
            {
                public abstract class CatalogProtoBase
                {
                    public virtual Task<ProductReply> GetProduct(ProductRequest request, ServerCallContext context)
                        => Task.FromResult(new ProductReply(request.Id));
                }
            }

            public sealed class CatalogGrpcService : CatalogProto.CatalogProtoBase
            {
                private readonly CatalogService _service;
                public CatalogGrpcService(CatalogService service) => _service = service;

                public override async Task<ProductReply> GetProduct(ProductRequest request, ServerCallContext context)
                {
                    _ = await _service.GetProductsAsync();
                    return new ProductReply(request.Id);
                }
            }

            public sealed class OrderSubmittedIntegrationEventHandler : IIntegrationEventHandler<OrderSubmittedIntegrationEvent>
            {
                private readonly CatalogDbContext _db;
                public OrderSubmittedIntegrationEventHandler(CatalogDbContext db) => _db = db;

                public async Task HandleAsync(OrderSubmittedIntegrationEvent integrationEvent, CancellationToken ct = default)
                {
                    _ = await _db.Products.ToListAsync(ct);
                }
            }
            """);

        WriteFile("Orders.Worker/Orders.Worker.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.Worker">
              <ItemGroup>
                <PackageReference Include="RabbitMQ.Client" Version="6.8.1" />
                <PackageReference Include="StackExchange.Redis" Version="2.8.0" />
                <ProjectReference Include="..\Shop.Contracts\Shop.Contracts.csproj" />
              </ItemGroup>
            </Project>
            """);

        WriteFile("Orders.Worker/OrderOutboxWorker.cs", """
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.Extensions.Hosting;

            namespace Orders.Worker;

            public sealed class OrderOutboxWorker : BackgroundService
            {
                protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
            }
            """);

        WriteFile("Shop.Web/Shop.Web.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <ItemGroup>
                <PackageReference Include="Microsoft.AspNetCore.Components.Web" Version="8.0.0" />
                <ProjectReference Include="..\Shop.Contracts\Shop.Contracts.csproj" />
              </ItemGroup>
            </Project>
            """);

        WriteFile("Shop.Web/Pages/Catalog.razor", """
            @page "/catalog"
            @inject HttpClient Http

            <h1>Catalog</h1>
            """);

        WriteFile("Shop.AppHost/Shop.AppHost.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Aspire.Hosting.AppHost" Version="9.0.0" />
              </ItemGroup>
            </Project>
            """);

        WriteFile("Shop.AppHost/Program.cs", """
            using Aspire.Hosting;

            var builder = DistributedApplication.CreateBuilder(args);
            var pg = builder.AddPostgres("pg").AddDatabase("ordersdb");
            var bus = builder.AddRabbitMQ("eventbus");
            var catalogApi = builder.AddProject<Projects.Catalog_Api>("catalog-api");
            builder.AddProject<Projects.Orders_Api>("orders-api")
                .WithReference(catalogApi)
                .WithReference(pg)
                .WithReference(bus);
            """);

        WriteFile("Orders.Tests/Orders.Tests.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="xunit" Version="2.9.0" />
                <ProjectReference Include="..\Orders.Api\Orders.Api.csproj" />
              </ItemGroup>
            </Project>
            """);

        WriteFile("Orders.Tests/OrderTests.cs", """
            using System.Threading.Tasks;
            using MediatR;
            using Orders.Api;
            using Xunit;

            namespace Orders.Tests;

            public sealed class OrderTests
            {
                [Fact]
                public async Task Orders_TestDoubleDispatch_IsNotProductionBlindSpot()
                {
                    var cmd = new CreateOrderCommand(10m);
                    _ = cmd;
                    ISender _testMediator = null!;
                    object testDynamicCommand = new object();
                    await _testMediator.Send(testDynamicCommand);
                }
            }
            """);
    }
}
