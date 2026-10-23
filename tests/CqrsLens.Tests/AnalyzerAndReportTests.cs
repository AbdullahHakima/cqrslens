using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

public class AnalyzerAndReportTests
{
    [Theory]
    [InlineData("Shop.Domain", "Domain")]
    [InlineData("Shop.Application", "Application")]
    [InlineData("Shop.Infrastructure", "Infrastructure")]
    [InlineData("Shop.Api", "Api")]
    [InlineData("Shop.Web", "Web")]
    public void LayerClassifier_MatchesNamingConventions(string project, string expected)
    {
        var layer = LayerClassifier.Classify(project, "/src/" + project, "/src");
        Assert.Equal(expected, layer);
    }

    [Fact]
    public void LayerClassifier_FallsBackToTopLevelFolder()
    {
        var layer = LayerClassifier.Classify("Billing", "/src/services/Billing", "/src");
        Assert.Equal("services", layer);
    }

    [Fact]
    public void MermaidId_SanitizesNames()
    {
        Assert.Equal("SampleShop_Application", ReportGenerator.MermaidId("SampleShop.Application"));
        Assert.Equal("n_9lives", ReportGenerator.MermaidId("9lives"));
    }

    [Fact]
    public void Analyze_EndToEnd_LinksHandlersAndValidatorsToRequests()
    {
        var root = Path.Combine(Path.GetTempPath(), "cqrslens-e2e-" + Guid.NewGuid().ToString("N"));
        try
        {
            var appDir = Path.Combine(root, "Shop.Application");
            Directory.CreateDirectory(appDir);
            File.WriteAllText(Path.Combine(appDir, "Shop.Application.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
            File.WriteAllText(Path.Combine(appDir, "Feature.cs"), """
                public sealed record CreateOrderCommand(int CustomerId) : IRequest<Guid>;
                public sealed class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, Guid> { }
                public sealed class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand> { }
                """);

            var result = Analyzer.Analyze(root);

            var request = Assert.Single(result.Requests);
            Assert.Equal(new[] { "CreateOrderCommandHandler" }, request.Handlers);
            Assert.Equal(new[] { "CreateOrderCommandValidator" }, request.Validators);

            var report = ReportGenerator.Generate(result);
            Assert.Contains("```mermaid", report);
            Assert.Contains("req_CreateOrderCommand --> hdl_CreateOrderCommandHandler", report);
            Assert.Contains("val_CreateOrderCommandValidator -.->|validates| req_CreateOrderCommand", report);
            Assert.Contains("| CreateOrderCommand | Command | Guid |", report);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        }
    }
}
