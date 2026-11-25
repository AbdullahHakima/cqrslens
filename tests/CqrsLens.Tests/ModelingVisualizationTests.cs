using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// Verification coverage for Phase B visual modeling outputs (<c>md</c>, <c>html</c>,
/// zero-JS CSP <c>svg</c>, and directory batch <c>--tdd-check</c>), CLI strict gates,
/// <see cref="MermaidSvgRenderer"/> parse compatibility, and XSS escaping.
/// </summary>
public sealed class ModelingVisualizationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "cqrslens-phase-b-" + Guid.NewGuid().ToString("N"));

    public ModelingVisualizationTests()
    {
        Directory.CreateDirectory(_root);
        WriteSampleSolution();
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private void WriteFile(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private void WriteSampleSolution()
    {
        WriteFile("Shop.Domain/Shop.Domain.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        WriteFile("Shop.Application/Shop.Application.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="..\Shop.Domain\Shop.Domain.csproj" />
              </ItemGroup>
            </Project>
            """);
        WriteFile("Shop.Infrastructure/Shop.Infrastructure.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="..\Shop.Application\Shop.Application.csproj" />
              </ItemGroup>
            </Project>
            """);
        WriteFile("Shop.Domain/Contracts.cs", """
            namespace MediatR
            {
                public interface IRequest<out TResponse> { }
                public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse> { }
            }
            """);
        WriteFile("Shop.Application/CreateOrder.cs", """
            using MediatR;
            namespace Shop.Application;
            public sealed record CreateOrderCommand(string Customer) : IRequest<int>;
            public sealed class CreateOrderHandler : IRequestHandler<CreateOrderCommand, int> { }
            """);
    }

    private static (int Code, string Stdout, string Stderr) Run(params string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var code = CliRunner.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public void Proposal_RendersMarkdownHtmlAndZeroJsSvg_AndSupportsStrictGate()
    {
        WriteFile("bad-proposal.json", """
            {
              "projects": [{"name": "Shop.Payments", "layer": "Application"}],
              "references": [
                {"from": "Shop.Payments", "to": "Shop.Domain", "operation": "add"},
                {"from": "Shop.Domain", "to": "Shop.Infrastructure", "operation": "add"}
              ]
            }
            """);
        var proposalPath = Path.Combine(_root, "bad-proposal.json");

        var md = Run(_root, "--proposal", proposalPath, "--format", "md", "--strict");
        Assert.Equal(ExitCodes.StrictFailure, md.Code);
        Assert.Contains("# Architecture Proposal Evaluation", md.Stdout);
        Assert.Contains("```mermaid", md.Stdout);
        Assert.Contains("Coupling & Instability Metrics", md.Stdout);
        Assert.Contains("Blast Radius", md.Stdout);

        var htmlSvgOut = Path.Combine(_root, "out", "proposal.html");
        var svgRun = Run(_root, "--proposal", proposalPath, "--render", "svg", "-o", htmlSvgOut);
        Assert.Equal(ExitCodes.Success, svgRun.Code);
        var html = File.ReadAllText(htmlSvgOut);
        Assert.Contains("Content-Security-Policy", html);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<svg", html);

        var htmlMermaid = Run(_root, "--proposal", proposalPath, "--format", "html");
        Assert.Equal(ExitCodes.Success, htmlMermaid.Code);
        Assert.Contains("pre class=\"mermaid\"", htmlMermaid.Stdout);
        Assert.Contains("mermaid.min.js", htmlMermaid.Stdout);

        // Verify emitted Mermaid parses through MermaidSvgRenderer without fallback errors.
        var scan = Analyzer.Analyze(_root);
        var eval = ArchitectureProposalEvaluator.Evaluate(
            scan,
            [new("Shop.Payments", "Application")],
            [new("Shop.Domain", "Shop.Infrastructure")]);
        var mermaid = ProposalDiagramBuilder.BuildMermaid(scan, eval)!;
        var svg = MermaidSvgRenderer.Render(mermaid);
        Assert.Contains("<svg", svg);
        Assert.Contains("Shop.Payments", svg);
    }

    [Fact]
    public void Impact_RendersMarkdownAndZeroJsSvg_WithHotspots()
    {
        var md = Run(_root, "--impact", "CreateOrderCommand", "--impact-kind", "request", "--format", "md");
        Assert.Equal(ExitCodes.Success, md.Code);
        Assert.Contains("# Change Impact Report", md.Stdout);
        Assert.Contains("```mermaid", md.Stdout);
        Assert.Contains("Uncovered Hotspots", md.Stdout);

        var svgHtml = Run(_root, "--impact", "CreateOrderCommand", "--format", "html", "--render", "svg");
        Assert.Equal(ExitCodes.Success, svgHtml.Code);
        Assert.Contains("Content-Security-Policy", svgHtml.Stdout);
        Assert.DoesNotContain("<script", svgHtml.Stdout, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<svg", svgHtml.Stdout);

        var scan = Analyzer.Analyze(_root);
        var report = ChangeImpactAnalyzer.Analyze(scan, "CreateOrderCommand", "request");
        var svg = MermaidSvgRenderer.Render(ImpactDiagramBuilder.BuildMermaid(report)!);
        Assert.Contains("<svg", svg);
    }

    [Fact]
    public void SliceDesign_AndTddCheck_SupportDirectoryBatchVisualOutputsAndStrictExit()
    {
        WriteFile("specs/create-order.slice.json", """
            {
              "schemaVersion": 1,
              "project": "Shop.Application",
              "request": "Shop.Application.CreateOrderCommand",
              "kind": "Command",
              "responseType": "int",
              "handler": "Shop.Application.CreateOrderHandler",
              "validator": null,
              "endpoint": null,
              "scenarios": [
                {"name": "creates order", "given": "valid input", "when": "handled", "then": "returns id"}
              ]
            }
            """);
        WriteFile("specs/nested/missing-order.slice.json", """
            {
              "schemaVersion": 1,
              "project": "Shop.Application",
              "request": "Shop.Application.CancelOrderCommand",
              "kind": "Command",
              "responseType": null,
              "handler": "Shop.Application.CancelOrderHandler",
              "validator": null,
              "endpoint": null,
              "scenarios": [
                {"name": "cancels order", "given": "existing id", "when": "handled", "then": "cancelled"}
              ]
            }
            """);

        var specsDir = Path.Combine(_root, "specs");
        var sliceBatchMd = Run(_root, "--slice-design", specsDir, "--format", "md");
        Assert.Equal(ExitCodes.Success, sliceBatchMd.Code);
        Assert.Contains("# Slice Design Batch Verification", sliceBatchMd.Stdout);
        Assert.Contains("create-order.slice.json", sliceBatchMd.Stdout);
        Assert.Contains("missing-order.slice.json", sliceBatchMd.Stdout);

        var sliceSingleSvg = Run(_root, "--slice-design", Path.Combine(specsDir, "create-order.slice.json"), "--format", "html", "--render", "svg");
        Assert.Equal(ExitCodes.Success, sliceSingleSvg.Code);
        Assert.DoesNotContain("<script", sliceSingleSvg.Stdout, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<svg", sliceSingleSvg.Stdout);

        var tddBatchJson = Run(_root, "--tdd-check", specsDir, "--strict");
        Assert.Equal(ExitCodes.StrictFailure, tddBatchJson.Code);
        using var jsonDoc = JsonDocument.Parse(tddBatchJson.Stdout);
        Assert.Equal(2, jsonDoc.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(0, jsonDoc.RootElement.GetProperty("verified").GetInt32());
        Assert.Equal("spec_draft", jsonDoc.RootElement.GetProperty("phase").GetString());

        var tddBatchMd = Run(_root, "--tdd-check", specsDir, "--format", "md");
        Assert.Equal(ExitCodes.Success, tddBatchMd.Code);
        Assert.Contains("# Closed-Loop TDD Batch Report", tddBatchMd.Stdout);
        Assert.Contains("TDD Gate Pipeline", tddBatchMd.Stdout);

        var tddBatchSvg = Run(_root, "--tdd-check", specsDir, "--format", "html", "--render", "svg");
        Assert.Equal(ExitCodes.Success, tddBatchSvg.Code);
        Assert.DoesNotContain("<script", tddBatchSvg.Stdout, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<svg", tddBatchSvg.Stdout);
    }

    [Fact]
    public void ModelingVisualRenderers_EscapeUntrustedXssPayloads()
    {
        var malicious = new ArchitectureProposalEvaluation(
            "conflicts",
            1,
            [new("<script>alert(1)</script>", "Application")],
            [],
            [new("TestCode", "error", "Bad <script>x</script>", "Fix <img src=x onerror=alert(1)>")]);

        var html = ProposalReportGenerator.GenerateHtml(Analyzer.Analyze(_root), malicious, DiagramRenderMode.Svg);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", html);

        var design = new SliceDesign(
            1,
            "App",
            "App.<script>Cmd</script>",
            "Command",
            null,
            "App.Handler",
            null,
            null,
            [new("<img src=x onerror=alert(2)>", "given <x>", "when <y>", "then <z>")]);
        var sliceHtml = SliceDesignReportGenerator.GenerateHtml(
            new SliceDesignEvaluation("missing", design, []),
            DiagramRenderMode.Svg);
        Assert.DoesNotContain("<script", sliceHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", sliceHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;img src=x onerror=alert(2)&gt;", sliceHtml);
    }
}
