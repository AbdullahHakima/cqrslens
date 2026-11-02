using System.Text.RegularExpressions;
using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// Phase 5 reporting coverage: flow diagrams (partitioning, deduplication,
/// planner/rendered edge-count agreement, and Markdown/HTML rendering in both
/// Mermaid and zero-JS SVG modes).
/// </summary>
public sealed class Phase5ReportingTests : IDisposable
{
    private readonly string _root;

    public Phase5ReportingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cqrslens-p5-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private void WriteProject(string name, params string[] references)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        var refs = string.Concat(references.Select(r => $"    <ProjectReference Include=\"{r}\" />\n"));
        var itemGroup = references.Length == 0 ? "" : $"  <ItemGroup>\n{refs}  </ItemGroup>\n";
        File.WriteAllText(Path.Combine(dir, name + ".csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" + itemGroup + "</Project>\n");
    }

    private void WriteFile(string relative, string code)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, code);
    }

    private void WriteContracts()
    {
        WriteFile("App/Contracts.cs", """
            using System.Threading.Tasks;

            namespace MediatR
            {
                public interface IRequest { }
                public interface IRequest<TResponse> { }
                public interface IRequestHandler<TRequest> { }
                public interface IRequestHandler<TRequest, TResponse> { }
                public interface INotification { }
                public interface INotificationHandler<TNotification> { }
                public interface ISender
                {
                    Task Send(IRequest request);
                    Task<TResponse> Send<TResponse>(IRequest<TResponse> request);
                }
                public interface IPublisher { Task Publish(object notification); }
                public interface IMediator : ISender, IPublisher { }
            }

            namespace Microsoft.EntityFrameworkCore
            {
                public class DbContext { }
                public class DbSet<T> { }
            }

            namespace Microsoft.AspNetCore.Mvc
            {
                public class ControllerBase { }
                public class ApiControllerAttribute : System.Attribute { }
                public class HttpPostAttribute : System.Attribute { }
            }
            """);
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void KnownFlow_RendersFlowDiagramsInMarkdownAndHtml_InBothModes(AnalysisMode mode)
    {
        WriteProject("App");
        WriteContracts();
        WriteFile("App/Features.cs", """
            using System.Threading;
            using System.Threading.Tasks;
            using MediatR;
            using Microsoft.EntityFrameworkCore;

            namespace App.Features;

            public sealed class AppDbContext : DbContext { }

            public sealed record CommandA(int Id) : IRequest<int>;
            public sealed record CommandB(int Id) : IRequest<int>;
            public sealed record CommandC(int Id) : IRequest<int>;
            public sealed record EventE(int Id) : INotification;

            public sealed class CommandAHandler : IRequestHandler<CommandA, int>
            {
                private readonly IMediator _mediator;
                private readonly AppDbContext _db;
                public CommandAHandler(IMediator mediator, AppDbContext db)
                {
                    _mediator = mediator;
                    _db = db;
                }

                public async Task<int> Handle(CommandA request, CancellationToken cancellationToken)
                {
                    await _mediator.Publish(new EventE(request.Id));
                    await _mediator.Send(new CommandB(request.Id));
                    return request.Id;
                }
            }

            public sealed class CommandBHandler : IRequestHandler<CommandB, int>
            {
                public Task<int> Handle(CommandB request, CancellationToken cancellationToken)
                    => Task.FromResult(request.Id);
            }

            public sealed class CommandCHandler : IRequestHandler<CommandC, int>
            {
                public Task<int> Handle(CommandC request, CancellationToken cancellationToken)
                    => Task.FromResult(request.Id);
            }

            public sealed class EventEHandler : INotificationHandler<EventE> { }
            """);
        WriteFile("App/OrdersController.cs", """
            using System.Threading.Tasks;
            using MediatR;
            using Microsoft.AspNetCore.Mvc;
            using App.Features;

            namespace App.Api;

            [ApiController]
            public sealed class OrdersController : ControllerBase
            {
                private readonly IMediator _mediator;
                public OrdersController(IMediator mediator) => _mediator = mediator;

                [HttpPost]
                public async Task<int> Create() => await _mediator.Send(new CommandA(1));
            }
            """);

        var result = Analyzer.Analyze(_root, mode);
        var sections = DiagramBuilder.FlowDiagramSections(result);

        Assert.NotEmpty(sections);
        Assert.Equal(result.FlowEdges.Count, sections.Sum(s => s.Edges.Count));
        Assert.All(sections, s =>
        {
            Assert.True(DiagramBuilder.IsWithinMermaidLimits(s.Source));
            Assert.Equal(s.Edges.Count, CountRenderedEdges(s.Source));
        });

        var combinedSource = string.Join("\n", sections.Select(s => s.Source));
        // Endpoint -> Request -> Handler chain
        Assert.Contains("ep_OrdersController_Create([\"OrdersController.Create\"]):::clsFlowEndpoint", combinedSource);
        Assert.Contains("req_CommandA[\"CommandA\"]:::clsFlowRequest", combinedSource);
        Assert.Contains("hdl_CommandAHandler[\"CommandAHandler\"]:::clsFlowHandler", combinedSource);
        Assert.Contains("ep_OrdersController_Create -->|dispatches| req_CommandA", combinedSource);
        Assert.Contains("req_CommandA -->|handled by| hdl_CommandAHandler", combinedSource);
        // Handler -> Request, Handler -> Notification, Handler -> DbContext edges
        Assert.Contains("hdl_CommandAHandler -->|sends| req_CommandB", combinedSource);
        Assert.Contains("hdl_CommandAHandler -->|publishes| ntf_EventE", combinedSource);
        Assert.Contains("hdl_CommandAHandler -->|uses| dbc_AppDbContext", combinedSource);

        // Markdown report includes the Flow section
        var md = ReportGenerator.Generate(result);
        Assert.Contains("## Flow", md);
        Assert.Contains("ep_OrdersController_Create -->|dispatches| req_CommandA", md);
        Assert.Contains("hdl_CommandAHandler -->|publishes| ntf_EventE", md);

        // HTML report in Mermaid mode includes Flow section
        var htmlMermaid = HtmlReportGenerator.Generate(result, DiagramRenderMode.Mermaid);
        Assert.Contains("<h2>Flow</h2>", htmlMermaid);
        Assert.Contains("ep_OrdersController_Create --&gt;|dispatches| req_CommandA", htmlMermaid);
        Assert.Contains("hdl_CommandAHandler --&gt;|publishes| ntf_EventE", htmlMermaid);

        // HTML report in zero-JS SVG mode renders inline SVG for the Flow section
        var htmlSvg = HtmlReportGenerator.Generate(result, DiagramRenderMode.Svg);
        Assert.Contains("<h2>Flow</h2>", htmlSvg);
        Assert.Contains("<title>Flow map</title>", htmlSvg);
        Assert.Contains("data-source=\"ep_OrdersController_Create\" data-target=\"req_CommandA\"", htmlSvg);
        Assert.Contains("data-source=\"hdl_CommandAHandler\" data-target=\"ntf_EventE\"", htmlSvg);
        Assert.DoesNotContain("<script", htmlSvg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FlowDiagramSections_DeduplicatesHandConstructedEdgesAndMatchesRenderedEdgeCount()
    {
        var result = new AnalysisResult { RootPath = "/tmp" };
        var req = new CqrsRequest
        {
            Name = "CreateOrderCommand",
            Namespace = "App.Orders",
            ProjectName = "App",
        };
        req.Handlers.Add("App.Orders.CreateOrderCommandHandler");
        result.Requests.Add(req);

        var edge1 = new FlowEdge
        {
            Kind = FlowEdgeKind.EndpointToRequest,
            From = "OrdersController.Create",
            To = "App.Orders.CreateOrderCommand",
            ProjectName = "App",
        };
        var edge2 = new FlowEdge
        {
            Kind = FlowEdgeKind.RequestToHandler,
            From = "App.Orders.CreateOrderCommand",
            To = "App.Orders.CreateOrderCommandHandler",
            ProjectName = "App",
        };
        // Add each edge twice to verify deduplication in FlowDiagramPlanner.
        result.FlowEdges.Add(edge1);
        result.FlowEdges.Add(edge1);
        result.FlowEdges.Add(edge2);
        result.FlowEdges.Add(edge2);

        var sections = DiagramBuilder.FlowDiagramSections(result);
        var section = Assert.Single(sections);

        Assert.Equal(2, section.Edges.Count);
        Assert.Equal(2, CountRenderedEdges(section.Source));

        var svg = MermaidSvgRenderer.Render(section.Source);
        Assert.Equal(2, Regex.Count(svg, "<g class=\"edge\""));
    }

    [Fact]
    public void FlowDiagramSections_SplitsLargeFeatureWithoutDroppingAnyEdge()
    {
        var result = new AnalysisResult { RootPath = "/tmp" };
        const int chainCount = 220; // 220 * 2 = 440 edges > 400 MaxMermaidEdges
        for (var i = 0; i < chainCount; i++)
        {
            var reqName = $"Command{i:000}";
            var reqFull = $"App.Orders.{reqName}";
            var hdlFull = $"App.Orders.{reqName}Handler";
            var req = new CqrsRequest
            {
                Name = reqName,
                Namespace = "App.Orders",
                ProjectName = "App",
            };
            req.Handlers.Add(hdlFull);
            result.Requests.Add(req);

            result.FlowEdges.Add(new FlowEdge
            {
                Kind = FlowEdgeKind.EndpointToRequest,
                From = $"OrdersController.Action{i:000}",
                To = reqFull,
                ProjectName = "App",
            });
            result.FlowEdges.Add(new FlowEdge
            {
                Kind = FlowEdgeKind.RequestToHandler,
                From = reqFull,
                To = hdlFull,
                ProjectName = "App",
            });
        }

        var sections = DiagramBuilder.FlowDiagramSections(result);

        Assert.Equal(2, sections.Count);
        Assert.Equal(new[] { 1, 2 }, sections.Select(s => s.Part));
        Assert.All(sections, s => Assert.Equal(2, s.PartCount));
        Assert.All(sections, s => Assert.True(DiagramBuilder.IsWithinMermaidLimits(s.Source)));
        Assert.All(sections, s => Assert.Equal(s.Edges.Count, CountRenderedEdges(s.Source)));

        // Every flow edge appears in exactly one section.
        Assert.Equal(result.FlowEdges.Count, sections.Sum(s => s.Edges.Count));
        var partitionedTriples = sections
            .SelectMany(s => s.Edges)
            .Select(e => (e.Kind, e.From, e.To))
            .ToList();
        Assert.Equal(result.FlowEdges.Count, partitionedTriples.Distinct().Count());
    }

    [Fact]
    public void FlowSvgLayout_PlacesChainInHorizontalRanksAndPreservesCqrsMapThreeColumnLayout()
    {
        var result = new AnalysisResult { RootPath = "/tmp" };
        var req = new CqrsRequest
        {
            Name = "CreateOrderCommand",
            Namespace = "App.Orders",
            ProjectName = "App",
        };
        req.Handlers.Add("App.Orders.CreateOrderCommandHandler");
        req.Validators.Add("App.Orders.CreateOrderCommandValidator");
        result.Requests.Add(req);

        result.FlowEdges.Add(new FlowEdge
        {
            Kind = FlowEdgeKind.EndpointToRequest,
            From = "OrdersController.Create",
            To = "App.Orders.CreateOrderCommand",
            ProjectName = "App",
        });
        result.FlowEdges.Add(new FlowEdge
        {
            Kind = FlowEdgeKind.RequestToHandler,
            From = "App.Orders.CreateOrderCommand",
            To = "App.Orders.CreateOrderCommandHandler",
            ProjectName = "App",
        });
        result.FlowEdges.Add(new FlowEdge
        {
            Kind = FlowEdgeKind.HandlerToNotification,
            From = "App.Orders.CreateOrderCommandHandler",
            To = "App.Orders.OrderCreatedEvent",
            ProjectName = "App",
        });

        var flowSource = Assert.Single(DiagramBuilder.FlowDiagramSections(result)).Source;
        var flowSvg = MermaidSvgRenderer.Render(flowSource);

        // Endpoint -> Request -> Handler -> Notification must have strictly increasing X coordinates (not a 1-column pole).
        var epX = ExtractNodeX(flowSvg, "ep_OrdersController_Create");
        var reqX = ExtractNodeX(flowSvg, "req_CreateOrderCommand");
        var hdlX = ExtractNodeX(flowSvg, "hdl_CreateOrderCommandHandler");
        var ntfX = ExtractNodeX(flowSvg, "ntf_OrderCreatedEvent");
        Assert.True(epX < reqX && reqX < hdlX && hdlX < ntfX,
            $"Expected horizontal rank progression epX < reqX < hdlX < ntfX, got {epX}, {reqX}, {hdlX}, {ntfX}.");

        // Regression check: CQRS Map still uses 3-column LayoutLeftToRightMap (validator -> request subgraph -> handler).
        var cqrsSource = Assert.Single(DiagramBuilder.CqrsMapSections(result)).Source;
        var cqrsSvg = MermaidSvgRenderer.Render(cqrsSource);
        var valX = ExtractNodeX(cqrsSvg, "val_CreateOrderCommandValidator");
        var mapReqX = ExtractNodeX(cqrsSvg, "req_CreateOrderCommand");
        var mapHdlX = ExtractNodeX(cqrsSvg, "hdl_CreateOrderCommandHandler");
        Assert.True(valX < mapReqX && mapReqX < mapHdlX,
            $"Expected CQRS Map 3-column layout valX < mapReqX < mapHdlX, got {valX}, {mapReqX}, {mapHdlX}.");
    }

    [Fact]
    public void ParallelogramPublisherNodes_ParseAndRenderOnceInSvgWithoutRawSyntax()
    {
        var result = new AnalysisResult { RootPath = "/tmp" };
        result.Notifications.Add(new CqrsNotification
        {
            Name = "CustomerCreated",
            Namespace = "Northwind.Application.Customers.Commands.CreateCustomer",
            ProjectName = "Application",
            Handlers = { "Northwind.Application.Customers.Commands.CreateCustomer.CustomerCreatedHandler" },
        });
        result.DispatchCallSites.Add(new DispatchCallSite
        {
            TargetFullName = "Northwind.Application.Customers.Commands.CreateCustomer.CustomerCreated",
            TargetKind = DispatchTargetKind.Notification,
            SenderFullName = "Northwind.Application.Customers.Commands.CreateCustomer.CreateCustomerCommand.Handler",
            ProjectName = "Application",
        });

        var map = DiagramBuilder.NotificationMap(result);
        Assert.NotNull(map);
        Assert.Contains("pub_CreateCustomerCommand_Handler[/\"CreateCustomerCommand.Handler\"/]:::clsPublisher", map);

        var svg = MermaidSvgRenderer.Render(map!);
        Assert.Equal(1, Regex.Count(svg, "data-id=\"pub_CreateCustomerCommand_Handler\""));
        Assert.DoesNotContain("[/", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("/]", svg, StringComparison.Ordinal);
        Assert.Contains(">CreateCustomerCommand.Handler<", svg);
        Assert.Contains("data-source=\"pub_CreateCustomerCommand_Handler\" data-target=\"ntf_CustomerCreated\"", svg);
    }

    [Fact]
    public void ShortTypeName_HandlesGenericHandlerSpecificHandlerAndDuplicateLeafDisambiguation()
    {
        // Generic leaf "Handler" includes enclosing type.
        Assert.Equal(
            "CreateCustomerCommand.Handler",
            MermaidDiagramText.ShortTypeName("Northwind.Application.Customers.Commands.CreateCustomer.CreateCustomerCommand.Handler"));

        // Specific leaf is kept as-is.
        Assert.Equal(
            "UpsertCategoryCommandHandler",
            MermaidDiagramText.ShortTypeName("Northwind.Application.Categories.Commands.UpsertCategory.UpsertCategoryCommand.UpsertCategoryCommandHandler"));

        // Duplicate leaf names across modules use minimal disambiguating prefix, never the full namespace.
        var peers = new[]
        {
            "CompanyName.MyMeetings.Modules.Meetings.Domain.Users.Events.UserCreatedDomainEvent",
            "CompanyName.MyMeetings.Modules.Administration.Domain.Users.Events.UserCreatedDomainEvent",
        };
        Assert.Equal(
            "Meetings.UserCreatedDomainEvent",
            MermaidDiagramText.ShortTypeName(peers[0], peers));
        Assert.Equal(
            "Administration.UserCreatedDomainEvent",
            MermaidDiagramText.ShortTypeName(peers[1], peers));
    }

    [Fact]
    public void FlowchartLayout_WrapsWideRanksAndSubgraphRowsIntoMultipleRows()
    {
        var result = new AnalysisResult { RootPath = "/tmp" };
        for (var i = 0; i < 20; i++)
        {
            result.Projects.Add(new ProjectInfo
            {
                Name = $"Company.Solution.Modules.Module{i:00}.Application",
                FilePath = $"/tmp/Module{i:00}.csproj",
                DirectoryPath = $"/tmp/Module{i:00}",
                Layer = "Application",
            });
        }

        var projectSvg = MermaidSvgRenderer.Render(DiagramBuilder.ProjectGraph(result)!);
        var (projW, projH) = ExtractViewBox(projectSvg);
        Assert.True(projW <= 1300.0, $"Expected wrapped ProjectGraph width <= 1300, got {projW}.");
        Assert.True(projH >= 200.0, $"Expected multi-row ProjectGraph height >= 200, got {projH}.");

        var layerSvg = MermaidSvgRenderer.Render(DiagramBuilder.LayerGraph(result)!);
        var (layerW, layerH) = ExtractViewBox(layerSvg);
        Assert.True(layerW <= 1300.0, $"Expected wrapped LayerDiagram width <= 1300, got {layerW}.");
        Assert.True(layerH >= 200.0, $"Expected multi-row LayerDiagram height >= 200, got {layerH}.");
    }

    [Fact]
    public void DispatchCallSites_SurfaceInExplorerContractPanelAndMarkdownRequestsTableWithoutAddingFlowEdges()
    {
        var result = new AnalysisResult { RootPath = "/tmp" };
        var cmd = new CqrsRequest
        {
            Name = "CancelOrderCommand",
            Namespace = "eShop.Ordering.API.Application.Commands",
            ProjectName = "Ordering.API",
            ResponseType = "bool",
        };
        cmd.Handlers.Add("eShop.Ordering.API.Application.Commands.CancelOrderCommandHandler");
        result.Requests.Add(cmd);

        result.FlowEdges.Add(new FlowEdge
        {
            Kind = FlowEdgeKind.RequestToHandler,
            From = cmd.FullName,
            To = "eShop.Ordering.API.Application.Commands.CancelOrderCommandHandler",
            ProjectName = "Ordering.API",
        });

        result.DispatchCallSites.Add(new DispatchCallSite
        {
            TargetFullName = cmd.FullName,
            TargetKind = DispatchTargetKind.Request,
            SenderFullName = "eShop.Ordering.API.Application.IntegrationEvents.EventHandling.OrderPaymentFailedIntegrationEventHandler",
            ProjectName = "Ordering.API",
        });

        var md = ReportGenerator.Generate(result);
        Assert.Contains("| Dispatched by |", md);
        Assert.Contains("OrderPaymentFailedIntegrationEventHandler", md);

        var html = HtmlReportGenerator.Generate(result, DiagramRenderMode.Svg);
        Assert.Contains("<dt>Dispatched by</dt><dd><code>OrderPaymentFailedIntegrationEventHandler</code></dd>", html);

        // Hard limit: flow edges remain strictly equal to result.FlowEdges.Count (1).
        var flowSections = DiagramBuilder.FlowDiagramSections(result);
        Assert.Equal(1, flowSections.Sum(s => s.Edges.Count));
        Assert.Equal(1, flowSections.Sum(s => CountRenderedEdges(s.Source)));
    }

    [Fact]
    public void FeatureName_StripsSolutionPrefixAndLayerTokensToExtractFeature()
    {
        var northwindCustomer = new CqrsRequest
        {
            Name = "CreateCustomerCommand",
            Namespace = "Northwind.Application.Customers.Commands.CreateCustomer",
            ProjectName = "Application",
        };
        Assert.Equal("Customers", CqrsRequestOrdering.FeatureName(northwindCustomer));
        Assert.Equal("Application — Customers", CqrsRequestOrdering.FeatureHeading(northwindCustomer));

        var eshopCommand = new CqrsRequest
        {
            Name = "CancelOrderCommand",
            Namespace = "eShop.Ordering.API.Application.Commands",
            ProjectName = "Ordering.API",
        };
        Assert.Equal("Ordering.API", CqrsRequestOrdering.FeatureName(eshopCommand));
        Assert.Equal("Ordering.API", CqrsRequestOrdering.FeatureHeading(eshopCommand));
    }

    [Fact]
    public void NotificationSection_RendersHandledByEdgeLabelLegendAndHtmlFigureCaption()
    {
        var result = new AnalysisResult { RootPath = "/tmp" };
        result.Notifications.Add(new CqrsNotification
        {
            Name = "OrderPlacedNotification",
            Namespace = "Shop.Application.Events",
            ProjectName = "Shop.Application",
            Handlers = { "Shop.Application.Events.SendOrderEmailHandler" },
        });
        result.NotificationHandlers.Add(new NotificationHandlerInfo
        {
            Name = "SendOrderEmailHandler",
            Namespace = "Shop.Application.Events",
            NotificationName = "OrderPlacedNotification",
            ProjectName = "Shop.Application",
        });

        var md = ReportGenerator.Generate(result);
        Assert.Contains("-->|handled by|", md);
        Assert.DoesNotContain("-->|publishes to|", md);
        Assert.Contains("**Legend:** fuchsia = notification", md);
        Assert.Contains("indigo parallelogram = publisher", md);

        var html = HtmlReportGenerator.Generate(result, DiagramRenderMode.Svg);
        Assert.Contains("aria-label=\"Notification map legend\"", html);
        Assert.Contains("<figcaption>1 notification, 1 handler.</figcaption>", html);
    }

    [Fact]
    public void ChecksSection_GroupsByCodePreservesFindingTextsAndTotals_AndOverviewShowsCounts()
    {
        var result = new AnalysisResult { RootPath = "/tmp" };
        result.Checks.Add(new CheckFinding(
            CheckSeverity.Warning, ArchitectureChecker.CommandWithoutValidatorCode,
            "Command `A` has no validator.", Target: "A"));
        result.Checks.Add(new CheckFinding(
            CheckSeverity.Warning, ArchitectureChecker.CommandWithoutValidatorCode,
            "Command `B` has no validator.", Target: "B"));
        result.Checks.Add(new CheckFinding(
            CheckSeverity.Warning, ArchitectureChecker.CommandWithoutValidatorCode,
            "Command `C` has no validator.", Target: "C"));
        result.Checks.Add(new CheckFinding(
            CheckSeverity.Error, ArchitectureChecker.MissingHandlerCode,
            "Request `D` has no handler.", Target: "D"));
        result.Checks.Add(new CheckFinding(
            CheckSeverity.Error, ArchitectureChecker.MissingHandlerCode,
            "Request `E` has no handler.", Target: "E"));

        var md = ReportGenerator.Generate(result);
        Assert.Contains("| Architecture checks | 2 error(s), 3 warning(s) |", md);
        Assert.Contains("### ⚠️ CommandWithoutValidator (3)", md);
        Assert.Contains("### ❌ MissingHandler (2)", md);
        Assert.Contains("Command `A` has no validator.", md);
        Assert.Contains("Command `B` has no validator.", md);
        Assert.Contains("Request `E` has no handler.", md);

        var html = HtmlReportGenerator.Generate(result, DiagramRenderMode.Svg);
        Assert.Contains("check-group", html);
        Assert.Contains("<summary>⚠️ <strong>CommandWithoutValidator</strong> (3)</summary>", html);
        Assert.Contains("<summary>❌ <strong>MissingHandler</strong> (2)</summary>", html);
        Assert.Contains("Command `A` has no validator.", html);
        Assert.Contains("Architecture checks", html);
    }

    [Fact]
    public void EndpointLabels_AlwaysCarryTheControllerName()
    {
        var result = new AnalysisResult { RootPath = "/tmp" };
        var req = new CqrsRequest
        {
            Name = "DownloadCommand",
            Namespace = "App.Products",
            ProjectName = "App",
        };
        req.Handlers.Add("App.Products.DownloadCommandHandler");
        result.Requests.Add(req);
        result.FlowEdges.Add(new FlowEdge
        {
            Kind = FlowEdgeKind.EndpointToRequest,
            From = "ProductsController.Download",
            To = req.FullName,
            ProjectName = "App",
        });

        var source = Assert.Single(DiagramBuilder.FlowDiagramSections(result)).Source;
        // Even though "Download" is unique in the section, the controller is kept.
        Assert.Contains("ep_ProductsController_Download([\"ProductsController.Download\"]):::clsFlowEndpoint", source);
    }

    [Fact]
    public void FeatureFirstOrdering_KeepsAFeatureRequestsContiguous()
    {
        var result = new AnalysisResult { RootPath = "/tmp" };
        void Add(string name, string feature)
        {
            var req = new CqrsRequest
            {
                Name = name,
                Namespace = $"App.Features.{feature}",
                ProjectName = "App",
            };
            req.Handlers.Add($"App.Features.{feature}.{name}Handler");
            result.Requests.Add(req);
        }

        // Deliberately added in interleaved order.
        Add("CreateAlphaCommand", "Alpha");
        Add("GetBetaQuery", "Beta");
        Add("GetAlphaQuery", "Alpha");
        Add("CreateBetaCommand", "Beta");

        var ordered = CqrsRequestOrdering.OrderRequests(result.Requests);
        Assert.Equal(new[] { "Alpha", "Alpha", "Beta", "Beta" },
            ordered.Select(r => CqrsRequestOrdering.FeatureName(r)));

        var sections = DiagramBuilder.CqrsMapSections(result);
        var orderedAgain = sections.SelectMany(s => s.Requests).Select(CqrsRequestOrdering.FeatureName).ToList();
        // A feature is never split across a part boundary in the small case.
        Assert.Equal(new[] { "Alpha", "Alpha", "Beta", "Beta" }, orderedAgain);
    }

    [Fact]
    public void NotificationsOmission_CaptionCountMatchesIsolatedOrphansAndDiagramDropsThem()
    {
        var result = new AnalysisResult { RootPath = "/tmp" };
        for (var i = 0; i < 40; i++)
        {
            result.Notifications.Add(new CqrsNotification
            {
                Name = $"Isolated{i:00}",
                Namespace = "App.Events",
                ProjectName = "App",
            });
        }
        for (var i = 0; i < 5; i++)
        {
            var handled = new CqrsNotification
            {
                Name = $"Handled{i:00}",
                Namespace = "App.Events",
                ProjectName = "App",
            };
            handled.Handlers.Add($"App.Events.Handled{i:00}Handler");
            result.Notifications.Add(handled);
        }

        var omission = NotificationDiagramBuilder.ComputeOmission(result);
        Assert.Equal(45, omission.Total);
        Assert.Equal(40, omission.Omitted);

        var map = DiagramBuilder.NotificationMap(result);
        Assert.NotNull(map);
        Assert.DoesNotContain("ntf_Isolated00", map);
        Assert.Contains("ntf_Handled00", map);

        var md = ReportGenerator.Generate(result);
        Assert.Contains("40 notifications with no publisher or handler are omitted from this diagram; all 45 are listed in the table below.", md);

        var html = HtmlReportGenerator.Generate(result, DiagramRenderMode.Svg);
        Assert.Contains("40 notifications with no publisher or handler are omitted from this diagram; all 45 are listed in the table below.", html);
    }

    [Fact]
    public void BehaviorChain_RendersInMarkdownAndHtmlPipelineBehaviors()
    {
        var result = new AnalysisResult { RootPath = "/tmp" };
        result.Behaviors.Add(new BehaviorInfo { Name = "LoggingBehavior", ProjectName = "App" });
        result.Behaviors.Add(new BehaviorInfo { Name = "ValidationBehavior", ProjectName = "App" });

        var chain = DiagramBuilder.BehaviorChain(result);
        Assert.NotNull(chain);
        Assert.Contains("bhv_LoggingBehavior", chain);
        Assert.DoesNotContain("[[", chain); // subroutine shape is not SVG-parseable

        var md = ReportGenerator.Generate(result);
        Assert.Contains("## Pipeline Behaviors", md);
        Assert.Contains("bhv_LoggingBehavior", md);
        Assert.Contains("scan order, not resolved execution order", md);

        var html = HtmlReportGenerator.Generate(result, DiagramRenderMode.Svg);
        Assert.Contains("Pipeline Behaviors", html);
        Assert.Contains("<title>Pipeline behavior chain</title>", html);
        Assert.Contains("scan order, not resolved execution order", html);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
    }

    private static double ExtractNodeX(string svg, string nodeId)
    {
        var match = Regex.Match(
            svg,
            $"<g class=\"node\" data-id=\"{Regex.Escape(nodeId)}\"><rect x=\"(?<x>[0-9.]+)\"");
        Assert.True(match.Success, $"Could not find node {nodeId} in SVG.");
        return double.Parse(match.Groups["x"].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static (double Width, double Height) ExtractViewBox(string svg)
    {
        var match = Regex.Match(svg, "viewBox=\"0 0 (?<w>[0-9.]+) (?<h>[0-9.]+)\"");
        Assert.True(match.Success, "Could not find viewBox in SVG.");
        return (
            double.Parse(match.Groups["w"].Value, System.Globalization.CultureInfo.InvariantCulture),
            double.Parse(match.Groups["h"].Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    private static int CountRenderedEdges(string mermaidSource)
        => mermaidSource
            .Split('\n')
            .Count(line => line.Contains("-->", StringComparison.Ordinal));
}
