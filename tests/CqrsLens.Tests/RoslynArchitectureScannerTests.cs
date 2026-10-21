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
            public interface IPipelineBehavior<TRequest, TResponse> { }
        }

        namespace FluentValidation
        {
            public abstract class AbstractValidator<T> { }
            public interface IValidator<T> { }
        }

        namespace Microsoft.EntityFrameworkCore
        {
            public class DbContext { }
            public class DbSet<T> { }
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
    public void ReportsUnresolvedContractReferencesInsteadOfSilentlyReturningZero()
    {
        var files = new[]
        {
            Source("NoUsing.cs", """
                namespace Shop.Application;

                public sealed record PlaceOrder : IRequest<Guid>;
                """),
        };

        var result = Scan(files);

        Assert.Empty(result.Requests);
        var warning = Assert.Single(result.Diagnostics);
        Assert.Equal(AnalysisResult.RoslynUnresolvedContractsCode, warning.Code);
        Assert.Equal(AnalysisDiagnosticSeverity.Warning, warning.Severity);
        Assert.Contains("IRequest", warning.Message);
    }

    [Fact]
    public void FindsShapesInRealPackageStyleSourceWithoutInjectedContracts()
    {
        var files = new[]
        {
            Source("Feature.cs", """
                using System;
                using MediatR;
                using FluentValidation;

                namespace Shop.Application;

                public sealed record PlaceOrder(Guid Id) : IRequest<Guid>;

                public sealed class PlaceOrderHandler : IRequestHandler<PlaceOrder, Guid>
                {
                }

                public sealed class PlaceOrderValidator : AbstractValidator<PlaceOrder>
                {
                }
                """),
        };

        var result = Scan(files);

        var request = Assert.Single(result.Requests);
        Assert.Equal("PlaceOrder", request.Name);
        Assert.Equal("Shop.Application", request.Namespace);
        Assert.Equal("Guid", request.ResponseType);
        Assert.Single(result.Handlers);
        Assert.Single(result.Validators);
        Assert.Empty(result.Checks);
    }

    [Fact]
    public void ResolvesShapesAcrossProjectGroups()
    {
        var files = new[]
        {
            new SourceFile("Domain.cs", """
                using MediatR;
                namespace Shop.Domain;
                public sealed record GetOrder(int Id) : IRequest<string>;
                """, "Shop.Domain"),
            new SourceFile("Application.cs", """
                using MediatR;
                namespace Shop.Application;
                public sealed class GetOrderHandler : IRequestHandler<Shop.Domain.GetOrder, string>
                {
                }
                """, "Shop.Application"),
        };

        var result = Scan(files);

        var request = Assert.Single(result.Requests);
        var handler = Assert.Single(result.Handlers);
        Assert.Equal("Shop.Domain.GetOrder", request.FullName);
        Assert.Equal("GetOrder", handler.RequestName);
        Assert.Equal("Shop.Domain", handler.RequestNamespace);
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


    [Fact]
    public void FindsBehaviorsAndDbContextsWithoutInjectedContracts()
    {
        var files = new[]
        {
            Source("Infrastructure.cs", """
                using MediatR;
                using Microsoft.EntityFrameworkCore;
                using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

                namespace Shop.Infrastructure;

                public sealed class LoggingBehavior<TRequest, TResponse> :
                    IPipelineBehavior<TRequest, TResponse>
                    where TRequest : notnull
                {
                }

                public sealed class Order { }

                public sealed class ShopDbContext : DbContext
                {
                    public DbSet<Order> Orders { get; set; } = null!;
                }

                public sealed class User { }

                public sealed class IdentityContext : IdentityDbContext<User>
                {
                    public DbSet<Order> Orders { get; set; } = null!;
                }
                """),
        };

        var result = Scan(files);

        Assert.Single(result.Behaviors);
        Assert.Equal(2, result.DbContexts.Count);
        Assert.All(result.DbContexts, c => Assert.Single(c.DbSets));
        Assert.Contains(result.DbContexts, c => c.Name == "ShopDbContext");
        Assert.Contains(result.DbContexts, c => c.Name == "IdentityContext");
        Assert.Empty(result.Checks);
    }

    [Fact]
    public void FindsPipelineBehaviorsAndDbSets()
    {
        var files = new[]
        {
            Source("Contracts.cs", Contracts),
            Source("Infrastructure.cs", """
                using MediatR;
                using Microsoft.EntityFrameworkCore;

                namespace Shop.Infrastructure;

                public sealed class LoggingBehavior<TRequest, TResponse> :
                    IPipelineBehavior<TRequest, TResponse>
                {
                }

                public sealed class Order { }

                public sealed class ShopDbContext : DbContext
                {
                    public DbSet<Order> Orders { get; set; } = null!;
                }
                """),
        };

        var result = Scan(files);

        var behavior = Assert.Single(result.Behaviors);
        Assert.Equal("LoggingBehavior", behavior.Name);
        Assert.Equal("Shop.Application", behavior.ProjectName);

        var context = Assert.Single(result.DbContexts);
        Assert.Equal("ShopDbContext", context.Name);
        Assert.Equal("Shop.Infrastructure", context.Namespace);
        var set = Assert.Single(context.DbSets);
        Assert.Equal("Order", set.Entity);
        Assert.Equal("Orders", set.Property);
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
