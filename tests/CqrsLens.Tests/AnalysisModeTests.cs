using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

public sealed class AnalysisModeTests : IDisposable
{
    private readonly string _root;

    public AnalysisModeTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "cqrslens-analysis-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        File.WriteAllText(
            Path.Combine(_root, "Demo.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");

        File.WriteAllText(Path.Combine(_root, "Contracts.cs"), """
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
            """);

        File.WriteAllText(Path.Combine(_root, "Feature.cs"), """
            using System;
            using MediatR;
            using FluentValidation;
            using Microsoft.EntityFrameworkCore;

            namespace Demo;

            public interface ICommand<T> : IRequest<T> { }

            public sealed record PlaceOrder() : ICommand<Guid>;

            public sealed class PlaceOrderHandler :
                IRequestHandler<PlaceOrder, Guid>
            {
            }

            public sealed class PlaceOrderValidator :
                AbstractValidator<PlaceOrder>
            {
            }

            public sealed class LoggingBehavior<TRequest, TResponse> :
                IPipelineBehavior<TRequest, TResponse>
                where TRequest : IRequest<TResponse>
            {
            }

            public sealed class Order { }

            public sealed class DemoDbContext : DbContext
            {
                public DbSet<Order> Orders { get; set; } = null!;
            }
            """);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void RoslynMode_MatchesTextModeForCoreShapes()
    {
        var textResult = Analyzer.Analyze(_root, AnalysisMode.Text);
        var roslynResult = Analyzer.Analyze(_root, AnalysisMode.Roslyn);

        Assert.Equal(
            textResult.Requests.Select(RequestSignature).OrderBy(x => x),
            roslynResult.Requests.Select(RequestSignature).OrderBy(x => x));

        Assert.Equal(
            textResult.Handlers.Select(h => $"{h.FullName}|{h.RequestName}|{h.ProjectName}").OrderBy(x => x),
            roslynResult.Handlers.Select(h => $"{h.FullName}|{h.RequestName}|{h.ProjectName}").OrderBy(x => x));

        Assert.Equal(
            textResult.Validators.Select(v => $"{v.FullName}|{v.TargetType}|{v.ProjectName}").OrderBy(x => x),
            roslynResult.Validators.Select(v => $"{v.FullName}|{v.TargetType}|{v.ProjectName}").OrderBy(x => x));

        Assert.Equal(
            textResult.Behaviors.Select(b => $"{b.Name}|{b.ProjectName}").OrderBy(x => x),
            roslynResult.Behaviors.Select(b => $"{b.Name}|{b.ProjectName}").OrderBy(x => x));

        Assert.Equal(
            textResult.DbContexts.Select(ContextSignature).OrderBy(x => x),
            roslynResult.DbContexts.Select(ContextSignature).OrderBy(x => x));
    }

    private static string RequestSignature(CqrsRequest request)
        => $"{request.FullName}|{request.Kind}|{request.ResponseType}|{request.ViaMarker}|" +
           $"{string.Join(",", request.Handlers)}|{string.Join(",", request.Validators)}";

    private static string ContextSignature(DbContextInfo context)
        => $"{context.Namespace}.{context.Name}|{context.ProjectName}|" +
           string.Join(",", context.DbSets
               .OrderBy(s => s.Property)
               .Select(s => $"{s.Property}:{s.Entity}"));
}
