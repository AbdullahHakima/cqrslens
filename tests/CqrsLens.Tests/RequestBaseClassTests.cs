using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// Regression coverage for #53: request discovery follows source-declared base
/// classes, including generic response substitution, without turning arbitrary
/// DTO base classes into requests.
/// </summary>
public sealed class RequestBaseClassTests : IDisposable
{
    private readonly string _root;

    public RequestBaseClassTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "cqrslens-request-base-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            Path.Combine(_root, "App.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private void WriteFile(string relative, string code)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, code);
    }

    [Fact]
    public void QueryBaseClass_IsDiscoveredAndKeepsItsResponseType()
    {
        WriteFile("Contracts.cs", """
            namespace App.Contracts;

            public interface IQuery<TResult> : IRequest<TResult> { }
            public abstract class QueryBase<TResult> : IQuery<TResult> { }
            public abstract class EntityBase { }
            """);
        WriteFile("Features/GetThingQuery.cs", """
            namespace App.Features;

            public sealed class GetThingQuery : QueryBase<ThingDto> { }
            public sealed class ThingDto : EntityBase { }
            """);

        var result = Analyzer.Analyze(_root);

        var request = Assert.Single(result.Requests);
        Assert.Equal("GetThingQuery", request.Name);
        Assert.Equal("App.Features", request.Namespace);
        Assert.Equal("ThingDto", request.ResponseType);
        Assert.DoesNotContain(result.Requests, r => r.Name == "ThingDto");
    }

    [Fact]
    public void ConfiguredMarkerThroughCustomBase_IsDiscovered()
    {
        WriteFile("Contracts.cs", """
            namespace App.Contracts;

            public interface ICommand : IRequest { }
            public abstract class CommandBase : ICommand { }
            """);
        WriteFile("Features/DoThingCommand.cs", """
            namespace App.Features;

            public sealed class DoThingCommand : CommandBase { }
            """);

        var result = Analyzer.Analyze(_root, new HashSet<string> { "ICommand" });

        var request = Assert.Single(result.Requests);
        Assert.Equal("DoThingCommand", request.Name);
        Assert.Equal("ICommand", request.ViaMarker);
        Assert.Null(request.ResponseType);
    }

    [Fact]
    public void MultiLevelBaseChain_SubstitutesGenericResponse()
    {
        WriteFile("Contracts.cs", """
            namespace App.Contracts;

            public interface IQuery<TResult> : IRequest<TResult> { }
            public abstract class QueryBase<TResult> : IQuery<TResult> { }
            public abstract class PagedQueryBase<TResult> : QueryBase<TResult> { }
            """);
        WriteFile("Features/GetPageQuery.cs", """
            namespace App.Features;

            public sealed class GetPageQuery : PagedQueryBase<PageDto> { }
            public sealed class PageDto { }
            """);

        var result = Analyzer.Analyze(_root);

        var request = Assert.Single(result.Requests);
        Assert.Equal("GetPageQuery", request.Name);
        Assert.Equal("PageDto", request.ResponseType);
    }

    [Fact]
    public void DirectOverloadedMarkers_AreResolvedByArity()
    {
        WriteFile("Contracts.cs", """
            namespace App.Contracts;

            public interface ICommand : IRequest { }
            public interface ICommand<TResult> : IRequest<TResult> { }
            """);
        WriteFile("Features/Commands.cs", """
            namespace App.Features;

            public sealed class DoThingCommand : Contracts.ICommand { }
            public sealed class CreateThingCommand : Contracts.ICommand<Guid> { }
            """);

        var result = Analyzer.Analyze(_root, new HashSet<string> { "ICommand" });

        Assert.Equal(2, result.Requests.Count);
        Assert.Null(result.Requests.Single(r => r.Name == "DoThingCommand").ResponseType);
        Assert.Equal(
            "Guid",
            result.Requests.Single(r => r.Name == "CreateThingCommand").ResponseType);
    }

    [Fact]
    public void GenericAndNonGenericBases_AreResolvedByArity()
    {
        WriteFile("Contracts.cs", """
            namespace App.Contracts;

            public interface ICommand : IRequest { }
            public interface ICommand<TResult> : IRequest<TResult> { }
            public abstract class CommandBase : ICommand { }
            public abstract class CommandBase<TResult> : ICommand<TResult> { }
            """);
        WriteFile("Features/Commands.cs", """
            namespace App.Features;

            public sealed class DoThingCommand : CommandBase { }
            public sealed class CreateThingCommand : CommandBase<Guid> { }
            """);

        var result = Analyzer.Analyze(_root, new HashSet<string> { "ICommand" });

        Assert.Equal(2, result.Requests.Count);
        var doThing = result.Requests.Single(r => r.Name == "DoThingCommand");
        var createThing = result.Requests.Single(r => r.Name == "CreateThingCommand");
        Assert.Null(doThing.ResponseType);
        Assert.Equal("Guid", createThing.ResponseType);
    }

    [Fact]
    public void HandlerWrapperWithOverloadedContracts_ResolvesTheRequestArgument()
    {
        WriteFile("Contracts.cs", """
            namespace App.Contracts;

            public interface ICommandHandler<TCommand> : IRequestHandler<TCommand> { }
            public interface ICommandHandler<TCommand, TResult> : IRequestHandler<TCommand, TResult> { }
            """);
        WriteFile("Features/DoThingCommand.cs", """
            namespace App.Features;

            public sealed record DoThingCommand : IRequest<int>;
            public sealed class DoThingHandler : App.Contracts.ICommandHandler<DoThingCommand, int> { }
            """);

        var result = Analyzer.Analyze(_root);

        var handler = Assert.Single(result.Handlers);
        Assert.Equal("DoThingCommand", handler.RequestName);
        var request = Assert.Single(result.Requests);
        Assert.Equal(new[] { "App.Features.DoThingHandler" }, request.Handlers);
    }

    [Fact]
    public void DtoBaseClass_IsNotABroadenedRequest()
    {
        WriteFile("Contracts.cs", """
            namespace App.Contracts;

            public abstract class EntityBase { }
            public abstract class AggregateRoot : EntityBase { }
            """);
        WriteFile("Features/ProductDto.cs", """
            namespace App.Features;

            public sealed class ProductDto : AggregateRoot { }
            public sealed class ProductQuery : AggregateRoot { }
            """);

        var result = Analyzer.Analyze(_root);

        Assert.Empty(result.Requests);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == AnalysisResult.UnresolvedContractCode);
    }

    [Fact]
    public void UnresolvedRequestBase_IsInformationalInsteadOfSilent()
    {
        WriteFile("Features/DoThingCommand.cs", """
            namespace App.Features;

            public sealed class DoThingCommand : ExternalCommandBase { }
            """);

        var result = Analyzer.Analyze(_root);

        Assert.Empty(result.Requests);
        var diagnostic = Assert.Single(
            result.Diagnostics,
            d => d.Code == AnalysisResult.UnresolvedContractCode);
        Assert.Equal(AnalysisDiagnosticSeverity.Info, diagnostic.Severity);
        Assert.Contains("ExternalCommandBase", diagnostic.Message);
        Assert.True(result.CanRunStrictChecks);
    }

    [Fact]
    public void AmbiguousRequestBase_IsInformationalAndNotGuessed()
    {
        WriteFile("Alpha/QueryBase.cs", """
            namespace Alpha.Contracts;
            public interface IQuery<T> : IRequest<T> { }
            public abstract class QueryBase<T> : IQuery<T> { }
            """);
        WriteFile("Beta/QueryBase.cs", """
            namespace Beta.Contracts;
            public interface IQuery<T> : IRequest<T> { }
            public abstract class QueryBase<T> : IQuery<T> { }
            """);
        WriteFile("Features/GetThingQuery.cs", """
            namespace App.Features;
            public sealed class GetThingQuery : QueryBase<ThingDto> { }
            public sealed class ThingDto { }
            """);

        var result = Analyzer.Analyze(_root);

        Assert.Empty(result.Requests);
        var diagnostic = Assert.Single(
            result.Diagnostics,
            d => d.Code == AnalysisResult.AmbiguousContractCode);
        Assert.Equal(AnalysisDiagnosticSeverity.Info, diagnostic.Severity);
        Assert.Contains("Alpha.Contracts.QueryBase", diagnostic.Message);
        Assert.Contains("Beta.Contracts.QueryBase", diagnostic.Message);
    }
}
