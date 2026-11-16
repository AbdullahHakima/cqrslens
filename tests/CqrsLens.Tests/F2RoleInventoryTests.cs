using System.Text.Json;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// F2 CP1 coverage for component role attribution (<see cref="ComponentRole.Controller"/>,
/// <see cref="ComponentRole.Service"/>, <see cref="ComponentRole.Repository"/>), negative
/// exclusions (name-only, unused/unregistered, Settings/Config, external framework types),
/// multi-role merging onto a single <see cref="ComponentId"/>, namesake disambiguation,
/// and <see cref="RelationKind.Exposes"/> links in both text and Roslyn modes.
/// </summary>
public class F2RoleInventoryTests
{
    private const string SharedContracts = """
        namespace MediatR
        {
            public interface IRequest<out TResponse> { }
            public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse> { }
            public interface IMediator { TResponse Send<TResponse>(IRequest<TResponse> request); }
        }

        namespace App.Contracts
        {
            public interface ISettings { }
            public interface IConfig { }
            public interface IRepository<T> { void Save(T item); }
            public interface IOrderRepository : IRepository<string> { void Archive(); }
            public interface IOrderQueryService { string GetSummary(); }
        }
        """;

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void Controllers_DiscoveredByInheritanceAndAttributes_RespectsNegativeControls(AnalysisMode mode)
    {
        using var dir = new TempDir();
        dir.Write("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        dir.Write("Controllers.cs", """
            using Microsoft.AspNetCore.Mvc;

            namespace App.Web
            {
                public abstract class BaseAppController : Controller
                {
                }

                public class DirectControllerBaseController : ControllerBase
                {
                }

                public class InheritedController : BaseAppController
                {
                }

                [ApiController]
                [Route("api/attr")]
                public class AttrEndpoint
                {
                }

                public class ActionAttrHandler
                {
                    [HttpGet("ping")]
                    public string Ping() => "pong";
                }

                public partial class PartialCatalogController : ControllerBase
                {
                }

                public partial class PartialCatalogController
                {
                    [HttpGet("items")]
                    public string Items() => "ok";
                }

                [NonController]
                public class SuppressedController : ControllerBase
                {
                }

                public class FakeController
                {
                }

                public static class StaticControllerHelper
                {
                    public static void Help() { }
                }
            }
            """);

        var result = Analyzer.Analyze(dir.Path, mode);
        var controllers = result.Spine.Components
            .Where(c => c.HasRole(ComponentRole.Controller))
            .Select(c => c.FullName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "App.Web.ActionAttrHandler",
                "App.Web.AttrEndpoint",
                "App.Web.DirectControllerBaseController",
                "App.Web.InheritedController",
                "App.Web.PartialCatalogController",
            ],
            controllers);

        Assert.DoesNotContain(result.Spine.Components, c => c.FullName == "App.Web.BaseAppController");
        Assert.DoesNotContain(result.Spine.Components, c => c.FullName == "App.Web.SuppressedController");
        Assert.DoesNotContain(result.Spine.Components, c => c.FullName == "App.Web.FakeController");
        Assert.DoesNotContain(result.Spine.Components, c => c.FullName == "App.Web.StaticControllerHelper");
        Assert.Single(result.Spine.Components, c => c.FullName == "App.Web.PartialCatalogController");
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void Services_ViaCallEdgesAndExplicitRegistrations_ExcludesUnusedAndNameOnly(AnalysisMode mode)
    {
        using var dir = new TempDir();
        dir.Write("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        dir.Write("Contracts.cs", SharedContracts);
        dir.Write("Services.cs", """
            using MediatR;

            namespace App.Services
            {
                public interface IHelper { void Run(); }
                public class Helper : IHelper { public void Run() { } }

                public interface IUnusedHelper { void Skip(); }
                public class InjectedButUncalledService : IUnusedHelper { public void Skip() { } }

                public class StandaloneService { }
                public class TransientSelfService { }
                public class SingletonSelfService { }
                public class TypedHttpClient { }
                public interface IApiClient { }
                public class ApiClient : IApiClient { }
                public class FactoryCreatedService { }
                public class AutofacSelfService { }
                public class PlainNamedService { }

                public abstract class AbstractBaseService { }
                public static class StaticServiceHelper { public static void Do() { } }

                public record DoWorkCommand(int Id) : IRequest<int>;

                public class DoWorkHandler : IRequestHandler<DoWorkCommand, int>
                {
                    private readonly IHelper _helper;
                    private readonly IUnusedHelper _unused;

                    public DoWorkHandler(IHelper helper, IUnusedHelper unused)
                    {
                        _helper = helper;
                        _unused = unused;
                    }

                    public int Handle(DoWorkCommand request)
                    {
                        _helper.Run();
                        return 1;
                    }
                }
            }
            """);
        dir.Write("Startup.cs", """
            using App.Services;

            namespace App
            {
                public static class Startup
                {
                    public static void Configure(dynamic services, dynamic builder)
                    {
                        services.AddScoped<StandaloneService>();
                        services.AddTransient<TransientSelfService>();
                        services.AddSingleton<SingletonSelfService>();
                        services.AddHttpClient<TypedHttpClient>();
                        services.AddHttpClient<IApiClient, ApiClient>();
                        services.AddScoped<IHelper>(_ => new FactoryCreatedService());
                        builder.RegisterType<AutofacSelfService>().AsSelf();
                    }
                }
            }
            """);

        var result = Analyzer.Analyze(dir.Path, mode);
        var services = result.Spine.Components
            .Where(c => c.HasRole(ComponentRole.Service))
            .Select(c => c.FullName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "App.Services.ApiClient",
                "App.Services.AutofacSelfService",
                "App.Services.FactoryCreatedService",
                "App.Services.Helper",
                "App.Services.SingletonSelfService",
                "App.Services.StandaloneService",
                "App.Services.TransientSelfService",
                "App.Services.TypedHttpClient",
            ],
            services);

        Assert.DoesNotContain(services, s => s == "App.Services.InjectedButUncalledService");
        Assert.DoesNotContain(services, s => s == "App.Services.PlainNamedService");
        Assert.DoesNotContain(services, s => s == "App.Services.AbstractBaseService");
        Assert.DoesNotContain(services, s => s == "App.Services.StaticServiceHelper");
        Assert.DoesNotContain(services, s => s == "App.Services.DoWorkHandler");
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void Repositories_OptionA_ResolvedOnly_NeverServiceFromRepoBindingAlone(AnalysisMode mode)
    {
        using var dir = new TempDir();
        dir.Write("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        dir.Write("Contracts.cs", SharedContracts);
        dir.Write("Repos.cs", """
            using App.Contracts;
            using MediatR;

            namespace App.Data
            {
                public interface IAmbiguousRepository { void Ping(); }
                public interface ISelectedRepository { void Select(); }

                public class GenericEntityRepository<T> : IRepository<T>
                {
                    public void Save(T item) { }
                }

                public class OrderRepository : IOrderRepository
                {
                    public void Save(string item) { }
                    public void Archive() { }
                }

                public class SelectedRepositoryA : ISelectedRepository { public void Select() { } }
                public class SelectedRepositoryB : ISelectedRepository { public void Select() { } }

                public class AmbiguousRepoA : IAmbiguousRepository { public void Ping() { } }
                public class AmbiguousRepoB : IAmbiguousRepository { public void Ping() { } }

                public class PlainNamedRepository { }

                public record PlaceOrder(int Id) : IRequest<int>;

                public class PlaceOrderHandler : IRequestHandler<PlaceOrder, int>
                {
                    private readonly IOrderRepository _orders;
                    private readonly IRepository<string> _generic;

                    public PlaceOrderHandler(IOrderRepository orders, IRepository<string> generic)
                    {
                        _orders = orders;
                        _generic = generic;
                    }

                    public int Handle(PlaceOrder request)
                    {
                        _orders.Archive();
                        _generic.Save("x");
                        return 1;
                    }
                }
            }
            """);
        dir.Write("Startup.cs", """
            using App.Contracts;
            using App.Data;

            namespace App
            {
                public static class Startup
                {
                    public static void Configure(dynamic services)
                    {
                        services.AddScoped(typeof(IRepository<>), typeof(GenericEntityRepository<>));
                        services.AddScoped<ISelectedRepository, SelectedRepositoryA>();
                    }
                }
            }
            """);

        var result = Analyzer.Analyze(dir.Path, mode);
        var repos = result.Spine.Components
            .Where(c => c.HasRole(ComponentRole.Repository))
            .Select(c => c.FullName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "App.Data.GenericEntityRepository`1",
                "App.Data.OrderRepository",
                "App.Data.SelectedRepositoryA",
            ],
            repos);

        // Using IRepository / IOrderRepository bindings must not automatically confer Service role.
        foreach (var repoName in repos)
        {
            var node = Assert.Single(result.Spine.Components, c => c.FullName == repoName);
            Assert.False(node.HasRole(ComponentRole.Service));
        }

        Assert.DoesNotContain(repos, r => r == "App.Data.SelectedRepositoryB");
        Assert.DoesNotContain(repos, r => r == "App.Data.AmbiguousRepoA");
        Assert.DoesNotContain(repos, r => r == "App.Data.AmbiguousRepoB");
        Assert.DoesNotContain(repos, r => r == "App.Data.PlainNamedRepository");
        Assert.DoesNotContain(repos, r => r == "App.Data.PlaceOrderHandler");
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void SettingsConfigAndExternalFrameworkTypes_NeverBecomeServiceComponents(AnalysisMode mode)
    {
        using var dir = new TempDir();
        dir.Write("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        dir.Write("Contracts.cs", SharedContracts);
        dir.Write("Types.cs", """
            using App.Contracts;
            using MediatR;
            using Microsoft.Extensions.Logging;
            using Microsoft.Extensions.Options;

            namespace App.Services
            {
                public class AppSettings : ISettings { }
                public class FilterOptions : IOptions<FilterOptions>
                {
                    public int Threshold() => 5;
                }
                public class DatabaseConfig : IConfig { }

                public interface ICustomLogger { void Write(string msg); }
                public class InTreeLogger : ICustomLogger { public void Write(string msg) { } }

                public record WorkCmd(int Id) : IRequest<int>;

                public class WorkHandler : IRequestHandler<WorkCmd, int>
                {
                    private readonly ILogger<WorkHandler> _logger;
                    private readonly FilterOptions _options;
                    private readonly ICustomLogger _customLogger;

                    public WorkHandler(
                        ILogger<WorkHandler> logger,
                        FilterOptions options,
                        ICustomLogger customLogger)
                    {
                        _logger = logger;
                        _options = options;
                        _customLogger = customLogger;
                    }

                    public int Handle(WorkCmd request)
                    {
                        _logger.LogInformation("hi");
                        _options.Threshold();
                        _customLogger.Write("ok");
                        return 1;
                    }
                }
            }
            """);
        dir.Write("Startup.cs", """
            using App.Contracts;
            using App.Services;
            using Microsoft.AspNetCore.Http;

            namespace App
            {
                public static class Startup
                {
                    public static void Configure(dynamic services)
                    {
                        services.AddSingleton<AppSettings>();
                        services.AddScoped<ISettings, AppSettings>();
                        services.AddSingleton<DatabaseConfig>();
                        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
                        services.AddSingleton<ICustomLogger, InTreeLogger>();
                    }
                }
            }
            """);

        var result = Analyzer.Analyze(dir.Path, mode);
        var services = result.Spine.Components
            .Where(c => c.HasRole(ComponentRole.Service))
            .Select(c => c.FullName)
            .ToArray();

        Assert.Equal(["App.Services.InTreeLogger"], services);
        Assert.DoesNotContain(result.Spine.Components, c => c.FullName.Contains("Settings", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Spine.Components, c => c.FullName.Contains("Config", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Spine.Components, c => c.FullName.Contains("FilterOptions", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Spine.Components, c => c.FullName.Contains("HttpContextAccessor", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Spine.Components, c => c.FullName.Contains("ILogger", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void OneSymbolTwoRoles_AndNamesakesAcrossModules_AndExposesRelation(AnalysisMode mode)
    {
        using var dir = new TempDir();
        dir.Write("ModA/ModA.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        dir.Write("ModB/ModB.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        dir.Write("ModA/Contracts.cs", SharedContracts);
        dir.Write("ModA/Components.cs", """
            using App.Contracts;
            using MediatR;
            using Microsoft.AspNetCore.Mvc;

            namespace ModA.Domain
            {
                public class OrderDataService : IOrderRepository, IOrderQueryService
                {
                    public void Save(string item) { }
                    public void Archive() { }
                    public string GetSummary() => "ok";
                }

                public class WidgetModelFactory { }
                public class Store<T> { }
                public class Store { }

                public record GetOrdersQuery() : IRequest<string>;

                [ApiController]
                [Route("api/orders")]
                public class OrdersController : ControllerBase
                {
                    private readonly IMediator _mediator;
                    public OrdersController(IMediator mediator) => _mediator = mediator;

                    [HttpGet]
                    public string GetAll() => _mediator.Send(new GetOrdersQuery());
                }
            }
            """);
        dir.Write("ModB/Components.cs", """
            namespace ModB.Domain
            {
                public class WidgetModelFactory { }
            }
            """);
        dir.Write("ModA/Startup.cs", """
            using App.Contracts;
            using ModA.Domain;

            namespace ModA
            {
                public static class Startup
                {
                    public static void Configure(dynamic services)
                    {
                        services.AddScoped<IOrderRepository, OrderDataService>();
                        services.AddScoped<IOrderQueryService, OrderDataService>();
                        services.AddScoped<OrdersController>();
                        services.AddScoped<WidgetModelFactory>();
                        services.AddScoped<ModB.Domain.WidgetModelFactory>();
                        services.AddScoped(typeof(Store<>));
                        services.AddScoped<Store>();
                    }
                }
            }
            """);

        var result = Analyzer.Analyze(dir.Path, mode);

        // One symbol with two roles -> single ComponentNode with both roles.
        var dualRepoSvc = Assert.Single(result.Spine.Components, c => c.FullName == "ModA.Domain.OrderDataService");
        Assert.True(dualRepoSvc.HasRole(ComponentRole.Repository));
        Assert.True(dualRepoSvc.HasRole(ComponentRole.Service));

        var dualCtrlSvc = Assert.Single(result.Spine.Components, c => c.FullName == "ModA.Domain.OrdersController");
        Assert.True(dualCtrlSvc.HasRole(ComponentRole.Controller));
        Assert.True(dualCtrlSvc.HasRole(ComponentRole.Service));

        // Namesake classes & generic arity overloads retain distinct ComponentIds.
        var factories = result.Spine.Components
            .Where(c => c.Name == "WidgetModelFactory" && c.HasRole(ComponentRole.Service))
            .ToList();
        Assert.Equal(2, factories.Count);
        Assert.NotEqual(factories[0].Id, factories[1].Id);

        var stores = result.Spine.Components
            .Where(c => c.Name == "Store" && c.HasRole(ComponentRole.Service))
            .ToList();
        Assert.Equal(2, stores.Count);
        Assert.NotEqual(stores[0].Id, stores[1].Id);

        // Exposes relation connects Controller -> Endpoint and serializes as "exposes".
        var exposes = Assert.Single(result.Spine.Relations, r => r.Kind == RelationKind.Exposes);
        Assert.Equal(NodeReferenceKind.Component, exposes.From.Kind);
        Assert.Equal(dualCtrlSvc.Id, exposes.From.ComponentId);
        Assert.Equal(NodeReferenceKind.Endpoint, exposes.To.Kind);
        var occ = Assert.Single(exposes.Occurrences);
        Assert.Equal(EvidenceRule.ControllerEndpointExposure, occ.Rule);

        var json = JsonReportGenerator.Generate(result);
        using var doc = JsonDocument.Parse(json);
        var relKinds = doc.RootElement
            .GetProperty("relations")
            .EnumerateArray()
            .Select(r => r.GetProperty("kind").GetString())
            .ToList();
        Assert.Contains("exposes", relKinds);
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "cqrslens-f2cp1-" + Guid.NewGuid().ToString("N"));

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
}
