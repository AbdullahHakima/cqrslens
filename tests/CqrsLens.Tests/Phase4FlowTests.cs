using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// Phase 4 flow-graph coverage: call-site resolution for the supported dispatch
/// shapes, the solution-wide edges, and the two honest reachability warnings.
/// </summary>
public sealed class Phase4FlowTests : IDisposable
{
    private readonly string _root;

    public Phase4FlowTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cqrslens-p4-" + Guid.NewGuid().ToString("N"));
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
    public void KnownFlow_ResolvesEdgesAndFlagsOnlyDeadShapes(AnalysisMode mode)
    {
        WriteProject("App");
        WriteContracts();
        WriteFile("App/Features.cs", """
            using System.Threading;
            using System.Threading.Tasks;
            using MediatR;

            namespace App.Features;

            public sealed record CommandA(int Id) : IRequest<int>;
            public sealed record CommandB(int Id) : IRequest<int>;
            public sealed record CommandC(int Id) : IRequest<int>;
            public sealed record EventE(int Id) : INotification;
            public sealed record EventF(int Id) : INotification;

            public sealed class CommandAHandler : IRequestHandler<CommandA, int>
            {
                private readonly IMediator _mediator;
                public CommandAHandler(IMediator mediator) => _mediator = mediator;

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
            public sealed class EventFHandler : INotificationHandler<EventF> { }
            """);
        WriteFile("App/OrdersController.cs", """
            using System.Threading.Tasks;
            using MediatR;
            using Microsoft.AspNetCore.Mvc;

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

        Assert.Equal(3, result.Requests.Count);
        Assert.Equal(2, result.Notifications.Count);

        Assert.Contains(result.FlowEdges, e =>
            e.Kind == FlowEdgeKind.EndpointToRequest &&
            e.To == "App.Features.CommandA");
        Assert.Contains(result.FlowEdges, e =>
            e.Kind == FlowEdgeKind.HandlerToNotification &&
            e.From == "App.Features.CommandAHandler" &&
            e.To == "App.Features.EventE");
        Assert.Contains(result.FlowEdges, e =>
            e.Kind == FlowEdgeKind.HandlerToRequest &&
            e.From == "App.Features.CommandAHandler" &&
            e.To == "App.Features.CommandB");

        var unreachable = Assert.Single(
            result.Checks, c => c.Code == ArchitectureChecker.UnreachableRequestCode);
        Assert.Equal("App.Features.CommandC", unreachable.Target);
        Assert.Equal(CheckSeverity.Warning, unreachable.Severity);

        var unpublished = Assert.Single(
            result.Checks, c => c.Code == ArchitectureChecker.UnpublishedNotificationCode);
        Assert.Equal("App.Features.EventF", unpublished.Target);
        Assert.Equal(CheckSeverity.Warning, unpublished.Severity);

        Assert.DoesNotContain(result.Checks, c =>
            c.Code == ArchitectureChecker.UnreachableRequestCode &&
            c.Target is "App.Features.CommandA" or "App.Features.CommandB");
        Assert.DoesNotContain(result.Checks, c =>
            c.Code == ArchitectureChecker.UnpublishedNotificationCode &&
            c.Target == "App.Features.EventE");

        // The JSON contract exposes the edges and call sites.
        var json = JsonReportGenerator.Generate(result);
        Assert.Contains("\"flowEdges\":", json);
        Assert.Contains("\"endpointToRequest\"", json);
        Assert.Contains("\"handlerToRequest\"", json);
        Assert.Contains("\"dispatchCallSites\":", json);
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void RuntimeTypedSend_FabricatesNoEdgeAndIsAccountedFor(AnalysisMode mode)
    {
        WriteProject("App");
        WriteContracts();
        WriteFile("App/Runtime.cs", """
            using System.Threading;
            using System.Threading.Tasks;
            using MediatR;

            namespace App.Runtime;

            public sealed record RuntimeCommand(int Id) : IRequest<int>;
            public sealed class RuntimeCommandHandler : IRequestHandler<RuntimeCommand, int>
            {
                public Task<int> Handle(RuntimeCommand request, CancellationToken cancellationToken)
                    => Task.FromResult(request.Id);
            }

            public sealed class RuntimeDispatcher
            {
                // The request is constructed but its static type is the base
                // interface, so no concrete target is resolvable and no edge may be
                // fabricated; the same is true of an interface cast.
                public Task Go(ISender sender)
                {
                    IRequest runtimeTyped = new RuntimeCommand(1);
                    return sender.Send(runtimeTyped);
                }

                public Task GoCast(ISender sender, IRequest runtimeTyped)
                    => sender.Send((IRequest<object>)runtimeTyped);
            }
            """);

        var result = Analyzer.Analyze(_root, mode);

        Assert.DoesNotContain(result.DispatchCallSites, s =>
            s.TargetFullName == "App.Runtime.RuntimeCommand");
        Assert.DoesNotContain(result.FlowEdges, e =>
            e.Kind == FlowEdgeKind.HandlerToRequest && e.To == "App.Runtime.RuntimeCommand");
        Assert.True(result.UnresolvedDispatches.Count >= 2);
        Assert.Contains(result.UnresolvedDispatches, u => u.ReceiverName == "sender");

        var warning = Assert.Single(
            result.Checks, c => c.Code == ArchitectureChecker.UnreachableRequestCode);
        Assert.Equal("App.Runtime.RuntimeCommand", warning.Target);
        Assert.Contains("unresolved dispatch call", warning.Message);
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void GenericAndInvokeAsyncDispatchShapes_AreResolved(AnalysisMode mode)
    {
        WriteProject("App");
        WriteContracts();
        WriteFile("App/Bus.cs", """
            using System.Threading;
            using System.Threading.Tasks;
            using MediatR;

            namespace App.Bus;

            public interface IMessageBus
            {
                Task InvokeAsync<T>(T message);
                Task PublishAsync<T>(T message);
            }

            public sealed record BusCommand(int Id) : IRequest<int>;
            public sealed class BusCommandHandler : IRequestHandler<BusCommand, int>
            {
                public Task<int> Handle(BusCommand request, CancellationToken cancellationToken)
                    => Task.FromResult(request.Id);
            }

            public sealed record BusEvent(int Id) : INotification;
            public sealed class BusEventHandler : INotificationHandler<BusEvent> { }

            public sealed class BusDispatcher
            {
                public async Task Go(IMessageBus bus)
                {
                    await bus.InvokeAsync<BusCommand>(new BusCommand(1));
                    await bus.PublishAsync<BusEvent>(new BusEvent(1));
                }
            }
            """);

        var result = Analyzer.Analyze(_root, mode);

        Assert.Contains(result.DispatchCallSites, s =>
            s.TargetFullName == "App.Bus.BusCommand" && s.TargetKind == DispatchTargetKind.Request);
        Assert.Contains(result.DispatchCallSites, s =>
            s.TargetFullName == "App.Bus.BusEvent" && s.TargetKind == DispatchTargetKind.Notification);

        Assert.DoesNotContain(result.Checks, c =>
            c.Code == ArchitectureChecker.UnreachableRequestCode && c.Target == "App.Bus.BusCommand");
        Assert.DoesNotContain(result.Checks, c =>
            c.Code == ArchitectureChecker.UnpublishedNotificationCode && c.Target == "App.Bus.BusEvent");
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void TestProjectSend_IsNotProductionReachability(AnalysisMode mode)
    {
        WriteProject("App");
        WriteProject("App.Tests", @"..\App\App.csproj");
        WriteContracts();
        WriteFile("App/TestOnly.cs", """
            using System.Threading;
            using System.Threading.Tasks;
            using MediatR;

            namespace App.TestOnly;

            public sealed record TestOnlyCommand(int Id) : IRequest<int>;
            public sealed class TestOnlyCommandHandler : IRequestHandler<TestOnlyCommand, int>
            {
                public Task<int> Handle(TestOnlyCommand request, CancellationToken cancellationToken)
                    => Task.FromResult(request.Id);
            }
            """);
        WriteFile("App.Tests/TestOnlyTests.cs", """
            using MediatR;
            using App.TestOnly;

            namespace App.Tests;

            public sealed class TestOnlyDispatcher
            {
                public Task Go(ISender sender) => sender.Send(new TestOnlyCommand(1));
            }
            """);

        var result = Analyzer.Analyze(_root, mode);

        var site = Assert.Single(result.DispatchCallSites, s =>
            s.TargetFullName == "App.TestOnly.TestOnlyCommand");
        Assert.True(site.IsTestProject);

        var warning = Assert.Single(
            result.Checks, c => c.Code == ArchitectureChecker.UnreachableRequestCode);
        Assert.Equal("App.TestOnly.TestOnlyCommand", warning.Target);
        Assert.Contains("test-project", warning.Message);
    }

    [Fact]
    public void HandlerUsingDbContext_ProducesATypeLevelEdge()
    {
        WriteProject("App");
        WriteContracts();
        WriteFile("App/Db.cs", """
            using System.Threading;
            using System.Threading.Tasks;
            using MediatR;
            using Microsoft.EntityFrameworkCore;

            namespace App.Persistence;

            public sealed class AppDbContext : DbContext { }
            public sealed record SaveCommand(int Id) : IRequest<int>;

            public sealed class SaveCommandHandler : IRequestHandler<SaveCommand, int>
            {
                private readonly AppDbContext _db;
                public SaveCommandHandler(AppDbContext db) => _db = db;

                public Task<int> Handle(SaveCommand request, CancellationToken cancellationToken)
                    => Task.FromResult(request.Id);
            }
            """);

        var result = Analyzer.Analyze(_root, AnalysisMode.Text);

        Assert.Contains(result.FlowEdges, e =>
            e.Kind == FlowEdgeKind.HandlerToDbContext &&
            e.From == "App.Persistence.SaveCommandHandler" &&
            e.To == "App.Persistence.AppDbContext");
    }
}
