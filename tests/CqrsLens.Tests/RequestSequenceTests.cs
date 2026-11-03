using System.Text.RegularExpressions;
using System.Xml.Linq;
using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// Phase 6 Stage 3 brief S1: per-request sequence diagrams (rendering only).
/// The fixtures below assert the brief's acceptance cases directly on the model:
/// a request with two registered behaviors, a DbContext hop and a published
/// notification renders every participant in order with the standing label; a
/// request with no endpoint edge omits the actor; an unresolved request renders
/// no sequence. Model changes are not exercised here.
/// </summary>
public sealed class RequestSequenceTests
{
    private const string HandlerFull = "Shop.Application.Orders.CreateOrderCommandHandler";
    private const string ContextFull = "Shop.Infrastructure.ShopDbContext";
    private const string NotificationFull = "Shop.Application.Orders.OrderPlaced";

    [Fact]
    public void RequestWithTwoBehaviorsDbContextAndNotification_RendersParticipantsInOrderWithLabel()
    {
        var sequences = RequestSequenceBuilder.Build(Model(withEndpoint: true));

        var sequence = Assert.Single(sequences, s => s.RequestName == "CreateOrderCommand");
        var source = sequence.Source;

        Assert.StartsWith("sequenceDiagram", source);

        // Participants, in order: endpoint, mediator, behaviors (registered order),
        // handler, DbContext, published notification, its handler.
        var declared = ParticipantIds(source);
        Assert.Equal(new[] { "ep", "m", "b1", "b2", "h1", "c1", "n1", "nh1" }, declared);

        // Behavior registered order is preserved (Performance then Validation).
        Assert.Contains("participant b1 as RequestPerformanceBehaviour", source);
        Assert.Contains("participant b2 as RequestValidationBehavior", source);

        // The DbContext carries the DbSet it touches.
        Assert.Contains("h1->>c1: uses Orders", source);

        // The published notification routes to its handler.
        Assert.Contains("h1->>n1: publish", source);
        Assert.Contains("n1->>nh1: handle", source);

        // Entry, behavior chain and return arrows.
        Assert.Contains("ep->>m: CreateOrderCommand", source);
        Assert.Contains("m->>b1: handle", source);
        Assert.Contains("b1->>b2: next", source);
        Assert.Contains("b2->>h1: handle", source);
        Assert.Contains("h1-->>m: response", source);
        Assert.Contains("m-->>ep: response", source);

        // The standing label is present as a Mermaid note whenever behaviors show.
        Assert.Contains(
            "Note over b1,b2: Registered order — execution order is not guaranteed.",
            source);

        Assert.Contains("Behaviors shown in registered order (2)", sequence.Caption);
        Assert.Contains("execution order is not guaranteed", sequence.Caption);
    }

    [Fact]
    public void RequestWithoutEndpointEdge_OmitsTheActor()
    {
        var sequences = RequestSequenceBuilder.Build(Model(withEndpoint: false));

        var sequence = Assert.Single(sequences, s => s.RequestName == "CreateOrderCommand");
        var source = sequence.Source;

        Assert.DoesNotContain("participant ep", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ep->>", source, StringComparison.Ordinal);
        Assert.DoesNotContain("-->>ep", source, StringComparison.Ordinal);

        // The mediator is the first participant when there is no endpoint.
        Assert.Equal("m", ParticipantIds(source)[0]);
        Assert.Contains("m->>b1: handle", source);

        Assert.Contains("(no endpoint edge in the model)", sequence.Caption);
    }

    [Fact]
    public void UnresolvedRequest_RendersNoSequenceAndStaysListed()
    {
        var result = Model(withEndpoint: true);
        var sequences = RequestSequenceBuilder.Build(result);

        Assert.DoesNotContain(sequences, s => s.RequestName == "UnresolvedCommand");
        Assert.Equal(
            result.Requests.Count(r => r.Handlers.Count > 0),
            sequences.Count);

        // The unresolved request is still reported in the requests table, but never
        // gets a guessed handler or a sequence header.
        var md = ReportGenerator.Generate(result);
        Assert.DoesNotContain("### UnresolvedCommand", md);
        Assert.DoesNotContain("%% Request sequence: UnresolvedCommand", md);
        Assert.Contains("UnresolvedCommand", md);
    }

    [Fact]
    public void Sequences_AreOrderedByRequestName_AndDeterministic()
    {
        var result = Model(withEndpoint: true);
        var zed = new CqrsRequest
        {
            Name = "ZetaCommand",
            Namespace = "Shop.Application.Orders",
            ProjectName = "Shop.Application",
        };
        zed.Handlers.Add("Shop.Application.Orders.ZetaCommandHandler");
        result.Requests.Add(zed);

        var first = RequestSequenceBuilder.Build(result);
        var second = RequestSequenceBuilder.Build(result);

        Assert.Equal(new[] { "CreateOrderCommand", "ZetaCommand" }, first.Select(s => s.RequestName));
        Assert.Equal(first.Select(s => s.Source), second.Select(s => s.Source));
    }

    [Fact]
    public void SequenceContent_HasNoTimestampsDurationsOrMachinePaths()
    {
        foreach (var sequence in RequestSequenceBuilder.Build(Model(withEndpoint: true)))
        {
            Assert.DoesNotMatch(@"\d{4}-\d{2}-\d{2}", sequence.Source);
            Assert.DoesNotContain("Elapsed", sequence.Source, StringComparison.Ordinal);
            Assert.DoesNotMatch(@"\b\d+(\.\d+)?\s?ms\b", sequence.Source);
            Assert.DoesNotContain("/tmp", sequence.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("\\", sequence.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("Shop.Application/", sequence.Source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Sequences_RenderInMarkdownAndHtml_InBothRenderModes()
    {
        var result = Model(withEndpoint: true);

        var md = ReportGenerator.Generate(result);
        Assert.Contains("## Request sequences", md);
        Assert.Contains("### CreateOrderCommand", md);
        Assert.Contains("participant ep as POST /orders", md);
        Assert.Contains("Note over b1,b2: Registered order — execution order is not guaranteed.", md);

        var htmlMermaid = HtmlReportGenerator.Generate(result, DiagramRenderMode.Mermaid);
        Assert.Contains("<h2>Request sequences</h2>", htmlMermaid);
        Assert.Contains("<pre class=\"mermaid\">", htmlMermaid);
        Assert.Contains("m-&gt;&gt;b1: handle", htmlMermaid);

        var htmlSvg = HtmlReportGenerator.Generate(result, DiagramRenderMode.Svg);
        Assert.Contains("<h2>Request sequences</h2>", htmlSvg);
        Assert.Contains("<title>Request sequence: CreateOrderCommand</title>", htmlSvg);
        Assert.DoesNotContain("<script", htmlSvg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SequenceDiagram_RendersToWellFormedStandaloneSvg()
    {
        var sequence = Assert.Single(RequestSequenceBuilder.Build(Model(withEndpoint: true)));

        var rendered = MermaidSvgRenderer.Render(sequence.Source);
        Assert.Contains("<title>Request sequence: CreateOrderCommand</title>", rendered);
        Assert.DoesNotContain("sequenceDiagram", rendered, StringComparison.Ordinal);

        var standalone = SvgExporter.ToStandaloneSvg(rendered);
        var document = XDocument.Parse(standalone);
        Assert.Equal("svg", document.Root!.Name.LocalName);
        Assert.False(string.IsNullOrEmpty(document.Root.Attribute("viewBox")?.Value));
        Assert.DoesNotContain(document.Descendants(), e => e.Name.LocalName == "script");
    }

    private static string[] ParticipantIds(string source)
        => Regex.Matches(source, @"(?m)^\s*participant\s+(?<id>\S+)\s+as\s+")
            .Select(m => m.Groups["id"].Value)
            .ToArray();

    private static AnalysisResult Model(bool withEndpoint)
    {
        var result = new AnalysisResult { RootPath = "/tmp/shop" };

        var command = new CqrsRequest
        {
            Name = "CreateOrderCommand",
            Namespace = "Shop.Application.Orders",
            ProjectName = "Shop.Application",
        };
        command.Handlers.Add(HandlerFull);
        result.Requests.Add(command);

        // Unresolved request: no handler, so it must never be drawn.
        result.Requests.Add(new CqrsRequest
        {
            Name = "UnresolvedCommand",
            Namespace = "Shop.Application.Orders",
            ProjectName = "Shop.Application",
        });

        var handler = new HandlerInfo
        {
            Name = "CreateOrderCommandHandler",
            Namespace = "Shop.Application.Orders",
            RequestName = "CreateOrderCommand",
            ProjectName = "Shop.Application",
        };
        handler.TouchedEntities.Add("Orders (Order)");
        result.Handlers.Add(handler);

        var context = new DbContextInfo
        {
            Name = "ShopDbContext",
            Namespace = "Shop.Infrastructure",
            ProjectName = "Shop.Infrastructure",
        };
        context.DbSets.Add(new DbSetInfo("Order", "Orders"));
        result.DbContexts.Add(context);

        var notification = new CqrsNotification
        {
            Name = "OrderPlaced",
            Namespace = "Shop.Application.Orders",
            ProjectName = "Shop.Application",
        };
        notification.Handlers.Add("Shop.Application.Orders.OrderPlacedHandler");
        result.Notifications.Add(notification);

        result.Behaviors.Add(new BehaviorInfo
        {
            Name = "RequestPerformanceBehaviour",
            ProjectName = "Shop.Application",
            RegistrationSite = "Shop.Application/DependencyInjection.cs:10-11",
            RegistrationOrder = 1,
        });
        result.Behaviors.Add(new BehaviorInfo
        {
            Name = "RequestValidationBehavior",
            ProjectName = "Shop.Application",
            RegistrationSite = "Shop.Application/DependencyInjection.cs:10-11",
            RegistrationOrder = 2,
        });

        if (withEndpoint)
        {
            result.Endpoints.Add(new EndpointInfo
            {
                Name = "OrdersController.Create",
                HttpMethod = "POST",
                Route = "/orders",
                TargetRequestName = "CreateOrderCommand",
                TargetRequestFullName = command.FullName,
                ProjectName = "Shop.Api",
            });
        }

        result.FlowEdges.Add(new FlowEdge
        {
            Kind = FlowEdgeKind.RequestToHandler,
            From = command.FullName,
            To = HandlerFull,
            ProjectName = "Shop.Application",
        });
        result.FlowEdges.Add(new FlowEdge
        {
            Kind = FlowEdgeKind.HandlerToDbContext,
            From = HandlerFull,
            To = ContextFull,
            ProjectName = "Shop.Application",
        });
        result.FlowEdges.Add(new FlowEdge
        {
            Kind = FlowEdgeKind.HandlerToNotification,
            From = HandlerFull,
            To = NotificationFull,
            ProjectName = "Shop.Application",
        });

        return result;
    }
}

/// <summary>
/// End-to-end fixture: the scanner's own model (behaviors, DbContext DbSets,
/// notification, endpoint) drives the sequence, so the participant hop and the
/// "uses Orders" DbSet attribution are exercised against real scan labels.
/// </summary>
public sealed class RequestSequenceAnalyzerTests : IDisposable
{
    private readonly string _root;

    public RequestSequenceAnalyzerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cqrslens-s1-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            Path.Combine(_root, "Demo.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void ScannedFixture_SequenceCarriesBehaviorsDbContextAndNotification(AnalysisMode mode)
    {
        WriteContracts();
        WriteFile("DependencyInjection.cs", """
            namespace Demo;

            public static class DependencyInjection
            {
                public static void AddApp()
                {
                    Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(PerformanceBehavior<,>));
                    Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
                }
            }
            """);
        WriteFile("Behaviors.cs", """
            using MediatR;

            namespace Demo;

            public sealed class PerformanceBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> { }
            public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> { }
            """);
        WriteFile("Features.cs", """
            using System.Threading;
            using System.Threading.Tasks;
            using MediatR;
            using Microsoft.EntityFrameworkCore;

            namespace Demo.Features;

            public sealed class Order { public int Id { get; set; } }
            public sealed class ShopDbContext : DbContext { public DbSet<Order> Orders { get; set; } }

            public sealed record CreateOrderCommand(int Id) : IRequest<int>;
            public sealed record OrderPlaced(int Id) : INotification;

            public sealed class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, int>
            {
                private readonly IMediator _mediator;
                private readonly ShopDbContext _db;
                public CreateOrderCommandHandler(IMediator mediator, ShopDbContext db)
                {
                    _mediator = mediator;
                    _db = db;
                }

                public async Task<int> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
                {
                    _db.Orders.Add(new Order());
                    await _mediator.Publish(new OrderPlaced(request.Id));
                    return request.Id;
                }
            }

            public sealed class OrderPlacedHandler : INotificationHandler<OrderPlaced> { }
            """);
        WriteFile("OrdersController.cs", """
            using System.Threading.Tasks;
            using MediatR;
            using Microsoft.AspNetCore.Mvc;

            namespace Demo.Api;

            [ApiController]
            public sealed class OrdersController : ControllerBase
            {
                private readonly IMediator _mediator;
                public OrdersController(IMediator mediator) => _mediator = mediator;

                [HttpPost]
                public Task<int> Create() => _mediator.Send(new Demo.Features.CreateOrderCommand(1));
            }
            """);

        var result = Analyzer.Analyze(_root, mode);
        var sequence = Assert.Single(RequestSequenceBuilder.Build(result), s => s.RequestName == "CreateOrderCommand");
        var source = sequence.Source;

        Assert.Contains("participant b1 as PerformanceBehavior", source);
        Assert.Contains("participant b2 as ValidationBehavior", source);
        Assert.Contains("b1->>b2: next", source);
        Assert.Contains("h1->>c1: uses Orders", source);
        Assert.Contains("h1->>n1: publish", source);
        Assert.Contains("Note over b1,b2: Registered order — execution order is not guaranteed.", source);

        // The render-check seam also holds for a real scanned sequence.
        var document = XDocument.Parse(
            SvgExporter.ToStandaloneSvg(MermaidSvgRenderer.Render(source)));
        Assert.Equal("svg", document.Root!.Name.LocalName);
    }

    private void WriteContracts() => WriteFile("Contracts.cs", """
        using System;
        using System.Threading;
        using System.Threading.Tasks;

        namespace MediatR
        {
            public interface IRequest { }
            public interface IRequest<TResponse> { }
            public interface IRequestHandler<TRequest, TResponse> { }
            public interface INotification { }
            public interface INotificationHandler<TNotification> { }
            public interface IPipelineBehavior<TRequest, TResponse> { }
            public interface ISender { Task<TResponse> Send<TResponse>(IRequest<TResponse> request); }
            public interface IPublisher { Task Publish(object notification); }
            public interface IMediator : ISender, IPublisher { }
        }

        namespace Demo
        {
            public static class Services
            {
                public static void AddTransient(Type service, Type implementation) { }
            }
        }

        namespace Microsoft.EntityFrameworkCore
        {
            public class DbContext { }
            public class DbSet<T> { public void Add(T entity) { } }
        }

        namespace Microsoft.AspNetCore.Mvc
        {
            public class ControllerBase { }
            public class ApiControllerAttribute : Attribute { }
            public class HttpPostAttribute : Attribute { }
        }
        """);

    private void WriteFile(string relative, string code)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, code);
    }
}
