using System.Text.Json.Nodes;
using Xunit;

namespace CqrsLens.Tests;

public sealed class SliceDesignCliTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("cqrslens-design-").FullName;

    [Theory]
    [InlineData("text")]
    [InlineData("roslyn")]
    public void SavedDesign_CanBeReviewedBeforeCodingAndVerifiedAfterward(string analysis)
    {
        var input = CreateDesign();
        var path = WriteDesign(input);

        var draft = Run(_root, "--slice-design", path, "--analysis", analysis);
        Assert.Equal(ExitCodes.Success, draft.Code);
        Assert.Empty(draft.Error);
        Assert.NotEqual("matched", JsonNode.Parse(draft.Output)!["status"]!.GetValue<string>());
        Assert.Equal(ExitCodes.StrictFailure, Run(_root, "--slice-design", path, "--strict").Code);

        File.WriteAllText(Path.Combine(_root, "Shop.Application.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(_root, "PlaceOrderCommand.cs"), """
            using MediatR;
            namespace Shop.Orders;
            public sealed record PlaceOrderCommand : IRequest<int>;
            public sealed class PlaceOrderHandler : IRequestHandler<PlaceOrderCommand, int> { }
            """);

        var verified = Run(_root, "--slice-design", path, "--strict", "--analysis", analysis);

        Assert.Equal(ExitCodes.Success, verified.Code);
        Assert.Empty(verified.Error);
        var evaluation = JsonNode.Parse(verified.Output)!;
        Assert.Equal("matched", evaluation["status"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(input["scenarios"], evaluation["design"]!["scenarios"]));
        Assert.All(evaluation["checks"]!.AsArray(), check =>
            Assert.Equal("matched", check!["status"]!.GetValue<string>()));
        Assert.Equal(input.ToJsonString(), File.ReadAllText(path));
        Assert.False(File.Exists(Path.Combine(_root, "ARCHITECTURE.md")));
    }

    [Theory]
    [InlineData("missing-version", "schemaVersion")]
    [InlineData("future-version", "schemaVersion")]
    [InlineData("missing-response", "responseType")]
    [InlineData("unknown-field", "validtor")]
    [InlineData("bad-endpoint", "endpoint.route")]
    [InlineData("duplicate-scenario", "repeated")]
    [InlineData("void-string", "Use null")]
    public void InvalidDesign_IsAUsageError(string mutation, string expected)
    {
        var design = CreateDesign();
        switch (mutation)
        {
            case "missing-version": design.Remove("schemaVersion"); break;
            case "future-version": design["schemaVersion"] = 2; break;
            case "missing-response": design.Remove("responseType"); break;
            case "unknown-field": design["validtor"] = "Wrong"; break;
            case "bad-endpoint": design["endpoint"] = new JsonObject { ["method"] = "POST" }; break;
            case "duplicate-scenario": design["scenarios"]!.AsArray().Add(design["scenarios"]![0]!.DeepClone()); break;
            case "void-string": design["responseType"] = "void"; break;
        }

        var result = Run(_root, "--slice-design", WriteDesign(design));

        Assert.Equal(ExitCodes.Usage, result.Code);
        Assert.Empty(result.Output);
        Assert.Contains(expected, result.Error);
    }

    [Fact]
    public void DesignMode_CannotSilentlyIgnoreScanOnlyOptions()
    {
        var result = Run(_root, "--slice-design", "feature.json", "--sarif", "out.sarif");

        Assert.Equal(ExitCodes.Usage, result.Code);
        Assert.Contains("--slice-design", result.Error);
    }

    [Fact]
    public void RichOptionalFields_AreParsedAndVerified()
    {
        var appDir = Path.Combine(_root, "Shop.Application");
        var infraDir = Path.Combine(_root, "Shop.Infrastructure");
        Directory.CreateDirectory(appDir);
        Directory.CreateDirectory(infraDir);

        File.WriteAllText(Path.Combine(appDir, "Shop.Application.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(infraDir, "Shop.Infrastructure.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(infraDir, "ShopDbContext.cs"), """
            using Microsoft.EntityFrameworkCore;
            namespace Shop.Infrastructure;
            public sealed class Order { public int Id { get; set; } }
            public sealed class ShopDbContext : DbContext
            {
                public DbSet<Order> Orders => null!;
            }
            """);
        File.WriteAllText(Path.Combine(appDir, "PlaceOrderCommand.cs"), """
            using MediatR;
            using Shop.Infrastructure;
            namespace Shop.Orders;
            public interface IOrderAuditService { }
            public sealed class OrderAuditService : IOrderAuditService { }
            public sealed record OrderPlacedNotification(int OrderId) : INotification;
            public sealed record SendAuditCommand(int OrderId) : IRequest<bool>;
            public sealed record PlaceOrderCommand(int CustomerId) : IRequest<int>;
            public sealed class PlaceOrderHandler : IRequestHandler<PlaceOrderCommand, int>
            {
                private readonly ShopDbContext _db;
                private readonly IOrderAuditService _audit;
                private readonly IPublisher _publisher;
                private readonly ISender _sender;

                public PlaceOrderHandler(ShopDbContext db, IOrderAuditService audit, IPublisher publisher, ISender sender)
                {
                    _db = db;
                    _audit = audit;
                    _publisher = publisher;
                    _sender = sender;
                }

                public async Task<int> Handle(PlaceOrderCommand request, CancellationToken ct)
                {
                    _ = _db.Orders;
                    await _sender.Send(new SendAuditCommand(request.CustomerId), ct);
                    await _publisher.Publish(new OrderPlacedNotification(request.CustomerId), ct);
                    return 1;
                }
            }
            """);

        var rich = JsonNode.Parse("""
            {
              "schemaVersion": 1,
              "project": "Shop.Application",
              "request": "Shop.Orders.PlaceOrderCommand",
              "kind": "Command",
              "responseType": "int",
              "handler": "Shop.Orders.PlaceOrderHandler",
              "properties": [{ "name": "CustomerId", "type": "int" }],
              "publishes": ["OrderPlacedNotification"],
              "dispatches": ["SendAuditCommand"],
              "persistence": { "dbContexts": ["ShopDbContext"], "entities": ["Order"] },
              "dependencies": ["ShopDbContext", "IOrderAuditService"],
              "forbiddenDependencies": ["Shop.Legacy."],
              "scenarios": [{ "name": "places order", "given": "valid input", "when": "submitted", "then": "returns id" }]
            }
            """)!.AsObject();

        var path = WriteDesign(rich);
        var run = Run(_root, "--slice-design", path, "--strict");

        Assert.Equal(ExitCodes.Success, run.Code);
        var eval = JsonNode.Parse(run.Output)!;
        Assert.Equal("matched", eval["status"]!.GetValue<string>());
        Assert.Contains(eval["checks"]!.AsArray(), c => c!["field"]!.GetValue<string>() == "forbiddenDependencies");
    }

    [Theory]
    [InlineData("""{"persistence":{"bad":[]}}""", "Unexpected field 'bad'")]
    [InlineData("""{"properties":[{"name":"Id","bad":"x"}]}""", "Unexpected field 'bad'")]
    [InlineData("""{"publishes":[""]}""", "publishes[0]")]
    public void RichOptionalFields_RejectUnknownKeysAndInvalidValues(string patchJson, string expectedError)
    {
        var design = CreateDesign();
        foreach (var kv in JsonNode.Parse(patchJson)!.AsObject())
            design[kv.Key] = kv.Value!.DeepClone();

        var result = Run(_root, "--slice-design", WriteDesign(design));
        Assert.Equal(ExitCodes.Usage, result.Code);
        Assert.Contains(expectedError, result.Error);
    }

    [Fact]
    public void DirectoryMode_EvaluatesAllSliceDesignsSkipsBuildFoldersAndRespectsStrict()
    {
        File.WriteAllText(Path.Combine(_root, "Shop.Application.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(_root, "PlaceOrderCommand.cs"), """
            using MediatR;
            namespace Shop.Orders;
            public sealed record PlaceOrderCommand : IRequest<int>;
            public sealed class PlaceOrderHandler : IRequestHandler<PlaceOrderCommand, int> { }
            """);

        var designsDir = Path.Combine(_root, "designs");
        Directory.CreateDirectory(Path.Combine(designsDir, "b"));
        Directory.CreateDirectory(Path.Combine(designsDir, "a"));
        Directory.CreateDirectory(Path.Combine(designsDir, "bin", "Debug"));

        File.WriteAllText(Path.Combine(designsDir, "b", "first.slice.json"), CreateDesign().ToJsonString());

        var second = CreateDesign();
        second["request"] = "Shop.Orders.MissingCommand";
        File.WriteAllText(Path.Combine(designsDir, "a", "second.slice.json"), second.ToJsonString());

        // Must be skipped under bin/
        File.WriteAllText(Path.Combine(designsDir, "bin", "Debug", "ignored.slice.json"), "{ invalid json");

        var nonStrict = Run(_root, "--slice-design", designsDir);
        Assert.Equal(ExitCodes.Success, nonStrict.Code);
        var batch = JsonNode.Parse(nonStrict.Output)!.AsObject();
        Assert.Equal("missing", batch["status"]!.GetValue<string>());
        Assert.Equal("designs", batch["root"]!.GetValue<string>());
        Assert.Equal(2, batch["total"]!.GetValue<int>());
        Assert.Equal(1, batch["matched"]!.GetValue<int>());
        var results = batch["results"]!.AsArray();
        Assert.Equal(2, results.Count);
        Assert.Equal("designs/a/second.slice.json", results[0]!["file"]!.GetValue<string>());
        Assert.Equal("designs/b/first.slice.json", results[1]!["file"]!.GetValue<string>());

        var strictFail = Run(_root, "--slice-design", designsDir, "--strict");
        Assert.Equal(ExitCodes.StrictFailure, strictFail.Code);

        // Fix second design -> strict passes
        second["request"] = "Shop.Orders.PlaceOrderCommand";
        File.WriteAllText(Path.Combine(designsDir, "a", "second.slice.json"), second.ToJsonString());
        var strictPass = Run(_root, "--slice-design", designsDir, "--strict");
        Assert.Equal(ExitCodes.Success, strictPass.Code);
        var passBatch = JsonNode.Parse(strictPass.Output)!.AsObject();
        Assert.Equal("matched", passBatch["status"]!.GetValue<string>());
        Assert.Equal(2, passBatch["matched"]!.GetValue<int>());

        // Empty directory -> usage error
        var emptyDir = Path.Combine(_root, "empty-designs");
        Directory.CreateDirectory(emptyDir);
        var emptyResult = Run(_root, "--slice-design", emptyDir, "--strict");
        Assert.Equal(ExitCodes.Usage, emptyResult.Code);
        Assert.Contains("no '*.slice.json'", emptyResult.Error);
    }

    private static JsonObject CreateDesign() => JsonNode.Parse("""
        {
          "schemaVersion": 1,
          "project": "Shop.Application",
          "request": "Shop.Orders.PlaceOrderCommand",
          "kind": "Command",
          "responseType": "int",
          "handler": "Shop.Orders.PlaceOrderHandler",
          "scenarios": [{ "name": "places an order", "given": "A valid order",
                           "when": "It is submitted", "then": "The caller receives its ID" }]
        }
        """)!.AsObject();

    private string WriteDesign(JsonObject design)
    {
        var path = Path.Combine(_root, "feature.slice.json");
        File.WriteAllText(path, design.ToJsonString());
        return path;
    }

    private static (int Code, string Output, string Error) Run(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = CliRunner.Run(args, output, error);
        return (code, output.ToString(), error.ToString());
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
