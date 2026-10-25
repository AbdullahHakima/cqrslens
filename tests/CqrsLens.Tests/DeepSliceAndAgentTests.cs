using Xunit;

namespace CqrsLens.Tests;

public class DeepSliceAndAgentTests
{
    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "cqrslens-deepslice-" + Guid.NewGuid().ToString("N"));

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
    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void DeepSliceAnalyzer_TracesControllerToRequestToHandlerToDbSetAndNotification(AnalysisMode mode)
    {
        using var dir = new TempDir();

        dir.Write("src/Domain/Domain.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        dir.Write("src/Domain/Order.cs", """
            namespace App.Domain;
            public class Order
            {
                public int Id { get; set; }
                public string CustomerEmail { get; set; } = "";
                public decimal Total { get; set; }
            }
            """);

        dir.Write("src/Application/Application.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="..\Domain\Domain.csproj" />
              </ItemGroup>
            </Project>
            """);
        dir.Write("src/Application/Contracts.cs", """
            namespace MediatR
            {
                public interface IRequest<out TResponse> { }
                public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse> { }
                public interface INotification { }
                public interface INotificationHandler<in TNotification> where TNotification : INotification { }
                public interface IPipelineBehavior<in TRequest, TResponse> { }
            }
            namespace FluentValidation
            {
                public abstract class AbstractValidator<T> { }
            }
            namespace Microsoft.EntityFrameworkCore
            {
                public class DbContext { }
                public class DbSet<TEntity> where TEntity : class { }
            }
            """);
        dir.Write("src/Application/CreateOrder.cs", """
            using MediatR;
            using FluentValidation;
            using App.Domain;

            namespace App.Application.Orders;

            public record CreateOrderCommand(string CustomerEmail, decimal Total) : IRequest<int>;

            public record OrderCreatedNotification(int OrderId, string CustomerEmail) : INotification;

            public class CreateOrderValidator : AbstractValidator<CreateOrderCommand> { }

            public class SendOrderReceiptHandler : INotificationHandler<OrderCreatedNotification> { }
            """);

        dir.Write("src/Infrastructure/Infrastructure.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="..\Application\Application.csproj" />
                <ProjectReference Include="..\Domain\Domain.csproj" />
              </ItemGroup>
            </Project>
            """);
        dir.Write("src/Infrastructure/AppDbContext.cs", """
            using Microsoft.EntityFrameworkCore;
            using MediatR;
            using App.Domain;
            using App.Application.Orders;

            namespace App.Infrastructure;

            public class AppDbContext : DbContext
            {
                public DbSet<Order> Orders { get; set; } = null!;
            }

            public class CreateOrderHandler(AppDbContext db, IEmailSender emailSender)
                : IRequestHandler<CreateOrderCommand, int>
            {
                public int Handle(CreateOrderCommand request)
                {
                    var entities = db.Orders;
                    var evt = new OrderCreatedNotification(1, request.CustomerEmail);
                    return 1;
                }
            }

            public interface IEmailSender { }
            """);

        dir.Write("src/Api/Api.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="..\Application\Application.csproj" />
              </ItemGroup>
            </Project>
            """);
        dir.Write("src/Api/OrdersController.cs", """
            using App.Application.Orders;

            namespace App.Api.Controllers;

            [ApiController]
            [Route("api/[controller]")]
            public class OrdersController : ControllerBase
            {
                [HttpPost]
                public int Create(CreateOrderCommand command)
                {
                    return 1;
                }

                [HttpPost("{id}/clone")]
                public int CloneOrder(int id)
                {
                    var cmd = new CreateOrderCommand("clone@example.com", 10m);
                    return 1;
                }
            }

            public class ControllerBase { }
            public class ApiControllerAttribute : System.Attribute { }
            public class RouteAttribute : System.Attribute { public RouteAttribute(string t) { } }
            public class HttpPostAttribute : System.Attribute
            {
                public HttpPostAttribute() { }
                public HttpPostAttribute(string t) { }
            }
            """);

        var result = Analyzer.Analyze(dir.Path, mode);

        var cmd = Assert.Single(result.Requests, r => r.Name == "CreateOrderCommand");
        Assert.Equal(2, cmd.Properties.Count);
        Assert.Contains(cmd.Properties, p => p.Name == "CustomerEmail" && p.Type == "string");
        Assert.Contains(cmd.Properties, p => p.Name == "Total" && p.Type == "decimal");

        Assert.Contains("POST /api/orders", cmd.Entrypoints);
        Assert.Contains("POST /api/orders/{id}/clone", cmd.Entrypoints);
        Assert.Equal(2, result.Endpoints.Count);

        var handler = Assert.Single(result.Handlers, h => h.Name == "CreateOrderHandler");
        Assert.Contains("AppDbContext", handler.Dependencies);
        Assert.Contains("IEmailSender", handler.Dependencies);

        Assert.Contains("Orders (Order)", handler.TouchedEntities);
        Assert.Contains("Orders (Order)", cmd.TouchedEntities);

        Assert.Contains("App.Application.Orders.OrderCreatedNotification", handler.PublishedNotifications);
        Assert.Contains("App.Application.Orders.OrderCreatedNotification", cmd.PublishedNotifications);

        var agentReport = AgentReportGenerator.Generate(result);
        Assert.Contains("## Architectural DNA & Conventions", agentReport);
        Assert.Contains("### [Command] `CreateOrderCommand` → `int`", agentReport);
        Assert.Contains("`CustomerEmail: string`, `Total: decimal`", agentReport);
        Assert.Contains("`POST /api/orders` (`OrdersController.Create`", agentReport);
        Assert.Contains("`AppDbContext`, `IEmailSender`", agentReport);
        Assert.Contains("`Orders (Order)`", agentReport);
        Assert.Contains("`OrderCreatedNotification` → `SendOrderReceiptHandler`", agentReport);
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void DeepSliceAnalyzer_DiscoversMinimalApisAndFastEndpoints(AnalysisMode mode)
    {
        using var dir = new TempDir();

        dir.Write("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        dir.Write("Program.cs", """
            namespace MediatR
            {
                public interface IRequest<out TResponse> { }
                public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse> { }
            }

            namespace FastEndpoints
            {
                public abstract class Endpoint<TRequest, TResponse>
                {
                    protected void Put(string route) { }
                }
            }

            namespace App
            {
                using MediatR;
                using FastEndpoints;

                public record GetInvoiceQuery(int Id) : IRequest<string>;
                public record PayInvoiceCommand(int Id, decimal Amount) : IRequest<bool>;
                public record ArchiveInvoiceCommand(int Id) : IRequest<bool>;

                public class GetInvoiceHandler : IRequestHandler<GetInvoiceQuery, string> { }
                public class PayInvoiceHandler : IRequestHandler<PayInvoiceCommand, bool> { }
                public class ArchiveInvoiceHandler : IRequestHandler<ArchiveInvoiceCommand, bool> { }

                public class ArchiveInvoiceEndpoint : Endpoint<ArchiveInvoiceCommand, bool>
                {
                    public void Configure()
                    {
                        Put("/api/invoices/archive");
                    }
                }

                public static class EndpointMappings
                {
                    public static void Map(WebApp app)
                    {
                        var invoices = app.MapGroup("/api/invoices");
                        invoices.MapGet("/{id}", (int id) => new GetInvoiceQuery(id));
                        invoices.MapPost("/pay", (PayInvoiceCommand cmd) => true);
                    }
                }

                public class WebApp
                {
                    public RouteGroup MapGroup(string prefix) => new();
                }

                public class RouteGroup
                {
                    public void MapGet(string route, System.Func<int, object> handler) { }
                    public void MapPost(string route, System.Func<PayInvoiceCommand, bool> handler) { }
                }
            }
            """);

        var result = Analyzer.Analyze(dir.Path, mode);

        var getQuery = Assert.Single(result.Requests, r => r.Name == "GetInvoiceQuery");
        Assert.Contains("GET /api/invoices/{id}", getQuery.Entrypoints);

        var payCmd = Assert.Single(result.Requests, r => r.Name == "PayInvoiceCommand");
        Assert.Contains("POST /api/invoices/pay", payCmd.Entrypoints);

        var archiveCmd = Assert.Single(result.Requests, r => r.Name == "ArchiveInvoiceCommand");
        Assert.Contains("PUT /api/invoices/archive", archiveCmd.Entrypoints);
    }

    [Fact]
    public void Cli_SliceOption_StreamsFilteredVerticalSliceToStdoutWithoutWritingDefaultFile()
    {
        using var dir = new TempDir();
        dir.Write("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        dir.Write("Slices.cs", """
            namespace MediatR
            {
                public interface IRequest<out TResponse> { }
                public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse> { }
            }

            namespace App
            {
                using MediatR;

                public record CreateOrderCommand(string Sku) : IRequest<int>;
                public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, int> { }

                public record GetUserProfileQuery(int UserId) : IRequest<string>;
                public class GetUserProfileHandler : IRequestHandler<GetUserProfileQuery, string> { }
            }
            """);

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exitCode = CliRunner.Run(new[] { dir.Path, "--slice", "Order" }, stdout, stderr);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal("", stderr.ToString());

        var output = stdout.ToString();
        Assert.Contains("# CqrsLens Vertical Slice Trace — `Order`", output);
        Assert.Contains("CreateOrderCommand", output);
        Assert.Contains("CreateOrderHandler", output);
        Assert.DoesNotContain("GetUserProfileQuery", output);
    }

    [Fact]
    public void Cli_FormatAgent_WritesAgentArchitectureFile()
    {
        using var dir = new TempDir();
        dir.Write("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        dir.Write("Slices.cs", """
            namespace MediatR
            {
                public interface IRequest<out TResponse> { }
                public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse> { }
            }

            namespace App
            {
                using MediatR;

                public record PingQuery(string Echo) : IRequest<string>;
                public class PingHandler : IRequestHandler<PingQuery, string> { }
            }
            """);

        var agentFile = Path.Combine(dir.Path, "ARCHITECTURE.agent.md");
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exitCode = CliRunner.Run(new[] { dir.Path, "--format", "agent", "-o", agentFile }, stdout, stderr);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.True(File.Exists(agentFile));
        var content = File.ReadAllText(agentFile);
        Assert.Contains("# CqrsLens Agent Architecture Map", content);
        Assert.Contains("PingQuery", content);
        Assert.Contains("`Echo: string`", content);
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void DeepSliceAnalyzer_HandlesEndpointGroupMethodsConstRoutesAndGetterOnlyProperties(AnalysisMode mode)
    {
        using var dir = new TempDir();
        dir.Write("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        dir.Write("RealWorldPatterns.cs", """
            namespace MediatR
            {
                public interface IRequest<out TResponse> { }
                public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse> { }
            }
            namespace FastEndpoints
            {
                public abstract class Endpoint<TRequest>
                {
                    protected void Post(string route) { }
                }
            }
            namespace App
            {
                using MediatR;
                using FastEndpoints;

                public class AcceptProposalCommand : IRequest<int>
                {
                    public AcceptProposalCommand(System.Guid proposalId)
                    {
                        ProposalId = proposalId;
                    }

                    internal System.Guid ProposalId { get; }
                }

                public class AcceptProposalHandler : IRequestHandler<AcceptProposalCommand, int> { }

                public class CreateContributorCommand : IRequest<int>
                {
                    public string Name { get; } = "";
                }

                public class CreateContributorHandler : IRequestHandler<CreateContributorCommand, int> { }

                public class CreateContributorRequest
                {
                    public const string Route = "/Contributors";
                }

                public class CreateContributorEndpoint : Endpoint<CreateContributorRequest>
                {
                    public void Configure()
                    {
                        Post(CreateContributorRequest.Route);
                    }

                    public void Handle()
                    {
                        var cmd = new CreateContributorCommand();
                    }
                }

                public class Proposals
                {
                    public static void Map(GroupBuilder groupBuilder)
                    {
                        groupBuilder.MapPatch(AcceptProposal, "{id}/accept");
                    }

                    public static int AcceptProposal(AcceptProposalCommand command) => 1;
                }

                public class GroupBuilder
                {
                    public void MapPatch(System.Func<AcceptProposalCommand, int> handler, string route) { }
                }
            }
            """);

        var result = Analyzer.Analyze(dir.Path, mode);

        var acceptCmd = Assert.Single(result.Requests, r => r.Name == "AcceptProposalCommand");
        Assert.Contains(acceptCmd.Properties, p => p.Name == "ProposalId" && p.Type == "System.Guid");
        Assert.Contains("PATCH /api/Proposals/{id}/accept", acceptCmd.Entrypoints);

        var createContributorCmd = Assert.Single(result.Requests, r => r.Name == "CreateContributorCommand");
        Assert.Contains(createContributorCmd.Properties, p => p.Name == "Name" && p.Type == "string");
        Assert.Contains("POST /Contributors", createContributorCmd.Entrypoints);
    }
}
