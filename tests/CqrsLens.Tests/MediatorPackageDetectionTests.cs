using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// Regression tests for Mediator package-reference detection, covering the bare
/// <c>Mediator</c> meta-package, <c>Mediator.*</c> sub-packages, and non-matching
/// sibling package names when no <c>using Mediator;</c> directive is present.
/// </summary>
public sealed class MediatorPackageDetectionTests : IDisposable
{
    private readonly string _root;

    public MediatorPackageDetectionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cqrslens-mediator-pkg-" + Guid.NewGuid().ToString("N"));
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

    private void WriteOrdersFixture(string packageName, bool includeUsingDirective = false)
    {
        WriteProject($"""<PackageReference Include="{packageName}" Version="3.0.0" />""");

        var usingPrefix = includeUsingDirective ? "using Mediator;\n\n" : string.Empty;
        var mediatorQualifier = includeUsingDirective ? string.Empty : "Mediator.";

        WriteFile("Orders.cs", $$"""
            {{usingPrefix}}namespace App.Orders;

            public sealed record CreateOrderCommand(string Sku) : {{mediatorQualifier}}ICommand<int>;
            public sealed record GetOrderQuery(int Id) : {{mediatorQualifier}}IQuery<string>;

            public sealed class CreateOrderHandler : {{mediatorQualifier}}ICommandHandler<CreateOrderCommand, int>
            {
                public ValueTask<int> Handle(CreateOrderCommand command, CancellationToken ct) => new(1);
            }

            public sealed class GetOrderHandler : {{mediatorQualifier}}IQueryHandler<GetOrderQuery, string>
            {
                public ValueTask<string> Handle(GetOrderQuery query, CancellationToken ct) => new("x");
            }
            """);

        WriteFile("OrdersController.cs", $$"""
            {{usingPrefix}}using Microsoft.AspNetCore.Mvc;

            namespace App.Orders;

            [ApiController]
            [Route("orders")]
            public sealed class OrdersController({{mediatorQualifier}}IMediator mediator) : ControllerBase
            {
                [HttpGet("{id:int}")]
                public async Task<string> Get(int id, CancellationToken ct)
                    => await mediator.Send(new GetOrderQuery(id), ct);
            }
            """);
    }

    private static void AssertLinkedOrdersFixture(AnalysisResult result)
    {
        Assert.Contains(MediatorConventions.MediatorName, result.Conventions);
        Assert.Equal(2, result.Requests.Count);

        var create = Assert.Single(result.Requests, r => r.Name == "CreateOrderCommand");
        Assert.Equal("Command", create.Kind);
        Assert.Contains("App.Orders.CreateOrderHandler", create.Handlers);

        var get = Assert.Single(result.Requests, r => r.Name == "GetOrderQuery");
        Assert.Equal("Query", get.Kind);
        Assert.Contains("App.Orders.GetOrderHandler", get.Handlers);
        Assert.Contains("GET /orders/{id:int}", get.Entrypoints);

        var endpoint = Assert.Single(result.Endpoints);
        Assert.Equal("OrdersController.Get", endpoint.Name);
        Assert.Equal("GET", endpoint.HttpMethod);
        Assert.Equal("/orders/{id:int}", endpoint.Route);
        Assert.Equal("GetOrderQuery", endpoint.TargetRequestName);
        Assert.Equal("App.Orders.GetOrderQuery", endpoint.TargetRequestFullName);

        Assert.DoesNotContain(result.Checks, c => c.Code == ArchitectureChecker.MissingHandlerCode);
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void BareMediatorPackage_WithoutUsingDirective_DetectsRequestsHandlersAndControllerEndpoint(AnalysisMode mode)
    {
        WriteOrdersFixture("Mediator", includeUsingDirective: false);

        var result = Analyzer.Analyze(_root, mode);

        AssertLinkedOrdersFixture(result);
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void BareMediatorPackage_WithUsingDirective_DetectsRequestsHandlersAndControllerEndpoint(AnalysisMode mode)
    {
        WriteOrdersFixture("Mediator", includeUsingDirective: true);

        var result = Analyzer.Analyze(_root, mode);

        AssertLinkedOrdersFixture(result);
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void MediatorAbstractionsPackage_WithoutUsingDirective_DetectsRequestsHandlersAndControllerEndpoint(AnalysisMode mode)
    {
        WriteOrdersFixture("Mediator.Abstractions", includeUsingDirective: false);

        var result = Analyzer.Analyze(_root, mode);

        AssertLinkedOrdersFixture(result);
    }

    [Fact]
    public void UnrelatedMediatorXPackage_WithoutUsingDirective_DoesNotEnableMediatorConvention()
    {
        WriteOrdersFixture("MediatorX", includeUsingDirective: false);

        var result = Analyzer.Analyze(_root, AnalysisMode.Text);

        Assert.Empty(result.Conventions);
        Assert.Empty(result.Requests);
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void FutureMediatorDotSubPackage_WithoutUsingDirective_DetectsRequestsAndHandlers(AnalysisMode mode)
    {
        WriteOrdersFixture("Mediator.Example", includeUsingDirective: false);

        var result = Analyzer.Analyze(_root, mode);

        AssertLinkedOrdersFixture(result);
    }
}
