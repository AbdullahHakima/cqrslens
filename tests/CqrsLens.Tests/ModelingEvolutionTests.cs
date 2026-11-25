using Xunit;

namespace CqrsLens.Tests;

public sealed class ModelingEvolutionTests : IDisposable
{
    private readonly string _root;

    public ModelingEvolutionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cqrslens-modeling-evo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void MsBuildLoader_ParsesTargetFrameworksConditionalRefsAndRejectsXxe()
    {
        var workspace = CreateSubdir("msbuild");
        Write(workspace, "Directory.Build.props", """
            <Project>
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup Condition="'$(IncludeShared)' == 'true'">
                <ProjectReference Include="src/Shared/Shared.csproj" />
              </ItemGroup>
            </Project>
            """);

        Write(workspace, "src/Domain/Domain.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        Write(workspace, "src/Shared/Shared.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        Write(workspace, "src/App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>net8.0; net9.0; NET8.0</TargetFrameworks>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="..\Domain\Domain.csproj" />
                <ProjectReference Include="..\DebugTools\DebugTools.csproj" Condition="'$(Configuration)' == 'Debug'" />
              </ItemGroup>
              <Choose>
                <When Condition="'$(UsePlugins)' == 'true'">
                  <ItemGroup Condition="'$(TargetFramework)' == 'net9.0'">
                    <ProjectReference Include="..\Shared\Shared.csproj" />
                  </ItemGroup>
                </When>
              </Choose>
            </Project>
            """);

        Write(workspace, "src/Malicious/Malicious.csproj", """
            <?xml version="1.0"?>
            <!DOCTYPE Project [
              <!ENTITY xxe SYSTEM "file:///etc/passwd">
            ]>
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="&xxe;" />
              </ItemGroup>
            </Project>
            """);

        var projects = CsprojParser.LoadProjects(workspace);

        var app = Assert.Single(projects, p => p.Name == "App");
        Assert.Equal(["net8.0", "net9.0"], app.TargetFrameworks);
        Assert.Equal(["Domain"], app.ReferenceNames);
        Assert.Equal([@"..\Domain\Domain.csproj"], app.ReferenceIncludes);
        Assert.Contains(app.ConditionalReferences, r =>
            r.Include == @"..\DebugTools\DebugTools.csproj" &&
            r.Condition == "'$(Configuration)' == 'Debug'" &&
            r.ResolvedProjectName is null);
        Assert.Contains(app.ConditionalReferences, r =>
            r.Include == @"..\Shared\Shared.csproj" &&
            r.Condition == "'$(UsePlugins)' == 'true' && '$(TargetFramework)' == 'net9.0'" &&
            r.ResolvedProjectName == "Shared");
        Assert.Contains(app.ConditionalReferences, r =>
            r.Condition == "'$(IncludeShared)' == 'true'" &&
            r.ResolvedProjectName == "Shared");

        var malicious = Assert.Single(projects, p => p.Name == "Malicious");
        Assert.Empty(malicious.ReferenceIncludes);
        Assert.Empty(CsprojParser.GetProjectReferenceIncludes(malicious.FilePath));
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void RichSliceDesign_EvaluatesMatchedFieldsAcrossBothScanners(AnalysisMode mode)
    {
        var workspace = WriteRichShopWorkspace("rich-slice-match-" + mode);
        var result = Analyzer.Analyze(workspace, mode);

        var design = new SliceDesign(
            1,
            "Shop.Application",
            "Shop.Application.Orders.PlaceOrderCommand",
            "Command",
            "Guid",
            "Shop.Application.Orders.PlaceOrderCommandHandler",
            "Shop.Application.Orders.PlaceOrderCommandValidator",
            new SliceDesignEndpoint("POST", "/api/orders"),
            [new SliceDesignScenario("places order", "valid input", "PlaceOrderCommand sent", "order persisted")],
            Properties:
            [
                new SliceDesignProperty("CustomerId", "System.Guid"),
                new SliceDesignProperty("Lines", "IReadOnlyList < Shop.Application.Orders.OrderLineInput >"),
            ],
            Publishes: ["OrderPlacedNotification"],
            Dispatches: ["SendAuditCommand"],
            Persistence: new SliceDesignPersistence(
                DbContexts: ["ShopDbContext"],
                Entities: ["Order"]),
            Dependencies: ["IShopDbContext", "OrderAuditService"],
            ForbiddenDependencies: ["Shop.Legacy.", "ForbiddenDirectClient"]);

        var evaluation = SliceDesignEvaluator.Evaluate(result, design);

        Assert.Equal("matched", evaluation.Status);
        Assert.Equal(
            new[]
            {
                "project", "request", "kind", "responseType", "handler", "validator", "endpoint",
                "properties", "publishes", "dispatches", "persistence.dbContexts", "persistence.entities",
                "dependencies", "forbiddenDependencies",
            },
            evaluation.Checks.Select(c => c.Field));
        Assert.All(evaluation.Checks, check => Assert.Equal("matched", check.Status));
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void RichSliceDesign_DetectsForbiddenDependenciesWithEvidenceAndFailsClosed(AnalysisMode mode)
    {
        var workspace = WriteRichShopWorkspace("rich-slice-forbidden-" + mode);
        var result = Analyzer.Analyze(workspace, mode);

        var baseDesign = new SliceDesign(
            1,
            "Shop.Application",
            "Shop.Application.Orders.PlaceOrderCommand",
            "Command",
            "Guid",
            "Shop.Application.Orders.PlaceOrderCommandHandler",
            null,
            null,
            [new SliceDesignScenario("places order", "valid input", "command sent", "order saved")]);

        // Forbidden by namespace prefix (through ServiceCallEdge To = Shop.Infrastructure.OrderAuditService),
        // simple name (IShopDbContext), and full name (Shop.Infrastructure.Persistence.ShopDbContext via ResolvedEdges).
        var forbiddenEval = SliceDesignEvaluator.Evaluate(result, baseDesign with
        {
            Properties = [new SliceDesignProperty("CustomerId", "string")],
            ForbiddenDependencies = ["Shop.Infrastructure.", "ShopDbContext"],
        });

        Assert.Equal("mismatch", forbiddenEval.Status);
        var propsCheck = Assert.Single(forbiddenEval.Checks, c => c.Field == "properties");
        Assert.Equal("mismatch", propsCheck.Status);

        var forbiddenCheck = Assert.Single(forbiddenEval.Checks, c => c.Field == "forbiddenDependencies");
        Assert.Equal("mismatch", forbiddenCheck.Status);
        Assert.NotEmpty(forbiddenCheck.Evidence);
        Assert.All(forbiddenCheck.Evidence, loc => Assert.True(loc.Line >= 1));

        // Fail closed when expected publication is missing and scan has UnresolvedDispatches.
        result.UnresolvedDispatches.Add(new UnresolvedDispatch
        {
            Method = "Publish",
            ReceiverName = "_publisher",
            ProjectName = "Shop.Application",
            Location = new SourceLocation("Shop.Application/Orders/PlaceOrder.cs", 30, 1),
        });

        var unresolvedPubEval = SliceDesignEvaluator.Evaluate(result, baseDesign with
        {
            Publishes = ["NeverPublishedNotification"],
        });

        var pubCheck = Assert.Single(unresolvedPubEval.Checks, c => c.Field == "publishes");
        Assert.Equal("not_determinable", pubCheck.Status);
    }

    [Fact]
    public void RichSliceDesign_ValidatesOptionalFieldsForBlanksAndDuplicates()
    {
        var validBase = new SliceDesign(
            1, "Shop.Application", "CreateOrderCommand", "Command", "Guid", null, null, null,
            [new SliceDesignScenario("s1", "g", "w", "t")]);

        Assert.Throws<ArgumentException>(() => SliceDesignEvaluator.Validate(validBase with
        {
            Properties = [new SliceDesignProperty("Id"), new SliceDesignProperty("id")],
        }));

        Assert.Throws<ArgumentException>(() => SliceDesignEvaluator.Validate(validBase with
        {
            Properties = [new SliceDesignProperty(" ")],
        }));

        Assert.Throws<ArgumentException>(() => SliceDesignEvaluator.Validate(validBase with
        {
            Publishes = ["OrderPlaced", "orderplaced"],
        }));

        Assert.Throws<ArgumentException>(() => SliceDesignEvaluator.Validate(validBase with
        {
            Persistence = new SliceDesignPersistence(DbContexts: [" "]),
        }));

        Assert.Throws<ArgumentException>(() => SliceDesignEvaluator.Validate(validBase with
        {
            ForbiddenDependencies = ["Shop.Infra.", "shop.infra."],
        }));
    }

    [Fact]
    public void ArchitectureProposal_EvaluatesSelfRefsUnknownLayersTransitiveViolationsMetricsAndBlastRadius()
    {
        var workspace = WriteRichShopWorkspace("proposal-upgrade");
        var result = Analyzer.Analyze(workspace, AnalysisMode.Text);

        // 1. Self reference -> invalid with SelfProjectReference.
        var selfRef = ArchitectureProposalEvaluator.Evaluate(result, [],
            [new ArchitectureProposalReference("Shop.Domain", "Shop.Domain")]);
        Assert.Equal("invalid", selfRef.Status);
        Assert.Equal("SelfProjectReference", Assert.Single(selfRef.Findings).Code);

        // 2. Unknown layer name -> warning ("review"), still computes metrics & blast radius.
        var unknownLayer = ArchitectureProposalEvaluator.Evaluate(result,
            [new ArchitectureProposalProject("Shop.Sandbox", "ExperimentalLayer")],
            [new ArchitectureProposalReference("Shop.Sandbox", "Shop.Domain")]);
        Assert.Equal("review", unknownLayer.Status);
        Assert.Contains(unknownLayer.Findings, f => f.Code == "UnknownLayerName" && f.Severity == "warning");
        Assert.NotNull(unknownLayer.Metrics);
        Assert.Contains(unknownLayer.Metrics!, m => m.Project == "Shop.Sandbox" && m.EfferentAfter == 1 && m.InstabilityAfter == 1.0);

        // 3. Transitive layer violation: Shop.Domain -> Shop.Shared (Other) already exists; adding Shop.Shared -> Shop.Infrastructure
        // lets Shop.Domain transitively reach Shop.Infrastructure without a direct Domain -> Infrastructure edge.
        var domain = result.Projects.Single(p => p.Name == "Shop.Domain");
        var shared = new ProjectInfo
        {
            Name = "Shop.Shared",
            Layer = "Other",
            DirectoryPath = Path.Combine(workspace, "Shop.Shared"),
            FilePath = Path.Combine(workspace, "Shop.Shared", "Shop.Shared.csproj"),
        };
        domain.ReferenceNames.Add("Shop.Shared");
        domain.ReferenceIncludes.Add("../Shop.Shared/Shop.Shared.csproj");
        result.Projects.Add(shared);

        // Add conditional reference on Shop.Shared so ConditionalProjectReference warning also surfaces.
        shared.ConditionalReferences.Add(new ConditionalProjectReference(
            "../Shop.Api/Shop.Api.csproj", "'$(EnableApi)' == 'true'", "Shop.Api"));

        var transitive = ArchitectureProposalEvaluator.Evaluate(result, [],
            [new ArchitectureProposalReference("Shop.Shared", "Shop.Infrastructure")]);

        Assert.Equal("conflicts", transitive.Status);
        Assert.Contains(transitive.Findings, f =>
            f.Code == "TransitiveLayerViolation" &&
            f.Severity == "error" &&
            f.DependencyPath is not null &&
            f.DependencyPath.SequenceEqual(new[] { "Shop.Domain", "Shop.Shared", "Shop.Infrastructure" }));
        Assert.Contains(transitive.Findings, f =>
            f.Code == "TransitiveLayerViolation" &&
            f.Severity == "error" &&
            f.DependencyPath is not null &&
            f.DependencyPath.SequenceEqual(new[] { "Shop.Application", "Shop.Domain", "Shop.Shared", "Shop.Infrastructure" }));
        Assert.Contains(transitive.Findings, f => f.Code == "ConditionalProjectReference" && f.Severity == "warning");

        // Verify direct violation is NOT duplicated as TransitiveLayerViolation (neither on the direct edge nor on an isolated 2-project scan).
        var direct = ArchitectureProposalEvaluator.Evaluate(result, [],
            [new ArchitectureProposalReference("Shop.Domain", "Shop.Infrastructure")]);
        Assert.Contains(direct.Findings, f => f.Code == ArchitectureChecker.LayerViolationCode);
        Assert.DoesNotContain(direct.Findings, f =>
            f.Code == "TransitiveLayerViolation" &&
            f.DependencyPath is not null &&
            f.DependencyPath.SequenceEqual(new[] { "Shop.Domain", "Shop.Infrastructure" }));

        var cleanTwoProjectScan = new AnalysisResult { RootPath = workspace };
        cleanTwoProjectScan.Projects.Add(new ProjectInfo
        {
            Name = "Clean.Domain",
            Layer = "Domain",
            DirectoryPath = Path.Combine(workspace, "Clean.Domain"),
            FilePath = Path.Combine(workspace, "Clean.Domain", "Clean.Domain.csproj"),
        });
        cleanTwoProjectScan.Projects.Add(new ProjectInfo
        {
            Name = "Clean.Infrastructure",
            Layer = "Infrastructure",
            DirectoryPath = Path.Combine(workspace, "Clean.Infrastructure"),
            FilePath = Path.Combine(workspace, "Clean.Infrastructure", "Clean.Infrastructure.csproj"),
        });
        var directClean = ArchitectureProposalEvaluator.Evaluate(cleanTwoProjectScan, [],
            [new ArchitectureProposalReference("Clean.Domain", "Clean.Infrastructure")]);
        Assert.Single(directClean.Findings, f => f.Code == ArchitectureChecker.LayerViolationCode);
        Assert.DoesNotContain(directClean.Findings, f => f.Code == "TransitiveLayerViolation");

        // Verify LayerRuleOverrides downgrade TransitiveLayerViolation on an acyclic 3-project scan.
        var cleanShared = new ProjectInfo
        {
            Name = "Clean.Shared",
            Layer = "Other",
            DirectoryPath = Path.Combine(workspace, "Clean.Shared"),
            FilePath = Path.Combine(workspace, "Clean.Shared", "Clean.Shared.csproj"),
        };
        cleanTwoProjectScan.Projects[0].ReferenceNames.Add("Clean.Shared");
        cleanTwoProjectScan.Projects.Add(cleanShared);
        cleanTwoProjectScan.LayerRuleOverrides = new Dictionary<string, CheckSeverity?>
        {
            [ArchitectureChecker.LayerViolationCode] = CheckSeverity.Warning,
        };
        var downgraded = ArchitectureProposalEvaluator.Evaluate(cleanTwoProjectScan, [],
            [new ArchitectureProposalReference("Clean.Shared", "Clean.Infrastructure")]);
        Assert.Equal("review", downgraded.Status);
        Assert.Single(downgraded.Findings, f => f.Code == "TransitiveLayerViolation" && f.Severity == "warning");

        cleanTwoProjectScan.LayerRuleOverrides = new Dictionary<string, CheckSeverity?>
        {
            [ArchitectureChecker.LayerViolationCode] = null,
        };
        var suppressed = ArchitectureProposalEvaluator.Evaluate(cleanTwoProjectScan, [],
            [new ArchitectureProposalReference("Clean.Shared", "Clean.Infrastructure")]);
        Assert.Equal("checks_passed", suppressed.Status);
        Assert.Empty(suppressed.Findings);

        // 4. Metrics + BlastRadius on removing Shop.Application -> Shop.Domain (Shop.Api depends on Shop.Application,
        // so Shop.Api transitively depended on Shop.Domain and joins BlastRadius.AffectedProjects!).
        var removal = ArchitectureProposalEvaluator.Evaluate(result, [],
            [new ArchitectureProposalReference("Shop.Application", "Shop.Domain", "remove")]);

        Assert.NotNull(removal.Metrics);
        var domainMetrics = Assert.Single(removal.Metrics!, m => m.Project == "Shop.Domain");
        Assert.Equal(2, domainMetrics.AfferentBefore); // Shop.Application and Shop.Infrastructure
        Assert.Equal(1, domainMetrics.AfferentAfter);  // Only Shop.Infrastructure
        Assert.Equal(1, domainMetrics.EfferentBefore); // Shop.Shared
        Assert.Equal(1, domainMetrics.EfferentAfter);
        Assert.Equal(Math.Round(1.0 / 3.0, 3), domainMetrics.InstabilityBefore);
        Assert.Equal(0.5, domainMetrics.InstabilityAfter);

        Assert.NotNull(removal.BlastRadius);
        Assert.Contains("Shop.Application", removal.BlastRadius!.AffectedProjects);
        Assert.Contains("Shop.Domain", removal.BlastRadius.AffectedProjects);
        Assert.Contains("Shop.Api", removal.BlastRadius.AffectedProjects);
        Assert.True(removal.BlastRadius.AffectedRequests >= 2);
        Assert.True(removal.BlastRadius.AffectedHandlers >= 2);
        Assert.True(removal.BlastRadius.AffectedValidators >= 1);
        Assert.True(removal.BlastRadius.AffectedEndpoints >= 1);
        Assert.Contains("Shop.Application.Orders.PlaceOrderCommand", removal.BlastRadius.SampleRequests);
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void SliceDesignDrafter_DraftsSchemaValidDesignAndReportsGaps(AnalysisMode mode)
    {
        var workspace = WriteRichShopWorkspace("drafter-" + mode);
        var result = Analyzer.Analyze(workspace, mode);

        // Complete slice drafts cleanly and validates & matches under SliceDesignEvaluator.
        var drafted = SliceDesignDrafter.Draft(result, "PlaceOrderCommand");
        Assert.Equal("drafted", drafted.Status);
        Assert.NotNull(drafted.Design);
        Assert.Equal("Shop.Application.Orders.PlaceOrderCommand", drafted.Request);
        Assert.Equal("Shop.Application", drafted.Design!.Project);
        Assert.Equal("Command", drafted.Design.Kind);
        Assert.Equal("Guid", drafted.Design.ResponseType);
        Assert.Equal("Shop.Application.Orders.PlaceOrderCommandHandler", drafted.Design.Handler);
        Assert.Equal("Shop.Application.Orders.PlaceOrderCommandValidator", drafted.Design.Validator);
        Assert.Equal(new SliceDesignEndpoint("POST", "/api/orders"), drafted.Design.Endpoint);
        Assert.Contains(drafted.Design.Scenarios, s => s.Name.StartsWith("DRAFT:", StringComparison.Ordinal));
        Assert.Contains("Order", drafted.Design.Persistence!.Entities!);
        Assert.Contains("Shop.Infrastructure.Persistence.ShopDbContext", drafted.Design.Persistence.DbContexts!);
        Assert.Contains("IShopDbContext", drafted.Design.Dependencies!);

        SliceDesignEvaluator.Validate(drafted.Design);
        var eval = SliceDesignEvaluator.Evaluate(result, drafted.Design);
        Assert.Equal("matched", eval.Status);

        // Incomplete slice: command without handler, validator, and inferred-only endpoint omitted.
        var cancelDraft = SliceDesignDrafter.Draft(result, "DraftOnlyCommand");
        Assert.Equal("drafted", cancelDraft.Status);
        Assert.Null(cancelDraft.Design!.Handler);
        Assert.Null(cancelDraft.Design.Validator);
        Assert.Null(cancelDraft.Design.Endpoint);
        Assert.Contains(cancelDraft.Gaps, g => g.Contains("Missing handler", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(cancelDraft.Gaps, g => g.Contains("no linked validator", StringComparison.OrdinalIgnoreCase));

        result.Endpoints.Add(new EndpointInfo
        {
            Name = "DraftOnlyEndpoint",
            HttpMethod = "POST",
            Route = "/api/draft-only",
            TargetRequestName = "DraftOnlyCommand",
            TargetRequestFullName = "Shop.Application.Orders.DraftOnlyCommand",
            IsInferred = true,
            ProjectName = "Shop.Api",
            Location = new SourceLocation("Shop.Api/DraftEndpoints.cs", 5, 1),
        });
        var inferredDraft = SliceDesignDrafter.Draft(result, "DraftOnlyCommand");
        Assert.Null(inferredDraft.Design!.Endpoint);
        Assert.Contains(inferredDraft.Gaps, g => g.Contains("Inferred-only endpoint", StringComparison.OrdinalIgnoreCase));

        // Not found, ambiguous, and unresolved.
        var notFound = SliceDesignDrafter.Draft(result, "NonExistentCommand");
        Assert.Equal("not_found", notFound.Status);
        Assert.Null(notFound.Design);

        result.Requests.Add(new CqrsRequest { Name = "DuplicateCmd", Namespace = "ModuleA" });
        result.Requests.Add(new CqrsRequest { Name = "DuplicateCmd", Namespace = "ModuleB" });
        var ambiguous = SliceDesignDrafter.Draft(result, "DuplicateCmd");
        Assert.Equal("ambiguous", ambiguous.Status);
        Assert.Equal(2, ambiguous.Candidates.Count);

        result.Diagnostics.Add(new AnalysisDiagnostic(
            AnalysisResult.SourceFileSkippedCode,
            AnalysisDiagnosticSeverity.Warning,
            "Skipped file"));
        var unresolved = SliceDesignDrafter.Draft(result, "MissingWhileBrokenCommand");
        Assert.Equal("unresolved", unresolved.Status);
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void ChangeImpactAnalyzer_TracesBidirectionalImpactHotspotsTestsAndBlindSpots(AnalysisMode mode)
    {
        var workspace = WriteRichShopWorkspace("impact-" + mode);
        var result = Analyzer.Analyze(workspace, mode);

        // 1. Entity target -> traces upstream through handler -> request -> endpoint & validator, with high confidence.
        var entityImpact = ChangeImpactAnalyzer.Analyze(result, "Order", targetKind: "entity", maxDepth: 5);
        Assert.Equal("analyzed", entityImpact.Status);
        Assert.Equal("high", entityImpact.Confidence);
        Assert.Equal("entity", entityImpact.Target!.Kind);
        Assert.Contains(entityImpact.ImpactedItems, i => i.Kind == "dbcontext" && i.Name == "ShopDbContext");
        Assert.Contains(entityImpact.ImpactedItems, i => i.Kind == "handler" && i.Name == "PlaceOrderCommandHandler");
        Assert.Contains(entityImpact.ImpactedItems, i => i.Kind == "request" && i.Name == "PlaceOrderCommand");
        Assert.Contains(entityImpact.ImpactedItems, i => i.Kind == "validator" && i.Name == "PlaceOrderCommandValidator");
        Assert.Contains(entityImpact.ImpactedItems, i => i.Kind == "endpoint" && i.Name.Contains("/api/orders", StringComparison.Ordinal));
        Assert.Contains(entityImpact.ExistingTestReferences, t =>
            t.MethodName == "PlaceOrderCommand_CanBeConstructed" &&
            t.ProjectName == "Shop.Application.Tests");
        Assert.Contains(entityImpact.UncoveredHotspots, h => h.Contains("CancelOrderCommand", StringComparison.Ordinal));

        // 2. Service target (interface & implementation) -> finds caller handler and upstream request.
        var serviceImpact = ChangeImpactAnalyzer.Analyze(result, "IOrderAuditService", targetKind: "service", maxDepth: 4);
        Assert.Equal("analyzed", serviceImpact.Status);
        Assert.Contains(serviceImpact.ImpactedItems, i => i.Kind == "service" && i.Name == "OrderAuditService" && i.Direction == "downstream");
        Assert.Contains(serviceImpact.ImpactedItems, i => i.Kind == "handler" && i.Name == "PlaceOrderCommandHandler" && i.Direction == "upstream");
        Assert.Contains(serviceImpact.ImpactedItems, i => i.Kind == "request" && i.Name == "PlaceOrderCommand" && i.Direction == "upstream");

        // 3. MaxDepth clamping & depth bound: depth 1 from Order only shows direct callers/DbContexts (depth 1).
        var shallowImpact = ChangeImpactAnalyzer.Analyze(result, "Order", targetKind: "entity", maxDepth: 0);
        Assert.Equal(1, shallowImpact.MaxDepth);
        Assert.All(shallowImpact.ImpactedItems, item => Assert.Equal(1, item.Depth));
        Assert.DoesNotContain(shallowImpact.ImpactedItems, item => item.Kind == "endpoint");

        // 4. Notification & Request targets.
        var notifImpact = ChangeImpactAnalyzer.Analyze(result, "OrderPlacedNotification", targetKind: "notification");
        Assert.Equal("analyzed", notifImpact.Status);
        Assert.Contains(notifImpact.ImpactedItems, i => i.Kind == "notificationHandler" && i.Name == "OrderPlacedNotificationHandler");
        Assert.Contains(notifImpact.ImpactedItems, i => i.Kind == "request" && i.Name == "SendAuditCommand");

        // 5. Project target.
        var projectImpact = ChangeImpactAnalyzer.Analyze(result, "Shop.Domain", targetKind: "project", maxDepth: 3);
        Assert.Equal("analyzed", projectImpact.Status);
        Assert.Contains(projectImpact.ImpactedItems, i => i.Kind == "project" && i.Name == "Shop.Application" && i.Direction == "upstream");
        Assert.Contains("Shop.Api", projectImpact.AffectedProjects);

        // 6. BlindSpots degrade Confidence to "partial".
        result.UnresolvedDispatches.Add(new UnresolvedDispatch
        {
            Method = "Send",
            ReceiverName = "_sender",
            ProjectName = "Shop.Api",
            Location = new SourceLocation("Shop.Api/Endpoints.cs", 12, 1),
        });
        var partialImpact = ChangeImpactAnalyzer.Analyze(result, "PlaceOrderCommand", targetKind: "request");
        Assert.Equal("partial", partialImpact.Confidence);
        Assert.Contains(partialImpact.BlindSpots, b => b.Code == "UnresolvedDispatch");

        // 7. Ambiguous & not_found targets.
        var missing = ChangeImpactAnalyzer.Analyze(result, "DoesNotExist");
        Assert.Equal("not_found", missing.Status);

        result.Requests.Add(new CqrsRequest { Name = "SharedName", Namespace = "ModA" });
        result.Requests.Add(new CqrsRequest { Name = "SharedName", Namespace = "ModB" });
        var ambiguous = ChangeImpactAnalyzer.Analyze(result, "SharedName", targetKind: "request");
        Assert.Equal("ambiguous", ambiguous.Status);
        Assert.Equal(2, ambiguous.Candidates.Count);
    }

    private string WriteRichShopWorkspace(string folderName)
    {
        var dir = CreateSubdir(folderName);

        Write(dir, "Shop.Domain/Shop.Domain.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        Write(dir, "Shop.Domain/Entities.cs", """
            using System;
            using MediatR;

            namespace Shop.Domain;

            public sealed class Order
            {
                public Guid Id { get; set; }
                public Guid CustomerId { get; set; }
            }

            public sealed record OrderPlacedNotification(Guid OrderId) : INotification;
            """);

        Write(dir, "Shop.Application/Shop.Application.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="..\Shop.Domain\Shop.Domain.csproj" />
              </ItemGroup>
            </Project>
            """);

        Write(dir, "Shop.Application/Contracts.cs", """
            using System.Threading;
            using System.Threading.Tasks;

            namespace MediatR
            {
                public interface IPublisher
                {
                    Task Publish(INotification notification, CancellationToken cancellationToken = default);
                }

                public interface ISender
                {
                    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);
                }
            }
            """);

        Write(dir, "Shop.Application/Orders/PlaceOrder.cs", """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using FluentValidation;
            using MediatR;
            using Microsoft.EntityFrameworkCore;
            using Shop.Domain;

            namespace Shop.Application.Orders;

            public interface IShopDbContext
            {
                DbSet<Order> Orders { get; }
            }

            public interface IOrderAuditService
            {
                void Record(Guid orderId);
            }

            public sealed record OrderLineInput(Guid ProductId, int Quantity);

            public sealed record PlaceOrderCommand(Guid CustomerId, IReadOnlyList<OrderLineInput> Lines) : IRequest<Guid>;

            public sealed record SendAuditCommand(Guid OrderId) : IRequest<bool>;

            public sealed record CancelOrderCommand(Guid OrderId) : IRequest<bool>;

            public sealed record DraftOnlyCommand(Guid OrderId) : IRequest<bool>;

            public sealed class PlaceOrderCommandValidator : AbstractValidator<PlaceOrderCommand>
            {
            }

            public sealed class PlaceOrderCommandHandler : IRequestHandler<PlaceOrderCommand, Guid>
            {
                private readonly IShopDbContext _db;
                private readonly IOrderAuditService _audit;
                private readonly IPublisher _publisher;
                private readonly ISender _sender;

                public PlaceOrderCommandHandler(
                    IShopDbContext db,
                    IOrderAuditService audit,
                    IPublisher publisher,
                    ISender sender)
                {
                    _db = db;
                    _audit = audit;
                    _publisher = publisher;
                    _sender = sender;
                }

                public async Task<Guid> Handle(PlaceOrderCommand request, CancellationToken cancellationToken)
                {
                    var order = new Order { Id = Guid.NewGuid(), CustomerId = request.CustomerId };
                    _ = _db.Orders;
                    _audit.Record(order.Id);
                    await _sender.Send(new SendAuditCommand(order.Id), cancellationToken);
                    await _publisher.Publish(new OrderPlacedNotification(order.Id), cancellationToken);
                    return order.Id;
                }
            }

            public sealed class SendAuditCommandHandler : IRequestHandler<SendAuditCommand, bool>
            {
                public Task<bool> Handle(SendAuditCommand request, CancellationToken cancellationToken)
                    => Task.FromResult(true);
            }

            public sealed class CancelOrderCommandHandler : IRequestHandler<CancelOrderCommand, bool>
            {
                private readonly IShopDbContext _db;
                public CancelOrderCommandHandler(IShopDbContext db) => _db = db;

                public Task<bool> Handle(CancelOrderCommand request, CancellationToken cancellationToken)
                    => Task.FromResult(_db.Orders != null);
            }

            public sealed class OrderPlacedNotificationHandler : INotificationHandler<OrderPlacedNotification>
            {
                private readonly ISender _sender;
                public OrderPlacedNotificationHandler(ISender sender) => _sender = sender;

                public Task Handle(OrderPlacedNotification notification, CancellationToken cancellationToken)
                    => _sender.Send(new SendAuditCommand(notification.OrderId), cancellationToken);
            }
            """);

        Write(dir, "Shop.Infrastructure/Shop.Infrastructure.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="..\Shop.Application\Shop.Application.csproj" />
                <ProjectReference Include="..\Shop.Domain\Shop.Domain.csproj" />
              </ItemGroup>
            </Project>
            """);

        Write(dir, "Shop.Infrastructure/Persistence/ShopDbContext.cs", """
            using System;
            using Microsoft.EntityFrameworkCore;
            using Shop.Application.Orders;
            using Shop.Domain;

            namespace Shop.Infrastructure.Persistence;

            public sealed class ShopDbContext : DbContext, IShopDbContext
            {
                public DbSet<Order> Orders => null!;
            }

            public sealed class OrderAuditService : IOrderAuditService
            {
                public void Record(Guid orderId) { }
            }
            """);

        Write(dir, "Shop.Api/Shop.Api.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="..\Shop.Application\Shop.Application.csproj" />
                <ProjectReference Include="..\Shop.Infrastructure\Shop.Infrastructure.csproj" />
              </ItemGroup>
            </Project>
            """);

        Write(dir, "Shop.Api/OrderEndpoints.cs", """
            using System;
            using Shop.Application.Orders;

            namespace Shop.Api;

            public static class OrderEndpoints
            {
                public static void Map(dynamic app)
                {
                    app.MapPost("/api/orders", (PlaceOrderCommand cmd) => cmd);
                    app.MapDelete("/api/orders/{id:guid}", (Guid id) => new CancelOrderCommand(id));
                }
            }
            """);

        Write(dir, "Shop.Application.Tests/Shop.Application.Tests.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="xunit" Version="2.9.3" />
                <ProjectReference Include="..\Shop.Application\Shop.Application.csproj" />
              </ItemGroup>
            </Project>
            """);

        Write(dir, "Shop.Application.Tests/PlaceOrderTests.cs", """
            using System;
            using Shop.Application.Orders;
            using Xunit;

            namespace Shop.Application.Tests;

            public sealed class PlaceOrderTests
            {
                [Fact]
                public void PlaceOrderCommand_CanBeConstructed()
                {
                    var cmd = new PlaceOrderCommand(Guid.NewGuid(), []);
                    Assert.NotNull(cmd);
                }
            }
            """);

        return dir;
    }

    private string CreateSubdir(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Write(string baseDir, string relativePath, string content)
    {
        var fullPath = Path.Combine(baseDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }
}
