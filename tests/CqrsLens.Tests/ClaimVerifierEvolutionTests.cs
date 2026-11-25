using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// Unit and integration tests for the expanded <c>verify_claim</c> relations:
/// <c>calls service</c>, <c>references project</c>, <c>touches entity</c>, and
/// <c>has test reference</c>, including their absence forms, aliases, provenance, and
/// fail-closed <c>not determinable</c> guarantees.
/// </summary>
public sealed class ClaimVerifierEvolutionTests
{
    private const string Contracts = """
        namespace MediatR
        {
            public interface IRequest<out TResponse> { }
            public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse> { }
            public interface INotification { }
            public interface INotificationHandler<in TNotification> where TNotification : INotification { }
        }

        namespace FluentValidation
        {
            public abstract class AbstractValidator<T> { }
        }

        namespace Microsoft.EntityFrameworkCore
        {
            public class DbContext { }
            public class DbSet<TEntity> where TEntity : class { }
        }
        """;

    // ---------------------------------------------------------------- calls service

    [Fact]
    public void CallsService_MatchesByImplementationInterfaceMethodAndQualifiedMethod()
    {
        using var dir = new TempDir();
        WriteServiceCallSolution(dir);
        var result = Analyzer.Analyze(dir.Path);

        var byImplFull = AgentClaimVerifier.Verify(result, "CreateOrderCommandHandler", "calls service", "Shop.Infra.PaymentGateway");
        var byImplSimple = AgentClaimVerifier.Verify(result, "CreateOrderCommand", "calls", "PaymentGateway");
        var byVia = AgentClaimVerifier.Verify(result, "CreateOrderCommandHandler", "invokes", "IPaymentGateway");
        var byMethod = AgentClaimVerifier.Verify(result, "CreateOrderCommand", "calls service", "ChargeAsync");
        var byTargetMethod = AgentClaimVerifier.Verify(result, "CreateOrderCommandHandler", "calls service", "PaymentGateway.ChargeAsync");
        var byViaMethod = AgentClaimVerifier.Verify(result, "OrderPlacedHandler", "calls service", "IAuditLog.RecordAsync");

        foreach (var check in new[] { byImplFull, byImplSimple, byVia, byMethod, byTargetMethod, byViaMethod })
        {
            Assert.Equal(ClaimVerdict.Supported, check.Verdict);
            Assert.Contains(check.Evidence, e => e.Contains(" (`", StringComparison.Ordinal));
        }

        var refuted = AgentClaimVerifier.Verify(result, "CreateOrderCommandHandler", "does not call service", "PaymentGateway");
        Assert.Equal(ClaimVerdict.NotSupported, refuted.Verdict);
        Assert.Contains(refuted.Evidence, e => e.Contains("ChargeAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void CallsService_CompleteScanRefutesAbsentServiceAndSupportsAbsence()
    {
        using var dir = new TempDir();
        WriteServiceCallSolution(dir);
        var result = Analyzer.Analyze(dir.Path);

        var positive = AgentClaimVerifier.Verify(result, "CreateOrderCommandHandler", "calls service", "AuditLog");
        var absence = AgentClaimVerifier.Verify(result, "CreateOrderCommandHandler", "does not call", "AuditLog");
        var invokeAbsence = AgentClaimVerifier.Verify(result, "CreateOrderCommand", "does not invoke", "AuditLog.RecordAsync");

        Assert.Equal(ClaimVerdict.NotSupported, positive.Verdict);
        Assert.Equal(ClaimVerdict.Supported, absence.Verdict);
        Assert.Equal(ClaimVerdict.Supported, invokeAbsence.Verdict);
        Assert.Contains(absence.Evidence, e => e.Contains(" (`", StringComparison.Ordinal));
    }

    [Fact]
    public void CallsService_AbsenceFailsClosedWhenIncompleteFilteredOrUnresolvedDispatch()
    {
        using var dir = new TempDir();
        WriteServiceCallSolution(dir);

        var filtered = Analyzer.Analyze(dir.Path, AnalysisMode.Text, new AnalysisOptions
        {
            Exclude = ["**/Ignored.cs"],
        });
        var filteredCheck = AgentClaimVerifier.Verify(filtered, "CreateOrderCommandHandler", "does not call service", "AuditLog");
        Assert.Equal(ClaimVerdict.NotDeterminable, filteredCheck.Verdict);
        Assert.Equal("source filters active", filteredCheck.Reason);

        var incomplete = Analyzer.Analyze(dir.Path);
        incomplete.Diagnostics.Add(new AnalysisDiagnostic(
            AnalysisResult.SourceFileSkippedCode,
            AnalysisDiagnosticSeverity.Warning,
            "could not read Extra.cs"));
        var incompleteCheck = AgentClaimVerifier.Verify(incomplete, "CreateOrderCommand", "does not call service", "AuditLog");
        Assert.Equal(ClaimVerdict.NotDeterminable, incompleteCheck.Verdict);
        Assert.Equal("scan incomplete", incompleteCheck.Reason);

        var withUnresolvedDispatch = Analyzer.Analyze(dir.Path);
        withUnresolvedDispatch.UnresolvedDispatches.Add(new UnresolvedDispatch
        {
            Method = "Send",
            ReceiverName = "_mediator",
            ProjectName = "Shop.Api",
            Location = new SourceLocation("Shop.Api/OrdersController.cs", 12, 9),
        });
        var unresolvedCheck = AgentClaimVerifier.Verify(withUnresolvedDispatch, "CreateOrderCommandHandler", "does not call service", "AuditLog");
        Assert.Equal(ClaimVerdict.NotDeterminable, unresolvedCheck.Verdict);
        Assert.Contains("unresolved dispatch", unresolvedCheck.Reason, StringComparison.OrdinalIgnoreCase);

        var ambiguousDi = Analyzer.Analyze(dir.Path);
        ambiguousDi.ServiceResolutions.Add(new ServiceResolution
        {
            Interface = "Shop.Domain.IAuditLog",
            InterfaceName = "Shop.Domain.IAuditLog",
            Identity = "Shop.Domain.IAuditLog",
            IsAmbiguous = true,
            Implementation = null,
        });
        ambiguousDi.Handlers.Single(h => h.Name == "CreateOrderCommandHandler").Dependencies.Add("IAuditLog");
        var ambiguousDiCheck = AgentClaimVerifier.Verify(ambiguousDi, "CreateOrderCommandHandler", "does not call service", "AuditLog");
        Assert.Equal(ClaimVerdict.NotDeterminable, ambiguousDiCheck.Verdict);
        Assert.Equal("ambiguous service resolution", ambiguousDiCheck.Reason);
    }

    // ---------------------------------------------------------------- references project

    [Fact]
    public void ReferencesProject_HandlesUnconditionalConditionalAndAbsentReferences()
    {
        using var dir = new TempDir();
        dir.Write("Shop.Domain/Shop.Domain.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        dir.Write("Shop.Extras/Shop.Extras.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        dir.Write("Shop.Application/Shop.Application.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="..\Shop.Domain\Shop.Domain.csproj" />
                <ProjectReference Include="..\Shop.Extras\Shop.Extras.csproj" Condition="'$(EnableExtras)' == 'true'" />
              </ItemGroup>
            </Project>
            """);

        var result = Analyzer.Analyze(dir.Path);

        var supported = AgentClaimVerifier.Verify(result, "Shop.Application", "references project", "Shop.Domain");
        Assert.Equal(ClaimVerdict.Supported, supported.Verdict);
        Assert.Contains(supported.Evidence, e => e.Contains(" (`", StringComparison.Ordinal));

        var refutedAbsence = AgentClaimVerifier.Verify(result, "Shop.Application", "does not reference", "Shop.Domain");
        Assert.Equal(ClaimVerdict.NotSupported, refutedAbsence.Verdict);

        var condPositive = AgentClaimVerifier.Verify(result, "Shop.Application", "project references", "Shop.Extras");
        var condAbsence = AgentClaimVerifier.Verify(result, "Shop.Application", "does not reference project", "Shop.Extras");
        Assert.Equal(ClaimVerdict.NotDeterminable, condPositive.Verdict);
        Assert.Equal(ClaimVerdict.NotDeterminable, condAbsence.Verdict);
        Assert.Equal("conditional project reference", condPositive.Reason);
        Assert.Contains("'$(EnableExtras)' == 'true'", condPositive.Detail, StringComparison.Ordinal);
        Assert.Contains("'$(EnableExtras)' == 'true'", condAbsence.Detail, StringComparison.Ordinal);

        var absentPositive = AgentClaimVerifier.Verify(result, "Shop.Domain", "references project", "Shop.Application");
        var absentNegative = AgentClaimVerifier.Verify(result, "Shop.Domain", "does not reference project", "Shop.Application");
        Assert.Equal(ClaimVerdict.NotSupported, absentPositive.Verdict);
        Assert.Equal(ClaimVerdict.Supported, absentNegative.Verdict);
        Assert.Contains(absentNegative.Evidence, e => e.Contains(" (`", StringComparison.Ordinal));
    }

    [Fact]
    public void ReferencesProject_FailsClosedOnFilteredOrIncompleteScans()
    {
        using var dir = new TempDir();
        dir.Write("Shop.Domain/Shop.Domain.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        dir.Write("Shop.Application/Shop.Application.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        var filtered = Analyzer.Analyze(dir.Path, AnalysisMode.Text, new AnalysisOptions
        {
            Include = ["Shop.Application/**"],
        });
        var check = AgentClaimVerifier.Verify(filtered, "Shop.Application", "does not reference project", "Shop.Domain");
        Assert.Equal(ClaimVerdict.NotDeterminable, check.Verdict);
        Assert.Equal("source filters active", check.Reason);

        var incomplete = Analyzer.Analyze(dir.Path);
        incomplete.Diagnostics.Add(new AnalysisDiagnostic(
            AnalysisResult.SourceFileSkippedCode,
            AnalysisDiagnosticSeverity.Error,
            "broken"));
        var incompleteCheck = AgentClaimVerifier.Verify(incomplete, "Shop.Application", "does not reference project", "Shop.Domain");
        Assert.Equal(ClaimVerdict.NotDeterminable, incompleteCheck.Verdict);
        Assert.Equal("scan incomplete", incompleteCheck.Reason);
    }

    // ---------------------------------------------------------------- touches entity

    [Fact]
    public void TouchesEntity_MatchesExactAndDbSetEntityLabelsWithoutSubstringCollisions()
    {
        using var dir = new TempDir();
        WriteEfCoreSolution(dir);
        var result = Analyzer.Analyze(dir.Path);

        var bySimpleEntity = AgentClaimVerifier.Verify(result, "CreateOrderCommandHandler", "touches entity", "Order");
        var byExactLabel = AgentClaimVerifier.Verify(result, "CreateOrderCommand", "reads or writes entity", "Orders (Order)");
        var byFullEntity = AgentClaimVerifier.Verify(result, "CreateOrderCommand", "uses entity", "Shop.Domain.Order");

        Assert.Equal(ClaimVerdict.Supported, bySimpleEntity.Verdict);
        Assert.Equal(ClaimVerdict.Supported, byExactLabel.Verdict);
        Assert.Equal(ClaimVerdict.Supported, byFullEntity.Verdict);
        Assert.Contains(bySimpleEntity.Evidence, e => e.Contains(" (`", StringComparison.Ordinal));

        var refutedAbsence = AgentClaimVerifier.Verify(result, "CreateOrderCommand", "does not touch entity", "Order");
        Assert.Equal(ClaimVerdict.NotSupported, refutedAbsence.Verdict);

        // OrderItem != Order in both directions: substring must not match.
        var orderItemCheck = AgentClaimVerifier.Verify(result, "CreateOrderCommandHandler", "touches entity", "OrderItem");
        var reverseSubstringCheck = AgentClaimVerifier.Verify(result, "AddOrderItemCommandHandler", "touches entity", "Order");
        var reverseSubstringAbsence = AgentClaimVerifier.Verify(result, "AddOrderItemCommandHandler", "does not touch entity", "Order");
        var reverseExact = AgentClaimVerifier.Verify(result, "AddOrderItemCommandHandler", "touches entity", "OrderItem");
        var nonTouchAbsence = AgentClaimVerifier.Verify(result, "ListOrdersQueryHandler", "does not touch entity", "OrderItem");
        var nonTouchHandlerAbsence = AgentClaimVerifier.Verify(result, "PingQueryHandler", "does not touch entity", "Order");
        Assert.Equal(ClaimVerdict.NotSupported, orderItemCheck.Verdict);
        Assert.Equal(ClaimVerdict.NotSupported, reverseSubstringCheck.Verdict);
        Assert.Equal(ClaimVerdict.Supported, reverseSubstringAbsence.Verdict);
        Assert.Equal(ClaimVerdict.Supported, reverseExact.Verdict);
        Assert.Equal(ClaimVerdict.Supported, nonTouchAbsence.Verdict);
        Assert.Equal(ClaimVerdict.Supported, nonTouchHandlerAbsence.Verdict);
    }

    [Fact]
    public void TouchesEntity_AbsenceFailsClosedOnFilteredOrIncompleteScans()
    {
        using var dir = new TempDir();
        WriteEfCoreSolution(dir);

        var filtered = Analyzer.Analyze(dir.Path, AnalysisMode.Text, new AnalysisOptions
        {
            Exclude = ["**/Ignored.cs"],
        });
        var filteredCheck = AgentClaimVerifier.Verify(filtered, "PingQueryHandler", "does not touch entity", "Order");
        Assert.Equal(ClaimVerdict.NotDeterminable, filteredCheck.Verdict);
        Assert.Equal("source filters active", filteredCheck.Reason);

        var incomplete = Analyzer.Analyze(dir.Path);
        incomplete.Diagnostics.Add(new AnalysisDiagnostic(
            AnalysisResult.SourceFileSkippedCode,
            AnalysisDiagnosticSeverity.Warning,
            "skipped DbContext"));
        var incompleteCheck = AgentClaimVerifier.Verify(incomplete, "PingQueryHandler", "does not touch entity", "Order");
        Assert.Equal(ClaimVerdict.NotDeterminable, incompleteCheck.Verdict);
        Assert.Equal("scan incomplete", incompleteCheck.Reason);
    }

    // ---------------------------------------------------------------- has test reference

    [Fact]
    public void HasTestReference_DiscoversStaticTestCouplingWithProvenanceAndCaveat()
    {
        using var dir = new TempDir();
        WriteTestedSolution(dir);
        var result = Analyzer.Analyze(dir.Path);

        var bareRequest = AgentClaimVerifier.Verify(result, "CreateOrderCommand", "has test reference", null);
        var byProject = AgentClaimVerifier.Verify(result, "CreateOrderCommand", "referenced by tests", "Shop.Application.Tests");
        var byClass = AgentClaimVerifier.Verify(result, "CreateOrderCommandValidator", "has tests", "CreateOrderCommandValidatorTests");

        foreach (var check in new[] { bareRequest, byProject, byClass })
        {
            Assert.Equal(ClaimVerdict.Supported, check.Verdict);
            Assert.Contains(check.Evidence, e => e.Contains(".cs:", StringComparison.Ordinal) && e.Contains(" (`", StringComparison.Ordinal));
            var fullText = $"{check.Basis}\n{string.Join("\n", check.Evidence)}";
            Assert.Contains("test-project coupling, not runtime assertion coverage", fullText, StringComparison.OrdinalIgnoreCase);
        }

        var refutedAbsence = AgentClaimVerifier.Verify(result, "CreateOrderCommand", "has no test reference", null);
        Assert.Equal(ClaimVerdict.NotSupported, refutedAbsence.Verdict);
        Assert.Contains("test-project coupling, not runtime assertion coverage",
            $"{refutedAbsence.Basis}\n{string.Join("\n", refutedAbsence.Evidence)}",
            StringComparison.OrdinalIgnoreCase);

        var untestedAbsence = AgentClaimVerifier.Verify(result, "CancelOrderCommand", "has no tests", null);
        var untestedPositive = AgentClaimVerifier.Verify(result, "CancelOrderCommand", "has test reference", null);
        var wrongProjectAbsence = AgentClaimVerifier.Verify(result, "CreateOrderCommand", "has no test reference", "Other.Tests");
        Assert.Equal(ClaimVerdict.Supported, untestedAbsence.Verdict);
        Assert.Equal(ClaimVerdict.NotSupported, untestedPositive.Verdict);
        Assert.Equal(ClaimVerdict.Supported, wrongProjectAbsence.Verdict);

        var formatted = AgentReportGenerator.VerifyClaim(result, "CreateOrderCommand", "has test reference", null);
        Assert.StartsWith("verdict: supported", formatted, StringComparison.Ordinal);
        Assert.Contains("test-project coupling, not runtime assertion coverage", formatted, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HasTestReference_AbsenceFailsClosedOnFilteredOrIncompleteScans()
    {
        using var dir = new TempDir();
        WriteTestedSolution(dir);

        var filtered = Analyzer.Analyze(dir.Path, AnalysisMode.Text, new AnalysisOptions
        {
            Include = ["Shop.Application/**"],
        });
        var filteredAbsence = AgentClaimVerifier.Verify(filtered, "CancelOrderCommand", "has no test reference", null);
        Assert.Equal(ClaimVerdict.NotDeterminable, filteredAbsence.Verdict);
        Assert.Equal("source filters active", filteredAbsence.Reason);

        var incomplete = Analyzer.Analyze(dir.Path);
        incomplete.Diagnostics.Add(new AnalysisDiagnostic(
            AnalysisResult.SourceFileSkippedCode,
            AnalysisDiagnosticSeverity.Warning,
            "skipped test file"));
        var incompleteAbsence = AgentClaimVerifier.Verify(incomplete, "CancelOrderCommand", "has no test reference", null);
        Assert.Equal(ClaimVerdict.NotDeterminable, incompleteAbsence.Verdict);
        Assert.Equal("scan incomplete", incompleteAbsence.Reason);
    }

    // ---------------------------------------------------------------- fixtures

    private static void WriteServiceCallSolution(TempDir dir)
    {
        dir.Write("Shop.App/Shop.App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        dir.Write("Shop.App/Contracts.cs", Contracts);
        dir.Write("Shop.App/Services.cs", """
            namespace Shop.Domain
            {
                public interface IPaymentGateway { void ChargeAsync(); }
                public interface IAuditLog { void RecordAsync(); }
            }

            namespace Shop.Infra
            {
                public sealed class PaymentGateway : Shop.Domain.IPaymentGateway
                {
                    public void ChargeAsync() { }
                }

                public sealed class AuditLog : Shop.Domain.IAuditLog
                {
                    public void RecordAsync() { }
                }
            }
            """);
        dir.Write("Shop.App/Orders.cs", """
            namespace Shop.Application
            {
                using MediatR;
                using Shop.Domain;

                public sealed record OrderPlaced : INotification;
                public sealed record CreateOrderCommand : IRequest<int>;

                public sealed class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, int>
                {
                    private readonly IPaymentGateway _payments;
                    public CreateOrderCommandHandler(IPaymentGateway payments) => _payments = payments;
                    public int Handle(CreateOrderCommand request)
                    {
                        _payments.ChargeAsync();
                        return 1;
                    }
                }

                public sealed class OrderPlacedHandler : INotificationHandler<OrderPlaced>
                {
                    private readonly IAuditLog _audit;
                    public OrderPlacedHandler(IAuditLog audit) => _audit = audit;
                    public void Handle(OrderPlaced notification) => _audit.RecordAsync();
                }
            }
            """);
    }

    private static void WriteEfCoreSolution(TempDir dir)
    {
        dir.Write("Shop.App/Shop.App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        dir.Write("Shop.App/Contracts.cs", Contracts);
        dir.Write("Shop.App/Model.cs", """
            namespace Shop.Domain
            {
                public sealed class Order { public int Id { get; set; } }
                public sealed class OrderItem { public int Id { get; set; } }
            }

            namespace Shop.Persistence
            {
                using Microsoft.EntityFrameworkCore;
                using Shop.Domain;

                public sealed class ShopDbContext : DbContext
                {
                    public DbSet<Order> Orders { get; set; } = null!;
                    public DbSet<OrderItem> OrderItems { get; set; } = null!;
                }
            }
            """);
        dir.Write("Shop.App/Slices.cs", """
            namespace Shop.Application
            {
                using MediatR;
                using Shop.Persistence;

                public sealed record CreateOrderCommand : IRequest<int>;
                public sealed record AddOrderItemCommand : IRequest<int>;
                public sealed record ListOrdersQuery : IRequest<int>;
                public sealed record PingQuery : IRequest<int>;

                public sealed class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, int>
                {
                    private readonly ShopDbContext _db;
                    public CreateOrderCommandHandler(ShopDbContext db) => _db = db;
                    public int Handle(CreateOrderCommand request)
                    {
                        var x = _db.Orders;
                        return 0;
                    }
                }

                public sealed class AddOrderItemCommandHandler : IRequestHandler<AddOrderItemCommand, int>
                {
                    private readonly ShopDbContext _db;
                    public AddOrderItemCommandHandler(ShopDbContext db) => _db = db;
                    public int Handle(AddOrderItemCommand request)
                    {
                        var x = _db.OrderItems;
                        return 0;
                    }
                }

                public sealed class ListOrdersQueryHandler : IRequestHandler<ListOrdersQuery, int>
                {
                    private readonly ShopDbContext _db;
                    public ListOrdersQueryHandler(ShopDbContext db) => _db = db;
                    public int Handle(ListOrdersQuery request)
                    {
                        var x = _db.Orders;
                        return 0;
                    }
                }

                public sealed class PingQueryHandler : IRequestHandler<PingQuery, int>
                {
                    public int Handle(PingQuery request) => 0;
                }
            }
            """);
    }

    private static void WriteTestedSolution(TempDir dir)
    {
        dir.Write("Shop.Application/Shop.Application.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        dir.Write("Shop.Application/Contracts.cs", Contracts);
        dir.Write("Shop.Application/Orders.cs", """
            namespace Shop.Application.Orders
            {
                using FluentValidation;
                using MediatR;

                public sealed record CreateOrderCommand(int CustomerId) : IRequest<int>;
                public sealed record CancelOrderCommand(int OrderId) : IRequest<int>;

                public sealed class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, int>
                {
                    public int Handle(CreateOrderCommand request) => 1;
                }

                public sealed class CancelOrderCommandHandler : IRequestHandler<CancelOrderCommand, int>
                {
                    public int Handle(CancelOrderCommand request) => 1;
                }

                public sealed class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
                {
                }
            }
            """);
        dir.Write("Shop.Application.Tests/Shop.Application.Tests.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="xunit" Version="2.9.3" />
                <ProjectReference Include="..\Shop.Application\Shop.Application.csproj" />
              </ItemGroup>
            </Project>
            """);
        dir.Write("Shop.Application.Tests/CreateOrderCommandValidatorTests.cs", """
            namespace Shop.Application.Tests
            {
                using Shop.Application.Orders;
                using Xunit;

                public sealed class CreateOrderCommandValidatorTests
                {
                    [Fact]
                    public void ValidatesCreateOrderCommand()
                    {
                        var validator = new CreateOrderCommandValidator();
                        var command = new CreateOrderCommand(42);
                    }
                }
            }
            """);
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "cqrslens-claim-evo-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public void Write(string relativePath, string contents)
        {
            var full = System.IO.Path.Combine(Path, relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
            File.WriteAllText(full, contents);
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* best effort */ }
        }
    }
}
