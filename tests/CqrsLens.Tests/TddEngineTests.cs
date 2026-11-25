using System.Xml;
using Xunit;

namespace CqrsLens.Tests;

public sealed class TddEngineTests
{
    private const string ContractsCs = """
        namespace MediatR
        {
            public interface IMediator
            {
                System.Threading.Tasks.Task Send(object request);
                System.Threading.Tasks.Task Publish(object notification);
            }
        }
        """;

    [Fact]
    public void DiscoverTestProjects_ClassifiesFrameworksMockingAssertionsAndProductionRefs()
    {
        using var dir = new TempDir();
        WriteCsproj(dir, "Shop.Domain/Shop.Domain.csproj", packages: [], refs: []);
        WriteCsproj(dir, "Shop.Application/Shop.Application.csproj",
            packages: ["MediatR", "FluentValidation"],
            refs: ["../Shop.Domain/Shop.Domain.csproj"]);
        WriteCsproj(dir, "Shop.UnitTests/Shop.UnitTests.csproj",
            packages: ["xunit.v3", "NSubstitute", "FluentAssertions"],
            refs: ["../Shop.Application/Shop.Application.csproj"]);
        WriteCsproj(dir, "Shop.Specs/Shop.Specs.csproj",
            packages: ["NUnit", "Moq", "Shouldly"],
            refs: ["../Shop.Application/Shop.Application.csproj", "../Shop.UnitTests/Shop.UnitTests.csproj"]);
        WriteCsproj(dir, "Shop.IntegrationChecks/Shop.IntegrationChecks.csproj",
            packages: ["TUnit", "FakeItEasy", "AwesomeAssertions"],
            refs: ["../Shop.Domain/Shop.Domain.csproj"]);
        WriteCsproj(dir, "Shop.MsTests/Shop.MsTests.csproj",
            packages: ["MSTest.TestFramework"],
            refs: ["../Shop.Application/Shop.Application.csproj"]);

        var result = Analyzer.Analyze(dir.Path);
        var discovered = TestProjectDiscovery.DiscoverTestProjects(result);

        Assert.Equal(4, discovered.Count);

        var unit = Assert.Single(discovered, p => p.Name == "Shop.UnitTests");
        Assert.Equal("Shop.UnitTests/Shop.UnitTests.csproj", unit.FilePath);
        Assert.Equal(["xUnit"], unit.Frameworks);
        Assert.Equal(["NSubstitute"], unit.MockingLibraries);
        Assert.Equal(["FluentAssertions"], unit.AssertionLibraries);
        Assert.Equal(["Shop.Application"], unit.ReferencedProductionProjects);

        var specs = Assert.Single(discovered, p => p.Name == "Shop.Specs");
        Assert.Equal(["NUnit"], specs.Frameworks);
        Assert.Equal(["Moq"], specs.MockingLibraries);
        Assert.Equal(["Shouldly"], specs.AssertionLibraries);
        Assert.Equal(["Shop.Application"], specs.ReferencedProductionProjects);

        // Detected via TUnit package even though name has no Tests/Specs suffix.
        var tunit = Assert.Single(discovered, p => p.Name == "Shop.IntegrationChecks");
        Assert.Equal(["TUnit"], tunit.Frameworks);
        Assert.Equal(["FakeItEasy"], tunit.MockingLibraries);
        Assert.Equal(["AwesomeAssertions"], tunit.AssertionLibraries);
        Assert.Equal(["Shop.Domain"], tunit.ReferencedProductionProjects);

        var mstest = Assert.Single(discovered, p => p.Name == "Shop.MsTests");
        Assert.Equal(["MSTest"], mstest.Frameworks);
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void FindTestReferences_DetectsAttributesKindsHelpersAndAvoidsFalsePositives(AnalysisMode mode)
    {
        using var dir = new TempDir();
        WriteFullSampleSolution(dir);

        var result = Analyzer.Analyze(dir.Path, mode);
        var allRefs = TestProjectDiscovery.FindAllTestReferences(result);

        Assert.NotEmpty(allRefs);
        Assert.All(allRefs, r =>
        {
            Assert.False(Path.IsPathRooted(r.File));
            Assert.True(r.Line >= 1);
            Assert.DoesNotContain('\\', r.File);
        });

        // [Fact] dispatch via _mediator.Send(new CreateOrderCommand(...))
        Assert.Contains(allRefs, r =>
            r.MethodName == "Dispatch_CreateOrder_Succeeds" &&
            r.TargetFullName == "Shop.Application.Orders.CreateOrderCommand" &&
            r.TargetKind == "request" &&
            r.ReferenceKind == "dispatch");

        // [Theory] using field _sut instantiated in constructor
        Assert.Contains(allRefs, r =>
            r.MethodName == "Handler_ViaFieldAndCtor_ReturnsId" &&
            r.TargetFullName == "Shop.Application.Orders.CreateOrderCommandHandler" &&
            r.TargetKind == "handler" &&
            r.ReferenceKind == "instantiation");

        // [SkippableFact] instantiating validator directly
        Assert.Contains(allRefs, r =>
            r.MethodName == "Validator_RejectsEmpty" &&
            r.TargetFullName == "Shop.Application.Orders.CreateOrderCommandValidator" &&
            r.TargetKind == "validator" &&
            r.ReferenceKind == "instantiation");

        // [TestMethod] type-reference via typeof(...)
        Assert.Contains(allRefs, r =>
            r.MethodName == "Contract_TypeReference" &&
            r.TargetFullName == "Shop.Application.Orders.CreateOrderCommand" &&
            r.TargetKind == "request" &&
            r.ReferenceKind == "type-reference");

        // [TestCase] delegating to private helper method
        Assert.Contains(allRefs, r =>
            r.MethodName == "HelperDelegated_Test" &&
            r.TargetFullName == "Shop.Application.Orders.CreateOrderCommand" &&
            r.TargetKind == "request" &&
            r.ReferenceKind == "dispatch");

        // Additional framework attributes ([Test], [TestCaseSource], [DataTestMethod], [SkippableTheory])
        Assert.Contains(allRefs, r => r.MethodName == "NUnit_Test_Attribute");
        Assert.Contains(allRefs, r => r.MethodName == "NUnit_TestCaseSource_Attribute");
        Assert.Contains(allRefs, r => r.MethodName == "MsTest_DataTestMethod_Attribute");
        Assert.Contains(allRefs, r => r.MethodName == "Xunit_SkippableTheory_Attribute");

        // Substring guard: Order request must NOT match OrderItem, comments, or string literals.
        Assert.DoesNotContain(allRefs, r =>
            r.MethodName == "Unrelated_OrderItem_AndDecoys" &&
            r.TargetFullName == "Shop.Application.Orders.Order");

        // Slice-scoped query only returns targets belonging to CreateOrderCommand slice.
        var createOrderReq = Assert.Single(result.Requests, r => r.Name == "CreateOrderCommand");
        var sliceRefs = TestProjectDiscovery.FindTestReferencesForSlice(result, createOrderReq);
        Assert.All(sliceRefs, r => Assert.Contains(r.TargetFullName, new[]
        {
            "Shop.Application.Orders.CreateOrderCommand",
            "Shop.Application.Orders.CreateOrderCommandHandler",
            "Shop.Application.Orders.CreateOrderCommandValidator",
        }));
        Assert.DoesNotContain(sliceRefs, r => r.TargetFullName == "Shop.Application.Orders.Order");
    }

    [Fact]
    public void TrxTestResultsParser_NormalizesOutcomesTruncatesMessagesAndRejectsXxe()
    {
        var longError = new string('E', 650);
        var vsTrx = $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <TestRun id="1" name="Run" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results>
                <UnitTestResult testName="Shop.Tests.CreateOrderTests.PassTest" outcome="Passed" duration="00:00:00.012" />
                <UnitTestResult testName="Shop.Tests.CreateOrderTests.FailTest" outcome="Failed" duration="00:00:00.045">
                  <Output>
                    <ErrorInfo>
                      <Message>  {{longError}}  </Message>
                    </ErrorInfo>
                  </Output>
                </UnitTestResult>
                <UnitTestResult testName="Shop.Tests.CreateOrderTests.Skip1" outcome="NotExecuted" />
                <UnitTestResult testName="Shop.Tests.CreateOrderTests.Skip2" outcome="Inconclusive" />
                <UnitTestResult testName="Shop.Tests.CreateOrderTests.TimedOut" outcome="Timeout" />
              </Results>
            </TestRun>
            """;

        var summary = TrxTestResultsParser.Parse(new StringReader(vsTrx), "results.trx");
        Assert.Equal("results.trx", summary.SourceFile);
        Assert.Equal(5, summary.Total);
        Assert.Equal(1, summary.Passed);
        Assert.Equal(1, summary.Failed);
        Assert.Equal(2, summary.Skipped);

        var failed = Assert.Single(summary.Results, r => r.Outcome == "failed");
        Assert.Equal(500, failed.ErrorMessage!.Length);
        Assert.Equal("other", Assert.Single(summary.Results, r => r.TestName.EndsWith("TimedOut", StringComparison.Ordinal)).Outcome);

        // No-namespace TRX via ParseFile
        using var dir = new TempDir();
        var noNsTrx = """
            <TestRun>
              <Results>
                <UnitTestResult testName="CreateOrderTests.Works" outcome="Passed" duration="00:00:00.001" />
              </Results>
            </TestRun>
            """;
        var trxPath = dir.Write("TestResults/run.trx", noNsTrx);
        var fileSummary = TrxTestResultsParser.ParseFile(trxPath);
        Assert.Equal(trxPath, fileSummary.SourceFile);
        Assert.Equal(1, fileSummary.Passed);

        // Reject DTD / XXE
        const string xxeTrx = """
            <?xml version="1.0" encoding="utf-8"?>
            <!DOCTYPE foo [ <!ENTITY xxe SYSTEM "file:///etc/passwd"> ]>
            <TestRun><Results><UnitTestResult testName="&xxe;" outcome="Passed" /></Results></TestRun>
            """;
        Assert.Throws<XmlException>(() => TrxTestResultsParser.Parse(new StringReader(xxeTrx), "xxe.trx"));

        var xxePath = dir.Write("TestResults/xxe.trx", xxeTrx);
        Assert.Throws<XmlException>(() => TrxTestResultsParser.ParseFile(xxePath));

        // Reject non-TestRun root
        Assert.Throws<InvalidDataException>(() =>
            TrxTestResultsParser.Parse(new StringReader("<OtherRoot />"), "bad.trx"));
    }

    [Fact]
    public void AgentSliceTestPlanGenerator_EnrichesPlanWithFactsWithoutInventingOnAmbiguous()
    {
        using var dir = new TempDir();
        WriteFullSampleSolution(dir);

        var result = Analyzer.Analyze(dir.Path);
        SliceTestScenario[] scenarios =
        [
            new("Reject empty items", "An empty line list", "CreateOrderCommand is validated", "Validation error is returned"),
            new("Persist order", "Valid customer", "Order is saved to database", "Order row exists in DbContext"),
            new("Notify subscribers", "Order succeeds", "Event is published", "OrderPlacedNotification is emitted"),
            new("Expose HTTP endpoint", "Valid payload", "POST /api/orders is called", "Returns 201 status code"),
            new("Compute order total", "Multiple lines", "Handler executes", "Returns newly generated Guid"),
        ];

        var plan = AgentSliceTestPlanGenerator.Generate(result, "CreateOrderCommand", scenarios);

        Assert.Equal("existing", plan.Status);
        Assert.NotNull(plan.Payload);
        Assert.Equal(
            [new SliceTestPayloadField("CustomerId", "Guid"), new SliceTestPayloadField("Amount", "decimal")],
            plan.Payload);

        Assert.NotNull(plan.Collaborators);
        var repo = Assert.Single(plan.Collaborators, c => c.DeclaredType == "IOrderRepository");
        Assert.Equal("Shop.Infrastructure.OrderRepository", repo.ResolvedType);
        Assert.Equal("both", repo.ResolutionSource);
        Assert.Equal(["SaveAsync"], repo.CalledMethods);
        Assert.Equal("fake-or-stub (NSubstitute)", repo.RecommendedDouble);

        var db = Assert.Single(plan.Collaborators, c => c.DeclaredType == "IShopDbContext");
        Assert.Equal("Shop.Infrastructure.ShopDbContext", db.ResolvedType);
        Assert.Equal("real-or-test-container", db.RecommendedDouble);

        var clock = Assert.Single(plan.Collaborators, c => c.DeclaredType == "SystemClock");
        Assert.Equal("real", clock.RecommendedDouble);

        Assert.NotNull(plan.ActiveBehaviors);
        Assert.Equal(["LoggingBehavior"], plan.ActiveBehaviors);

        Assert.NotNull(plan.TestProjects);
        Assert.Contains(plan.TestProjects, p => p.Name == "Shop.Tests");

        Assert.NotNull(plan.DiscoveredTests);
        Assert.NotEmpty(plan.DiscoveredTests);

        Assert.NotNull(plan.ScenarioMappings);
        Assert.Equal("validation", Assert.Single(plan.ScenarioMappings, m => m.ScenarioName == "Reject empty items").RecommendedBoundary);
        Assert.Equal("persistence", Assert.Single(plan.ScenarioMappings, m => m.ScenarioName == "Persist order").RecommendedBoundary);
        Assert.Equal("notification", Assert.Single(plan.ScenarioMappings, m => m.ScenarioName == "Notify subscribers").RecommendedBoundary);
        Assert.Equal("http", Assert.Single(plan.ScenarioMappings, m => m.ScenarioName == "Expose HTTP endpoint").RecommendedBoundary);
        Assert.Equal("request-behavior", Assert.Single(plan.ScenarioMappings, m => m.ScenarioName == "Compute order total").RecommendedBoundary);
        Assert.All(plan.ScenarioMappings, m => Assert.Equal("heuristic", m.Confidence));

        // When no test project references the request's project, OpenQuestions suggests <Project>.Tests.
        var unreferencedResult = new AnalysisResult { RootPath = dir.Path };
        unreferencedResult.Requests.Add(new CqrsRequest
        {
            Name = "OrphanSliceCommand",
            Namespace = "Billing.Orders",
            ProjectName = "Billing.Application",
        });
        var unreferencedPlan = AgentSliceTestPlanGenerator.Generate(
            unreferencedResult,
            "OrphanSliceCommand",
            [new("S1", "Given", "When", "Then")]);
        Assert.Contains(unreferencedPlan.OpenQuestions, q =>
            q.Contains("Billing.Application.Tests", StringComparison.Ordinal));
    }

    [Fact]
    public void TddCycleEvaluator_CoversAllSixPhasesFailClosed()
    {
        var scenarios = new[]
        {
            new SliceDesignScenario("Creates order", "Valid customer", "Command sent", "Order is saved"),
        };

        // 1. spec_draft: valid design before implementation exists
        using (var emptyDir = new TempDir())
        {
            WriteCsproj(emptyDir, "Shop.Application/Shop.Application.csproj", packages: ["MediatR"], refs: []);
            var emptyScan = Analyzer.Analyze(emptyDir.Path);
            var draftDesign = new SliceDesign(
                1,
                "Shop.Application",
                "Shop.Application.Orders.CreateOrderCommand",
                "Command",
                "Guid",
                "Shop.Application.Orders.CreateOrderCommandHandler",
                "Shop.Application.Orders.CreateOrderCommandValidator",
                new("POST", "/api/orders"),
                scenarios);

            var draftEval = TddCycleEvaluator.Evaluate(emptyScan, draftDesign);
            Assert.Equal("spec_draft", draftEval.Phase);
            Assert.Equal("missing", draftEval.DesignStatus);
            Assert.False(draftEval.HasDiscoveredTests);
            Assert.False(draftEval.HasRuntimeResults);
            Assert.NotEmpty(draftEval.NextActions);
        }

        // Full solution setup for red, green_incomplete, verified, drifted, inconclusive
        using var dir = new TempDir();
        WriteFullSampleSolution(dir);
        var fullScan = Analyzer.Analyze(dir.Path);

        var matchedDesign = new SliceDesign(
            1,
            "Shop.Application",
            "Shop.Application.Orders.CreateOrderCommand",
            "Command",
            "Guid",
            "Shop.Application.Orders.CreateOrderCommandHandler",
            "Shop.Application.Orders.CreateOrderCommandValidator",
            new("POST", "/api/orders"),
            scenarios);

        // 2. green_incomplete (no runtime TRX supplied yet)
        var noTrxEval = TddCycleEvaluator.Evaluate(fullScan, matchedDesign, runtimeResults: null);
        Assert.Equal("green_incomplete", noTrxEval.Phase);
        Assert.Equal("matched", noTrxEval.DesignStatus);
        Assert.True(noTrxEval.HasDiscoveredTests);
        Assert.False(noTrxEval.HasRuntimeResults);

        // 2b. green_incomplete (TRX supplied with matching passing test, but unrelated failure in TRX)
        var unrelatedFailTrx = ParseTrx(
            ("Shop.Tests.CreateOrderTests.Dispatch_CreateOrder_Succeeds", "Passed"),
            ("Shop.Tests.OtherTests.Unrelated_Failed", "Failed"));
        var unrelatedFailEval = TddCycleEvaluator.Evaluate(fullScan, matchedDesign, unrelatedFailTrx);
        Assert.Equal("green_incomplete", unrelatedFailEval.Phase);
        Assert.Single(unrelatedFailEval.MatchingPassedTests);
        Assert.Empty(unrelatedFailEval.MatchingFailedTests);
        Assert.Contains(unrelatedFailEval.NextActions, a => a.Contains("outside this slice failed", StringComparison.Ordinal));

        // 2c. green_incomplete (slice-scoped warning present, e.g. CommandWithoutValidator on ArchiveOrderCommand)
        var archiveDesign = new SliceDesign(
            1,
            "Shop.Application",
            "Shop.Application.Orders.ArchiveOrderCommand",
            "Command",
            "bool",
            "Shop.Application.Orders.ArchiveOrderCommandHandler",
            null,
            null,
            scenarios);
        var archiveWarningTrx = ParseTrx(("Shop.Tests.ArchiveOrderTests.ArchiveOrderCommand_Works", "Passed"));
        var warningEval = TddCycleEvaluator.Evaluate(fullScan, archiveDesign, archiveWarningTrx);
        Assert.Equal("green_incomplete", warningEval.Phase);
        Assert.Contains(warningEval.SliceChecks, c => c.Code == ArchitectureChecker.CommandWithoutValidatorCode);

        // 3. red (matching slice test failed in TRX)
        var redTrx = ParseTrx(
            ("Shop.Tests.CreateOrderTests.Dispatch_CreateOrder_Succeeds", "Failed"),
            ("Shop.Tests.CreateOrderTests.Validator_RejectsEmpty", "Passed"));
        var redEval = TddCycleEvaluator.Evaluate(fullScan, matchedDesign, redTrx);
        Assert.Equal("red", redEval.Phase);
        Assert.Single(redEval.MatchingFailedTests);
        Assert.Single(redEval.MatchingPassedTests);

        // 3b. Substring guard in TRX matching: OrderItemTests failure must NOT turn Order slice red.
        var orderDesign = new SliceDesign(
            1,
            "Shop.Application",
            "Shop.Application.Orders.Order",
            "Request",
            "bool",
            "Shop.Application.Orders.OrderHandler",
            null,
            null,
            scenarios);
        var orderItemFailTrx = ParseTrx(
            ("Shop.Tests.OrderSliceTests.Order_Runs", "Passed"),
            ("Shop.Tests.OrderItemTests.OrderItem_Failed", "Failed"));
        var orderNotRed = TddCycleEvaluator.Evaluate(fullScan, orderDesign, orderItemFailTrx);
        Assert.Empty(orderNotRed.MatchingFailedTests);
        Assert.NotEqual("red", orderNotRed.Phase);

        // 4. verified (design matched, 0 slice errors/warnings, discovered tests > 0, TRX all green & matching pass > 0)
        var verifiedTrx = ParseTrx(
            ("Shop.Tests.CreateOrderTests.Dispatch_CreateOrder_Succeeds", "Passed"),
            ("Shop.Tests.CreateOrderTests.Validator_RejectsEmpty", "Passed"));
        var verifiedEval = TddCycleEvaluator.Evaluate(fullScan, matchedDesign, verifiedTrx);
        Assert.Equal("verified", verifiedEval.Phase);
        Assert.Equal("matched", verifiedEval.DesignStatus);
        Assert.True(verifiedEval.HasDiscoveredTests);
        Assert.True(verifiedEval.HasRuntimeResults);
        Assert.Equal(2, verifiedEval.RuntimePassed);
        Assert.Equal(0, verifiedEval.RuntimeFailed);
        Assert.Equal(2, verifiedEval.MatchingPassedTests.Count);
        Assert.Empty(verifiedEval.MatchingFailedTests);
        Assert.Empty(verifiedEval.SliceChecks);

        // 5. drifted (design mismatch)
        var driftedDesign = matchedDesign with { Kind = "Query" };
        var driftedEval = TddCycleEvaluator.Evaluate(fullScan, driftedDesign, verifiedTrx);
        Assert.Equal("drifted", driftedEval.Phase);
        Assert.Equal("mismatch", driftedEval.DesignStatus);

        // 5b. drifted (active slice-scoped Error check even if design checks matched)
        var scanWithError = Analyzer.Analyze(dir.Path);
        scanWithError.Checks.Add(new CheckFinding(
            CheckSeverity.Error,
            ArchitectureChecker.LayerViolationCode,
            "Domain/Application violation.",
            Target: "Shop.Application -> Shop.Infrastructure",
            ProjectName: "Shop.Application"));
        var checkErrorEval = TddCycleEvaluator.Evaluate(scanWithError, matchedDesign, verifiedTrx);
        Assert.Equal("drifted", checkErrorEval.Phase);

        // 6. inconclusive (filtered scan -> not_determinable)
        var filteredScan = Analyzer.Analyze(dir.Path, AnalysisMode.Text, new AnalysisOptions
        {
            Include = ["does-not-exist/**/*.cs"],
        });
        var inconclusiveEval = TddCycleEvaluator.Evaluate(filteredScan, matchedDesign, verifiedTrx);
        Assert.Equal("inconclusive", inconclusiveEval.Phase);
        Assert.Equal("not_determinable", inconclusiveEval.DesignStatus);

        // 6b. inconclusive (!CanRunStrictChecks via error diagnostic)
        var brokenScan = Analyzer.Analyze(dir.Path);
        brokenScan.Diagnostics.Add(new AnalysisDiagnostic(
            AnalysisResult.SourceFileSkippedCode,
            AnalysisDiagnosticSeverity.Error,
            "Fatal read failure"));
        var brokenEval = TddCycleEvaluator.Evaluate(brokenScan, matchedDesign, verifiedTrx);
        Assert.Equal("inconclusive", brokenEval.Phase);
    }

    private static TrxTestSummary ParseTrx(params (string TestName, string Outcome)[] items)
    {
        var entries = string.Join(
            Environment.NewLine,
            items.Select(i => $"<UnitTestResult testName=\"{i.TestName}\" outcome=\"{i.Outcome}\" />"));
        var xml = $"""
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results>
                {entries}
              </Results>
            </TestRun>
            """;
        return TrxTestResultsParser.Parse(new StringReader(xml), "memory.trx");
    }

    private static void WriteFullSampleSolution(TempDir dir)
    {
        WriteCsproj(dir, "Shop.Domain/Shop.Domain.csproj", packages: [], refs: []);
        WriteCsproj(dir, "Shop.Application/Shop.Application.csproj",
            packages: ["MediatR", "FluentValidation"],
            refs: ["../Shop.Domain/Shop.Domain.csproj"]);
        WriteCsproj(dir, "Shop.Infrastructure/Shop.Infrastructure.csproj",
            packages: ["Microsoft.EntityFrameworkCore"],
            refs: ["../Shop.Application/Shop.Application.csproj"]);
        WriteCsproj(dir, "Shop.Api/Shop.Api.csproj",
            packages: [],
            refs: ["../Shop.Application/Shop.Application.csproj", "../Shop.Infrastructure/Shop.Infrastructure.csproj"]);
        WriteCsproj(dir, "Shop.Tests/Shop.Tests.csproj",
            packages: ["xunit", "Xunit.SkippableFact", "NSubstitute", "FluentAssertions"],
            refs: ["../Shop.Application/Shop.Application.csproj", "../Shop.Infrastructure/Shop.Infrastructure.csproj"]);

        dir.Write("Shop.Domain/Contracts.cs", ContractsCs);
        dir.Write("Shop.Domain/Entities.cs", """
            namespace Shop.Domain;

            using MediatR;

            public sealed class OrderEntity
            {
                public Guid Id { get; set; }
            }

            public sealed record OrderPlacedNotification(Guid OrderId) : INotification;
            """);

        dir.Write("Shop.Application/Orders/CreateOrderCommand.cs", """
            namespace Shop.Application.Orders;

            using FluentValidation;
            using MediatR;
            using Shop.Domain;

            public interface IOrderRepository
            {
                Task SaveAsync(OrderEntity entity);
            }

            public interface IShopDbContext
            {
                Microsoft.EntityFrameworkCore.DbSet<OrderEntity> Orders { get; }
            }

            public sealed class SystemClock
            {
                public DateTime UtcNow => DateTime.UtcNow;
            }

            public sealed record CreateOrderCommand(Guid CustomerId, decimal Amount) : IRequest<Guid>;

            public sealed class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand> { }

            public sealed class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, Guid>
            {
                private readonly IOrderRepository _repository;
                private readonly IShopDbContext _db;
                private readonly SystemClock _clock;
                private readonly IMediator _mediator;

                public CreateOrderCommandHandler(
                    IOrderRepository repository,
                    IShopDbContext db,
                    SystemClock clock,
                    IMediator mediator)
                {
                    _repository = repository;
                    _db = db;
                    _clock = clock;
                    _mediator = mediator;
                }

                public async Task<Guid> Handle(CreateOrderCommand request)
                {
                    var now = _clock.UtcNow;
                    var order = new OrderEntity { Id = Guid.NewGuid() };
                    await _repository.SaveAsync(order);
                    await _mediator.Publish(new OrderPlacedNotification(order.Id));
                    return order.Id;
                }
            }

            public sealed class OrderPlacedNotificationHandler : INotificationHandler<OrderPlacedNotification>
            {
                public Task Handle(OrderPlacedNotification notification) => Task.CompletedTask;
            }

            public sealed record ArchiveOrderCommand(Guid Id) : IRequest<bool>;

            public sealed class ArchiveOrderCommandHandler : IRequestHandler<ArchiveOrderCommand, bool>
            {
                public Task<bool> Handle(ArchiveOrderCommand request) => Task.FromResult(true);
            }

            public sealed record Order(Guid Id) : IRequest<bool>;

            public sealed class OrderHandler : IRequestHandler<Order, bool>
            {
                public Task<bool> Handle(Order request) => Task.FromResult(true);
            }

            public sealed class OrderItem { }

            public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> { }

            public static class DependencyInjection
            {
                public static void AddApplication(object services)
                {
                    services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
                }
            }
            """);

        dir.Write("Shop.Infrastructure/ShopDbContext.cs", """
            namespace Shop.Infrastructure;

            using Microsoft.EntityFrameworkCore;
            using Shop.Application.Orders;
            using Shop.Domain;

            public sealed class ShopDbContext : DbContext, IShopDbContext
            {
                public DbSet<OrderEntity> Orders { get; set; } = null!;
            }

            public sealed class OrderRepository : IOrderRepository
            {
                public Task SaveAsync(OrderEntity entity) => Task.CompletedTask;
            }

            public static class InfraSetup
            {
                public static void AddInfrastructure(object services)
                {
                    services.AddScoped<IOrderRepository, OrderRepository>();
                }
            }
            """);

        dir.Write("Shop.Api/OrdersEndpoints.cs", """
            namespace Shop.Api;

            using MediatR;
            using Shop.Application.Orders;

            public static class OrdersEndpoints
            {
                public static void Map(object app)
                {
                    app.MapPost("/api/orders", async (CreateOrderCommand cmd, IMediator mediator) =>
                        await mediator.Send(cmd));
                }
            }
            """);

        dir.Write("Shop.Tests/CreateOrderTests.cs", """
            namespace Shop.Tests;

            using MediatR;
            using Shop.Application.Orders;
            using Shop.Infrastructure;
            using Xunit;

            public sealed class CreateOrderTests
            {
                private readonly IMediator _mediator = null!;
                private readonly CreateOrderCommandHandler _sut;

                public CreateOrderTests()
                {
                    _sut = new CreateOrderCommandHandler(new OrderRepository(), new ShopDbContext(), new SystemClock(), _mediator);
                }

                [Fact]
                public async Task Dispatch_CreateOrder_Succeeds()
                {
                    await _mediator.Send(new CreateOrderCommand(Guid.NewGuid(), 10m));
                }

                [Theory]
                [InlineData(1)]
                public void Handler_ViaFieldAndCtor_ReturnsId(int dummy)
                {
                    _ = _sut;
                }

                [SkippableFact]
                public void Validator_RejectsEmpty()
                {
                    var validator = new CreateOrderCommandValidator();
                    _ = validator;
                }

                [TestMethod]
                public void Contract_TypeReference()
                {
                    var t = typeof(CreateOrderCommand);
                    _ = t;
                }

                [TestCase(1)]
                public async Task HelperDelegated_Test(int x)
                {
                    await DispatchViaHelper();
                }

                private async Task DispatchViaHelper()
                {
                    await _mediator.Send(new CreateOrderCommand(Guid.NewGuid(), 5m));
                }

                [Test]
                public void NUnit_Test_Attribute()
                {
                    var cmd = new CreateOrderCommand(Guid.NewGuid(), 1m);
                    _ = cmd;
                }

                [TestCaseSource("Cases")]
                public void NUnit_TestCaseSource_Attribute()
                {
                    var v = typeof(CreateOrderCommandValidator);
                    _ = v;
                }

                [DataTestMethod]
                public void MsTest_DataTestMethod_Attribute()
                {
                    var h = typeof(CreateOrderCommandHandler);
                    _ = h;
                }

                [SkippableTheory]
                public void Xunit_SkippableTheory_Attribute(CreateOrderCommand cmd)
                {
                    _ = cmd;
                }

                [Fact]
                public void Unrelated_OrderItem_AndDecoys()
                {
                    // Order should never match inside a comment.
                    var text = "Order";
                    var item = new OrderItem();
                    _ = text;
                    _ = item;
                }
            }

            public sealed class OrderSliceTests
            {
                [Fact]
                public async Task Order_Runs()
                {
                    IMediator mediator = null!;
                    await mediator.Send(new Order(Guid.NewGuid()));
                }
            }

            public sealed class ArchiveOrderTests
            {
                [Fact]
                public async Task ArchiveOrderCommand_Works()
                {
                    IMediator mediator = null!;
                    await mediator.Send(new ArchiveOrderCommand(Guid.NewGuid()));
                }
            }
            """);
    }

    private static void WriteCsproj(TempDir dir, string relativePath, string[] packages, string[] refs)
    {
        var pkgLines = string.Join(
            Environment.NewLine,
            packages.Select(p => $"    <PackageReference Include=\"{p}\" Version=\"1.0.0\" />"));
        var refLines = string.Join(
            Environment.NewLine,
            refs.Select(r => $"    <ProjectReference Include=\"{r}\" />"));
        var content = $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
            {pkgLines}
              </ItemGroup>
              <ItemGroup>
            {refLines}
              </ItemGroup>
            </Project>
            """;
        dir.Write(relativePath, content);
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "cqrslens-tdd-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public string Write(string relativePath, string contents)
        {
            var full = System.IO.Path.Combine(Path, relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
            File.WriteAllText(full, contents);
            return full;
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* best effort */ }
        }
    }
}
