using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace CqrsLens.Tests;

public class McpServerTests
{
    [Fact]
    public void CliParser_McpFlag_DefaultsToCurrentDirectoryWhenPathOmitted()
    {
        var parse = CliParser.Parse(["--mcp"]);

        Assert.Equal(CliCommandKind.Run, parse.Kind);
        Assert.NotNull(parse.Options);
        Assert.True(parse.Options!.Mcp);
        Assert.False(string.IsNullOrWhiteSpace(parse.Options.Path));
    }

    [Fact]
    public void CliParser_McpFlag_PreservesExplicitPath()
    {
        var parse = CliParser.Parse(["./my-solution", "--mcp"]);

        Assert.Equal(CliCommandKind.Run, parse.Kind);
        Assert.NotNull(parse.Options);
        Assert.True(parse.Options!.Mcp);
        Assert.Equal("./my-solution", parse.Options.Path);
    }

    [Fact]
    public void McpServer_Run_NewlineDelimited_SupportsFullLifecycleAndAllFourTools()
    {
        using var temp = CreateSampleCqrsWorkspace();

        var requests = new[]
        {
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"test-agent","version":"1.0.0"}}}""",
            """{"jsonrpc":"2.0","method":"notifications/initialized"}""",
            """{"jsonrpc":"2.0","id":2,"method":"ping"}""",
            """{"jsonrpc":"2.0","id":3,"method":"tools/list"}""",
            """{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"get_architecture_dna","arguments":{}}}""",
            """{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"trace_slice","arguments":{"filter":"CreateOrder"}}}""",
            """{"jsonrpc":"2.0","id":6,"method":"tools/call","params":{"name":"trace_entity_impact","arguments":{"entity":"Order"}}}""",
            """{"jsonrpc":"2.0","id":7,"method":"tools/call","params":{"name":"check_architecture","arguments":{"strict":true}}}""",
        };

        var input = string.Join("\n", requests) + "\n";
        using var stdin = new StringReader(input);
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = McpServer.Run(
            new CliOptions { Path = temp.Path, Mcp = true },
            stdin,
            stdout,
            stderr);

        Assert.Equal(ExitCodes.Success, exitCode);

        var lines = stdout.ToString()
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        // 7 responses expected (notifications/initialized has no id and produces no response)
        Assert.Equal(7, lines.Length);

        // 1. initialize
        var initResp = JsonNode.Parse(lines[0])!.AsObject();
        Assert.Equal(1, initResp["id"]!.GetValue<int>());
        Assert.Equal("2024-11-05", initResp["result"]!["protocolVersion"]!.GetValue<string>());
        Assert.Equal("cqrslens", initResp["result"]!["serverInfo"]!["name"]!.GetValue<string>());

        // 2. ping
        var pingResp = JsonNode.Parse(lines[1])!.AsObject();
        Assert.Equal(2, pingResp["id"]!.GetValue<int>());
        Assert.NotNull(pingResp["result"]);

        // 3. tools/list
        var listResp = JsonNode.Parse(lines[2])!.AsObject();
        var tools = listResp["result"]!["tools"]!.AsArray();
        Assert.Equal(4, tools.Count);
        var toolNames = tools.Select(t => t!["name"]!.GetValue<string>()).ToList();
        Assert.Contains("get_architecture_dna", toolNames);
        Assert.Contains("trace_slice", toolNames);
        Assert.Contains("trace_entity_impact", toolNames);
        Assert.Contains("check_architecture", toolNames);

        // 4. get_architecture_dna
        var dnaResp = JsonNode.Parse(lines[3])!.AsObject();
        Assert.False(dnaResp["result"]!["isError"]!.GetValue<bool>());
        var dnaText = dnaResp["result"]!["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("## Architectural DNA & Conventions", dnaText);
        Assert.Contains("## Vertical Slice Index", dnaText);
        Assert.Contains("`CreateOrderCommand`", dnaText);
        Assert.Contains("POST /api/orders", dnaText);

        // 5. trace_slice
        var sliceResp = JsonNode.Parse(lines[4])!.AsObject();
        Assert.False(sliceResp["result"]!["isError"]!.GetValue<bool>());
        var sliceText = sliceResp["result"]!["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("### [Command] `CreateOrderCommand`", sliceText);
        Assert.Contains("`CustomerId: Guid`", sliceText);
        Assert.Contains("`CreateOrderCommandValidator`", sliceText);
        Assert.Contains("Orders (Order)", sliceText);
        Assert.Contains("`OrderCreatedNotification`", sliceText);

        // 6. trace_entity_impact
        var entityResp = JsonNode.Parse(lines[5])!.AsObject();
        Assert.False(entityResp["result"]!["isError"]!.GetValue<bool>());
        var entityText = entityResp["result"]!["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("# CqrsLens Entity Impact Trace — `Order`", entityText);
        Assert.Contains("`CreateOrderCommand`", entityText);
        Assert.Contains("`POST /api/orders`", entityText);

        // 7. check_architecture
        var checkResp = JsonNode.Parse(lines[6])!.AsObject();
        Assert.False(checkResp["result"]!["isError"]!.GetValue<bool>());
        var checkText = checkResp["result"]!["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("# CqrsLens Architecture Checks", checkText);
        Assert.Contains("MissingHandler", checkText);
        Assert.Contains("OrphanDeleteOrderCommand", checkText);
    }

    [Fact]
    public void McpServer_Run_ContentLengthFraming_PreservesFramingInResponse()
    {
        using var temp = CreateSampleCqrsWorkspace();

        var body = """{"jsonrpc":"2.0","id":42,"method":"ping"}""";
        var byteCount = Encoding.UTF8.GetByteCount(body);
        var framedInput = $"Content-Length: {byteCount}\r\n\r\n{body}";

        using var stdin = new StringReader(framedInput);
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = McpServer.Run(
            new CliOptions { Path = temp.Path, Mcp = true },
            stdin,
            stdout,
            stderr);

        Assert.Equal(ExitCodes.Success, exitCode);

        var output = stdout.ToString();
        Assert.StartsWith("Content-Length: ", output);
        Assert.Contains("\r\n\r\n", output);

        var splitIndex = output.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var jsonPart = output[(splitIndex + 4)..];
        var resp = JsonNode.Parse(jsonPart)!.AsObject();
        Assert.Equal(42, resp["id"]!.GetValue<int>());
    }

    [Fact]
    public void McpServer_HandlesErrorsAndInvalidArgumentsGracefully()
    {
        using var temp = CreateSampleCqrsWorkspace();
        var options = new CliOptions { Path = temp.Path, Mcp = true };

        // Invalid JSON
        var badJsonResp = JsonNode.Parse(McpServer.ProcessMessage("{not-json", options)!)!.AsObject();
        Assert.Equal(-32700, badJsonResp["error"]!["code"]!.GetValue<int>());

        // Unknown method
        var unknownMethodResp = JsonNode.Parse(
            McpServer.ProcessMessage("""{"jsonrpc":"2.0","id":1,"method":"unknown/method"}""", options)!)!.AsObject();
        Assert.Equal(-32601, unknownMethodResp["error"]!["code"]!.GetValue<int>());

        // Missing required 'filter' on trace_slice
        var missingFilterResp = JsonNode.Parse(
            McpServer.ProcessMessage(
                """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"trace_slice","arguments":{}}}""",
                options)!)!.AsObject();
        Assert.True(missingFilterResp["result"]!["isError"]!.GetValue<bool>());

        // Unknown tool
        var unknownToolResp = JsonNode.Parse(
            McpServer.ProcessMessage(
                """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"non_existent_tool","arguments":{}}}""",
                options)!)!.AsObject();
        Assert.Equal(-32602, unknownToolResp["error"]!["code"]!.GetValue<int>());
    }

    private static TempDir CreateSampleCqrsWorkspace()
    {
        var temp = new TempDir();

        var domainDir = Path.Combine(temp.Path, "Orders.Domain");
        var appDir = Path.Combine(temp.Path, "Orders.Application");
        var apiDir = Path.Combine(temp.Path, "Orders.Api");
        Directory.CreateDirectory(domainDir);
        Directory.CreateDirectory(appDir);
        Directory.CreateDirectory(apiDir);

        File.WriteAllText(Path.Combine(domainDir, "Orders.Domain.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        File.WriteAllText(Path.Combine(appDir, "Orders.Application.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        File.WriteAllText(Path.Combine(apiDir, "Orders.Api.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk.Web\"></Project>");

        File.WriteAllText(Path.Combine(domainDir, "Order.cs"), """
            using System;
            namespace Orders.Domain;

            public class Order
            {
                public Guid Id { get; set; }
                public Guid CustomerId { get; set; }
                public decimal TotalAmount { get; set; }
            }

            public record OrderCreatedNotification(Guid OrderId) : MediatR.INotification;
            """);

        File.WriteAllText(Path.Combine(appDir, "CreateOrder.cs"), """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using FluentValidation;
            using MediatR;
            using Microsoft.EntityFrameworkCore;
            using Orders.Domain;

            namespace Orders.Application;

            public interface IAppDbContext
            {
                DbSet<Order> Orders { get; }
                Task<int> SaveChangesAsync(CancellationToken ct = default);
            }

            public record CreateOrderCommand(Guid CustomerId, decimal TotalAmount) : IRequest<Guid>;

            public record OrphanDeleteOrderCommand(Guid OrderId) : IRequest;

            public class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
            {
                public CreateOrderCommandValidator()
                {
                    RuleFor(x => x.TotalAmount).GreaterThan(0);
                }
            }

            public class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, Guid>
            {
                private readonly IAppDbContext _db;
                private readonly IPublisher _publisher;

                public CreateOrderCommandHandler(IAppDbContext db, IPublisher publisher)
                {
                    _db = db;
                    _publisher = publisher;
                }

                public async Task<Guid> Handle(CreateOrderCommand request, CancellationToken ct)
                {
                    var order = new Order { Id = Guid.NewGuid(), CustomerId = request.CustomerId, TotalAmount = request.TotalAmount };
                    _db.Orders.Add(order);
                    await _db.SaveChangesAsync(ct);
                    await _publisher.Publish(new OrderCreatedNotification(order.Id), ct);
                    return order.Id;
                }
            }
            """);

        File.WriteAllText(Path.Combine(apiDir, "OrdersController.cs"), """
            using System.Threading.Tasks;
            using MediatR;
            using Microsoft.AspNetCore.Mvc;
            using Orders.Application;

            namespace Orders.Api;

            [ApiController]
            [Route("api/orders")]
            public class OrdersController : ControllerBase
            {
                private readonly ISender _sender;
                public OrdersController(ISender sender) => _sender = sender;

                [HttpPost]
                public async Task<IActionResult> Create(CreateOrderCommand command)
                {
                    var id = await _sender.Send(command);
                    return Ok(id);
                }
            }
            """);

        return temp;
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "cqrslens-mcp-tests-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Ignore temp cleanup races
            }
        }
    }
}
