using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

public class RoslynArchitectureScannerTests
{
    private const string Contracts = """
        namespace MediatR
        {
            public interface IRequest { }
            public interface IRequest<TResponse> { }
            public interface IRequestHandler<TRequest> { }
            public interface IRequestHandler<TRequest, TResponse> { }
        }

        namespace FluentValidation
        {
            public abstract class AbstractValidator<T> { }
            public interface IValidator<T> { }
        }
        """;

    [Fact]
    public void FindsRequestsHandlersValidatorsAndMarkerResponseTypes()
    {
        var files = new[]
        {
            Source("Contracts.cs", Contracts),
            Source("Feature.cs", """
                using System;
                using MediatR;
                using FluentValidation;

                namespace Shop.Application;

                public interface ICommand<T> : IRequest<T> { }

                public sealed record PlaceOrder(Guid Id) : ICommand<Guid>;

                public sealed class PlaceOrderHandler :
                    IRequestHandler<PlaceOrder, Guid>
                {
                }

                public sealed class PlaceOrderValidator :
                    AbstractValidator<PlaceOrder>
                {
                }
                """),
        };

        var result = Scan(files);

        var request = Assert.Single(result.Requests);
        Assert.Equal("PlaceOrder", request.Name);
        Assert.Equal("Shop.Application", request.Namespace);
        Assert.Equal("Guid", request.ResponseType);
        Assert.Equal("ICommand", request.ViaMarker);

        var handler = Assert.Single(result.Handlers);
        Assert.Equal("Shop.Application.PlaceOrderHandler", handler.FullName);
        Assert.Equal("PlaceOrder", handler.RequestName);
        Assert.Equal("Shop.Application", handler.RequestNamespace);

        var validator = Assert.Single(result.Validators);
        Assert.Equal("Shop.Application.PlaceOrderValidator", validator.FullName);
        Assert.Equal("PlaceOrder", validator.TargetType);
        Assert.Equal("Shop.Application", validator.TargetNamespace);
    }

    [Fact]
    public void ResolvesATypeAliasToTheUnderlyingRequestSymbol()
    {
        var files = new[]
        {
            Source("Contracts.cs", Contracts),
            Source("Alias.cs", """
                using CountRequest = MediatR.IRequest<int>;

                namespace Shop.Application.Counts;

                public sealed record GetCount() : CountRequest;
                """),
        };

        var request = Assert.Single(Scan(files).Requests);

        Assert.Equal("GetCount", request.Name);
        Assert.Equal("Shop.Application.Counts", request.Namespace);
        Assert.Equal("int", request.ResponseType);
    }

    [Fact]
    public void KeepsBlockNamespacesPerDeclaration()
    {
        var files = new[]
        {
            Source("Contracts.cs", Contracts),
            Source("Queries.cs", """
                using MediatR;

                namespace Alpha
                {
                    public sealed record GetData() : IRequest<int>;
                }

                namespace Beta
                {
                    public sealed record GetData() : IRequest<string>;
                }
                """),
        };

        var requests = Scan(files).Requests.OrderBy(r => r.Namespace).ToList();

        Assert.Equal(2, requests.Count);
        Assert.Equal("Alpha", requests[0].Namespace);
        Assert.Equal("int", requests[0].ResponseType);
        Assert.Equal("Beta", requests[1].Namespace);
        Assert.Equal("string", requests[1].ResponseType);
    }

    private static SourceFile Source(string path, string code)
        => new(path, code, "Shop.Application");

    private static AnalysisResult Scan(IReadOnlyList<SourceFile> files)
    {
        var result = new AnalysisResult { RootPath = "/tmp" };
        new RoslynArchitectureScanner().Scan(
            files,
            new HashSet<string>(StringComparer.Ordinal),
            result);
        return result;
    }
}
