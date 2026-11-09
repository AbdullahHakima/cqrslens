using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// Stage 2 CP1 coverage for method-body call edges: an invocation whose target expression is
/// exactly an injected member. Fail-closed cases (property chain, local, new, property access,
/// ambiguous/unresolved interface, DbContext, self-call) must produce no edge.
/// </summary>
public class ServiceCallEdgeTests
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

    [Fact]
    public void FieldInvocation_ProducesAnEdge()
    {
        var result = Scan(
            """
            namespace App.Domain
            {
                public interface IRepo { void Save(); }
            }

            namespace App.Infra
            {
                public class Repo : App.Domain.IRepo { public void Save() { } }
            }

            namespace App.Application
            {
                using App.Domain;
                using MediatR;

                public record Ping : IRequest<int>;

                public class PingHandler : IRequestHandler<Ping, int>
                {
                    private readonly IRepo _repo;
                    public PingHandler(IRepo repo) { _repo = repo; }
                    public int Handle(Ping request) { _repo.Save(); return 0; }
                }
            }
            """);

        var edge = Assert.Single(result.ServiceCallEdges);
        Assert.Equal("App.Application.PingHandler", edge.From);
        Assert.Equal("App.Domain.IRepo", edge.Via);
        Assert.Equal("Save", edge.Method);
        Assert.Equal("App.Infra.Repo", edge.To);
    }

    [Fact]
    public void PrimaryConstructorParameterInvocation_ProducesAnEdge()
    {
        var result = Scan(
            """
            namespace App.Domain
            {
                public interface IRepo { void Save(); }
            }

            namespace App.Infra
            {
                public class Repo : App.Domain.IRepo { public void Save() { } }
            }

            namespace App.Application
            {
                using App.Domain;
                using MediatR;

                public record Ping : IRequest<int>;

                public class PingHandler(IRepo repo) : IRequestHandler<Ping, int>
                {
                    public int Handle(Ping request) { repo.Save(); return 0; }
                }
            }
            """);

        var edge = Assert.Single(result.ServiceCallEdges);
        Assert.Equal("App.Domain.IRepo", edge.Via);
        Assert.Equal("App.Infra.Repo", edge.To);
    }

    [Fact]
    public void PropertyChain_IsNotAnEdge()
    {
        var result = Scan(
            """
            namespace App.Domain
            {
                public interface IUnitOfWork { void Commit(); }
                public interface IRepo { IUnitOfWork UnitOfWork { get; } void Save(); }
            }

            namespace App.Infra
            {
                public class Repo : App.Domain.IRepo
                {
                    public App.Domain.IUnitOfWork UnitOfWork => null!;
                    public void Save() { }
                }
            }

            namespace App.Application
            {
                using App.Domain;
                using MediatR;

                public record Ping : IRequest<int>;

                public class PingHandler : IRequestHandler<Ping, int>
                {
                    private readonly IRepo _repo;
                    public PingHandler(IRepo repo) { _repo = repo; }
                    public int Handle(Ping request) { _repo.UnitOfWork.Commit(); return 0; }
                }
            }
            """);

        Assert.Empty(result.ServiceCallEdges);
    }

    [Fact]
    public void LocalAndNewConstructedTargets_AreNotEdges()
    {
        var result = Scan(
            """
            namespace App.Domain
            {
                public interface IRepo { void Save(); }
            }

            namespace App.Infra
            {
                public class Repo : App.Domain.IRepo { public void Save() { } }
            }

            namespace App.Application
            {
                using App.Domain;
                using App.Infra;
                using MediatR;

                public record Ping : IRequest<int>;

                public class PingHandler : IRequestHandler<Ping, int>
                {
                    private readonly IRepo _repo;
                    public PingHandler(IRepo repo) { _repo = repo; }
                    public int Handle(Ping request)
                    {
                        var local = new Repo();
                        local.Save();
                        new Repo().Save();
                        _repo.Save();
                        return 0;
                    }
                }
            }
            """);

        // Only the call on the injected member counts; the local and the new-constructed
        // target are not members.
        var edge = Assert.Single(result.ServiceCallEdges);
        Assert.Equal("App.Application.PingHandler", edge.From);
        Assert.Equal("App.Domain.IRepo", edge.Via);
        Assert.Equal("Save", edge.Method);
        Assert.Equal("App.Infra.Repo", edge.To);
    }

    [Fact]
    public void PropertyAccessWithoutInvocation_IsNotAnEdge()
    {
        var result = Scan(
            """
            namespace App.Domain
            {
                public interface IRepo { int Count { get; } void Save(); }
            }

            namespace App.Infra
            {
                public class Repo : App.Domain.IRepo { public int Count => 0; public void Save() { } }
            }

            namespace App.Application
            {
                using App.Domain;
                using MediatR;

                public record Ping : IRequest<int>;

                public class PingHandler : IRequestHandler<Ping, int>
                {
                    private readonly IRepo _repo;
                    public PingHandler(IRepo repo) { _repo = repo; }
                    public int Handle(Ping request) { return _repo.Count; }
                }
            }
            """);

        Assert.Empty(result.ServiceCallEdges);
    }

    [Fact]
    public void AmbiguousInterface_ProducesNoEdge()
    {
        var result = Scan(
            """
            namespace App.Domain
            {
                public interface IRepo { void Save(); }
            }

            namespace App.Infra
            {
                public class RepoOne : App.Domain.IRepo { public void Save() { } }
                public class RepoTwo : App.Domain.IRepo { public void Save() { } }
            }

            namespace App.Application
            {
                using App.Domain;
                using MediatR;

                public record Ping : IRequest<int>;

                public class PingHandler : IRequestHandler<Ping, int>
                {
                    private readonly IRepo _repo;
                    public PingHandler(IRepo repo) { _repo = repo; }
                    public int Handle(Ping request) { _repo.Save(); return 0; }
                }
            }
            """);

        Assert.Empty(result.ServiceCallEdges);
    }

    [Fact]
    public void RegistrationDisambiguates_AndProducesAnEdge()
    {
        var result = Scan(
            """
            namespace App.Domain
            {
                public interface IRepo { void Save(); }
            }

            namespace App.Infra
            {
                public class RepoOne : App.Domain.IRepo { public void Save() { } }
                public class RepoTwo : App.Domain.IRepo { public void Save() { } }
            }

            namespace App.Application
            {
                using App.Domain;
                using MediatR;

                public record Ping : IRequest<int>;

                public class PingHandler : IRequestHandler<Ping, int>
                {
                    private readonly IRepo _repo;
                    public PingHandler(IRepo repo) { _repo = repo; }
                    public int Handle(Ping request) { _repo.Save(); return 0; }
                }
            }

            namespace App
            {
                using App.Application;
                using App.Domain;
                using App.Infra;

                public static class Setup
                {
                    public static void Configure(object services)
                    {
                        services.AddScoped<IRepo, RepoTwo>();
                    }
                }
            }
            """);

        var edge = Assert.Single(result.ServiceCallEdges);
        Assert.Equal("App.Infra.RepoTwo", edge.To);
    }

    [Fact]
    public void UnresolvedInterface_ProducesNoEdge()
    {
        var result = Scan(
            """
            namespace App.Domain
            {
                public interface IRepo { void Save(); }
            }

            namespace App.Application
            {
                using App.Domain;
                using MediatR;

                public record Ping : IRequest<int>;

                public class PingHandler : IRequestHandler<Ping, int>
                {
                    private readonly IRepo _repo;
                    public PingHandler(IRepo repo) { _repo = repo; }
                    public int Handle(Ping request) { _repo.Save(); return 0; }
                }
            }
            """);

        // IRepo has no implementation in the scan: no resolution, no edge.
        Assert.Empty(result.ServiceCallEdges);
    }

    [Fact]
    public void ConcreteTypedMember_ProducesADirectEdge()
    {
        var result = Scan(
            """
            namespace App.Infra
            {
                public class Repo { public void Save() { } }
            }

            namespace App.Application
            {
                using App.Infra;
                using MediatR;

                public record Ping : IRequest<int>;

                public class PingHandler : IRequestHandler<Ping, int>
                {
                    private readonly Repo _repo;
                    public PingHandler(Repo repo) { _repo = repo; }
                    public int Handle(Ping request) { _repo.Save(); return 0; }
                }
            }
            """);

        var edge = Assert.Single(result.ServiceCallEdges);
        Assert.Equal("App.Infra.Repo", edge.Via);
        Assert.Equal("App.Infra.Repo", edge.To);
    }

    [Fact]
    public void DbContextTarget_ProducesNoEdge()
    {
        var result = Scan(
            """
            namespace App.Persistence
            {
                using Microsoft.EntityFrameworkCore;

                public class AppDbContext : DbContext
                {
                    public void DoThing() { }
                }
            }

            namespace App.Application
            {
                using App.Persistence;
                using MediatR;

                public record Ping : IRequest<int>;

                public class PingHandler : IRequestHandler<Ping, int>
                {
                    private readonly AppDbContext _db;
                    public PingHandler(AppDbContext db) { _db = db; }
                    public int Handle(Ping request) { _db.DoThing(); return 0; }
                }
            }
            """);

        Assert.Empty(result.ServiceCallEdges);
    }

    [Fact]
    public void SelfCall_ProducesNoEdge()
    {
        var result = Scan(
            """
            namespace App.Domain
            {
                public interface IFoo { void Do(); }
            }

            namespace App.Infra
            {
                public class Foo : App.Domain.IFoo
                {
                    private readonly App.Domain.IFoo _self;
                    public Foo(App.Domain.IFoo self) { _self = self; }
                    public void Do() { _self.Do(); }
                }
            }
            """);

        // Foo is the chosen implementation of IFoo and is injected (into itself); the call
        // resolves back to Foo, which is a self-call: no edge.
        Assert.Empty(result.ServiceCallEdges);
    }

    [Fact]
    public void RepeatedCalls_DedupeToOneEdge()
    {
        var result = Scan(
            """
            namespace App.Domain
            {
                public interface IRepo { void Save(); }
            }

            namespace App.Infra
            {
                public class Repo : App.Domain.IRepo { public void Save() { } }
            }

            namespace App.Application
            {
                using App.Domain;
                using MediatR;

                public record Ping : IRequest<int>;

                public class PingHandler : IRequestHandler<Ping, int>
                {
                    private readonly IRepo _repo;
                    public PingHandler(IRepo repo) { _repo = repo; }
                    public int Handle(Ping request) { _repo.Save(); _repo.Save(); return 0; }
                }
            }
            """);

        var edge = Assert.Single(result.ServiceCallEdges);
        Assert.Equal("Save", edge.Method);
    }

    [Fact]
    public void GenericClassDeclaration_ResolvesThroughTheIndex()
    {
        var result = Scan(
            """
            namespace App.Domain
            {
                public interface IService { void Run(); }
            }

            namespace App.Infra
            {
                public class GenericService<T> : App.Domain.IService { public void Run() { } }
            }

            namespace App.Application
            {
                using App.Domain;
                using MediatR;

                public record Ping : IRequest<int>;

                public class PingHandler : IRequestHandler<Ping, int>
                {
                    private readonly IService _service;
                    public PingHandler(IService service) { _service = service; }
                    public int Handle(Ping request) { _service.Run(); return 0; }
                }
            }
            """);

        var edge = Assert.Single(result.ServiceCallEdges);
        Assert.Equal("App.Infra.GenericService", edge.To);
    }

    [Fact]
    public void ExternalGenericRegistration_ResolvesToTheWrittenImplementationName()
    {
        var result = Scan(
            """
            namespace App.Application
            {
                using MediatR;

                // Both the interface and its implementation live in the library; only the
                // registration references the (closed generic) implementation.
                public interface IIntegrationEventLogService { void SaveEvent(); }
                public interface IOrderingIntegrationEventService { void AddAndSave(); }

                public class OrderingIntegrationEventService : IOrderingIntegrationEventService
                {
                    private readonly IIntegrationEventLogService _eventLogService;
                    public OrderingIntegrationEventService(IIntegrationEventLogService service)
                    {
                        _eventLogService = service;
                    }

                    public void AddAndSave() { _eventLogService.SaveEvent(); }
                }

                public record Ping : IRequest<int>;

                public class PingHandler : IRequestHandler<Ping, int>
                {
                    private readonly IOrderingIntegrationEventService _svc;
                    public PingHandler(IOrderingIntegrationEventService svc) { _svc = svc; }
                    public int Handle(Ping request) { _svc.AddAndSave(); return 0; }
                }
            }

            namespace App
            {
                using App.Application;

                public static class Setup
                {
                    public static void Configure(object services)
                    {
                        services.AddTransient<IIntegrationEventLogService, IntegrationEventLogService<MyContext>>();
                        services.AddTransient<IOrderingIntegrationEventService, OrderingIntegrationEventService>();
                    }

                    private sealed class MyContext { }
                }
            }
            """);

        // The implementation class is not declared in the scan (library): the index records
        // the written name, and the edge target is that name.
        Assert.Contains(result.ServiceCallEdges, e =>
            e.From == "App.Application.OrderingIntegrationEventService" &&
            e.Method == "SaveEvent" &&
            e.To == "IntegrationEventLogService");
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void TextAndRoslyn_ProduceTheSameEdges(AnalysisMode mode)
    {
        var source = """
            namespace App.Domain
            {
                public interface IRepo { void Save(); }
            }

            namespace App.Infra
            {
                public class Repo : App.Domain.IRepo { public void Save() { } }
            }

            namespace App.Application
            {
                using App.Domain;
                using MediatR;

                public record Ping : IRequest<int>;

                public class PingHandler : IRequestHandler<Ping, int>
                {
                    private readonly IRepo _repo;
                    public PingHandler(IRepo repo) { _repo = repo; }
                    public int Handle(Ping request) { _repo.Save(); return 0; }
                }
            }
            """;

        var result = Scan(source, mode);

        var edge = Assert.Single(result.ServiceCallEdges);
        Assert.Equal("App.Infra.Repo", edge.To);
    }

    private static AnalysisResult Scan(string source, AnalysisMode mode = AnalysisMode.Text)
    {
        using var dir = new TempDir();
        dir.Write("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        dir.Write("Contracts.cs", Contracts);
        dir.Write("App.cs", source);
        return Analyzer.Analyze(dir.Path, mode);
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "cqrslens-sce-" + Guid.NewGuid().ToString("N"));

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
