using Xunit;

namespace CqrsLens.Tests;

public sealed class NotificationsAndExpandedRulesTests : IDisposable
{
    private readonly string _root;

    public NotificationsAndExpandedRulesTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "cqrslens-notifications-rules-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private static void WriteFile(string root, string relativePath, string contents)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void NotificationsAndDomainEvents_AreDiscoveredAndLinkedInBothModes(AnalysisMode mode)
    {
        WriteFile(_root, "Shop.Application/Shop.Application.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <Using Include="MediatR" />
              </ItemGroup>
            </Project>
            """);

        WriteFile(_root, "Shop.Application/Events.cs", """
            namespace Shop.Application.Events;

            public interface IDomainEvent : INotification { }
            public interface IOrderDomainEventHandler<in T> : INotificationHandler<T>
                where T : IDomainEvent { }

            public sealed record OrderPlacedNotification(int OrderId) : INotification;
            public sealed record OrderShippedDomainEvent(int OrderId) : IDomainEvent;
            public sealed record UnhandledAuditEvent(string Action) : INotification;

            public sealed class SendOrderConfirmationEmailHandler
                : INotificationHandler<OrderPlacedNotification>
            {
            }

            public sealed class ReserveInventoryOnOrderPlacedHandler
                : INotificationHandler<OrderPlacedNotification>
            {
            }

            public sealed class NotifyCarrierOnOrderShippedHandler
                : IOrderDomainEventHandler<OrderShippedDomainEvent>
            {
            }
            """);

        var result = Analyzer.Analyze(_root, mode);

        Assert.Equal(3, result.Notifications.Count);
        Assert.Equal(3, result.NotificationHandlers.Count);

        var placed = Assert.Single(result.Notifications, n => n.Name == "OrderPlacedNotification");
        Assert.Null(placed.ViaMarker);
        Assert.Equal(
            new[]
            {
                "Shop.Application.Events.ReserveInventoryOnOrderPlacedHandler",
                "Shop.Application.Events.SendOrderConfirmationEmailHandler",
            },
            placed.Handlers);

        var shipped = Assert.Single(result.Notifications, n => n.Name == "OrderShippedDomainEvent");
        Assert.Equal("IDomainEvent", shipped.ViaMarker);
        Assert.Equal(
            new[] { "Shop.Application.Events.NotifyCarrierOnOrderShippedHandler" },
            shipped.Handlers);

        var unhandled = Assert.Single(result.Notifications, n => n.Name == "UnhandledAuditEvent");
        Assert.Empty(unhandled.Handlers);
        Assert.Contains(result.Checks, c =>
            c.Code == ArchitectureChecker.OrphanNotificationCode &&
            c.Severity == CheckSeverity.Warning &&
            c.Target == "Shop.Application.Events.UnhandledAuditEvent");

        var notificationMap = DiagramBuilder.NotificationMap(result);
        Assert.NotNull(notificationMap);
        Assert.Contains("ntf_OrderPlacedNotification[\"OrderPlacedNotification\"]:::clsNotification", notificationMap);
        Assert.Contains("ntf_UnhandledAuditEvent[\"UnhandledAuditEvent\"]:::clsOrphanNotification", notificationMap);
        Assert.Contains("-->|publishes to|", notificationMap);

        var svg = MermaidSvgRenderer.Render(notificationMap!);
        Assert.Contains("<svg ", svg);
        Assert.Contains("OrderPlacedNotification", svg);

        var md = ReportGenerator.Generate(result);
        Assert.Contains("## Domain Events & Notifications", md);
        Assert.Contains("| Notifications (domain events) | 3 |", md);
        Assert.Contains("| Notification handlers | 3 |", md);
        Assert.Contains("| OrderShippedDomainEvent | IDomainEvent | NotifyCarrierOnOrderShippedHandler |", md);

        var html = HtmlReportGenerator.Generate(result, DiagramRenderMode.Svg);
        Assert.Contains("Domain Events &amp; Notifications", html);
        Assert.Contains("OrderPlacedNotification", html);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);

        var json = JsonReportGenerator.Generate(result);
        Assert.Contains("\"notifications\":", json);
        Assert.Contains("\"notificationHandlers\":", json);
    }

    [Fact]
    public void ExpandedArchitectureChecks_DetectMultipleHandlersApplicationLayerViolationsCyclesAndOrphanValidators()
    {
        WriteFile(_root, "Shop.Domain/Shop.Domain.csproj", """
            <Project Sdk="Microsoft.NET.Sdk" />
            """);
        WriteFile(_root, "Shop.Application/Shop.Application.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="..\Shop.Domain\Shop.Domain.csproj" />
                <ProjectReference Include="..\Shop.Infrastructure\Shop.Infrastructure.csproj" />
              </ItemGroup>
            </Project>
            """);
        WriteFile(_root, "Shop.Infrastructure/Shop.Infrastructure.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="..\Shop.Application\Shop.Application.csproj" />
              </ItemGroup>
            </Project>
            """);

        WriteFile(_root, "Shop.Application/Features.cs", """
            namespace Shop.Application;

            public sealed record CreateOrderCommand(string Sku) : IRequest<int>;
            public sealed class PrimaryCreateOrderHandler : IRequestHandler<CreateOrderCommand, int> { }
            public sealed class DuplicateCreateOrderHandler : IRequestHandler<CreateOrderCommand, int> { }
            public sealed class CreateOrderValidator : AbstractValidator<CreateOrderCommand> { }

            public sealed record OrphanDto(string Name);
            public sealed class OrphanDtoValidator : AbstractValidator<OrphanDto> { }
            """);

        var result = Analyzer.Analyze(_root);

        // 1. MultipleHandlers (Error)
        Assert.Contains(result.Checks, c =>
            c.Code == ArchitectureChecker.MultipleHandlersCode &&
            c.Severity == CheckSeverity.Error &&
            c.Target == "Shop.Application.CreateOrderCommand");

        // 2. ApplicationLayerViolation (Error)
        Assert.Contains(result.Checks, c =>
            c.Code == ArchitectureChecker.ApplicationLayerViolationCode &&
            c.Severity == CheckSeverity.Error &&
            c.Target == "Shop.Application -> Shop.Infrastructure");

        // 3. CircularProjectDependency (Error)
        Assert.Contains(result.Checks, c =>
            c.Code == ArchitectureChecker.CircularProjectDependencyCode &&
            c.Severity == CheckSeverity.Error &&
            c.Target == "Shop.Application -> Shop.Infrastructure -> Shop.Application");

        // 4. OrphanValidator (Warning)
        Assert.Contains(result.Checks, c =>
            c.Code == ArchitectureChecker.OrphanValidatorCode &&
            c.Severity == CheckSeverity.Warning &&
            c.Target == "Shop.Application.OrphanDtoValidator");

        // Project graph highlights ApplicationLayerViolation with |violates|
        var projectGraph = DiagramBuilder.ProjectGraph(result);
        Assert.NotNull(projectGraph);
        Assert.Contains("-->|violates|", projectGraph);

        // SARIF generation includes descriptors for all new rules
        var sarif = SarifReportGenerator.Generate(result);
        Assert.Contains("MultipleHandlers", sarif);
        Assert.Contains("ApplicationLayerViolation", sarif);
        Assert.Contains("CircularProjectDependency", sarif);
        Assert.Contains("OrphanValidator", sarif);
    }
}
