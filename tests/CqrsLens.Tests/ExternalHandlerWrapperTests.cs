using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// Regression coverage for #54: source-declared handler wrappers are followed
/// automatically, while wrappers supplied by an external package need explicit
/// handler roots. Default scans stay unchanged.
/// </summary>
public sealed class ExternalHandlerWrapperTests : IDisposable
{
    private readonly string _root;

    public ExternalHandlerWrapperTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "cqrslens-external-handlers-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            Path.Combine(_root, "App.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <Using Include="MediatR" />
              </ItemGroup>
            </Project>
            """);
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

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void SourceDeclaredWrapper_IsFollowedWithoutConfiguration(AnalysisMode mode)
    {
        WriteFile("Feature.cs", """
            namespace App.Features;

            public interface ICommandHandler<TCommand> : IRequestHandler<TCommand> { }
            public sealed record PingCommand : IRequest<int>;
            public sealed class PingHandler : ICommandHandler<PingCommand> { }
            """);

        var result = Analyzer.Analyze(_root, mode);

        var request = Assert.Single(result.Requests);
        var handler = Assert.Single(result.Handlers);
        Assert.Equal("PingCommand", handler.RequestName);
        Assert.Contains("App.Features.PingHandler", request.Handlers);
        Assert.DoesNotContain(result.Checks, c => c.Code == ArchitectureChecker.MissingHandlerCode);
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void DefaultScan_LeavesExternalWrapperOpaque(AnalysisMode mode)
    {
        WriteFile("Feature.cs", """
            using Ardalis.SharedKernel;

            namespace App.Features;

            public sealed record CreateContributorCommand : ICommand<Result<int>>;
            public sealed class CreateContributorHandler
                : ICommandHandler<CreateContributorCommand, Result<int>> { }
            """);

        var result = Analyzer.Analyze(
            _root,
            mode,
            new AnalysisOptions
            {
                Markers = new HashSet<string> { "ICommand" },
            });

        Assert.Single(result.Requests);
        Assert.Empty(result.Handlers);
        var diagnostic = Assert.Single(
            result.Diagnostics,
            d => d.Code == AnalysisResult.UnresolvedContractCode);
        Assert.Contains("ICommandHandler", diagnostic.Message);
        Assert.Contains("--handler-markers", diagnostic.Message);
        Assert.Contains(result.Checks, c =>
            c.Code == ArchitectureChecker.MissingHandlerCode);
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void ConfiguredExternalWrapper_LinksArdalisStyleHandler(AnalysisMode mode)
    {
        WriteFile("Feature.cs", """
            using Ardalis.SharedKernel;

            namespace App.Features;

            public sealed record CreateContributorCommand : ICommand<Result<int>>;
            public sealed class CreateContributorHandler
                : ICommandHandler<CreateContributorCommand, Result<int>> { }
            """);

        var result = Analyzer.Analyze(
            _root,
            mode,
            new AnalysisOptions
            {
                Markers = new HashSet<string> { "ICommand" },
                HandlerMarkers = new HashSet<string> { "ICommandHandler" },
            });

        var request = Assert.Single(result.Requests);
        Assert.Equal("ICommand", request.ViaMarker);
        Assert.Equal("Result<int>", request.ResponseType);
        var handler = Assert.Single(result.Handlers);
        Assert.Equal("CreateContributorCommand", handler.RequestName);
        Assert.Equal("App.Features", handler.RequestNamespace);
        Assert.Contains("App.Features.CreateContributorHandler", request.Handlers);
        Assert.DoesNotContain(result.Checks, c => c.Code == ArchitectureChecker.MissingHandlerCode);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == AnalysisResult.UnresolvedContractCode);
    }
}
