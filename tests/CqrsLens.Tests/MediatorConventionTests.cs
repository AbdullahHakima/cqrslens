using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// Built-in conventions for Mediator (martinothamar) and Wolverine. Both are
/// enabled automatically from package references or using directives; plain
/// MediatR scans are unaffected.
/// </summary>
public sealed class MediatorConventionTests : IDisposable
{
    private readonly string _root;

    public MediatorConventionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cqrslens-conventions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private void WriteProject(string packages)
        => File.WriteAllText(Path.Combine(_root, "App.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                {packages}
              </ItemGroup>
            </Project>
            """);

    private void WriteFile(string relative, string code)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, code);
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void Mediator_CommandsQueriesAndHandlers_AreDetectedWithoutMarkers(AnalysisMode mode)
    {
        WriteProject("""<PackageReference Include="Mediator.Abstractions" Version="3.0.0" />""");
        WriteFile("Orders.cs", """
            using Mediator;

            namespace App.Orders;

            public sealed record CreateOrderCommand(string Sku) : ICommand<int>;
            public sealed record GetOrderQuery(int Id) : IQuery<string>;

            public sealed class CreateOrderHandler : ICommandHandler<CreateOrderCommand, int>
            {
                public ValueTask<int> Handle(CreateOrderCommand command, CancellationToken ct) => new(1);
            }

            public sealed class GetOrderHandler : IQueryHandler<GetOrderQuery, string>
            {
                public ValueTask<string> Handle(GetOrderQuery query, CancellationToken ct) => new("x");
            }
            """);

        var result = Analyzer.Analyze(_root, mode);

        Assert.Contains(MediatorConventions.MediatorName, result.Conventions);
        Assert.Equal(2, result.Requests.Count);
        var create = Assert.Single(result.Requests, r => r.Name == "CreateOrderCommand");
        Assert.Equal("Command", create.Kind);
        Assert.Contains("App.Orders.CreateOrderHandler", create.Handlers);
        var get = Assert.Single(result.Requests, r => r.Name == "GetOrderQuery");
        Assert.Contains("App.Orders.GetOrderHandler", get.Handlers);
        Assert.DoesNotContain(result.Checks, c => c.Code == ArchitectureChecker.MissingHandlerCode);
    }

    [Fact]
    public void Wolverine_ConventionHandlers_LinkToSourceDeclaredMessages()
    {
        WriteProject("""<PackageReference Include="WolverineFx" Version="3.0.0" />""");
        WriteFile("Messages.cs", """
            namespace App.Messages;

            public sealed record PlaceOrder(string Sku);
            public sealed record ShipOrder(int Id);
            public sealed record OrphanCommand;
            """);
        WriteFile("Handlers.cs", """
            using App.Messages;
            using Wolverine;

            namespace App.Handlers;

            public static class PlaceOrderHandler
            {
                public static Task Handle(PlaceOrder command, IMessageBus bus) => Task.CompletedTask;
            }

            public class ShippingConsumer
            {
                public async Task ConsumeAsync(ShipOrder message, CancellationToken ct) => await Task.Yield();
                public void Handle(string notAMessage) { }
            }
            """);

        var result = Analyzer.Analyze(_root, AnalysisMode.Text);

        Assert.Contains(MediatorConventions.WolverineName, result.Conventions);
        var place = Assert.Single(result.Requests, r => r.Name == "PlaceOrder");
        Assert.Equal("Wolverine", place.ViaMarker);
        Assert.Contains("App.Handlers.PlaceOrderHandler", place.Handlers);
        var ship = Assert.Single(result.Requests, r => r.Name == "ShipOrder");
        Assert.Contains("App.Handlers.ShippingConsumer", ship.Handlers);
        Assert.DoesNotContain(result.Requests, r => r.Name == "OrphanCommand");
        Assert.Equal(2, result.Handlers.Count);
        Assert.NotNull(result.Handlers[0].Location);
    }

    [Fact]
    public void PlainMediatR_DoesNotEnableConventions()
    {
        WriteProject("""<PackageReference Include="MediatR" Version="12.0.0" />""");
        WriteFile("Ping.cs", """
            using MediatR;
            namespace App;
            public sealed record PingQuery : IRequest<int>;
            public sealed class PingHandler : IRequestHandler<PingQuery, int>
            {
                public Task<int> Handle(PingQuery request, CancellationToken ct) => Task.FromResult(1);
            }
            public sealed record ICommandLike;
            public sealed class StandaloneHandler
            {
                public void Handle(ICommandLike value) { }
            }
            """);

        var result = Analyzer.Analyze(_root, AnalysisMode.Text);

        Assert.Empty(result.Conventions);
        Assert.Single(result.Requests);
        Assert.Single(result.Handlers);
    }

    [Fact]
    public void Conventions_AppearInMarkdownAndJsonReports()
    {
        WriteProject("""<PackageReference Include="WolverineFx" Version="3.0.0" />""");
        WriteFile("A.cs", """
            namespace App;
            public sealed record Ping;
            public static class PingHandler { public static void Handle(Ping ping) { } }
            """);

        var result = Analyzer.Analyze(_root, AnalysisMode.Text, generatedAt: DateTimeOffset.UnixEpoch);

        Assert.Contains("Conventions: Wolverine", ReportGenerator.Generate(result));
        Assert.Contains("\"conventions\"", JsonReportGenerator.Generate(result));
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void Mediator_MultiProjectSameNamedRequestsAndBehaviors_LinkPerProject(AnalysisMode mode)
    {
        WriteFile("ConsoleA/ConsoleA.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Mediator.Abstractions" Version="3.0.0" />
              </ItemGroup>
            </Project>
            """);
        WriteFile("ConsoleA/Program.cs", """
            using Mediator;

            public sealed record Ping : IRequest<string>;
            public sealed class PingHandler : IRequestHandler<Ping, string>
            {
                public ValueTask<string> Handle(Ping request, CancellationToken ct) => new("a");
            }
            public sealed class LoggingBehavior<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
                where TMessage : notnull, IMessage
            {
            }
            """);

        WriteFile("StreamingB/StreamingB.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Mediator.Abstractions" Version="3.0.0" />
              </ItemGroup>
            </Project>
            """);
        WriteFile("StreamingB/Program.cs", """
            using Mediator;

            public sealed record StreamPing : IStreamRequest<string>;
            public sealed class PingHandler : IStreamRequestHandler<StreamPing, string>
            {
            }
            """);

        WriteFile("InternalMessages.Domain/InternalMessages.Domain.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Mediator.Abstractions" Version="3.0.0" />
              </ItemGroup>
            </Project>
            """);
        WriteFile("InternalMessages.Domain/Ping.cs", """
            using Mediator;
            namespace InternalMessages.Domain;
            public sealed record Ping : IRequest<string>;
            """);

        WriteFile("InternalMessages.Application/InternalMessages.Application.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="../InternalMessages.Domain/InternalMessages.Domain.csproj" />
              </ItemGroup>
            </Project>
            """);
        WriteFile("InternalMessages.Application/PingHandler.cs", """
            using InternalMessages.Domain;
            using Mediator;
            namespace InternalMessages.Application;
            public sealed class PingHandler : IRequestHandler<Ping, string>
            {
                public ValueTask<string> Handle(Ping request, CancellationToken ct) => new("domain");
            }
            """);

        var result = Analyzer.Analyze(_root, mode);

        Assert.Equal(3, result.Requests.Count);
        Assert.Equal(3, result.Handlers.Count);
        Assert.Single(result.Behaviors);
        Assert.Empty(result.Diagnostics);

        var domainPing = Assert.Single(result.Requests, r => r.FullName == "InternalMessages.Domain.Ping");
        Assert.Contains("InternalMessages.Application.PingHandler", domainPing.Handlers);

        var consolePing = Assert.Single(result.Requests, r => r.FullName == "Ping" && r.ProjectName == "ConsoleA");
        Assert.Contains("PingHandler", consolePing.Handlers);

        var streamPing = Assert.Single(result.Requests, r => r.FullName == "StreamPing" && r.ProjectName == "StreamingB");
        Assert.Contains("PingHandler", streamPing.Handlers);
    }
}
