using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// F2 CP2 verification across <see cref="AnalysisMode.Text"/> and <see cref="AnalysisMode.Roslyn"/>:
/// test-project exclusion, factory lambda and generic resolution, controller-factory-service-repository
/// <see cref="RelationKind.Calls"/> chains, Linq2DB <see cref="RelationKind.TouchesData"/> guards,
/// event-publish call-site attribution, unproven observations, additive JSON schema, and
/// severable conventional controller endpoint discovery (<see cref="RelationKind.Exposes"/>).
/// </summary>
public class F2RelationAndResolutionTests
{
    private static readonly JsonSerializerOptions IndentedCamelJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void TestProjectExclusion_EliminatesFalseAmbiguityAndTestServiceRoles(AnalysisMode mode)
    {
        using var dir = new TempDir();
        dir.Write("src/App.Core/App.Core.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        dir.Write("src/App.Core/Services.cs", """
            using Microsoft.Extensions.DependencyInjection;

            namespace App.Services
            {
                public interface IPictureService { string GetUrl(int id); }
                public class PictureService : IPictureService
                {
                    public string GetUrl(int id) => $"/img/{id}";
                }

                public static class AppStartup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddScoped<IPictureService, PictureService>();
                    }
                }
            }
            """);
        dir.Write("tests/App.Tests/App.Tests.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="..\..\src\App.Core\App.Core.csproj" />
              </ItemGroup>
            </Project>
            """);
        dir.Write("tests/App.Tests/TestDoubles.cs", """
            using Microsoft.Extensions.DependencyInjection;
            using App.Services;

            namespace App.Tests
            {
                public class TestPictureService : IPictureService
                {
                    public string GetUrl(int id) => "/test";
                }

                public class ProductModelFactoryForTest { }

                public static class BaseNopTest
                {
                    public static void Init(IServiceCollection services)
                    {
                        services.AddScoped<IPictureService, TestPictureService>();
                        services.AddTransient<ProductModelFactoryForTest>();
                    }
                }
            }
            """);

        var result = Analyzer.Analyze(dir.Path, mode);

        var resolution = Assert.Single(
            result.ServiceResolutions,
            r => r.Interface == "App.Services.IPictureService");
        Assert.False(resolution.IsAmbiguous);
        Assert.Equal("App.Services.PictureService", resolution.Implementation);

        Assert.DoesNotContain(
            result.Spine.Unproven.AmbiguousResolutions,
            a => a.InterfaceSignature == "App.Services.IPictureService");

        var pictureService = Assert.Single(
            result.Spine.Components,
            c => c.FullName == "App.Services.PictureService");
        Assert.True(pictureService.HasRole(ComponentRole.Service));

        Assert.DoesNotContain(
            result.Spine.Components,
            c => c.FullName is "App.Tests.TestPictureService" or "App.Tests.ProductModelFactoryForTest");
        Assert.DoesNotContain(
            result.ServiceResolutions,
            r => string.Equals(r.Implementation, "App.Tests.TestPictureService", StringComparison.Ordinal) ||
                 string.Equals(r.Implementation, "App.Tests.ProductModelFactoryForTest", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void FactoryLambdaParsing_CapturesMultiBranch_IgnoresPropertyChainAndNonImplementing(AnalysisMode mode)
    {
        using var dir = new TempDir();
        dir.Write("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        dir.Write("Services.cs", """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            namespace App
            {
                public interface IThumbService { void Save(); }
                public class LocalThumbService : IThumbService { public void Save() { } }
                public class CloudThumbService : IThumbService { public void Save() { } }

                public interface IDataProvider { }
                public class MainDataProvider : IDataProvider { }
                public interface IProviderManager { IDataProvider DataProvider { get; } }

                public interface IHelper { void Help(); }
                public class UnrelatedClass { }

                public interface IApiClient { void Fetch(); }
                public class TypedApiClient : IApiClient { public void Fetch() { } }

                public static class Startup
                {
                    public static void Configure(IServiceCollection services, bool useCloud)
                    {
                        services.AddScoped<IThumbService>(_ =>
                        {
                            if (useCloud)
                                return new CloudThumbService();
                            return new LocalThumbService();
                        });

                        services.AddTransient(sp => sp.GetRequiredService<IProviderManager>().DataProvider);
                        services.AddScoped<IHelper>(_ => new UnrelatedClass());
                        services.AddHttpClient<IApiClient, TypedApiClient>();
                    }
                }
            }
            """);

        var result = Analyzer.Analyze(dir.Path, mode);

        var thumb = Assert.Single(result.ServiceResolutions, r => r.Interface == "App.IThumbService");
        Assert.True(thumb.IsAmbiguous);
        var thumbCandidates = thumb.Candidates.Select(c => c.Type).OrderBy(t => t, StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { "App.CloudThumbService", "App.LocalThumbService" }, thumbCandidates);

        // .DataProvider on GetRequiredService<IProviderManager>().DataProvider must not bind to IProviderManager.
        var dp = Assert.Single(result.ServiceResolutions, r => r.Interface == "App.IDataProvider");
        Assert.Equal("App.MainDataProvider", dp.Implementation);

        // UnrelatedClass does not implement IHelper, so factory-new guard blocks false binding.
        Assert.DoesNotContain(
            result.ServiceResolutions,
            r => r.Interface == "App.IHelper" && r.Implementation == "App.UnrelatedClass");

        // Typed HttpClient registration resolves cleanly and admits component role.
        var http = Assert.Single(result.ServiceResolutions, r => r.Interface == "App.IApiClient");
        Assert.Equal("App.TypedApiClient", http.Implementation);
        Assert.True(Assert.Single(result.Spine.Components, c => c.FullName == "App.TypedApiClient").HasRole(ComponentRole.Service));
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void OpenGenericFallback_ResolvesWhenNoClosedEntry_AndFailsClosedOnClosedAmbiguity(AnalysisMode mode)
    {
        using var dir = new TempDir();
        dir.Write("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        dir.Write("Generics.cs", """
            using Microsoft.Extensions.DependencyInjection;

            namespace App
            {
                public class Order { public int Id { get; set; } }
                public class SpecialItem { public int Id { get; set; } }
                public class Attr { }
                public class Val { }

                public interface IRepository<T> { T GetById(int id); }
                public class EntityRepository<T> : IRepository<T>
                {
                    public T GetById(int id) => default!;
                }

                public class AltSpecialRepositoryA : IRepository<SpecialItem>
                {
                    public SpecialItem GetById(int id) => new();
                }
                public class AltSpecialRepositoryB : IRepository<SpecialItem>
                {
                    public SpecialItem GetById(int id) => new();
                }

                public interface IAttributeParser<TAttr, TVal> { void Parse(); }
                public class AttributeParser<TAttr, TVal> : IAttributeParser<TAttr, TVal>
                {
                    public void Parse() { }
                }

                public class GenericOrderService
                {
                    private readonly IRepository<Order> _orders;
                    private readonly IRepository<SpecialItem> _specials;
                    private readonly IAttributeParser<Attr, Val> _parser;

                    public GenericOrderService(
                        IRepository<Order> orders,
                        IRepository<SpecialItem> specials,
                        IAttributeParser<Attr, Val> parser)
                    {
                        _orders = orders;
                        _specials = specials;
                        _parser = parser;
                    }

                    public void Work()
                    {
                        _orders.GetById(1);
                        _specials.GetById(2);
                        _parser.Parse();
                    }
                }

                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddScoped<GenericOrderService>();
                        services.AddScoped(typeof(IRepository<>), typeof(EntityRepository<>));
                        services.AddScoped(typeof(IAttributeParser<,>), typeof(AttributeParser<,>));
                        services.AddScoped<IRepository<SpecialItem>, AltSpecialRepositoryA>();
                        services.AddScoped<IRepository<SpecialItem>, AltSpecialRepositoryB>();
                    }
                }
            }
            """);

        var result = Analyzer.Analyze(dir.Path, mode);

        var openCalls = result.Spine.GetRelations(RelationKind.Calls)
            .Where(r => r.From.Name == "App.GenericOrderService")
            .ToList();

        Assert.Contains(openCalls, r =>
            r.Method == "GetById" &&
            r.To.SymbolId?.QualifiedName == "App.EntityRepository`1");
        Assert.Contains(openCalls, r =>
            r.Method == "Parse" &&
            r.To.SymbolId?.QualifiedName == "App.AttributeParser`2");

        // Closed-generic ambiguity on IRepository<SpecialItem> must FAIL CLOSED rather than falling back to EntityRepository<>.
        Assert.DoesNotContain(openCalls, r => r.Method == "GetById" && r.Occurrences.Count > 1);
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void CallChains_FollowControllerFactoryServiceRepo_PartialsFromServicesAndMultiOccurrence(AnalysisMode mode)
    {
        using var dir = new TempDir();
        dir.Write("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        dir.Write("ChainPart1.cs", """
            using Microsoft.AspNetCore.Mvc;
            using Microsoft.Extensions.DependencyInjection;

            namespace App
            {
                public class Order { public int Id { get; set; } }
                public interface IRepository<T> { void Insert(T entity); void Update(T entity); }
                public class EntityRepository<T> : IRepository<T>
                {
                    public void Insert(T entity) { }
                    public void Update(T entity) { }
                }

                public interface IOrderService { void PlaceOrder(Order order); }
                public partial class OrderService : IOrderService
                {
                    private readonly IRepository<Order> _orders;
                    public OrderService(IRepository<Order> orders) => _orders = orders;
                }

                public interface IOrderModelFactory { void Prepare(); }
                public class OrderModelFactory : IOrderModelFactory
                {
                    private readonly IOrderService _orderService;
                    public OrderModelFactory(IOrderService orderService) => _orderService = orderService;
                    public void Prepare() => _orderService.PlaceOrder(new Order());
                }

                public class OrderController : Controller
                {
                    private readonly IOrderModelFactory _factory;
                    public OrderController(IOrderModelFactory factory) => _factory = factory;

                    public IActionResult Index([FromServices] IOrderService directService)
                    {
                        _factory.Prepare();
                        directService.PlaceOrder(new Order());
                        return Ok();
                    }
                }

                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddScoped(typeof(IRepository<>), typeof(EntityRepository<>));
                        services.AddScoped<IOrderService, OrderService>();
                        services.AddScoped<IOrderModelFactory, OrderModelFactory>();
                    }
                }
            }
            """);
        dir.Write("ChainPart2.cs", """
            namespace App
            {
                public partial class OrderService
                {
                    public void PlaceOrder(Order order)
                    {
                        _orders.Insert(order);
                        _orders.Insert(order);
                        // _orders.Update(order);
                    }
                }
            }
            """);

        var result = Analyzer.Analyze(dir.Path, mode);
        var calls = result.Spine.GetRelations(RelationKind.Calls);

        Assert.Contains(calls, r => r.From.Name == "App.OrderController" && r.Method == "Prepare" && r.To.SymbolId?.QualifiedName == "App.OrderModelFactory");
        Assert.Contains(calls, r => r.From.Name == "App.OrderController" && r.Method == "PlaceOrder" && r.To.SymbolId?.QualifiedName == "App.OrderService");
        Assert.Contains(calls, r => r.From.Name == "App.OrderModelFactory" && r.Method == "PlaceOrder" && r.To.SymbolId?.QualifiedName == "App.OrderService");

        var insertRel = Assert.Single(calls, r =>
            r.From.Name == "App.OrderService" &&
            r.Method == "Insert" &&
            r.To.SymbolId?.QualifiedName == "App.EntityRepository`1");
        Assert.Equal(2, insertRel.OccurrenceCount);

        // Commented-out call must never appear.
        Assert.DoesNotContain(calls, r => r.From.Name == "App.OrderService" && r.Method == "Update");
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void Linq2DbTouchesData_RequiresDirectRepoCallOnConcreteEntity_WithoutFactoryPropagation(AnalysisMode mode)
    {
        using var dir = new TempDir();
        dir.Write("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        dir.Write("Data.cs", """
            using System.Collections.Generic;
            using Microsoft.Extensions.DependencyInjection;

            namespace App
            {
                public class Order { public int Id { get; set; } }
                public class Customer { public int Id { get; set; } }
                public class Product { public int Id { get; set; } }

                public interface IRepository<T>
                {
                    IReadOnlyList<T> Table { get; }
                    void Insert(T entity);
                }

                public class EntityRepository<T> : IRepository<T>
                {
                    public IReadOnlyList<T> Table => new List<T>();
                    public void Insert(T entity) { }
                }

                public interface IOrderService { void Create(Order order); }
                public class OrderService : IOrderService
                {
                    private readonly IRepository<Order> _orderRepo;
                    private readonly IRepository<Customer> _unusedCustomerRepo;
                    private readonly IRepository<Product> _tableOnlyProductRepo;

                    public OrderService(
                        IRepository<Order> orderRepo,
                        IRepository<Customer> unusedCustomerRepo,
                        IRepository<Product> tableOnlyProductRepo)
                    {
                        _orderRepo = orderRepo;
                        _unusedCustomerRepo = unusedCustomerRepo;
                        _tableOnlyProductRepo = tableOnlyProductRepo;
                    }

                    public void Create(Order order)
                    {
                        _orderRepo.Insert(order);
                        var products = _tableOnlyProductRepo.Table;
                        _ = products.Count;
                    }
                }

                public interface IGenericAttrService<TAttribute> { void Save(TAttribute a); }
                public class GenericAttrService<TAttribute> : IGenericAttrService<TAttribute>
                {
                    private readonly IRepository<TAttribute> _attrRepo;
                    public GenericAttrService(IRepository<TAttribute> attrRepo) => _attrRepo = attrRepo;
                    public void Save(TAttribute a) => _attrRepo.Insert(a);
                }

                public interface IOrderFactory { void Build(Order o); }
                public class OrderFactory : IOrderFactory
                {
                    private readonly IOrderService _orderService;
                    public OrderFactory(IOrderService orderService) => _orderService = orderService;
                    public void Build(Order o) => _orderService.Create(o);
                }

                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddScoped(typeof(IRepository<>), typeof(EntityRepository<>));
                        services.AddScoped<IOrderService, OrderService>();
                        services.AddScoped<IOrderFactory, OrderFactory>();
                        services.AddScoped(typeof(IGenericAttrService<>), typeof(GenericAttrService<>));
                    }
                }
            }
            """);

        var result = Analyzer.Analyze(dir.Path, mode);

        var touches = result.Spine.GetRelations(RelationKind.TouchesData);
        var orderTouch = Assert.Single(touches);
        Assert.Equal("App.OrderService", orderTouch.From.Name);
        Assert.Equal("App.Order", orderTouch.To.Name);
        Assert.Contains(orderTouch.Occurrences, o => o.Rule == EvidenceRule.RepositoryDataAccess);

        // No Customer (unused repo), no Product (.Table property access only), no TAttribute (type param), no OrderFactory propagation.
        Assert.DoesNotContain(touches, t => t.To.Name is "App.Customer" or "App.Product" or "TAttribute" or "App.TAttribute");
        Assert.DoesNotContain(touches, t => t.From.Name == "App.OrderFactory");
        Assert.Empty(result.Spine.GetRelations(RelationKind.UsesData));
        Assert.Empty(result.Spine.GetRelations(RelationKind.UsesDataResolved));
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void EventPublish_RecordsCallDetailAndUnresolvedConsumer_ZeroPublishesRelations(AnalysisMode mode)
    {
        using var dir = new TempDir();
        dir.Write("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        dir.Write("Events.cs", """
            using Microsoft.Extensions.DependencyInjection;

            namespace App
            {
                public class OrderPlacedEvent { }
                public class ShipmentSentEvent { public ShipmentSentEvent(int id) { } }
                public class OrderPaidEvent { }

                public interface IEventPublisher
                {
                    void Publish<TEvent>(TEvent evt);
                }

                public class EventPublisher : IEventPublisher
                {
                    public void Publish<TEvent>(TEvent evt) { }
                }

                public static class EventPublisherExtensions
                {
                    public static void PublishShipmentSent(this IEventPublisher publisher, int id)
                    {
                        publisher.Publish(new ShipmentSentEvent(id));
                    }
                }

                public class OrderProcessingService
                {
                    private readonly IEventPublisher _publisher;
                    public OrderProcessingService(IEventPublisher publisher) => _publisher = publisher;

                    public void Process()
                    {
                        _publisher.Publish(new OrderPlacedEvent());
                        _publisher.PublishShipmentSent(42);
                        var paid = new OrderPaidEvent();
                        _publisher.Publish(paid);
                    }
                }

                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddScoped<IEventPublisher, EventPublisher>();
                        services.AddScoped<OrderProcessingService>();
                    }
                }
            }
            """);

        var result = Analyzer.Analyze(dir.Path, mode);

        Assert.Empty(result.Spine.GetRelations(RelationKind.Publishes));

        var publishCalls = result.Spine.GetRelations(RelationKind.Calls)
            .Where(r => r.From.Name == "App.OrderProcessingService")
            .SelectMany(r => r.Occurrences)
            .Where(o => o.Rule == EvidenceRule.EventPublishCallSite)
            .ToList();

        var details = publishCalls.Select(o => o.Detail).OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(new[]
        {
            "App.OrderPaidEvent",
            "App.OrderPlacedEvent",
            "App.ShipmentSentEvent",
        }, details);

        var consumerObs = result.Spine.Unproven.Observations
            .Where(o => o.Kind == UnprovenItemKind.UnresolvedEventConsumer)
            .Select(o => o.Subject)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(new[]
        {
            "App.OrderPaidEvent",
            "App.OrderPlacedEvent",
            "App.ShipmentSentEvent",
        }, consumerObs);
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void UnprovenObservations_CaptureConventionDi_ServiceLocator_Reflection_AndUnimplementedInterface(AnalysisMode mode)
    {
        using var dir = new TempDir();
        dir.Write("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        dir.Write("UnprovenCases.cs", """
            using System;
            using Microsoft.Extensions.DependencyInjection;

            namespace App
            {
                public interface IUnimplementedService { void Execute(); }
                public interface ILegacy { }

                public static class EngineContext
                {
                    public static IResolver Current { get; } = null!;
                    public interface IResolver { T Resolve<T>(); }
                }

                public class FragileService
                {
                    private readonly IUnimplementedService _unimplemented;
                    private readonly IServiceProvider _sp;

                    public FragileService(IUnimplementedService unimplemented, IServiceProvider sp)
                    {
                        _unimplemented = unimplemented;
                        _sp = sp;
                    }

                    public void Run()
                    {
                        _unimplemented.Execute();
                        _ = EngineContext.Current.Resolve<ILegacy>();
                        _ = _sp.GetRequiredService<ILegacy>();
                        _ = Activator.CreateInstance(typeof(ILegacy));
                        _ = Type.GetType("App.ILegacy");
                    }
                }

                public static class Startup
                {
                    public static void Register(IServiceCollection services)
                    {
                        services.AddScoped<FragileService>();
                        services.RegisterAssemblyTypes(typeof(Startup).Assembly);
                    }
                }
            }
            """);

        var result = Analyzer.Analyze(dir.Path, mode);
        var obs = result.Spine.Unproven.Observations;

        Assert.Contains(obs, o => o.Kind == UnprovenItemKind.ConventionRegistration);
        Assert.Contains(obs, o => o.Kind == UnprovenItemKind.ServiceLocator && o.Message.Contains("EngineContext", StringComparison.Ordinal));
        Assert.Contains(obs, o => o.Kind == UnprovenItemKind.ServiceLocator && o.Message.Contains("GetRequiredService", StringComparison.Ordinal));
        Assert.Contains(obs, o => o.Kind == UnprovenItemKind.DynamicReflection && o.Message.Contains("Activator.CreateInstance", StringComparison.Ordinal));
        Assert.Contains(obs, o => o.Kind == UnprovenItemKind.DynamicReflection && o.Message.Contains("Type.GetType", StringComparison.Ordinal));
        Assert.Contains(obs, o => o.Kind == UnprovenItemKind.UnimplementedInterface && o.Subject == "App.IUnimplementedService");

        Assert.DoesNotContain(result.Spine.GetRelations(RelationKind.Calls), r => r.To.Name.Contains("IUnimplementedService", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void ConventionalControllerEndpointDiscovery_EmitsExposesAndSpineEndpoints_WithoutLegacyMutation(AnalysisMode mode)
    {
        using var dir = new TempDir();
        dir.Write("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        dir.Write("Controllers.cs", """
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Mvc;

            namespace App.Controllers
            {
                public class OrderController : ControllerBase
                {
                    #region Methods (with parenthesized label)

                    [HttpGet("orders/{id}")]
                    public string ExplicitGet(int id) => id.ToString();

                    [HttpPost, ActionName("SubmitOrder")]
                    public async Task<IActionResult> SaveOrder() => await Task.FromResult<IActionResult>(Ok());

                    #endregion

                    #region Conventional actions (single block)

                    public Task<IActionResult> Details(int id) => Task.FromResult<IActionResult>(View());

                    #endregion

                    [NonAction]
                    public IActionResult Hidden() => View();

                    protected IActionResult InternalHelper() => View();

                    public void NonEndpointHelper() { }

                    #endregion
                }
            }
            """);

        var result = Analyzer.Analyze(dir.Path, mode);

        // Severable R-ENDPOINT gate: legacy result.Endpoints remains untouched.
        Assert.Empty(result.Endpoints);

        Assert.Equal(3, result.Spine.Endpoints.Count);
        Assert.Equal(3, result.Spine.GetRelations(RelationKind.Exposes).Count);

        var getEp = Assert.Single(result.Spine.Endpoints, e => e.Name == "OrderController.ExplicitGet");
        Assert.Equal("GET", getEp.HttpMethod);
        Assert.Equal("/orders/{id}", getEp.Route);
        Assert.False(getEp.IsInferred);

        var postEp = Assert.Single(result.Spine.Endpoints, e => e.Name == "OrderController.SaveOrder");
        Assert.Equal("POST", postEp.HttpMethod);
        Assert.Equal("/Order/SubmitOrder", postEp.Route);
        Assert.Equal(EvidenceStrength.Proven, postEp.VerbStrength);
        Assert.Equal(EvidenceStrength.Inferred, postEp.RouteStrength);

        var convEp = Assert.Single(result.Spine.Endpoints, e => e.Name == "OrderController.Details");
        Assert.Equal("UNKNOWN", convEp.HttpMethod);
        Assert.Equal("/Order/Details", convEp.Route);
        Assert.True(convEp.IsInferred);
        Assert.Equal(EvidenceStrength.Inferred, convEp.VerbStrength);
        Assert.Equal(EvidenceStrength.Inferred, convEp.RouteStrength);
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void AdditiveJsonSections_MatchDirectAndReferenceSerialization_AndRoundTrip(AnalysisMode mode)
    {
        using var dir = new TempDir();
        dir.Write("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        dir.Write("Sample.cs", """
            using Microsoft.AspNetCore.Mvc;
            using Microsoft.Extensions.DependencyInjection;

            namespace App
            {
                public class OrderPlacedEvent { }
                public interface IEventPublisher { void Publish<T>(T evt); }
                public class EventPublisher : IEventPublisher { public void Publish<T>(T evt) { } }

                public class SampleService
                {
                    private readonly IEventPublisher _pub;
                    public SampleService(IEventPublisher pub) => _pub = pub;
                    public void Go() => _pub.Publish(new OrderPlacedEvent());
                }

                public class SampleController : ControllerBase
                {
                    private readonly SampleService _svc;
                    public SampleController(SampleService svc) => _svc = svc;
                    public IActionResult Index() { _svc.Go(); return Ok(); }
                }

                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddScoped<IEventPublisher, EventPublisher>();
                        services.AddScoped<SampleService>();
                    }
                }
            }
            """);

        var result = Analyzer.Analyze(
            dir.Path,
            mode,
            generatedAt: new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero));

        var streamedJson = JsonReportGenerator.Generate(result);
        var doc = JsonReportGenerator.CreateDocument(result);
        var referenceJson = JsonSerializer.Serialize(doc, IndentedCamelJson) + "\n";

        Assert.Equal(referenceJson, streamedJson);

        using var jsonDoc = JsonDocument.Parse(streamedJson);
        var root = jsonDoc.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.True(root.GetProperty("components").GetArrayLength() >= 3);
        Assert.Single(root.GetProperty("spineEndpoints").EnumerateArray());
        Assert.True(root.GetProperty("unproven").GetProperty("totalCount").GetInt32() >= 1);

        var tempFile = Path.Combine(dir.Path, "report.json");
        File.WriteAllText(tempFile, streamedJson);
        var readBack = JsonReportGenerator.ReadDocument(tempFile);
        Assert.NotNull(readBack.Components);
        Assert.NotNull(readBack.SpineEndpoints);
        Assert.NotNull(readBack.Unproven);
        Assert.Contains(
            readBack.Relations!.SelectMany(r => r.Occurrences),
            o => o.Rule == "eventPublishCallSite" && o.Detail == "App.OrderPlacedEvent");

        // Legacy ServiceCallEdges must exclude ComponentMemberInvocation & RepositoryDataAccess.
        Assert.All(result.ServiceCallEdges, e => Assert.NotEmpty(e.Method));
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void RegisteredVsImplementerDistinction_MarkerExclusion_AndPluginManagerContracts(AnalysisMode mode)
    {
        using var dir = new TempDir();
        dir.Write("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        dir.Write("Patterns.cs", """
            using Microsoft.Extensions.DependencyInjection;

            namespace App
            {
                public interface IMarkerModel { }
                public class ProductModel : IMarkerModel { }
                public class OrderModel : IMarkerModel { }

                public interface IMessageService { void Send(); }
                public class DefaultMessageService : IMessageService { public void Send() { } }
                public class OverrideMessageService : IMessageService { public void Send() { } }
                public class ConcreteOnlyMessageService : DefaultMessageService { }

                public interface IPlugin { }
                public interface ISearchPluginContract : IPlugin { }
                public interface IAdminPluginContract : IPlugin { }
                public interface IPluginManager<TPlugin> where TPlugin : class, IPlugin { }
                public class PluginManager<TPlugin> : IPluginManager<TPlugin> where TPlugin : class, IPlugin { }

                public class SearchPluginManager : PluginManager<ISearchPluginContract> { }

                public class AdminMenuService
                {
                    private readonly IPluginManager<IAdminPluginContract> _plugins;
                    public AdminMenuService(IPluginManager<IAdminPluginContract> plugins) => _plugins = plugins;
                }

                public static class Startup
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.AddScoped<IMessageService, DefaultMessageService>();
                        services.AddScoped<IMessageService, OverrideMessageService>();
                        services.AddScoped<ConcreteOnlyMessageService>();
                        services.AddScoped(typeof(IPluginManager<>), typeof(PluginManager<>));
                        services.AddScoped<AdminMenuService>();
                    }
                }
            }
            """);

        var result = Analyzer.Analyze(dir.Path, mode);

        // Registered-vs-implementer: only the 2 IMessageService registrations appear, never the concrete-only subclass.
        var msgAmbig = Assert.Single(
            result.Spine.Unproven.AmbiguousResolutions,
            a => a.InterfaceSignature == "App.IMessageService");
        var msgCandidates = msgAmbig.Candidates.Select(c => c.Type).OrderBy(t => t, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "App.DefaultMessageService", "App.OverrideMessageService" }, msgCandidates);
        Assert.DoesNotContain(msgAmbig.Candidates, c => c.Type == "App.ConcreteOnlyMessageService");

        // Uninjected, unregistered marker interface must never appear in ambiguousResolutions.
        Assert.DoesNotContain(
            result.Spine.Unproven.AmbiguousResolutions,
            a => a.InterfaceSignature == "App.IMarkerModel");

        // PluginManager<T> / IPluginManager<T> contracts surface as dynamic reflection + unimplemented interfaces.
        var obs = result.Spine.Unproven.Observations;
        Assert.Contains(obs, o => o.Kind == UnprovenItemKind.DynamicReflection && o.Subject == "App.ISearchPluginContract");
        Assert.Contains(obs, o => o.Kind == UnprovenItemKind.UnimplementedInterface && o.Subject == "App.ISearchPluginContract");
        Assert.Contains(obs, o => o.Kind == UnprovenItemKind.DynamicReflection && o.Subject == "App.IAdminPluginContract");
        Assert.Contains(obs, o => o.Kind == UnprovenItemKind.UnimplementedInterface && o.Subject == "App.IAdminPluginContract");
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "cqrslens-f2cp2-" + Guid.NewGuid().ToString("N")[..8]);

        public TempDir() => Directory.CreateDirectory(Path);

        public void Write(string relativePath, string contents)
        {
            var fullPath = System.IO.Path.Combine(Path, relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, contents);
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
