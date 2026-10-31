using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>Coverage for analysis metadata, diagnostics and strict-check confidence (#29).</summary>
public sealed class AnalysisDiagnosticsTests : IDisposable
{
    private readonly string _root;

    public AnalysisDiagnosticsTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "cqrslens-diagnostics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            Path.Combine(_root, "App.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void TextScanner_AttachesRootRelativeLocationsToEveryShape()
    {
        File.WriteAllText(Path.Combine(_root, "Feature.cs"), """
            namespace Demo;

            public sealed record AddCommand : IRequest<int>;

            public sealed class AddCommandHandler : IRequestHandler<AddCommand, int>
            {
            }

            public sealed class AddCommandValidator : AbstractValidator<AddCommand>
            {
            }

            public sealed class LoggingBehavior<TRequest, TResponse>
                : IPipelineBehavior<TRequest, TResponse>
            {
            }

            public sealed class ShopDbContext : DbContext
            {
                public DbSet<Order> Orders { get; set; } = null!;
            }
            """);

        var result = Analyzer.Analyze(_root);

        Assert.Equal("text", result.ScannerName);
        AssertLocation(result.Requests[0].Location, 3);
        AssertLocation(result.Handlers[0].Location, 5);
        AssertLocation(result.Validators[0].Location, 9);
        AssertLocation(result.Behaviors[0].Location, 13);
        AssertLocation(result.DbContexts[0].Location, 18);
        Assert.All(
            result.Requests.Concat<object>(result.Handlers)
                .Concat(result.Validators)
                .Concat(result.Behaviors)
                .Concat(result.DbContexts),
            shape => Assert.NotNull(shape switch
            {
                CqrsRequest request => request.Location,
                HandlerInfo handler => handler.Location,
                ValidatorInfo validator => validator.Location,
                BehaviorInfo behavior => behavior.Location,
                DbContextInfo context => context.Location,
                _ => null,
            }));
    }

    [Fact]
    public void RoslynScanner_AttachesLocationsToSampleShopShapes()
    {
        var root = Path.Combine(FindRepositoryRoot(), "samples", "SampleShop");
        var result = Analyzer.Analyze(root, AnalysisMode.Roslyn);

        Assert.Equal("roslyn", result.ScannerName);
        Assert.NotEmpty(result.Requests);
        Assert.All(result.Requests, request =>
        {
            Assert.NotNull(request.Location);
            Assert.EndsWith(".cs", request.Location!.File, StringComparison.OrdinalIgnoreCase);
            Assert.False(Path.IsPathRooted(request.Location.File));
            Assert.True(request.Location.Line > 0);
            Assert.True(request.Location.Column > 0);
        });
        Assert.All(result.Handlers, handler => Assert.NotNull(handler.Location));
        Assert.All(result.Validators, validator => Assert.NotNull(validator.Location));
        Assert.All(result.Behaviors, behavior => Assert.NotNull(behavior.Location));
        Assert.All(result.DbContexts, context => Assert.NotNull(context.Location));
    }

    [Fact]
    public void Analyzer_UsesTheSuppliedTimestampInBothReports()
    {
        File.WriteAllText(Path.Combine(_root, "Ping.cs"), """
            namespace Demo;

            public sealed record PingQuery : IRequest<int>;
            public sealed class PingQueryHandler : IRequestHandler<PingQuery, int>
            {
            }
            """);

        var generatedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var result = Analyzer.Analyze(_root, AnalysisMode.Text, generatedAt: generatedAt);

        Assert.Equal(generatedAt, result.GeneratedAt);
        Assert.Equal("text", result.ScannerName);

        var md = ReportGenerator.Generate(result);
        Assert.Contains("2026-01-02 03:04:05 UTC", md);
        Assert.Contains("text scanner", md);

        var html = HtmlReportGenerator.Generate(result);
        Assert.Contains("2026-01-02 03:04:05 UTC", html);
        Assert.Contains("text scanner", html);
    }

    [Fact]
    public void Reports_RenderDiagnosticsAndEscapeHtml()
    {
        var result = new AnalysisResult { RootPath = _root };
        result.Diagnostics.Add(new AnalysisDiagnostic(
            "BrokenSource",
            AnalysisDiagnosticSeverity.Warning,
            "Could not parse <thing>.",
            new SourceLocation("App/Broken.cs", 4, 7),
            "Demo.App"));

        var md = ReportGenerator.Generate(result);
        Assert.Contains("## Analysis Diagnostics", md);
        Assert.Contains("BrokenSource", md);
        Assert.Contains("App/Broken.cs:4:7", md);

        var html = HtmlReportGenerator.Generate(result);
        Assert.Contains("Analysis Diagnostics", html);
        Assert.Contains("Could not parse &lt;thing&gt;.", html);
        Assert.Contains("App/Broken.cs:4:7", html);
    }

    [Fact]
    public void AmbiguousLinks_AreDiagnosedAndDisableStrictConfidence()
    {
        File.WriteAllText(Path.Combine(_root, "Requests.cs"), """
            namespace Alpha
            {
                public sealed record PingCommand : IRequest<int>;
            }

            namespace Beta
            {
                public sealed record PingCommand : IRequest<int>;
            }
            """);
        File.WriteAllText(Path.Combine(_root, "Handler.cs"), """
            namespace Handlers;
            public sealed class PingCommandHandler : IRequestHandler<PingCommand, int>
            {
            }
            """);

        var result = Analyzer.Analyze(_root);

        Assert.Equal(2, result.Requests.Count);
        Assert.Single(result.Handlers);
        var diagnostic = Assert.Single(
            result.Diagnostics,
            d => d.Code == AnalysisResult.AmbiguousLinkCode);
        Assert.Equal(AnalysisDiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("matches 2 requests", diagnostic.Message);
        Assert.False(result.CanRunStrictChecks);
        Assert.Contains(result.Checks, c =>
            c.Code == "MissingHandler" && c.Severity == CheckSeverity.Error);
    }

    [Fact]
    public void CompleteScan_AllowsStrictChecks()
    {
        File.WriteAllText(Path.Combine(_root, "Ping.cs"), """
            namespace Demo;

            public sealed record PingQuery : IRequest<int>;
            public sealed class PingQueryHandler : IRequestHandler<PingQuery, int>
            {
            }
            """);

        var result = Analyzer.Analyze(_root);

        Assert.True(result.CanRunStrictChecks);
        Assert.DoesNotContain(result.Diagnostics, d =>
            d.Code == AnalysisResult.SourceFileSkippedCode);
    }

    private static void AssertLocation(SourceLocation? location, int line)
    {
        Assert.NotNull(location);
        Assert.Equal("Feature.cs", location!.File);
        Assert.Equal(line, location.Line);
        Assert.True(location.Column > 0);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CqrsLens.sln")))
                return directory.FullName;
        }

        throw new InvalidOperationException("Could not find CqrsLens.sln above the test output directory.");
    }
}
