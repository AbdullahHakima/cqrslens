using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// CP2 coverage for the first service-resolution consumer: handler → DbContext
/// edges resolved through an injected interface, and the additive JSON section.
/// </summary>
public class HandlerDbContextResolvedTests
{
    private const string Contracts = """
        namespace MediatR
        {
            public interface IRequest<out TResponse> { }
            public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse> { }
        }

        namespace Microsoft.EntityFrameworkCore
        {
            public class DbContext { }
            public class DbSet<TEntity> where TEntity : class { }
        }
        """;

    private const string DefaultPersistence = """
        using Microsoft.EntityFrameworkCore;

        namespace App.Persistence;

        public interface IAppDbContext { }

        public class AppDbContext : DbContext, IAppDbContext { }
        """;

    private const string Handler = """
        using MediatR;
        using App.Persistence;

        namespace App.Application;

        public record Ping(int Id) : IRequest<int>;

        public class PingHandler : IRequestHandler<Ping, int>
        {
            public PingHandler(IAppDbContext db) { }

            public int Handle(Ping request) => 0;
        }
        """;

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void InjectedInterfaceResolvingToADbContext_ProducesAResolvedEdge(AnalysisMode mode)
    {
        var result = Scan(
            DefaultPersistence,
            Registration("services.AddScoped<IAppDbContext, AppDbContext>();"),
            mode);

        var edge = Assert.Single(result.ResolvedEdges);
        Assert.Equal(FlowEdgeKind.HandlerToDbContextResolved, edge.Kind);
        Assert.Equal("App.Application.PingHandler", edge.From);
        Assert.Equal("App.Persistence.IAppDbContext", edge.Via);
        Assert.Equal("App.Persistence.AppDbContext", edge.To);
    }

    [Fact]
    public void ResolvedEdge_IsKeptOutOfTheDirectFlowEdges()
    {
        var result = Scan(
            DefaultPersistence,
            Registration("services.AddScoped<IAppDbContext, AppDbContext>();"));

        Assert.Single(result.ResolvedEdges);
        Assert.DoesNotContain(result.FlowEdges, e => e.Kind == FlowEdgeKind.HandlerToDbContextResolved);
        Assert.DoesNotContain(result.FlowEdges, e => e.Kind == FlowEdgeKind.HandlerToDbContext);
    }

    [Fact]
    public void AmbiguousInterface_ProducesNoResolvedEdge()
    {
        var result = Scan(
            DefaultPersistence + """

            public class SecondDbContext : DbContext, IAppDbContext { }
            """,
            Registration(string.Empty));

        // The interface resolves ambiguously, so no edge is drawn.
        Assert.Empty(result.ResolvedEdges);
    }

    [Fact]
    public void RegistrationDisambiguates_AndProducesAResolvedEdge()
    {
        var result = Scan(
            DefaultPersistence + """

            public class SecondDbContext : DbContext, IAppDbContext { }
            """,
            Registration("services.AddScoped<IAppDbContext, SecondDbContext>();"));

        var edge = Assert.Single(result.ResolvedEdges);
        Assert.Equal("App.Persistence.SecondDbContext", edge.To);
    }

    [Fact]
    public void ImplementationThatIsNotADbContext_ProducesNoResolvedEdge()
    {
        var result = Scan(
            """
            namespace App.Persistence;

            public interface IAppDbContext { }

            public class PlainService : IAppDbContext { }
            """,
            Registration(string.Empty));

        // The index resolves, but the implementation is not a DbContext.
        Assert.Empty(result.ResolvedEdges);
    }

    [Fact]
    public void InterfaceWithNoImplementation_ProducesNoResolvedEdge()
    {
        var result = Scan(
            """
            namespace App.Persistence;

            public interface IAppDbContext { }
            """,
            Registration(string.Empty));

        Assert.Empty(result.ResolvedEdges);
    }

    [Fact]
    public void DirectConcreteDependency_IsADirectEdgeNotAResolvedOne()
    {
        var result = Scan(
            DefaultPersistence,
            Registration(string.Empty),
            handlerSource: """
                using MediatR;
                using App.Persistence;

                namespace App.Application;

                public record Ping(int Id) : IRequest<int>;

                public class PingHandler : IRequestHandler<Ping, int>
                {
                    public PingHandler(AppDbContext db) { }

                    public int Handle(Ping request) => 0;
                }
                """);

        Assert.Empty(result.ResolvedEdges);
        Assert.Contains(result.FlowEdges, e => e.Kind == FlowEdgeKind.HandlerToDbContext);
    }

    [Fact]
    public void JsonReport_EmitsTheAdditiveResolvedEdgesSection()
    {
        var result = Scan(
            DefaultPersistence,
            Registration("services.AddScoped<IAppDbContext, AppDbContext>();"));

        var json = JsonReportGenerator.Generate(result);

        Assert.Contains("\"resolvedEdges\"", json);
        Assert.Contains("\"kind\": \"handlerToDbContextResolved\"", json);
        Assert.Contains("\"via\": \"App.Persistence.IAppDbContext\"", json);
        Assert.Contains("\"to\": \"App.Persistence.AppDbContext\"", json);
    }

    [Fact]
    public void JsonReport_OmitsTheSectionWhenThereAreNoResolvedEdges()
    {
        var result = Scan(
            """
            namespace App.Persistence;

            public interface IAppDbContext { }
            """,
            Registration(string.Empty));

        var json = JsonReportGenerator.Generate(result);

        Assert.DoesNotContain("resolvedEdges", json);
    }

    private static string Registration(string body) => $$"""
        using App.Persistence;

        namespace App;

        public static class Setup
        {
            public static void Configure(object services)
            {
                {{body}}
            }
        }
        """;

    private static AnalysisResult Scan(
        string persistenceSource,
        string registrationSource,
        AnalysisMode mode = AnalysisMode.Text,
        string? handlerSource = null)
    {
        using var dir = new TempDir();
        dir.Write("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        dir.Write("Contracts.cs", Contracts);
        dir.Write("Persistence.cs", persistenceSource);
        dir.Write("Handler.cs", handlerSource ?? Handler);
        dir.Write("Setup.cs", registrationSource);
        return Analyzer.Analyze(dir.Path, mode);
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "cqrslens-hdbr-" + Guid.NewGuid().ToString("N"));

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
}
