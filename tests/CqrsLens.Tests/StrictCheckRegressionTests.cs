using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// Regression coverage for the confirmed --strict false positives: test projects
/// classified as production layers, and handlers/validators hidden behind
/// application-specific contracts.
/// </summary>
public class StrictCheckRegressionTests : IDisposable
{
    private readonly string _root;

    public StrictCheckRegressionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cqrslens-strict-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private void WriteProject(string folder, string name, params string[] references)
    {
        var dir = Path.Combine(_root, folder);
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

    [Theory]
    [InlineData("Shop.Domain.Tests", "Shop.Domain.Tests")]
    [InlineData("Shop.UnitTests", "Shop.UnitTests")]
    [InlineData("Shop.Test", "Shop.Test")]
    [InlineData("Shop.IntegrationTests", "Shop.IntegrationTests")]
    [InlineData("Shop.Domain", "tests/Shop.Domain")]
    public void LayerClassifier_TestProjectsAreNotProductionLayers(string name, string folder)
    {
        Assert.Equal("Tests", LayerClassifier.Classify(name, Path.Combine(_root, folder), _root));
    }

    [Fact]
    public void Checks_TestProjectReferencingInfrastructure_IsNotALayerViolation()
    {
        WriteProject("Shop.Domain", "Shop.Domain");
        WriteProject("Shop.Infrastructure", "Shop.Infrastructure");
        WriteProject("Shop.Domain.Tests", "Shop.Domain.Tests", @"..\Shop.Infrastructure\Shop.Infrastructure.csproj");

        var result = Analyzer.Analyze(_root);

        Assert.Equal("Tests", result.Projects.Single(p => p.Name == "Shop.Domain.Tests").Layer);
        Assert.DoesNotContain(result.Checks, c => c.Code == ArchitectureChecker.LayerViolationCode);
    }

    [Fact]
    public void Checks_DuplicateProjectNames_DoNotProduceFalseLayerViolation()
    {
        var result = new AnalysisResult { RootPath = "/tmp" };
        var domain = new ProjectInfo
        {
            Name = "Shop.Domain", FilePath = "/tmp/a/Shop.Domain.csproj",
            DirectoryPath = "/tmp/a", Layer = "Domain",
        };
        domain.ReferenceNames.Add("Shared");
        var sharedInfra = new ProjectInfo
        {
            Name = "Shared", FilePath = "/tmp/a/Shared.csproj",
            DirectoryPath = "/tmp/a", Layer = "Infrastructure",
        };
        var sharedOther = new ProjectInfo
        {
            Name = "Shared", FilePath = "/tmp/b/Shared.csproj",
            DirectoryPath = "/tmp/b", Layer = "Application",
        };
        result.Projects.AddRange(new[] { domain, sharedInfra, sharedOther });

        var findings = ArchitectureChecker.RunChecks(result);

        Assert.DoesNotContain(findings, f => f.Code == ArchitectureChecker.LayerViolationCode);
        Assert.Contains(findings, f => f.Code == ArchitectureChecker.AmbiguousProjectNameCode);
    }

    [Fact]
    public void CustomHandlerInterface_ResolvesRequestAndPassesStrictChecks()
    {
        WriteProject("App", "App");
        WriteFile("App/Contracts.cs", """
            namespace App.Contracts;
            public interface ICommandHandler<TCommand> : IRequestHandler<TCommand, Unit> { }
            """);
        WriteFile("App/PlaceOrder.cs", """
            namespace App.Features;
            public sealed record PlaceOrder : IRequest<Unit>;
            public sealed class PlaceOrderHandler : App.Contracts.ICommandHandler<PlaceOrder> { }
            """);

        var result = Analyzer.Analyze(_root);

        var request = Assert.Single(result.Requests);
        Assert.Equal(new[] { "App.Features.PlaceOrderHandler" }, request.Handlers);
        Assert.DoesNotContain(result.Checks, c => c.Code == ArchitectureChecker.MissingHandlerCode);
    }

    [Fact]
    public void FixedRequestHandlerInterface_ResolvesRequest()
    {
        WriteProject("App", "App");
        WriteFile("App/Contracts.cs", """
            namespace App.Contracts;
            public interface IPlaceOrderHandler : IRequestHandler<App.Features.PlaceOrder, Unit> { }
            """);
        WriteFile("App/PlaceOrder.cs", """
            namespace App.Features;
            public sealed record PlaceOrder : IRequest<Unit>;
            public sealed class PlaceOrderHandler : App.Contracts.IPlaceOrderHandler { }
            """);

        var result = Analyzer.Analyze(_root);

        var request = Assert.Single(result.Requests);
        Assert.Equal(new[] { "App.Features.PlaceOrderHandler" }, request.Handlers);
    }

    [Fact]
    public void ReorderedGenericHandlerInterface_ResolvesRequest()
    {
        WriteProject("App", "App");
        WriteFile("App/Contracts.cs", """
            namespace App.Contracts;
            public interface IReorderedHandler<TResponse, TRequest> : IRequestHandler<TRequest, TResponse> { }
            """);
        WriteFile("App/PlaceOrder.cs", """
            namespace App.Features;
            public sealed record PlaceOrder : IRequest<Guid>;
            public sealed class PlaceOrderHandler : App.Contracts.IReorderedHandler<Guid, PlaceOrder> { }
            """);

        var result = Analyzer.Analyze(_root);

        var request = Assert.Single(result.Requests);
        Assert.Equal(new[] { "App.Features.PlaceOrderHandler" }, request.Handlers);
    }

    [Fact]
    public void MultipleHandlerInterfaces_AreAllRepresentedAndLinked()
    {
        WriteProject("App", "App");
        WriteFile("App/Features.cs", """
            namespace App.Features;
            public sealed record FirstCommand : IRequest<int>;
            public sealed record SecondCommand : IRequest<string>;
            public sealed class BothHandler :
                IRequestHandler<FirstCommand, int>,
                IRequestHandler<SecondCommand, string> { }
            """);

        var result = Analyzer.Analyze(_root);

        Assert.Equal(2, result.Handlers.Count);
        Assert.Equal(new[] { "App.Features.BothHandler" },
            result.Requests.Single(r => r.Name == "FirstCommand").Handlers);
        Assert.Equal(new[] { "App.Features.BothHandler" },
            result.Requests.Single(r => r.Name == "SecondCommand").Handlers);
    }

    [Fact]
    public void ValidatorBaseClass_IsResolvedWithoutReportingTheAbstractBase()
    {
        WriteProject("App", "App");
        WriteFile("App/Validation.cs", """
            namespace App.Validation;
            public abstract class AppValidator<T> : AbstractValidator<T> { }
            """);
        WriteFile("App/PlaceOrder.cs", """
            namespace App.Features;
            public sealed record PlaceOrder : IRequest<Unit>;
            public sealed class PlaceOrderHandler : IRequestHandler<PlaceOrder, Unit> { }
            public sealed class PlaceOrderValidator : App.Validation.AppValidator<PlaceOrder> { }
            """);

        var result = Analyzer.Analyze(_root);

        Assert.Equal("PlaceOrderValidator", Assert.Single(result.Validators).Name);
        Assert.Equal(new[] { "App.Features.PlaceOrderValidator" },
            Assert.Single(result.Requests).Validators);
    }

    [Fact]
    public void OpenGenericBaseValidator_IsNotReportedAsAConcreteValidator()
    {
        var result = new AnalysisResult { RootPath = "/tmp" };
        CSharpScanner.ScanText("""
            public abstract class AppValidator<T> : AbstractValidator<T> { }
            """, "App", result);

        Assert.Empty(result.Validators);
    }

    [Fact]
    public void GenericRequestAndHandler_LinkByOpenTypeIdentity()
    {
        WriteProject("App", "App");
        WriteFile("App/GetThing.cs", """
            namespace App.Features;
            public sealed record GetThing<T> : IRequest<T>;
            public sealed class GetThingHandler<T> : IRequestHandler<GetThing<T>, T> { }
            """);

        var result = Analyzer.Analyze(_root);

        var request = Assert.Single(result.Requests);
        Assert.Equal("GetThing", request.Name);
        Assert.Equal(new[] { "App.Features.GetThingHandler" }, request.Handlers);
    }
}
