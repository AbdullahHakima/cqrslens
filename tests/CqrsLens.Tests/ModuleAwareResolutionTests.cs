using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// Regression coverage for #52: same-named contracts in different modules must
/// resolve through each file's namespace/using directives, and genuine
/// ambiguity must stay visible as an informational diagnostic.
/// </summary>
public sealed class ModuleAwareResolutionTests : IDisposable
{
    private readonly string _root;

    public ModuleAwareResolutionTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "cqrslens-module-resolution-" + Guid.NewGuid().ToString("N"));
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
    public void ModuleLocalContracts_ResolveThroughUsingDirectives()
    {
        WriteFile("Modules/Alpha/Contracts.cs", """
            namespace Modules.Alpha.Contracts;

            public interface ICommand<T> : IRequest<T> { }
            public interface ICommandHandler<T> : IRequestHandler<T> { }
            """);
        WriteFile("Modules/Beta/Contracts.cs", """
            namespace Modules.Beta.Contracts;

            public interface ICommand<T> : IRequest<T> { }
            public interface ICommandHandler<T> : IRequestHandler<T> { }
            """);
        WriteFile("Modules/Alpha/Feature.cs", """
            using Modules.Alpha.Contracts;

            namespace Modules.Alpha.Features;

            public sealed record CreateAlphaCommand : ICommand<Guid>;
            public sealed class CreateAlphaCommandHandler : ICommandHandler<CreateAlphaCommand> { }
            """);
        WriteFile("Modules/Beta/Feature.cs", """
            using Modules.Beta.Contracts;

            namespace Modules.Beta.Features;

            public sealed record CreateBetaCommand : ICommand<Guid>;
            public sealed class CreateBetaCommandHandler : ICommandHandler<CreateBetaCommand> { }
            """);

        var result = Analyzer.Analyze(_root, new HashSet<string> { "ICommand" });

        Assert.Equal(2, result.Requests.Count);
        var alpha = result.Requests.Single(r => r.Name == "CreateAlphaCommand");
        var beta = result.Requests.Single(r => r.Name == "CreateBetaCommand");
        Assert.Equal(new[] { "Modules.Alpha.Features.CreateAlphaCommandHandler" }, alpha.Handlers);
        Assert.Equal(new[] { "Modules.Beta.Features.CreateBetaCommandHandler" }, beta.Handlers);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == AnalysisDiagnosticSeverity.Warning);
    }

    [Fact]
    public void GlobalUsingInProject_ResolvesContractInAnotherFile()
    {
        WriteFile("Modules/Alpha/Contracts.cs", """
            namespace Modules.Alpha.Contracts;

            public interface IQuery<T> : IRequest<T> { }
            """);
        WriteFile("GlobalUsings.cs", "global using Modules.Alpha.Contracts;");
        WriteFile("Modules/Alpha/Feature.cs", """
            namespace Modules.Alpha.Features;

            public sealed record GetAlphaQuery : IQuery<int>;
            """);

        var result = Analyzer.Analyze(_root, new HashSet<string> { "IQuery" });

        var request = Assert.Single(result.Requests);
        Assert.Equal("GetAlphaQuery", request.Name);
        Assert.Equal("Modules.Alpha.Features", request.Namespace);
    }

    [Fact]
    public void NamespaceAlias_ResolvesQualifiedContract()
    {
        WriteFile("Modules/Alpha/Contracts.cs", """
            namespace Modules.Alpha.Contracts;

            public interface ICommand<T> : IRequest<T> { }
            """);
        WriteFile("Modules/Alpha/Feature.cs", """
            using Contracts = Modules.Alpha.Contracts;

            namespace Modules.Alpha.Features;

            public sealed record AliasCommand : Contracts.ICommand<int>;
            """);

        var result = Analyzer.Analyze(_root, new HashSet<string> { "ICommand" });

        var request = Assert.Single(result.Requests);
        Assert.Equal("AliasCommand", request.Name);
        Assert.Equal("int", request.ResponseType);
    }

    [Fact]
    public void HandlerUsingDirective_PicksTheImportedSameNamedRequest()
    {
        WriteFile("Alpha/Requests.cs", """
            namespace Alpha.Contracts;
            public sealed record PingCommand : IRequest<int>;
            """);
        WriteFile("Beta/Requests.cs", """
            namespace Beta.Contracts;
            public sealed record PingCommand : IRequest<string>;
            """);
        WriteFile("Handlers/PingHandler.cs", """
            using Alpha.Contracts;

            namespace Handlers;

            public sealed class PingHandler : IRequestHandler<PingCommand, int> { }
            """);

        var result = Analyzer.Analyze(_root);

        var alpha = result.Requests.Single(r => r.Namespace == "Alpha.Contracts");
        var beta = result.Requests.Single(r => r.Namespace == "Beta.Contracts");
        Assert.Equal(new[] { "Handlers.PingHandler" }, alpha.Handlers);
        Assert.Empty(beta.Handlers);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == AnalysisResult.AmbiguousLinkCode);
    }

    [Fact]
    public void AmbiguousMarkerContract_IsInformationalAndIsNotGuessed()
    {
        WriteFile("Contracts/Alpha.cs", """
            namespace Alpha.Contracts;
            public interface ICommand<T> : IRequest<T> { }
            """);
        WriteFile("Contracts/Beta.cs", """
            namespace Beta.Contracts;
            public interface ICommand<T> : IRequest<T> { }
            """);
        WriteFile("Feature.cs", """
            namespace App.Features;
            public sealed record DoThing : ICommand<int>;
            """);

        var result = Analyzer.Analyze(_root, new HashSet<string> { "ICommand" });

        Assert.Empty(result.Requests);
        var diagnostic = Assert.Single(
            result.Diagnostics,
            d => d.Code == AnalysisResult.AmbiguousContractCode);
        Assert.Equal(AnalysisDiagnosticSeverity.Info, diagnostic.Severity);
        Assert.Contains("ICommand", diagnostic.Message);
        Assert.Contains("Alpha.Contracts.ICommand", diagnostic.Message);
        Assert.Contains("Beta.Contracts.ICommand", diagnostic.Message);
        Assert.True(result.CanRunStrictChecks);

        Assert.Contains("AmbiguousContract", SummaryGenerator.Generate(result));
        Assert.Contains("AmbiguousContract", SarifReportGenerator.Generate(result));
    }

    [Fact]
    public void ExternalHandlerWrapper_IsInformationalInsteadOfSilent()
    {
        WriteFile("Feature.cs", """
            namespace App;

            public sealed record DoThingCommand : IRequest<int>;
            public sealed class DoThingHandler : IExternalCommandHandler<DoThingCommand> { }
            """);

        var result = Analyzer.Analyze(_root);

        var diagnostic = Assert.Single(
            result.Diagnostics,
            d => d.Code == AnalysisResult.UnresolvedContractCode);
        Assert.Equal(AnalysisDiagnosticSeverity.Info, diagnostic.Severity);
        Assert.Contains("IExternalCommandHandler", diagnostic.Message);
        Assert.DoesNotContain(result.Handlers, h => h.Name == "DoThingHandler");
    }
}
