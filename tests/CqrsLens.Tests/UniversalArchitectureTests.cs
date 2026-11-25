using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace CqrsLens.Tests;

public sealed class UniversalArchitectureTests : IDisposable
{
    private readonly string _root;

    public UniversalArchitectureTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cqrslens-universal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void ArchitectureStyleDetector_And_CircularServiceDependency_WorkWithoutMediatR(AnalysisMode mode)
    {
        CreateZeroMediatRWorkspace();

        var result = Analyze(mode);

        // Pure non-MediatR codebase: zero CQRS shapes or conventions.
        Assert.Empty(result.Requests);
        Assert.Empty(result.Handlers);
        Assert.Empty(result.Behaviors);
        Assert.Empty(result.Conventions);

        // a) ArchitectureStyleDetector detects non-CQRS styles and omits CQRS / Mediator.
        Assert.Contains("Layered / N-Tier", result.ArchitectureStyles);
        Assert.Contains("Minimal API", result.ArchitectureStyles);
        Assert.Contains("gRPC Services", result.ArchitectureStyles);
        Assert.Contains("Background Workers", result.ArchitectureStyles);
        Assert.Contains("Event-Driven", result.ArchitectureStyles);
        Assert.DoesNotContain("CQRS / Mediator", result.ArchitectureStyles);
        Assert.DoesNotContain("Clean Architecture", result.ArchitectureStyles);

        // b) CircularServiceDependency flags OrderService <-> InventoryService once, not acyclic/test services.
        var cycleFindings = result.Checks
            .Where(c => c.Code == ArchitectureChecker.CircularServiceDependencyCode)
            .ToList();
        var cycle = Assert.Single(cycleFindings);
        Assert.Equal(CheckSeverity.Warning, cycle.Severity);
        Assert.NotNull(cycle.Target);
        Assert.Contains("Shop.Services.InventoryService", cycle.Target, StringComparison.Ordinal);
        Assert.Contains("Shop.Services.OrderService", cycle.Target, StringComparison.Ordinal);

        Assert.DoesNotContain(cycleFindings, f =>
            f.Target!.Contains("CatalogService", StringComparison.Ordinal) ||
            f.Target!.Contains("BasketService", StringComparison.Ordinal) ||
            f.Target!.Contains("OutboxService", StringComparison.Ordinal) ||
            f.Target!.Contains("PricingCalculator", StringComparison.Ordinal) ||
            f.Target!.Contains("TestLoop", StringComparison.Ordinal));

        // SARIF generation supports CircularServiceDependency cleanly.
        var sarif = SarifReportGenerator.Generate(result);
        Assert.Contains("CircularServiceDependency", sarif);

        // Config rule override ("off") suppresses CircularServiceDependency.
        WriteFile(".cqrslens.json", """
            {
              "rules": {
                "CircularServiceDependency": "off"
              }
            }
            """);
        var outMd = Path.Combine(_root, "suppressed.md");
        var run = RunCli(_root, "-o", outMd, "--analysis", mode == AnalysisMode.Roslyn ? "roslyn" : "text");
        Assert.Equal(ExitCodes.Success, run.Code);
        Assert.DoesNotContain("CircularServiceDependency", File.ReadAllText(outMd));
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void DirectFlows_And_DirectSequences_RenderToStandaloneSvg_WithoutMediator(AnalysisMode mode)
    {
        CreateZeroMediatRWorkspace();
        var result = Analyze(mode);

        var flowSections = DirectFlowDiagramBuilder.Build(result);
        Assert.Contains(flowSections, s => s.ProjectName == "Shop.Api");
        Assert.Contains(flowSections, s => s.ProjectName == "Shop.Worker");

        foreach (var section in flowSections)
        {
            Assert.True(DiagramBuilder.IsWithinMermaidLimits(section.Source));
            Assert.DoesNotContain("Mediator", section.Source, StringComparison.Ordinal);
            var svg = MermaidSvgRenderer.Render(section.Source);
            Assert.Contains("<svg ", svg);
            Assert.DoesNotContain("Diagram syntax error", svg, StringComparison.OrdinalIgnoreCase);
        }

        var apiFlow = Assert.Single(flowSections, s => s.ProjectName == "Shop.Api");
        Assert.Contains("POST /api/catalog/items", apiFlow.Source);
        Assert.Contains("gRPC BasketGrpcService.UpdateBasket", apiFlow.Source);
        Assert.Contains("ShopDbContext", apiFlow.Source);
        Assert.Contains("OrderSubmittedIntegrationEvent", apiFlow.Source);

        var sequences = DirectSequenceBuilder.Build(result);
        Assert.True(sequences.Count >= 4);
        Assert.Contains(sequences, s => s.DisplayLabel == "POST /api/catalog/items");
        Assert.Contains(sequences, s => s.DisplayLabel == "GRPC BasketGrpcService.UpdateBasket");
        Assert.Contains(sequences, s => s.DisplayLabel == "WORKER OrderOutboxWorker");
        Assert.Contains(sequences, s => s.DisplayLabel == "CONSUME OrderSubmittedIntegrationEvent");

        foreach (var seq in sequences)
        {
            Assert.StartsWith("sequenceDiagram", seq.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("participant med", seq.Source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Mediator", seq.Source, StringComparison.Ordinal);
            var svg = MermaidSvgRenderer.Render(seq.Source);
            Assert.Contains("<svg ", svg);
            Assert.DoesNotContain("Diagram syntax error", svg, StringComparison.OrdinalIgnoreCase);
        }

        // Universal slice blueprint omits any empty Handler node and renders cleanly to SVG.
        var draft = SliceDesignDrafter.Draft(result, "CatalogApi.CreateCatalogItem");
        Assert.Equal("drafted", draft.Status);
        var eval = SliceDesignEvaluator.Evaluate(result, draft.Design!);
        Assert.Equal("matched", eval.Status);
        var blueprint = SliceDesignDiagramBuilder.BuildMermaid(eval);
        Assert.DoesNotContain("[\"Handler\"]", blueprint, StringComparison.Ordinal);
        var blueprintSvg = MermaidSvgRenderer.Render(blueprint);
        Assert.Contains("<svg ", blueprintSvg);
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void UniversalModeling_DraftEvaluatePlanAndTddCycle_VerifyEndpointGrpcWorkerConsumer(AnalysisMode mode)
    {
        CreateZeroMediatRWorkspace();
        var result = Analyze(mode);

        var targets = new (string Target, string ExpectedKind, string TestMethod)[]
        {
            ("CatalogApi.CreateCatalogItem", "Endpoint", "Shop.Tests.UniversalSliceTests.CatalogApi_CreateCatalogItem_PersistsAndPublishes"),
            ("BasketGrpcService.UpdateBasket", "Grpc", "Shop.Tests.UniversalSliceTests.BasketGrpcService_UpdateBasket_PersistsItem"),
            ("OrderOutboxWorker", "Worker", "Shop.Tests.UniversalSliceTests.OrderOutboxWorker_ProcessesPending"),
            ("OrderSubmittedConsumer", "Consumer", "Shop.Tests.UniversalSliceTests.OrderSubmittedConsumer_HandlesEvent"),
        };

        var greenTrx = ParseTrx("""
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results>
                <UnitTestResult testName="Shop.Tests.UniversalSliceTests.CatalogApi_CreateCatalogItem_PersistsAndPublishes" outcome="Passed" />
                <UnitTestResult testName="Shop.Tests.UniversalSliceTests.BasketGrpcService_UpdateBasket_PersistsItem" outcome="Passed" />
                <UnitTestResult testName="Shop.Tests.UniversalSliceTests.OrderOutboxWorker_ProcessesPending" outcome="Passed" />
                <UnitTestResult testName="Shop.Tests.UniversalSliceTests.OrderSubmittedConsumer_HandlesEvent" outcome="Passed" />
              </Results>
            </TestRun>
            """);

        foreach (var (target, expectedKind, testMethod) in targets)
        {
            var draft = SliceDesignDrafter.Draft(result, target);
            Assert.Equal("drafted", draft.Status);
            Assert.NotNull(draft.Design);
            var design = draft.Design!;
            Assert.Equal(expectedKind, design.Kind);
            Assert.False(design.IsCqrsKind);
            Assert.Null(design.Handler);
            Assert.Null(design.Validator);
            Assert.Null(design.ResponseType);

            var evaluation = SliceDesignEvaluator.Evaluate(result, design);
            Assert.Equal("matched", evaluation.Status);

            var plan = AgentSliceTestPlanGenerator.Generate(
                result,
                target,
                [new SliceTestScenario("works", "valid inputs", $"{target} runs", "expected outcome")]);
            Assert.Equal("existing", plan.Status);
            Assert.NotNull(plan.TestProjects);
            Assert.Contains(plan.TestProjects, tp => tp.Name == "Shop.Tests");
            Assert.NotNull(plan.Collaborators);
            Assert.NotEmpty(plan.Collaborators);
            Assert.NotNull(plan.DiscoveredTests);
            Assert.NotEmpty(plan.DiscoveredTests);

            var greenEval = TddCycleEvaluator.Evaluate(result, design, greenTrx);
            Assert.Equal("verified", greenEval.Phase);
            Assert.Equal("matched", greenEval.DesignStatus);
            Assert.True(greenEval.HasDiscoveredTests);
            Assert.True(greenEval.HasRuntimeResults);
            Assert.Contains(testMethod, greenEval.MatchingPassedTests);

            var redTrx = ParseTrx($"""
                <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
                  <Results>
                    <UnitTestResult testName="{testMethod}" outcome="Failed" />
                  </Results>
                </TestRun>
                """);
            var redEval = TddCycleEvaluator.Evaluate(result, design, redTrx);
            Assert.Equal("red", redEval.Phase);
            Assert.Contains(testMethod, redEval.MatchingFailedTests);
        }

        // Also confirm concrete Service drafting + evaluation without MediatR.
        var serviceDraft = SliceDesignDrafter.Draft(result, "Shop.Services.PricingCalculator");
        Assert.Equal("drafted", serviceDraft.Status);
        Assert.Equal("Service", serviceDraft.Design!.Kind);
        Assert.Equal("matched", SliceDesignEvaluator.Evaluate(result, serviceDraft.Design).Status);
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void UniversalChangeImpact_TracesEntityEntrypointAndEventAcrossSystem(AnalysisMode mode)
    {
        CreateZeroMediatRWorkspace();
        var result = Analyze(mode);

        // 1. Entity impact reaches direct Minimal API entrypoint, service, and DbContext.
        var entityImpact = ChangeImpactAnalyzer.Analyze(result, "CatalogItem", "entity");
        Assert.Equal("analyzed", entityImpact.Status);
        Assert.Contains(entityImpact.ImpactedItems, i =>
            i.Kind == "entrypoint" && i.Name.Contains("CreateCatalogItem", StringComparison.Ordinal));
        Assert.Contains(entityImpact.ImpactedItems, i =>
            i.Kind == "service" && i.Name == "CatalogService");
        Assert.Contains(entityImpact.ImpactedItems, i =>
            i.Kind == "dbcontext" && i.Name == "ShopDbContext");
        Assert.Contains("Shop.Api", entityImpact.AffectedProjects);
        Assert.Contains("Shop.Services", entityImpact.AffectedProjects);
        Assert.Contains("Shop.Data", entityImpact.AffectedProjects);

        // 2. Entrypoint impact reaches service, DbContext, entity, event, consumer project, and static tests.
        var entrypointImpact = ChangeImpactAnalyzer.Analyze(result, "CatalogApi.CreateCatalogItem", "entrypoint");
        Assert.Equal("analyzed", entrypointImpact.Status);
        Assert.NotNull(entrypointImpact.Target);
        Assert.Equal("entrypoint", entrypointImpact.Target!.Kind);
        Assert.Contains(entrypointImpact.ImpactedItems, i =>
            i.Kind == "service" && i.Name == "CatalogService");
        Assert.Contains(entrypointImpact.ImpactedItems, i =>
            i.Kind == "dbcontext" && i.Name == "ShopDbContext");
        Assert.Contains(entrypointImpact.ImpactedItems, i =>
            i.Kind == "entity" && i.Name == "CatalogItem");
        Assert.Contains(entrypointImpact.ImpactedItems, i =>
            i.Kind == "event" && i.Name == "OrderSubmittedIntegrationEvent");
        Assert.Contains("Shop.Worker", entrypointImpact.AffectedProjects);
        Assert.NotEmpty(entrypointImpact.ExistingTestReferences);

        var entrypointDiagram = ImpactDiagramBuilder.BuildMermaid(entrypointImpact);
        Assert.NotNull(entrypointDiagram);
        Assert.Contains("OrderSubmittedIntegrationEvent", entrypointDiagram);

        // 3. Integration event impact connects publisher (Shop.Api) and consumer (Shop.Worker).
        var eventImpact = ChangeImpactAnalyzer.Analyze(result, "OrderSubmittedIntegrationEvent", "event");
        Assert.Equal("analyzed", eventImpact.Status);
        Assert.Equal("event", eventImpact.Target!.Kind);
        Assert.Contains(eventImpact.ImpactedItems, i =>
            i.Kind == "entrypoint" && i.Name.Contains("CreateCatalogItem", StringComparison.Ordinal));
        Assert.Contains(eventImpact.ImpactedItems, i =>
            i.Kind == "entrypoint" && i.Name.Contains("OrderSubmittedConsumer", StringComparison.Ordinal));
        Assert.Contains("Shop.Api", eventImpact.AffectedProjects);
        Assert.Contains("Shop.Worker", eventImpact.AffectedProjects);
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void UniversalMcpTools_SupportZeroMediatRDiscoveryTraceImpactVerifyClaimAndPlanning(AnalysisMode mode)
    {
        CreateZeroMediatRWorkspace();
        var analysisArg = mode == AnalysisMode.Roslyn ? "roslyn" : "text";
        var options = new CliOptions
        {
            Path = _root,
            Mcp = true,
            AnalysisMode = mode,
            AnalysisModeSpecified = true,
        };
        var cache = new McpScanCache();

        // tools/list advertises entrypoint & event targetKind plus CircularServiceDependency rule.
        var listResp = SendMcp(options, cache, """{"jsonrpc":"2.0","id":1,"method":"tools/list"}""");
        var tools = listResp["result"]!["tools"]!.AsArray();
        var impactTool = Assert.Single(tools, t => t!["name"]!.GetValue<string>() == "analyze_change_impact")!;
        var targetKinds = impactTool["inputSchema"]!["properties"]!["targetKind"]!["enum"]!.AsArray()
            .Select(n => n!.GetValue<string>())
            .ToList();
        Assert.Contains("entrypoint", targetKinds);
        Assert.Contains("event", targetKinds);

        var checkTool = Assert.Single(tools, t => t!["name"]!.GetValue<string>() == "check_architecture")!;
        var ruleCodes = checkTool["inputSchema"]!["properties"]!["rules"]!["items"]!["enum"]!.AsArray()
            .Select(n => n!.GetValue<string>())
            .ToList();
        Assert.Contains(ArchitectureChecker.CircularServiceDependencyCode, ruleCodes);

        // get_architecture_dna shows styles, direct entrypoints, and integration flows.
        var dnaText = CallMcpText(options, cache, "get_architecture_dna", new JsonObject { ["analysis"] = analysisArg });
        Assert.Contains("**Architecture Styles**:", dnaText);
        Assert.Contains("`Layered / N-Tier`", dnaText);
        Assert.Contains("`Minimal API`", dnaText);
        Assert.Contains("**Direct Entrypoints & Workers**:", dnaText);
        Assert.Contains("**Integration Flows**:", dnaText);

        // trace_slice (text & json) returns Direct Operations.
        var traceText = CallMcpText(options, cache, "trace_slice", new JsonObject
        {
            ["query"] = "Catalog",
            ["analysis"] = analysisArg,
        });
        Assert.Contains("Direct Operations", traceText);
        Assert.Contains("CatalogApi.CreateCatalogItem", traceText);

        var traceJson = JsonNode.Parse(CallMcpText(options, cache, "trace_slice", new JsonObject
        {
            ["query"] = "Catalog",
            ["format"] = "json",
            ["analysis"] = analysisArg,
        }))!.AsObject();
        Assert.True(traceJson["totalMatched"]!.GetValue<int>() >= 1);
        Assert.NotEmpty(traceJson["directEntrypoints"]!.AsArray());

        // trace_entity_impact includes Direct Entrypoints & Workers Touching CatalogItem.
        var entityText = CallMcpText(options, cache, "trace_entity_impact", new JsonObject
        {
            ["entity"] = "CatalogItem",
            ["analysis"] = analysisArg,
        });
        Assert.Contains("## Direct Entrypoints & Workers Touching CatalogItem", entityText);
        Assert.Contains("POST /api/catalog/items", entityText);

        // verify_claim supports touches entity, uses DbContext, publishes, communicates with, and fails closed.
        Assert.StartsWith("verdict: supported", VerifyClaim(options, cache, analysisArg,
            "CatalogApi.CreateCatalogItem", "touches entity", "CatalogItem"));
        Assert.StartsWith("verdict: supported", VerifyClaim(options, cache, analysisArg,
            "CatalogApi.CreateCatalogItem", "uses DbContext", "ShopDbContext"));
        Assert.StartsWith("verdict: supported", VerifyClaim(options, cache, analysisArg,
            "CatalogApi.CreateCatalogItem", "publishes", "OrderSubmittedIntegrationEvent"));
        Assert.StartsWith("verdict: supported", VerifyClaim(options, cache, analysisArg,
            "Shop.Api", "communicates with", "Shop.Worker"));
        Assert.StartsWith("verdict: not supported", VerifyClaim(options, cache, analysisArg,
            "BasketGrpcService.UpdateBasket", "publishes", "OrderSubmittedIntegrationEvent"));
        Assert.StartsWith("verdict: not determinable", VerifyClaim(options, cache, analysisArg,
            "Shop.Worker", "communicates with", "Shop.Api"));
        Assert.StartsWith("verdict: not determinable", VerifyClaim(options, cache, analysisArg,
            "Shop.Worker", "does not communicate with", "Shop.Api"));

        // check_architecture filters cleanly on CircularServiceDependency.
        var checkText = CallMcpText(options, cache, "check_architecture", new JsonObject
        {
            ["rules"] = new JsonArray(ArchitectureChecker.CircularServiceDependencyCode),
            ["analysis"] = analysisArg,
        });
        Assert.Contains("**Matching findings**: 1", checkText);
        Assert.Contains("InventoryService", checkText);

        // analyze_change_impact works for targetKind="entrypoint" and targetKind="event".
        var epImpact = CallMcpStructured(options, cache, "analyze_change_impact", new JsonObject
        {
            ["target"] = "CatalogApi.CreateCatalogItem",
            ["targetKind"] = "entrypoint",
            ["analysis"] = analysisArg,
        });
        Assert.Equal("analyzed", epImpact["status"]!.GetValue<string>());

        var evtImpact = CallMcpStructured(options, cache, "analyze_change_impact", new JsonObject
        {
            ["target"] = "OrderSubmittedIntegrationEvent",
            ["targetKind"] = "event",
            ["analysis"] = analysisArg,
        });
        Assert.Equal("analyzed", evtImpact["status"]!.GetValue<string>());

        // plan_slice_tests works on non-MediatR Minimal API endpoint.
        var plan = CallMcpStructured(options, cache, "plan_slice_tests", new JsonObject
        {
            ["request"] = "CatalogApi.CreateCatalogItem",
            ["scenarios"] = new JsonArray(new JsonObject
            {
                ["name"] = "Creates catalog item",
                ["given"] = "A valid item payload",
                ["when"] = "POST /api/catalog/items is called",
                ["then"] = "Item is persisted and event published",
            }),
            ["analysis"] = analysisArg,
        });
        Assert.Equal("existing", plan["status"]!.GetValue<string>());
        Assert.NotEmpty(plan["discoveredTests"]!.AsArray());
    }

    [Fact]
    public void AdaptiveReporting_ConsoleMarkdownSvgHtmlAndJson_AdaptCleanlyForZeroCqrs()
    {
        CreateZeroMediatRWorkspace();
        var result = Analyze(AnalysisMode.Text);

        // Adaptive Console card
        var card = ConsoleArtwork.RenderSummaryCard(result, "ARCHITECTURE.md", useColor: false, width: 120);
        Assert.Contains("Architecture Styles", card);
        Assert.Contains("System Entrypoints", card);
        Assert.Contains("Integration Flows", card);
        Assert.DoesNotContain("Requests (Commands / Queries)", card);
        Assert.DoesNotContain("Pipeline Behaviors", card);

        // Adaptive Markdown (Full & Overview)
        var fullMd = ReportGenerator.Generate(result);
        Assert.Contains("| Architecture styles |", fullMd);
        Assert.Contains("| System entrypoints |", fullMd);
        Assert.Contains("## Direct Flows", fullMd);
        Assert.Contains("## Operation Sequences", fullMd);
        Assert.DoesNotContain("## CQRS Map", fullMd);
        Assert.DoesNotContain("## Validation Coverage", fullMd);
        Assert.DoesNotContain("## Pipeline Behaviors", fullMd);

        var overviewMd = ReportGenerator.Generate(result, CqrsMapSplitMode.Count, ReportViewMode.Overview);
        Assert.Contains("## Direct Flows", overviewMd);
        Assert.Contains("## Operation Sequences", overviewMd);

        // Adaptive zero-JS SVG HTML (Full & Overview)
        foreach (var mode in new[] { ReportViewMode.Full, ReportViewMode.Overview })
        {
            var html = HtmlReportGenerator.Generate(
                result,
                DiagramRenderMode.Svg,
                CqrsMapSplitMode.Count,
                mode,
                focusService: null,
                excludeTests: false);

            Assert.Contains(
                "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; img-src data:;\">",
                html);
            Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Architecture styles:", html);
            Assert.Contains("System entrypoints", html);
            Assert.Contains("Integration flows", html);
            Assert.Contains("Direct Flows", html);
            Assert.Contains("Operation Sequences", html);
            Assert.Contains("<svg ", html);
            Assert.DoesNotContain("Requests (commands + queries)", html);
            Assert.DoesNotContain("Pipeline behaviors", html);
            Assert.DoesNotContain("CQRS Map", html);
        }

        // JSON architectureStyles
        var json = JsonReportGenerator.Generate(result);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("architectureStyles", out var styles));
        var styleValues = styles.EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("Layered / N-Tier", styleValues);
        Assert.Contains("Minimal API", styleValues);
        Assert.Contains("gRPC Services", styleValues);
        Assert.Empty(doc.RootElement.GetProperty("requests").EnumerateArray());
        Assert.Empty(doc.RootElement.GetProperty("handlers").EnumerateArray());
        Assert.True(doc.RootElement.GetProperty("systemEntrypoints").GetArrayLength() >= 4);
    }

    [Fact]
    public void CliEndToEnd_UniversalWorkflow_DraftSliceDesignTddImpactAndSvgHtml()
    {
        CreateZeroMediatRWorkspace();

        // 1. Draft Endpoint & gRPC slice designs via CLI
        var endpointDraftRun = RunCli(_root, "--draft-slice", "CatalogApi.CreateCatalogItem");
        Assert.Equal(ExitCodes.Success, endpointDraftRun.Code);
        Assert.Empty(endpointDraftRun.Stderr);
        var endpointDesignJson = JsonNode.Parse(endpointDraftRun.Stdout)!["design"]!.ToJsonString();
        var endpointDesignPath = Path.Combine(_root, "specs", "catalog-create.slice.json");
        WriteFile("specs/catalog-create.slice.json", endpointDesignJson);

        var grpcDraftRun = RunCli(_root, "--draft-slice", "BasketGrpcService.UpdateBasket");
        Assert.Equal(ExitCodes.Success, grpcDraftRun.Code);
        Assert.Empty(grpcDraftRun.Stderr);
        WriteFile("specs/basket-update.slice.json", JsonNode.Parse(grpcDraftRun.Stdout)!["design"]!.ToJsonString());

        // 2. --slice-design --strict passes
        var sliceVerifyRun = RunCli(_root, "--slice-design", endpointDesignPath, "--strict");
        Assert.Equal(ExitCodes.Success, sliceVerifyRun.Code);
        Assert.Equal("matched", JsonNode.Parse(sliceVerifyRun.Stdout)!["status"]!.GetValue<string>());

        // 3. --tdd-check directory batch with green TRX -> verified (exit 0 under --strict)
        var greenTrxPath = Path.Combine(_root, "green.trx");
        WriteFile("green.trx", """
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results>
                <UnitTestResult testName="Shop.Tests.UniversalSliceTests.CatalogApi_CreateCatalogItem_PersistsAndPublishes" outcome="Passed" />
                <UnitTestResult testName="Shop.Tests.UniversalSliceTests.BasketGrpcService_UpdateBasket_PersistsItem" outcome="Passed" />
              </Results>
            </TestRun>
            """);
        var batchTddRun = RunCli(_root, "--tdd-check", Path.Combine(_root, "specs"), "--test-results", greenTrxPath, "--strict");
        Assert.Equal(ExitCodes.Success, batchTddRun.Code);
        var batchJson = JsonNode.Parse(batchTddRun.Stdout)!;
        Assert.Equal("verified", batchJson["phase"]!.GetValue<string>());
        Assert.Equal(2, batchJson["verified"]!.GetValue<int>());

        // 4. --tdd-check with red TRX -> red (exit 4 under --strict)
        var redTrxPath = Path.Combine(_root, "red.trx");
        WriteFile("red.trx", """
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results>
                <UnitTestResult testName="Shop.Tests.UniversalSliceTests.CatalogApi_CreateCatalogItem_PersistsAndPublishes" outcome="Failed" />
              </Results>
            </TestRun>
            """);
        var redTddRun = RunCli(_root, "--tdd-check", endpointDesignPath, "--test-results", redTrxPath, "--strict");
        Assert.Equal(ExitCodes.StrictFailure, redTddRun.Code);
        Assert.Equal("red", JsonNode.Parse(redTddRun.Stdout)!["phase"]!.GetValue<string>());

        // 5. --impact with --impact-kind entrypoint and event
        var epImpactRun = RunCli(_root, "--impact", "CatalogApi.CreateCatalogItem", "--impact-kind", "entrypoint");
        Assert.Equal(ExitCodes.Success, epImpactRun.Code);
        Assert.Equal("analyzed", JsonNode.Parse(epImpactRun.Stdout)!["status"]!.GetValue<string>());

        var evtImpactRun = RunCli(_root, "--impact", "OrderSubmittedIntegrationEvent", "--impact-kind", "event");
        Assert.Equal(ExitCodes.Success, evtImpactRun.Code);
        Assert.Equal("analyzed", JsonNode.Parse(evtImpactRun.Stdout)!["status"]!.GetValue<string>());

        // 6. --format html --render svg produces adaptive zero-JS HTML
        var htmlPath = Path.Combine(_root, "universal.html");
        var htmlRun = RunCli(_root, "--format", "html", "--render", "svg", "-o", htmlPath);
        Assert.Equal(ExitCodes.Success, htmlRun.Code);
        var html = File.ReadAllText(htmlPath);
        Assert.Contains("Content-Security-Policy", html);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Direct Flows", html);
    }

    private AnalysisResult Analyze(AnalysisMode mode)
        => Analyzer.Analyze(_root, mode, new AnalysisOptions
        {
            GeneratedAt = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero),
        });

    private static TrxTestSummary ParseTrx(string xml)
    {
        using var reader = new StringReader(xml);
        return TrxTestResultsParser.Parse(reader, "test-results.trx");
    }

    private static (int Code, string Stdout, string Stderr) RunCli(params string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var code = CliRunner.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private static JsonObject SendMcp(CliOptions options, McpScanCache cache, string json)
        => JsonNode.Parse(McpServer.ProcessMessage(json, options, cache)!)!.AsObject();

    private static string CallMcpText(CliOptions options, McpScanCache cache, string toolName, JsonObject arguments)
    {
        var req = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 1,
            ["method"] = "tools/call",
            ["params"] = new JsonObject
            {
                ["name"] = toolName,
                ["arguments"] = arguments,
            },
        }.ToJsonString();

        var result = SendMcp(options, cache, req)["result"]!;
        Assert.False(result["isError"]!.GetValue<bool>(), result["content"]![0]!["text"]!.GetValue<string>());
        return result["content"]![0]!["text"]!.GetValue<string>();
    }

    private static JsonObject CallMcpStructured(CliOptions options, McpScanCache cache, string toolName, JsonObject arguments)
    {
        var req = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 1,
            ["method"] = "tools/call",
            ["params"] = new JsonObject
            {
                ["name"] = toolName,
                ["arguments"] = arguments,
            },
        }.ToJsonString();

        var result = SendMcp(options, cache, req)["result"]!;
        Assert.False(result["isError"]!.GetValue<bool>(), result["content"]![0]!["text"]!.GetValue<string>());
        return result["structuredContent"]!.AsObject();
    }

    private static string VerifyClaim(
        CliOptions options,
        McpScanCache cache,
        string analysisArg,
        string subject,
        string relation,
        string obj)
        => CallMcpText(options, cache, "verify_claim", new JsonObject
        {
            ["subject"] = subject,
            ["relation"] = relation,
            ["object"] = obj,
            ["analysis"] = analysisArg,
        });

    private void WriteFile(string relativePath, string contents)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }

    private void CreateZeroMediatRWorkspace()
    {
        WriteFile("Shop.Contracts/Shop.Contracts.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        WriteFile("Shop.Contracts/IntegrationContracts.cs", """
            using System;
            using System.Threading;
            using System.Threading.Tasks;

            namespace Shop.Contracts;

            public sealed record OrderSubmittedIntegrationEvent(Guid OrderId, string CustomerEmail);

            public interface IIntegrationEventBus
            {
                Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken ct = default);
            }

            public interface IIntegrationEventHandler<in TEvent>
            {
                Task HandleAsync(TEvent integrationEvent, CancellationToken ct = default);
            }
            """);

        WriteFile("Shop.Data/Shop.Data.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Microsoft.EntityFrameworkCore" Version="8.0.0" />
                <ProjectReference Include="..\Shop.Contracts\Shop.Contracts.csproj" />
              </ItemGroup>
            </Project>
            """);
        WriteFile("Shop.Data/ShopDbContext.cs", """
            using System;
            using Microsoft.EntityFrameworkCore;

            namespace Shop.Data;

            public sealed class CatalogItem
            {
                public Guid Id { get; set; }
                public string Name { get; set; } = string.Empty;
                public decimal Price { get; set; }
            }

            public sealed class BasketItem
            {
                public Guid Id { get; set; }
                public Guid CustomerId { get; set; }
                public string Sku { get; set; } = string.Empty;
            }

            public sealed class OrderRecord
            {
                public Guid Id { get; set; }
                public string Status { get; set; } = string.Empty;
            }

            public sealed class ShopDbContext : DbContext
            {
                public DbSet<CatalogItem> CatalogItems => Set<CatalogItem>();
                public DbSet<BasketItem> BasketItems => Set<BasketItem>();
                public DbSet<OrderRecord> OrderRecords => Set<OrderRecord>();
            }
            """);

        WriteFile("Shop.Services/Shop.Services.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="..\Shop.Contracts\Shop.Contracts.csproj" />
                <ProjectReference Include="..\Shop.Data\Shop.Data.csproj" />
              </ItemGroup>
            </Project>
            """);
        WriteFile("Shop.Services/ShopServices.cs", """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.EntityFrameworkCore;
            using Shop.Data;

            namespace Shop.Services;

            public interface ICatalogService
            {
                Task<Guid> CreateItemAsync(string name, decimal price, CancellationToken ct = default);
            }

            public sealed class CatalogService : ICatalogService
            {
                private readonly ShopDbContext _db;
                public CatalogService(ShopDbContext db) => _db = db;

                public async Task<Guid> CreateItemAsync(string name, decimal price, CancellationToken ct = default)
                {
                    var item = new CatalogItem { Id = Guid.NewGuid(), Name = name, Price = price };
                    _db.CatalogItems.Add(item);
                    await _db.SaveChangesAsync(ct);
                    return item.Id;
                }
            }

            public interface IBasketService
            {
                Task UpsertItemAsync(Guid customerId, string sku);
            }

            public sealed class BasketService : IBasketService
            {
                private readonly ShopDbContext _db;
                public BasketService(ShopDbContext db) => _db = db;

                public async Task UpsertItemAsync(Guid customerId, string sku)
                {
                    _db.BasketItems.Add(new BasketItem { Id = Guid.NewGuid(), CustomerId = customerId, Sku = sku });
                    await _db.SaveChangesAsync();
                }
            }

            public interface IOutboxService
            {
                Task<List<OrderRecord>> ProcessPendingAsync(CancellationToken ct = default);
                Task RecordSubmissionAsync(Guid orderId, CancellationToken ct = default);
            }

            public sealed class OutboxService : IOutboxService
            {
                private readonly ShopDbContext _db;
                public OutboxService(ShopDbContext db) => _db = db;

                public Task<List<OrderRecord>> ProcessPendingAsync(CancellationToken ct = default)
                    => _db.OrderRecords.ToListAsync(ct);

                public async Task RecordSubmissionAsync(Guid orderId, CancellationToken ct = default)
                {
                    _db.OrderRecords.Add(new OrderRecord { Id = orderId, Status = "Submitted" });
                    await _db.SaveChangesAsync(ct);
                }
            }

            public interface ITaxCalculator
            {
                decimal ComputeTax(decimal amount);
            }

            public sealed class TaxCalculator : ITaxCalculator
            {
                public decimal ComputeTax(decimal amount) => amount * 0.1m;
            }

            public interface IPricingCalculator
            {
                decimal ComputeTotal(decimal subtotal);
            }

            public sealed class PricingCalculator : IPricingCalculator
            {
                private readonly ITaxCalculator _tax;
                public PricingCalculator(ITaxCalculator tax) => _tax = tax;

                public decimal ComputeTotal(decimal subtotal) => subtotal + _tax.ComputeTax(subtotal);
            }

            public interface IOrderService
            {
                Task ReserveForOrderAsync(Guid orderId);
                Task MarkBackorderedAsync(Guid orderId);
            }

            public interface IInventoryService
            {
                Task CheckStockAsync(Guid orderId);
            }

            public sealed class OrderService : IOrderService
            {
                private readonly IInventoryService _inventory;
                private readonly IPricingCalculator _pricing;

                public OrderService(IInventoryService inventory, IPricingCalculator pricing)
                {
                    _inventory = inventory;
                    _pricing = pricing;
                }

                public async Task ReserveForOrderAsync(Guid orderId)
                {
                    _ = _pricing.ComputeTotal(100m);
                    await _inventory.CheckStockAsync(orderId);
                }

                public Task MarkBackorderedAsync(Guid orderId) => Task.CompletedTask;
            }

            public sealed class InventoryService : IInventoryService
            {
                private readonly IOrderService _orders;
                public InventoryService(IOrderService orders) => _orders = orders;

                public async Task CheckStockAsync(Guid orderId)
                {
                    await _orders.MarkBackorderedAsync(orderId);
                }
            }
            """);

        WriteFile("Shop.Api/Shop.Api.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <ItemGroup>
                <PackageReference Include="Grpc.AspNetCore" Version="2.65.0" />
                <ProjectReference Include="..\Shop.Contracts\Shop.Contracts.csproj" />
                <ProjectReference Include="..\Shop.Services\Shop.Services.csproj" />
                <ProjectReference Include="..\Shop.Data\Shop.Data.csproj" />
              </ItemGroup>
            </Project>
            """);
        WriteFile("Shop.Api/CatalogEndpoints.cs", """
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.AspNetCore.Builder;
            using Microsoft.AspNetCore.Http;
            using Shop.Contracts;
            using Shop.Services;

            namespace Shop.Api;

            public static class CatalogApi
            {
                public static void Map(WebApplication app)
                {
                    app.MapPost("/api/catalog/items", CreateCatalogItem);
                }

                public static async Task<IResult> CreateCatalogItem(
                    string name,
                    decimal price,
                    ICatalogService catalogService,
                    IIntegrationEventBus eventBus,
                    CancellationToken ct)
                {
                    var id = await catalogService.CreateItemAsync(name, price, ct);
                    await eventBus.PublishAsync(new OrderSubmittedIntegrationEvent(id, "buyer@example.com"), ct);
                    return Results.Ok(id);
                }
            }
            """);
        WriteFile("Shop.Api/BasketGrpc.cs", """
            using System;
            using System.Threading.Tasks;
            using Grpc.Core;
            using Shop.Services;

            namespace Shop.Api;

            public sealed record UpdateBasketRequest(Guid CustomerId, string Sku);
            public sealed record UpdateBasketReply(bool Updated);

            public static class BasketProto
            {
                public abstract class BasketProtoBase
                {
                    public virtual Task<UpdateBasketReply> UpdateBasket(UpdateBasketRequest request, ServerCallContext context)
                        => Task.FromResult(new UpdateBasketReply(true));
                }
            }

            public sealed class BasketGrpcService : BasketProto.BasketProtoBase
            {
                private readonly IBasketService _basketService;
                public BasketGrpcService(IBasketService basketService) => _basketService = basketService;

                public override async Task<UpdateBasketReply> UpdateBasket(UpdateBasketRequest request, ServerCallContext context)
                {
                    await _basketService.UpsertItemAsync(request.CustomerId, request.Sku);
                    return new UpdateBasketReply(true);
                }
            }
            """);

        WriteFile("Shop.Worker/Shop.Worker.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.Worker">
              <ItemGroup>
                <PackageReference Include="RabbitMQ.Client" Version="6.8.1" />
                <ProjectReference Include="..\Shop.Contracts\Shop.Contracts.csproj" />
                <ProjectReference Include="..\Shop.Services\Shop.Services.csproj" />
                <ProjectReference Include="..\Shop.Data\Shop.Data.csproj" />
              </ItemGroup>
            </Project>
            """);
        WriteFile("Shop.Worker/WorkerModule.cs", """
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.Extensions.Hosting;
            using Shop.Contracts;
            using Shop.Services;

            namespace Shop.Worker;

            public sealed class OrderOutboxWorker : BackgroundService
            {
                private readonly IOutboxService _outboxService;
                public OrderOutboxWorker(IOutboxService outboxService) => _outboxService = outboxService;

                protected override async Task ExecuteAsync(CancellationToken stoppingToken)
                {
                    await _outboxService.ProcessPendingAsync(stoppingToken);
                }
            }

            public sealed class OrderSubmittedConsumer : IIntegrationEventHandler<OrderSubmittedIntegrationEvent>
            {
                private readonly IOutboxService _outboxService;
                public OrderSubmittedConsumer(IOutboxService outboxService) => _outboxService = outboxService;

                public async Task HandleAsync(OrderSubmittedIntegrationEvent integrationEvent, CancellationToken ct = default)
                {
                    await _outboxService.RecordSubmissionAsync(integrationEvent.OrderId, ct);
                }
            }
            """);

        WriteFile("Shop.Tests/Shop.Tests.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="xunit" Version="2.9.0" />
                <ProjectReference Include="..\Shop.Contracts\Shop.Contracts.csproj" />
                <ProjectReference Include="..\Shop.Services\Shop.Services.csproj" />
                <ProjectReference Include="..\Shop.Data\Shop.Data.csproj" />
                <ProjectReference Include="..\Shop.Api\Shop.Api.csproj" />
                <ProjectReference Include="..\Shop.Worker\Shop.Worker.csproj" />
              </ItemGroup>
            </Project>
            """);
        WriteFile("Shop.Tests/UniversalSliceTests.cs", """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Shop.Api;
            using Shop.Contracts;
            using Shop.Services;
            using Shop.Worker;
            using Xunit;

            namespace Shop.Tests;

            public interface ITestLoopA { void Ping(); }
            public interface ITestLoopB { void Pong(); }

            public sealed class TestLoopA : ITestLoopA
            {
                private readonly ITestLoopB _b;
                public TestLoopA(ITestLoopB b) => _b = b;
                public void Ping() => _b.Pong();
            }

            public sealed class TestLoopB : ITestLoopB
            {
                private readonly ITestLoopA _a;
                public TestLoopB(ITestLoopA a) => _a = a;
                public void Pong() => _a.Ping();
            }

            public sealed class UniversalSliceTests
            {
                [Fact]
                public void CatalogApi_CreateCatalogItem_PersistsAndPublishes()
                {
                    var route = "/api/catalog/items";
                    _ = typeof(CatalogApi);
                    _ = route;
                }

                [Fact]
                public void BasketGrpcService_UpdateBasket_PersistsItem()
                {
                    _ = typeof(BasketGrpcService);
                    var req = new UpdateBasketRequest(Guid.NewGuid(), "SKU-1");
                    _ = req;
                }

                [Fact]
                public void OrderOutboxWorker_ProcessesPending()
                {
                    OrderOutboxWorker? worker = null;
                    _ = worker;
                }

                [Fact]
                public void OrderSubmittedConsumer_HandlesEvent()
                {
                    OrderSubmittedConsumer? consumer = null;
                    var evt = new OrderSubmittedIntegrationEvent(Guid.NewGuid(), "buyer@example.com");
                    _ = consumer;
                    _ = evt;
                }
            }
            """);
    }
}
