using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

public class CSharpScannerTests
{
    private static AnalysisResult Scan(string code, string project = "App")
    {
        var result = new AnalysisResult { RootPath = "/tmp" };
        CSharpScanner.ScanText(code, project, result);
        return result;
    }

    [Fact]
    public void DetectsRecordRequestWithResponseType()
    {
        var result = Scan("public sealed record CreateOrderCommand(int CustomerId) : IRequest<Guid>;");

        var request = Assert.Single(result.Requests);
        Assert.Equal("CreateOrderCommand", request.Name);
        Assert.Equal("Guid", request.ResponseType);
        Assert.Equal("Command", request.Kind);
    }

    [Fact]
    public void DetectsClassQueryAndUnitRequest()
    {
        var result = Scan("""
            public sealed class GetOrdersQuery : IRequest<List<int>> { }
            public sealed record DeleteOrderCommand(Guid Id) : IRequest;
            """);

        Assert.Equal(2, result.Requests.Count);
        Assert.Equal("Query", result.Requests.Single(r => r.Name == "GetOrdersQuery").Kind);
        Assert.Null(result.Requests.Single(r => r.Name == "DeleteOrderCommand").ResponseType);
    }

    [Fact]
    public void DetectsHandlerWithAndWithoutResponse()
    {
        var result = Scan("""
            public sealed class CreateOrderHandler : IRequestHandler<CreateOrderCommand, Guid> { }
            public sealed class DeleteOrderHandler : IRequestHandler<DeleteOrderCommand> { }
            """);

        Assert.Equal(2, result.Handlers.Count);
        Assert.Equal("CreateOrderCommand", result.Handlers[0].RequestName);
        Assert.Equal("DeleteOrderCommand", result.Handlers[1].RequestName);
    }

    [Fact]
    public void HandlerDeclarationIsNotMistakenForARequest()
    {
        var result = Scan("public sealed class FooHandler : IRequestHandler<FooCommand, int> { }");
        Assert.Empty(result.Requests);
        Assert.Single(result.Handlers);
    }

    [Fact]
    public void DetectsValidatorsFromAbstractValidatorAndIValidator()
    {
        var result = Scan("""
            public sealed class CreateOrderValidator : AbstractValidator<CreateOrderCommand> { }
            public sealed class OtherValidator : IValidator<GetOrdersQuery> { }
            """);

        Assert.Equal(2, result.Validators.Count);
        Assert.Equal("CreateOrderCommand", result.Validators[0].TargetType);
        Assert.Equal("GetOrdersQuery", result.Validators[1].TargetType);
    }

    [Fact]
    public void DetectsPipelineBehavior()
    {
        var result = Scan("""
            public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
                where TRequest : notnull
            {
            }
            """);

        var behavior = Assert.Single(result.Behaviors);
        Assert.Equal("LoggingBehavior", behavior.Name);
    }

    [Fact]
    public void GenericConstraintOnPipelineBehavior_IsNotARequest()
    {
        var result = Scan("""
            public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
                where TRequest : IRequest<TResponse>
            {
            }
            """);

        Assert.Empty(result.Requests);
        Assert.Single(result.Behaviors);
    }

    [Fact]
    public void DetectsDbContextAndExtractsDbSets()
    {
        var result = Scan("""
            public sealed class ShopDbContext : DbContext
            {
                public DbSet<Order> Orders => Set<Order>();
                public DbSet<Product> Products => Set<Product>();
                public DbSet<Customer> Customers { get; set; }
            }
            """);

        var ctx = Assert.Single(result.DbContexts);
        Assert.Equal("ShopDbContext", ctx.Name);
        Assert.Equal(3, ctx.DbSets.Count);
        Assert.Contains(ctx.DbSets, s => s.Entity == "Order" && s.Property == "Orders");
        Assert.Contains(ctx.DbSets, s => s.Entity == "Product" && s.Property == "Products");
        Assert.Contains(ctx.DbSets, s => s.Entity == "Customer" && s.Property == "Customers");
    }

    [Fact]
    public void ExtractGenericArguments_SplitsTopLevelCommasOnly()
    {
        var args = CSharpScanner.ExtractGenericArguments(
            "IRequestHandler<Dictionary<string, int>, bool>", "IRequestHandler");

        Assert.NotNull(args);
        Assert.Equal(new[] { "Dictionary<string, int>", "bool" }, args);
    }

    [Fact]
    public void ExtractGenericArguments_IgnoresLongerIdentifiers()
    {
        var args = CSharpScanner.ExtractGenericArguments(
            "IRequestHandlerFactory<Nope>", "IRequest");
        Assert.Null(args);
    }
}
