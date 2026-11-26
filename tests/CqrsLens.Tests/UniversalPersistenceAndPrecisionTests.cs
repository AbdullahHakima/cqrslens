using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// End-to-end coverage for Option 1 capabilities across <see cref="AnalysisMode.Text"/>
/// and <see cref="AnalysisMode.Roslyn"/>:
/// 1. Method-scoped entrypoint slicing (including private helper chains) and inherited
///    MVC <c>[Area]</c> / <c>[ActionName]</c> route resolution.
/// 2. Universal non-EF persistence discovery (Linq2Db, Dapper, MongoDB, NHibernate,
///    ADO.NET, Redis) with strict negative controls for <c>AspNetCore.Http.ISession</c>
///    and non-Redis <c>IDatabase</c>.
/// 3. Reporting, strict-CSP SVG HTML, JSON round-trip, <c>--impact</c>, and MCP tools.
/// </summary>
public sealed class UniversalPersistenceAndPrecisionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "cqrslens-opt1-" + Guid.NewGuid().ToString("N"));

    public UniversalPersistenceAndPrecisionTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private void WriteFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void ControllerActionScoping_AndAreaRouting_IsolatesMethodDependenciesAndRoutes(AnalysisMode mode)
    {
        WriteFile("AdminShop.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        WriteFile("FrameworkStubs.cs", """
            using System;

            namespace Microsoft.AspNetCore.Mvc
            {
                public abstract class ControllerBase { }
                public abstract class Controller : ControllerBase { }
                [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
                public sealed class AreaAttribute(string routeValue) : Attribute { public string RouteValue { get; } = routeValue; }
                [AttributeUsage(AttributeTargets.Method)]
                public sealed class ActionNameAttribute(string name) : Attribute { public string Name { get; } = name; }
                [AttributeUsage(AttributeTargets.Method)]
                public sealed class HttpGetAttribute(string? template = null) : Attribute { }
                [AttributeUsage(AttributeTargets.Method)]
                public sealed class HttpPostAttribute(string? template = null) : Attribute { }
            }

            namespace Microsoft.EntityFrameworkCore
            {
                public class DbContext { }
                public class DbSet<TEntity> where TEntity : class { }
            }
            """);

        WriteFile("DomainAndServices.cs", """
            using Microsoft.EntityFrameworkCore;

            namespace AdminShop;

            public sealed class Order { public int Id { get; set; } }
            public sealed class Invoice { public int Id { get; set; } }
            public sealed class AuditLog { public int Id { get; set; } }
            public sealed class NotificationItem { public int Id { get; set; } }

            public sealed class ShopDbContext : DbContext
            {
                public DbSet<Order> Orders => null!;
                public DbSet<Invoice> Invoices => null!;
                public DbSet<AuditLog> AuditLogs => null!;
                public DbSet<NotificationItem> Notifications => null!;
            }

            public interface IOrderService { void ListOrders(); }
            public sealed class OrderService(ShopDbContext db) : IOrderService
            {
                public void ListOrders() { _ = db.Orders; }
            }

            public interface IBillingService { void Refund(int id); }
            public sealed class BillingService(ShopDbContext db) : IBillingService
            {
                public void Refund(int id) { _ = db.Invoices; }
            }

            public interface IAuditService { void LogDelete(int id); }
            public sealed class AuditService(ShopDbContext db) : IAuditService
            {
                public void LogDelete(int id) { _ = db.AuditLogs; }
            }

            public interface INotificationService { void Notify(); }
            public sealed class NotificationService(ShopDbContext db) : INotificationService
            {
                public void Notify() { _ = db.Notifications; }
            }
            """);

        WriteFile("OrderController.cs", """
            using Microsoft.AspNetCore.Mvc;

            namespace AdminShop.Areas.Admin.Controllers;

            [Area("Admin")]
            public abstract class BaseAdminController : Controller { }

            public sealed class OrderController : BaseAdminController
            {
                private readonly IOrderService _orderService;
                private readonly IBillingService _billingService;
                private readonly IAuditService _auditService;
                private readonly INotificationService _unusedNotification;

                public OrderController(
                    IOrderService orderService,
                    IBillingService billingService,
                    IAuditService auditService,
                    INotificationService unusedNotification)
                {
                    _orderService = orderService;
                    _billingService = billingService;
                    _auditService = auditService;
                    _unusedNotification = unusedNotification;
                }

                [HttpGet]
                public void List()
                {
                    _orderService.ListOrders();
                }

                [ActionName("Delete")]
                [HttpPost]
                public void DeleteConfirmed(int id)
                {
                    DeleteInternal(id);
                }

                private void DeleteInternal(int id)
                {
                    _billingService.Refund(id);
                    LogDeletion(id);
                }

                private void LogDeletion(int id)
                {
                    _auditService.LogDelete(id);
                }
            }
            """);

        var result = Analyzer.Analyze(_root, mode, new AnalysisOptions());

        var listEp = Assert.Single(result.SystemEntrypoints, e => e.Name == "OrderController.List");
        Assert.Equal("GET", listEp.Method);
        Assert.Equal("/admin/order/list", listEp.RouteOrTrigger);
        Assert.Contains(listEp.CalledServices, s => s.EndsWith("OrderService", StringComparison.Ordinal));
        Assert.DoesNotContain(listEp.CalledServices, s => s.Contains("BillingService", StringComparison.Ordinal));
        Assert.DoesNotContain(listEp.CalledServices, s => s.Contains("AuditService", StringComparison.Ordinal));
        Assert.DoesNotContain(listEp.CalledServices, s => s.Contains("NotificationService", StringComparison.Ordinal));
        Assert.Contains(listEp.TouchedEntities, e => e.Contains("Order", StringComparison.Ordinal));
        Assert.DoesNotContain(listEp.TouchedEntities, e => e.Contains("Invoice", StringComparison.Ordinal));
        Assert.DoesNotContain(listEp.TouchedEntities, e => e.Contains("AuditLog", StringComparison.Ordinal));

        var deleteEp = Assert.Single(result.SystemEntrypoints, e => e.Name == "OrderController.DeleteConfirmed");
        Assert.Equal("POST", deleteEp.Method);
        Assert.Equal("/admin/order/delete", deleteEp.RouteOrTrigger);
        Assert.Contains(deleteEp.CalledServices, s => s.EndsWith("BillingService", StringComparison.Ordinal));
        Assert.Contains(deleteEp.CalledServices, s => s.EndsWith("AuditService", StringComparison.Ordinal));
        Assert.DoesNotContain(deleteEp.CalledServices, s => s.Contains("OrderService", StringComparison.Ordinal));
        Assert.DoesNotContain(deleteEp.CalledServices, s => s.Contains("NotificationService", StringComparison.Ordinal));
        Assert.Contains(deleteEp.TouchedEntities, e => e.Contains("Invoice", StringComparison.Ordinal));
        Assert.Contains(deleteEp.TouchedEntities, e => e.Contains("AuditLog", StringComparison.Ordinal));
        Assert.DoesNotContain(deleteEp.TouchedEntities, e => e.Contains("Order", StringComparison.Ordinal));

        var flowSections = DirectFlowDiagramBuilder.Build(result);
        var section = Assert.Single(flowSections);
        Assert.Contains("/admin/order/list", section.Source);
        Assert.Contains("/admin/order/delete", section.Source);
        Assert.Contains("<svg", MermaidSvgRenderer.Render(section.Source));

        var sequences = DirectSequenceBuilder.Build(result);
        Assert.Contains(sequences, s => s.OperationName.Contains("OrderController.List", StringComparison.Ordinal) &&
                                        s.Source.Contains("OrderService", StringComparison.Ordinal) &&
                                        !s.Source.Contains("BillingService", StringComparison.Ordinal));
        Assert.Contains(sequences, s => s.OperationName.Contains("OrderController.DeleteConfirmed", StringComparison.Ordinal) &&
                                        s.Source.Contains("BillingService", StringComparison.Ordinal) &&
                                        !s.Source.Contains("OrderService", StringComparison.Ordinal));
        foreach (var seq in sequences)
            Assert.Contains("<svg", MermaidSvgRenderer.Render(seq.Source));
    }

    [Fact]
    public void UniversalNonEfPersistence_DiscoversAllTechnologiesWithNegativeControlsAndParity()
    {
        WriteUniversalNonEfFixture();

        var text = Analyzer.Analyze(_root, AnalysisMode.Text, new AnalysisOptions());
        var roslyn = Analyzer.Analyze(_root, AnalysisMode.Roslyn, new AnalysisOptions());

        Assert.Empty(text.DbContexts);
        Assert.Empty(roslyn.DbContexts);

        foreach (var result in new[] { text, roslyn })
        {
            Assert.Contains(result.DataStores, d => d.Kind == DataStoreKind.Linq2Db && d.ProjectName == "DataCore");
            Assert.Contains(result.DataStores, d => d.Kind == DataStoreKind.Dapper);
            Assert.Contains(result.DataStores, d => d.Kind == DataStoreKind.MongoDb);
            Assert.Contains(result.DataStores, d => d.Kind == DataStoreKind.NHibernate);
            Assert.Contains(result.DataStores, d => d.Kind == DataStoreKind.AdoNet);
            Assert.Contains(result.DataStores, d => d.Kind == DataStoreKind.Redis);

            // Negative controls: Portal.Web uses Microsoft.AspNetCore.Http.ISession and Custom.Storage.IDatabase only.
            Assert.DoesNotContain(result.DataStores, d =>
                d.ProjectName == "Portal.Web" && d.Kind is DataStoreKind.NHibernate or DataStoreKind.Redis);

            var linq2Db = result.DataStores.First(d => d.Kind == DataStoreKind.Linq2Db);
            Assert.Contains("Customer", linq2Db.Entities);
            Assert.Contains("Order", linq2Db.Entities);

            var dapper = result.DataStores.First(d => d.Kind == DataStoreKind.Dapper);
            Assert.Contains("Payment", dapper.Entities);
            Assert.Contains("Shipments", dapper.Entities);
            Assert.Contains("Invoices", dapper.Entities);

            var mongo = result.DataStores.First(d => d.Kind == DataStoreKind.MongoDb);
            Assert.Contains("Product", mongo.Entities);

            var nh = result.DataStores.First(d => d.Kind == DataStoreKind.NHibernate);
            Assert.Contains("Warehouse", nh.Entities);

            var ado = result.DataStores.First(d => d.Kind == DataStoreKind.AdoNet);
            Assert.Contains("StockMovements", ado.Entities);

            var checkoutEp = Assert.Single(result.SystemEntrypoints, e => e.Name == "CommerceController.Place");
            Assert.Contains(checkoutEp.TouchedDataStores, s => s.Contains("Linq2Db", StringComparison.Ordinal));
            Assert.Contains(checkoutEp.TouchedDataStores, s => s.Contains("Dapper", StringComparison.Ordinal));
            Assert.Contains(checkoutEp.TouchedDataStores, s => s.Contains("Redis", StringComparison.Ordinal));
            Assert.DoesNotContain(checkoutEp.TouchedDataStores, s => s.Contains("NHibernate", StringComparison.Ordinal));

            var sessionEp = Assert.Single(result.SystemEntrypoints, e => e.Name == "SessionProbeController.Ping");
            Assert.Empty(sessionEp.TouchedDataStores);

            var commerceProfile = Assert.Single(result.ServiceProfiles, p => p.ProjectName == "Commerce.Api");
            Assert.True(commerceProfile.DataStoreCount >= 5);
        }

        // Verify Text <-> Roslyn parity on DataStores and SystemEntrypoints.
        var textStores = text.DataStores
            .Select(d => $"{d.ProjectName}|{d.Kind}|{d.Technology}|{d.Name}|{string.Join(",", d.Entities.OrderBy(x => x))}")
            .OrderBy(x => x)
            .ToList();
        var roslynStores = roslyn.DataStores
            .Select(d => $"{d.ProjectName}|{d.Kind}|{d.Technology}|{d.Name}|{string.Join(",", d.Entities.OrderBy(x => x))}")
            .OrderBy(x => x)
            .ToList();
        Assert.Equal(textStores, roslynStores);

        var textEps = text.SystemEntrypoints
            .Select(e => $"{e.ProjectName}|{e.Method}|{e.RouteOrTrigger}|{e.Name}|{string.Join(",", e.TouchedDataStores.OrderBy(x => x))}")
            .OrderBy(x => x)
            .ToList();
        var roslynEps = roslyn.SystemEntrypoints
            .Select(e => $"{e.ProjectName}|{e.Method}|{e.RouteOrTrigger}|{e.Name}|{string.Join(",", e.TouchedDataStores.OrderBy(x => x))}")
            .OrderBy(x => x)
            .ToList();
        Assert.Equal(textEps, roslynEps);
    }

    [Fact]
    public void ReportingJsonHtmlImpactAndMcp_SurfaceUniversalDataStoresAndVerifyClaims()
    {
        WriteUniversalNonEfFixture();

        var result = Analyzer.Analyze(_root, AnalysisMode.Text, new AnalysisOptions());

        // 1. JSON serialization & deserialization round-trip
        var jsonText = JsonReportGenerator.Generate(result);
        var jsonPath = Path.Combine(_root, "arch.json");
        File.WriteAllText(jsonPath, jsonText);
        var parsedDoc = JsonReportGenerator.ReadDocument(jsonPath);
        Assert.NotNull(parsedDoc.DataStores);
        Assert.NotEmpty(parsedDoc.DataStores!);
        Assert.Contains(parsedDoc.DataStores!, d => d.Name == "Linq2Db (DataCore)" && d.Entities!.Contains("Customer"));
        Assert.Contains(parsedDoc.SystemEntrypoints!, e =>
            e.Name == "CommerceController.Place" &&
            (e.TouchedDataStores?.Contains("Linq2Db (DataCore)") ?? false));
        Assert.Contains(parsedDoc.ServiceProfiles!, p => p.ProjectName == "Commerce.Api" && p.DataStoreCount >= 5);

        // 2. Console card, Markdown, strict-CSP zero-JS SVG HTML
        var consoleCard = ConsoleArtwork.RenderSummaryCard(result, "ARCHITECTURE.md", useColor: false, width: 120);
        Assert.True(
            consoleCard.Contains("Data stores", StringComparison.OrdinalIgnoreCase),
            $"Expected 'Data stores' in console summary card:\n{consoleCard}");

        var markdown = ReportGenerator.Generate(result);
        Assert.Contains("Data Stores & Persistence", markdown);
        Assert.Contains("Linq2Db (DataCore)", markdown);

        var html = HtmlReportGenerator.Generate(
            result,
            DiagramRenderMode.Svg,
            CqrsMapSplitMode.Count,
            ReportViewMode.Full,
            focusService: null,
            excludeTests: false);
        Assert.Contains("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; img-src data:;\">", html);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Data stores", html);
        Assert.Contains("Linq2Db (DataCore)", html);
        Assert.Contains("<svg", html);

        var landscape = DiagramBuilder.SystemLandscape(result);
        Assert.NotNull(landscape);
        Assert.Contains("Linq2Db (DataCore)", landscape);
        Assert.Contains("<svg", MermaidSvgRenderer.Render(landscape!));

        // 3. Change Impact (--impact) on datastore & non-EF entity
        var storeImpact = ChangeImpactAnalyzer.Analyze(result, "Linq2Db (DataCore)", "datastore");
        Assert.Equal("analyzed", storeImpact.Status);
        Assert.Equal("datastore", storeImpact.Target?.Kind);
        Assert.NotEmpty(storeImpact.ImpactedItems);
        var impactMermaid = ImpactDiagramBuilder.BuildMermaid(storeImpact);
        Assert.NotNull(impactMermaid);
        Assert.Contains("Linq2Db (DataCore)", impactMermaid);
        Assert.Contains("<svg", MermaidSvgRenderer.Render(impactMermaid!));

        var entityImpact = ChangeImpactAnalyzer.Analyze(result, "Payment", "entity");
        Assert.Equal("analyzed", entityImpact.Status);
        Assert.Contains(entityImpact.ImpactedItems, i => i.Kind == "datastore" || i.Name.Contains("Dapper", StringComparison.OrdinalIgnoreCase));

        // 4. MCP surface: DNA, trace_slice, trace_entity_impact, analyze_change_impact, verify_claim
        var cache = new McpScanCache();
        var options = new CliOptions { Path = _root };

        var dnaText = CallMcpText("get_architecture_dna", new JsonObject { ["includeIndex"] = true }, options, cache);
        Assert.Contains("Linq2Db (DataCore)", dnaText);
        Assert.Contains("Data Stores", dnaText);

        var sliceText = CallMcpText("trace_slice", new JsonObject { ["query"] = "CommerceController.Place" }, options, cache);
        Assert.Contains("Linq2Db (DataCore)", sliceText);

        var sliceJson = CallMcpText("trace_slice", new JsonObject { ["query"] = "CommerceController.Place", ["format"] = "json" }, options, cache);
        using (var doc = JsonDocument.Parse(sliceJson))
        {
            var direct = doc.RootElement.GetProperty("directEntrypoints");
            Assert.True(direct.GetArrayLength() >= 1);
            var touchedStores = direct[0].GetProperty("touchedDataStores").EnumerateArray().Select(e => e.GetString()).ToList();
            Assert.Contains("Linq2Db (DataCore)", touchedStores);
        }

        var entityImpactMcp = CallMcpText("trace_entity_impact", new JsonObject { ["entity"] = "Payment" }, options, cache);
        Assert.Contains("Dapper", entityImpactMcp);

        var changeImpactMcp = CallMcpText(
            "analyze_change_impact",
            new JsonObject { ["target"] = "Linq2Db (DataCore)", ["targetKind"] = "datastore" },
            options,
            cache);
        Assert.Contains("Linq2Db (DataCore)", changeImpactMcp);

        var supportedStore = CallMcpText(
            "verify_claim",
            new JsonObject
            {
                ["subject"] = "CommerceController.Place",
                ["relation"] = "uses data store",
                ["object"] = "Dapper",
            },
            options,
            cache);
        Assert.StartsWith("verdict: supported", supportedStore);

        var contradictedAbsence = CallMcpText(
            "verify_claim",
            new JsonObject
            {
                ["subject"] = "CommerceController.Place",
                ["relation"] = "does not use data store",
                ["object"] = "Redis",
            },
            options,
            cache);
        Assert.StartsWith("verdict: not supported", contradictedAbsence);

        var failClosedAbsence = CallMcpText(
            "verify_claim",
            new JsonObject
            {
                ["subject"] = "CommerceController.Place",
                ["relation"] = "does not use data store",
                ["object"] = "MongoDB",
            },
            options,
            cache);
        Assert.StartsWith("verdict: not determinable", failClosedAbsence);

        // Zero-store solution settles absence cleanly as supported
        var zeroStoreDir = Path.Combine(_root, "ZeroStoreOnly");
        Directory.CreateDirectory(zeroStoreDir);
        File.WriteAllText(Path.Combine(zeroStoreDir, "EmptyApp.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        File.WriteAllText(Path.Combine(zeroStoreDir, "PingController.cs"), """
            namespace Microsoft.AspNetCore.Mvc
            {
                public abstract class ControllerBase { }
                [System.AttributeUsage(System.AttributeTargets.Method)]
                public sealed class HttpGetAttribute(string? template = null) : System.Attribute { }
            }
            namespace EmptyApp
            {
                using Microsoft.AspNetCore.Mvc;
                public sealed class PingController : ControllerBase
                {
                    [HttpGet("ping")]
                    public string Ping() => "ok";
                }
            }
            """);
        var zeroCache = new McpScanCache();
        var zeroOpts = new CliOptions { Path = zeroStoreDir };
        var zeroStoreEpClaim = CallMcpText(
            "verify_claim",
            new JsonObject
            {
                ["path"] = zeroStoreDir,
                ["subject"] = "PingController.Ping",
                ["relation"] = "does not use data store",
                ["object"] = "Redis",
            },
            zeroOpts,
            zeroCache);
        Assert.StartsWith("verdict: supported", zeroStoreEpClaim);

        var zeroStoreProjClaim = CallMcpText(
            "verify_claim",
            new JsonObject
            {
                ["path"] = zeroStoreDir,
                ["subject"] = "EmptyApp",
                ["relation"] = "does not use data store",
                ["object"] = "Redis",
            },
            zeroOpts,
            zeroCache);
        Assert.StartsWith("verdict: supported", zeroStoreProjClaim);
    }

    private void WriteUniversalNonEfFixture()
    {
        WriteFile("DataCore/DataCore.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        WriteFile("DataCore/DataContracts.cs", """
            using System.Collections.Generic;
            using System.Linq;

            namespace LinqToDB
            {
                public interface ITable<T> : IQueryable<T> { }
            }

            namespace DataCore
            {
                public sealed class Customer { public int Id { get; set; } }
                public sealed class Order { public int Id { get; set; } }

                public interface IRepository<TEntity>
                {
                    IQueryable<TEntity> Table { get; }
                }

                public sealed class NopDataProvider
                {
                    public LinqToDB.ITable<T> GetTable<T>() => null!;
                }

                public class EntityRepository<TEntity>(NopDataProvider provider) : IRepository<TEntity>
                {
                    public IQueryable<TEntity> Table => provider.GetTable<TEntity>();
                }

                public sealed class CustomerStore(NopDataProvider provider)
                {
                    public IQueryable<Customer> Customers => provider.GetTable<Customer>();
                }
            }
            """);

        WriteFile("Commerce.Api/Commerce.Api.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="..\DataCore\DataCore.csproj" />
              </ItemGroup>
            </Project>
            """);

        WriteFile("Commerce.Api/ExternalDriverStubs.cs", """
            using System;
            using System.Threading.Tasks;

            namespace Microsoft.AspNetCore.Mvc
            {
                public abstract class ControllerBase { }
                [AttributeUsage(AttributeTargets.Class)]
                public sealed class ApiControllerAttribute : Attribute { }
                [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
                public sealed class RouteAttribute(string template) : Attribute { }
                [AttributeUsage(AttributeTargets.Method)]
                public sealed class HttpPostAttribute(string? template = null) : Attribute { }
                [AttributeUsage(AttributeTargets.Method)]
                public sealed class HttpGetAttribute(string? template = null) : Attribute { }
            }

            namespace System.Data
            {
                public interface IDbConnection { }
            }

            namespace Dapper
            {
                using System.Collections.Generic;
                using System.Data;
                public static class SqlMapper
                {
                    public static Task<IEnumerable<T>> QueryAsync<T>(this IDbConnection cnn, string sql)
                        => Task.FromResult<IEnumerable<T>>(Array.Empty<T>());
                }
            }

            namespace MongoDB.Driver
            {
                public interface IMongoCollection<T> { }
                public interface IMongoDatabase
                {
                    IMongoCollection<T> GetCollection<T>(string name);
                }
            }

            namespace NHibernate
            {
                using System.Linq;
                public interface ISession
                {
                    IQueryable<T> Query<T>();
                }
            }

            namespace Microsoft.Data.SqlClient
            {
                public sealed class SqlConnection(string connectionString) : IDisposable
                {
                    public void Dispose() { }
                }
            }

            namespace StackExchange.Redis
            {
                public interface IDatabase
                {
                    bool StringSet(string key, string value);
                }

                public interface IConnectionMultiplexer
                {
                    IDatabase GetDatabase(int db = -1);
                }
            }
            """);

        WriteFile("Commerce.Api/PersistenceAccessors.cs", """
            using System.Data;
            using System.Linq;
            using System.Threading.Tasks;
            using Dapper;
            using DataCore;
            using Microsoft.Data.SqlClient;
            using MongoDB.Driver;
            using StackExchange.Redis;

            namespace Commerce.Api;

            public sealed class Payment { public int Id { get; set; } }
            public sealed class Product { public int Id { get; set; } }
            public sealed class Warehouse { public int Id { get; set; } }

            public interface IPaymentGateway { Task ChargeAsync(); }
            public sealed class DapperPaymentGateway(IDbConnection db) : IPaymentGateway
            {
                public Task ChargeAsync()
                    => db.QueryAsync<Payment>("SELECT * FROM Shipments s JOIN Invoices i ON i.Id = s.Id");
            }

            public interface ICatalogDocumentStore { void Load(); }
            public sealed class MongoCatalogStore(IMongoDatabase db) : ICatalogDocumentStore
            {
                public void Load()
                {
                    _ = db.GetCollection<Product>("products");
                }
            }

            public interface IBasketCache { void Save(string key); }
            public sealed class RedisBasketCache(IConnectionMultiplexer mux) : IBasketCache
            {
                public void Save(string key)
                {
                    var db = mux.GetDatabase();
                    db.StringSet(key, "1");
                }
            }

            public interface IStockLedger { void Record(); }
            public sealed class AdoStockLedger : IStockLedger
            {
                public void Record()
                {
                    using var conn = new SqlConnection("Server=.;Database=Shop;");
                    const string sql = "INSERT INTO StockMovements (Sku) VALUES ('A1')";
                    _ = sql;
                }
            }
            """);

        WriteFile("Commerce.Api/WarehouseRepository.cs", """
            using System.Linq;
            using NHibernate;

            namespace Commerce.Api;

            public sealed class WarehouseRepository(ISession session)
            {
                public IQueryable<Warehouse> All() => session.Query<Warehouse>();
            }
            """);

        WriteFile("Commerce.Api/CommerceController.cs", """
            using System.Threading.Tasks;
            using DataCore;
            using Microsoft.AspNetCore.Mvc;

            namespace Commerce.Api;

            [ApiController]
            [Route("api/[controller]")]
            public sealed class CommerceController(
                IRepository<Order> orders,
                IPaymentGateway payments,
                IBasketCache cache) : ControllerBase
            {
                [HttpPost("place")]
                public async Task Place()
                {
                    _ = orders.Table;
                    await payments.ChargeAsync();
                    cache.Save("order");
                }
            }
            """);

        // Negative control in its own project: AspNetCore.Http.ISession & non-Redis IDatabase
        WriteFile("Portal.Web/Portal.Web.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        WriteFile("Portal.Web/PortalStubs.cs", """
            using System;

            namespace Microsoft.AspNetCore.Mvc
            {
                public abstract class ControllerBase { }
                [AttributeUsage(AttributeTargets.Class)]
                public sealed class ApiControllerAttribute : Attribute { }
                [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
                public sealed class RouteAttribute(string template) : Attribute { }
                [AttributeUsage(AttributeTargets.Method)]
                public sealed class HttpGetAttribute(string? template = null) : Attribute { }
            }

            namespace Microsoft.AspNetCore.Http
            {
                public interface ISession
                {
                    void SetString(string key, string value);
                }
            }

            namespace Custom.Storage
            {
                public interface IDatabase
                {
                    void Flush();
                }
            }
            """);

        WriteFile("Portal.Web/SessionProbeController.cs", """
            using Custom.Storage;
            using Microsoft.AspNetCore.Http;
            using Microsoft.AspNetCore.Mvc;

            namespace Portal.Web;

            [ApiController]
            [Route("api/[controller]")]
            public sealed class SessionProbeController(ISession session, IDatabase customDb) : ControllerBase
            {
                [HttpGet("ping")]
                public void Ping()
                {
                    session.SetString("k", "v");
                    customDb.Flush();
                }
            }
            """);
    }

    private static string CallMcpText(
        string toolName,
        JsonObject arguments,
        CliOptions options,
        McpScanCache cache)
    {
        var payload = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 1,
            ["method"] = "tools/call",
            ["params"] = new JsonObject
            {
                ["name"] = toolName,
                ["arguments"] = arguments,
            },
        }.ToJsonString();

        var raw = McpServer.ProcessMessage(payload, options, cache);
        Assert.NotNull(raw);
        var root = JsonNode.Parse(raw!)!.AsObject();
        var result = root["result"]!.AsObject();
        Assert.False(result["isError"]!.GetValue<bool>(), $"MCP tool '{toolName}' failed: {result}");
        return result["content"]![0]!["text"]!.GetValue<string>();
    }
}
