using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// Phase 3 polish: behaviour on non-CQRS and framework codebases.
///
/// <list type="bullet">
/// <item>Problem A: validators over non-request types in a solution with no
/// request model are not orphan CQRS validators.</item>
/// <item>Problem B: name-guessed layers that do not describe the solution do
/// not produce error-level layer findings.</item>
/// <item>Problem C: notification handlers inventory their event as a
/// notification when the event carries no marker interface.</item>
/// </list>
/// Each problem also has a genuine-case test proving the check still fires.
/// </summary>
public sealed class Phase3PolishTests : IDisposable
{
    private readonly string _root;

    public Phase3PolishTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cqrslens-p3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private void WriteProject(string folder, string name, params string[] references)
    {
        var dir = Path.Combine(_root, folder);
        Directory.CreateDirectory(dir);
        var refs = string.Concat(references.Select(r => $"    <ProjectReference Include=\"{r}\" />\n"));
        var itemGroup = references.Length == 0 ? "" : $"  <ItemGroup>\n{refs}  </ItemGroup>\n";
        File.WriteAllText(Path.Combine(dir, name + ".csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" + itemGroup + "</Project>\n");
    }

    private void WriteFile(string relative, string code)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, code);
    }

    /// <summary>Source-declared contracts so Roslyn mode compiles.</summary>
    private void WriteMediatorContracts()
    {
        WriteFile("Contracts/Contracts.cs", """
            namespace MediatR
            {
                public interface IRequest { }
                public interface IRequest<TResponse> { }
                public interface IRequestHandler<TRequest> { }
                public interface IRequestHandler<TRequest, TResponse> { }
                public interface INotification { }
            }

            namespace FluentValidation
            {
                public abstract class AbstractValidator<T> { }
                public interface IValidator<T> { }
            }
            """);
    }

    // ---------------------------------------------------------------------
    // Problem A — OrphanValidator noise on non-MediatR codebases
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void NonCqrsSolution_ValidatorsOverNonRequestTypes_AreNotOrphanValidators(AnalysisMode mode)
    {
        // nopCommerce shape: many FluentValidation validators over view models /
        // DTOs and not a single CQRS request in the solution.
        WriteProject("App", "App");
        WriteMediatorContracts();
        WriteFile("App/ViewModels.cs", """
            using FluentValidation;

            namespace App.ViewModels;

            public sealed class CustomerDto { public string Name { get; set; } = ""; }
            public sealed class AddressDto { public string City { get; set; } = ""; }
            public sealed class OrderDto { public int Id { get; set; } }

            public sealed class CustomerDtoValidator : AbstractValidator<CustomerDto> { }
            public sealed class AddressDtoValidator : AbstractValidator<AddressDto> { }
            public sealed class OrderDtoValidator : AbstractValidator<OrderDto> { }
            """);

        var result = Analyzer.Analyze(_root, mode);

        Assert.Empty(result.Requests);
        Assert.Equal(3, result.Validators.Count);
        Assert.DoesNotContain(result.Checks, c => c.Code == ArchitectureChecker.OrphanValidatorCode);
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void MediatRShapedSolution_ValidatorForRenamedRequest_IsStillAnOrphan(AnalysisMode mode)
    {
        // Genuine case: the solution is MediatR-shaped (it has requests), and a
        // validator's target request was renamed/deleted. That must still be an
        // orphan warning; the Phase 3 fix must not disable the check.
        WriteProject("App", "App");
        WriteMediatorContracts();
        WriteFile("App/Features.cs", """
            using MediatR;
            using FluentValidation;

            namespace App.Features;

            public sealed record KeepOrderCommand : IRequest<int>;
            public sealed class KeepOrderHandler : IRequestHandler<KeepOrderCommand, int> { }
            public sealed class KeepOrderValidator : AbstractValidator<KeepOrderCommand> { }

            // This request no longer exists; only its validator remains.
            public sealed class RenamedCommandValidator : AbstractValidator<RenamedCommand> { }
            """);

        var result = Analyzer.Analyze(_root, mode);

        Assert.Single(result.Requests);
        var orphan = Assert.Single(
            result.Checks,
            c => c.Code == ArchitectureChecker.OrphanValidatorCode);
        Assert.Equal(CheckSeverity.Warning, orphan.Severity);
        Assert.Equal("App.Features.RenamedCommandValidator", orphan.Target);
    }

    // ---------------------------------------------------------------------
    // Problem B — error-level layer findings from name-guessed layers
    // ---------------------------------------------------------------------

    [Fact]
    public void LayerModelConfidence_IsAboutConventionCoverage_NotProjectNames()
    {
        static ProjectInfo Project(string name, string layer) => new()
        {
            Name = name, FilePath = $"/tmp/{name}.csproj", DirectoryPath = $"/tmp/{name}", Layer = layer,
        };

        // Convention classification describes the whole solution.
        Assert.True(LayerModelConfidence.IsAsserted(new[]
        {
            Project("Shop.Domain", "Domain"),
            Project("Shop.Application", "Application"),
            Project("Shop.Infrastructure", "Infrastructure"),
            Project("Shop.Api", "Api"),
        }));

        // Mostly ad-hoc folder buckets with a couple of coincidental layer names.
        Assert.False(LayerModelConfidence.IsAsserted(new[]
        {
            Project("Framework.Core", "framework"),
            Project("Framework.Modules", "modules"),
            Project("Framework.Templates", "templates"),
            Project("Framework.Content", "content"),
            Project("Framework.Account.Application", "Application"),
            Project("Framework.UI.Navigation", "UI"),
            Project("Framework.Infrastructure", "Infrastructure"),
        }));

        // Tests do not count towards the denominator.
        Assert.True(LayerModelConfidence.IsAsserted(new[]
        {
            Project("Shop.Domain", "Domain"),
            Project("Shop.Infrastructure", "Infrastructure"),
            Project("Shop.Domain.Tests", "Tests"),
        }));
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void NonCleanArchitectureShape_ApplicationToInfrastructure_IsNotAnError(AnalysisMode mode)
    {
        // OrchardCore shape: a large solution whose projects are mostly ad-hoc
        // buckets, plus a couple of coincidentally layer-named projects. The
        // conventional labels do not describe the architecture, so an outward
        // reference must not fail --strict. It is still reported, as a warning.
        WriteProject("Platform.Core", "Platform.Core");
        WriteProject("Platform.Modules", "Platform.Modules");
        WriteProject("Platform.Content", "Platform.Content");
        WriteProject("Platform.DisplayManagement", "Platform.DisplayManagement");
        WriteProject("Platform.Application.Targets", "Platform.Application.Targets",
            @"..\Platform.Infrastructure\Platform.Infrastructure.csproj");
        WriteProject("Platform.Infrastructure", "Platform.Infrastructure");

        var result = Analyzer.Analyze(_root, mode);

        Assert.DoesNotContain(result.Checks, c =>
            c.Code == ArchitectureChecker.ApplicationLayerViolationCode &&
            c.Severity == CheckSeverity.Error);
        Assert.False(ArchitectureChecker.HasErrors(result.Checks));

        var reported = Assert.Single(result.Checks, c =>
            c.Code == ArchitectureChecker.ApplicationLayerViolationCode);
        Assert.Equal(CheckSeverity.Warning, reported.Severity);
        Assert.Equal("Platform.Application.Targets -> Platform.Infrastructure", reported.Target);
    }

    [Fact]
    public void ConfiguredLayers_AreNotSecondGuessedAndKeepErrorSeverity()
    {
        // A user-configured layer model is not name-guessed: uncertainty must not
        // downgrade a violation the caller explicitly asked to be checked.
        WriteProject("Platform.Core", "Platform.Core");
        WriteProject("Platform.Modules", "Platform.Modules");
        WriteProject("Platform.Content", "Platform.Content");
        WriteProject("Platform.DisplayManagement", "Platform.DisplayManagement");
        WriteProject("Platform.Application.Targets", "Platform.Application.Targets",
            @"..\Platform.Infrastructure\Platform.Infrastructure.csproj");
        WriteProject("Platform.Infrastructure", "Platform.Infrastructure");

        var options = new AnalysisOptions
        {
            LayerOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Platform.Application.Targets"] = "Application",
                ["Platform.Infrastructure"] = "Infrastructure",
            },
        };

        var result = Analyzer.Analyze(_root, AnalysisMode.Text, options);

        var violation = Assert.Single(result.Checks, c =>
            c.Code == ArchitectureChecker.ApplicationLayerViolationCode);
        Assert.Equal(CheckSeverity.Error, violation.Severity);
        Assert.True(ArchitectureChecker.HasErrors(result.Checks));
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void ConventionallyLayeredSolution_ApplicationToInfrastructure_IsStillAnError(AnalysisMode mode)
    {
        // Genuine case: a conventionally-layered Clean Architecture solution.
        // The same reference must remain an error-level violation.
        WriteProject("Shop.Domain", "Shop.Domain");
        WriteProject("Shop.Infrastructure", "Shop.Infrastructure");
        WriteProject("Shop.Application", "Shop.Application", @"..\Shop.Infrastructure\Shop.Infrastructure.csproj");

        var result = Analyzer.Analyze(_root, mode);

        var violation = Assert.Single(result.Checks, c =>
            c.Code == ArchitectureChecker.ApplicationLayerViolationCode);
        Assert.Equal(CheckSeverity.Error, violation.Severity);
        Assert.Equal("Shop.Application -> Shop.Infrastructure", violation.Target);
        Assert.True(ArchitectureChecker.HasErrors(result.Checks));
    }

    // ---------------------------------------------------------------------
    // Problem C — notifications not inventoried from notification handlers
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void NotificationHandler_WithUnmarkedEvent_InventoriesTheEventWithoutDoubleCounting(AnalysisMode mode)
    {
        // Ardalis/custom-mediator shape: notification handlers whose event types
        // carry no marker interface, alongside a normal marker notification that
        // must not be double counted.
        WriteProject("App", "App");
        WriteMediatorContracts();
        WriteFile("App/Events.cs", """
            using MediatR;

            namespace App.Events;

            public sealed record ContributorDeletedEvent(int Id);
            public sealed class ContributorDeletedHandler : INotificationHandler<ContributorDeletedEvent> { }

            public sealed record OrderPlacedNotification(int Id) : INotification;
            public sealed class OrderPlacedHandler : INotificationHandler<OrderPlacedNotification> { }
            """);

        var result = Analyzer.Analyze(_root, mode);

        Assert.Equal(2, result.Notifications.Count);
        Assert.Equal(2, result.NotificationHandlers.Count);

        var contributorDeleted = Assert.Single(result.Notifications, n => n.Name == "ContributorDeletedEvent");
        Assert.Null(contributorDeleted.ViaMarker);
        Assert.Equal("App.Events.ContributorDeletedHandler", Assert.Single(contributorDeleted.Handlers));

        // The marker-declared notification keeps its single inventory entry.
        var placed = Assert.Single(result.Notifications, n => n.Name == "OrderPlacedNotification");
        Assert.Equal("App.Events.OrderPlacedHandler", Assert.Single(placed.Handlers));

        Assert.DoesNotContain(result.Checks, c => c.Code == ArchitectureChecker.OrphanNotificationCode);
    }
}
