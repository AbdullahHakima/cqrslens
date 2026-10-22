using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// Regression coverage for #51: FastEndpoints validators derive from a package
/// contract, so the base type is often absent from the scanned source.
/// </summary>
public sealed class FastEndpointsValidatorTests : IDisposable
{
    private readonly string _root;

    public FastEndpointsValidatorTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "cqrslens-fastendpoints-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            Path.Combine(_root, "App.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        WriteFixture();
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private void WriteFixture()
    {
        File.WriteAllText(Path.Combine(_root, "CreateOrder.cs"), """
            using FastEndpoints;
            using MediatR;

            namespace App.Features;

            public sealed record CreateOrderCommand : IRequest;

            public sealed class CreateOrderHandler : IRequestHandler<CreateOrderCommand> { }

            public sealed class CreateOrderValidator : Validator<CreateOrderCommand> { }
            """);
    }

    private static void AssertLinked(AnalysisResult result)
    {
        var request = Assert.Single(result.Requests);
        var validator = Assert.Single(result.Validators);

        Assert.Equal("CreateOrderValidator", validator.Name);
        Assert.Equal("CreateOrderCommand", validator.TargetType);
        Assert.Contains("App.Features.CreateOrderValidator", request.Validators);
        Assert.DoesNotContain(result.Diagnostics, d =>
            d.Code == AnalysisResult.UnresolvedContractCode);
        Assert.DoesNotContain(result.Checks, c =>
            c.Code == ArchitectureChecker.CommandWithoutValidatorCode);
    }

    [Fact]
    public void TextMode_LinksFastEndpointsValidator()
    {
        AssertLinked(Analyzer.Analyze(_root, AnalysisMode.Text));
    }

    [Fact]
    public void RoslynMode_LinksFastEndpointsValidator()
    {
        AssertLinked(Analyzer.Analyze(_root, AnalysisMode.Roslyn));
    }

    [Fact]
    public void StrictAndBaseline_SeeNoValidatorFinding()
    {
        var report = Path.Combine(_root, "ARCHITECTURE.md");
        var baseline = Path.Combine(_root, "baseline.json");

        var write = Run(new[]
        {
            _root, "-o", report, "--write-baseline", baseline,
        });
        Assert.Equal(ExitCodes.Success, write.Code);
        var baselineJson = File.ReadAllText(baseline);
        Assert.DoesNotContain(ArchitectureChecker.CommandWithoutValidatorCode, baselineJson);

        var check = Run(new[]
        {
            _root, "-o", report, "--baseline", baseline, "--strict",
        });
        Assert.Equal(ExitCodes.Success, check.Code);
        Assert.Contains("0 new finding(s)", check.Stdout);
    }

    private static (int Code, string Stdout, string Stderr) Run(string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var code = CliRunner.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }
}
