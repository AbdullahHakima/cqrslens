using System.Text;
using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// End-to-end golden corpus. The expected sets are written down explicitly
/// rather than derived from one scanner, so text mode and Roslyn mode can both
/// be checked against the same truth. Both modes must also agree on every
/// canonical signature, including source locations.
/// </summary>
public sealed class GoldenCorpusTests
{
    private static readonly string Root = Path.Combine(
        FindRepositoryRoot(), "tests", "fixtures", "GoldenCorpus");

    private static readonly string[] ExpectedProjects =
    [
        "Golden.Api|Api|refs=Golden.Application|packages=",
        "Golden.Application|Application|refs=Golden.Domain|packages=FluentValidation,MediatR",
        "Golden.Domain|Domain|refs=|packages=",
        "Golden.Infrastructure|Infrastructure|refs=Golden.Application|packages=Microsoft.EntityFrameworkCore",
    ];

    private static readonly string[] ExpectedRequests =
    [
        "Golden.Application.Orders.ArchiveOrderCommand|Command|bool|ICommand|Golden.Application|" +
            "handlers=Golden.Application.Orders.ArchiveOrderCommandHandler|" +
            "validators=Golden.Application.Orders.ArchiveOrderCommandValidator|" +
            "props=Id:Guid|entry=POST /api/orders/archive|touched=|pubs=|dispatch=",
        "Golden.Application.Orders.CancelOrderCommand|Command|||Golden.Application|" +
            "handlers=Golden.Application.Orders.CancelOrderCommandHandler|validators=|" +
            "props=Id:Guid|entry=DELETE /api/orders/{id:guid}|touched=|" +
            "pubs=Golden.Domain.Events.OrderCancelledNotification|dispatch=",
        "Golden.Application.Orders.CreateOrderCommand|Command|Guid||Golden.Application|" +
            "handlers=Golden.Application.Orders.CreateOrderCommandHandler|" +
            "validators=Golden.Application.Orders.CreateOrderCommandValidator|" +
            "props=CustomerId:Guid,Lines:IReadOnlyList<CreateOrderLine>|" +
            "entry=POST /api/orders,POST /api/v2/orders|touched=Orders (Order)|" +
            "pubs=Golden.Domain.Events.OrderPlacedNotification|dispatch=",
        "Golden.Application.Orders.GetOrderQuery|Query|OrderDto||Golden.Application|" +
            "handlers=Golden.Application.Orders.GetOrderQueryHandler|validators=|props=Id:Guid|" +
            "entry=GET /api/orders/details/{id:guid},GET /api/orders/{id:guid}|touched=|pubs=|dispatch=",
    ];

    private static readonly string[] ExpectedHandlers =
    [
        "Golden.Application.Orders.ArchiveOrderCommandHandler|ArchiveOrderCommand|" +
            "Golden.Application.Orders|deps=|touched=|pubs=|dispatch=",
        "Golden.Application.Orders.CancelOrderCommandHandler|CancelOrderCommand|" +
            "Golden.Application.Orders|deps=IPublisher|touched=|" +
            "pubs=Golden.Domain.Events.OrderCancelledNotification|dispatch=",
        "Golden.Application.Orders.CreateOrderCommandHandler|CreateOrderCommand|" +
            "Golden.Application.Orders|deps=IOrderRepository,IPublisher|touched=Orders (Order)|" +
            "pubs=Golden.Domain.Events.OrderPlacedNotification|dispatch=",
        "Golden.Application.Orders.GetOrderQueryHandler|GetOrderQuery|" +
            "Golden.Application.Orders|deps=|touched=|pubs=|dispatch=",
    ];

    private static readonly string[] ExpectedValidators =
    [
        "Golden.Application.Orders.ArchiveOrderCommandValidator|ArchiveOrderCommand|Golden.Application.Orders",
        "Golden.Application.Orders.CreateOrderCommandValidator|CreateOrderCommand|Golden.Application.Orders",
    ];

    private static readonly string[] ExpectedNotifications =
    [
        "Golden.Domain.Events.OrderCancelledNotification||" +
            "handlers=Golden.Application.Notifications.OrderCancelledNotificationHandler",
        "Golden.Domain.Events.OrderPlacedNotification||" +
            "handlers=Golden.Application.Notifications.OrderPlacedEmailHandler," +
            "Golden.Application.Notifications.OrderPlacedNotificationHandler",
    ];

    private static readonly string[] ExpectedNotificationHandlers =
    [
        "Golden.Application.Notifications.OrderCancelledNotificationHandler|OrderCancelledNotification|" +
            "Golden.Domain.Events|deps=|touched=",
        "Golden.Application.Notifications.OrderPlacedEmailHandler|OrderPlacedNotification|" +
            "Golden.Domain.Events|deps=|touched=",
        "Golden.Application.Notifications.OrderPlacedNotificationHandler|OrderPlacedNotification|" +
            "Golden.Domain.Events|deps=|touched=",
    ];

    private static readonly string[] ExpectedDbContexts =
    [
        "Golden.Infrastructure.Persistence.ShopDbContext|sets=" +
            "Customers:Customer:Golden.Domain.Customer,OrderLines:OrderLine:Golden.Domain.OrderLine," +
            "Orders:Order:Golden.Domain.Order",
        "  entity Golden.Domain.Customer|Id:Guid:key=True:fk=False:nav=False:col=False:target=:req=True," +
            "Name:string:key=False:fk=False:nav=False:col=False:target=:req=False," +
            "Orders:List<Order>:key=False:fk=False:nav=True:col=True:target=Golden.Domain.Order:req=False",
        "  entity Golden.Domain.Order|Id:Guid:key=True:fk=False:nav=False:col=False:target=:req=True," +
            "Customer:Customer:key=False:fk=False:nav=True:col=False:target=Golden.Domain.Customer:req=False," +
            "CustomerId:Guid:key=False:fk=True:nav=False:col=False:target=:req=True," +
            "Lines:List<OrderLine>:key=False:fk=False:nav=True:col=True:target=Golden.Domain.OrderLine:req=False," +
            "Status:OrderStatus:key=False:fk=False:nav=False:col=False:target=:req=False",
        "  entity Golden.Domain.OrderLine|Id:Guid:key=True:fk=False:nav=False:col=False:target=:req=True," +
            "OrderId:Guid:key=False:fk=True:nav=False:col=False:target=:req=True," +
            "Quantity:int:key=False:fk=False:nav=False:col=False:target=:req=True",
        "  rel Golden.Domain.Customer->Golden.Domain.Order|pn=Orders|dn=Customer|fk=CustomerId|" +
            "req=True|kind=OneToMany|by=navigation",
        "  rel Golden.Domain.Order->Golden.Domain.OrderLine|pn=Lines|dn=|fk=OrderId|" +
            "req=True|kind=OneToMany|by=navigation",
    ];

    private static readonly string[] ExpectedEndpoints =
    [
        "DELETE /api/orders/{id:guid}|MapDelete|CancelOrderCommand|" +
            "Golden.Application.Orders.CancelOrderCommand|False|Golden.Api",
        "GET /api/orders/details/{id:guid}|OrdersController.Get|GetOrderQuery|" +
            "Golden.Application.Orders.GetOrderQuery|False|Golden.Api",
        "GET /api/orders/{id:guid}|MapGet|GetOrderQuery|" +
            "Golden.Application.Orders.GetOrderQuery|False|Golden.Api",
        "POST /api/orders|MapPost|CreateOrderCommand|" +
            "Golden.Application.Orders.CreateOrderCommand|False|Golden.Api",
        "POST /api/orders/archive|OrdersController.Archive|ArchiveOrderCommand|" +
            "Golden.Application.Orders.ArchiveOrderCommand|False|Golden.Api",
        "POST /api/v2/orders|MapPost|CreateOrderCommand|" +
            "Golden.Application.Orders.CreateOrderCommand|False|Golden.Api",
    ];

    private static readonly string[] ExpectedChecks =
    [
        "CommandWithoutValidator|Warning|Golden.Application.Orders.CancelOrderCommand|" +
            "Golden.Application|Golden.Application/Orders/GetAndCancel.cs",
    ];

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void Corpus_ProducesExactGoldenShape(AnalysisMode mode)
    {
        var result = Analyzer.Analyze(Root, mode);

        Assert.Equal(Root, result.RootPath);
        Assert.Equal(12, result.DiscoveredFileCount);
        Assert.Equal(12, result.FileCount);
        Assert.Empty(result.SkippedFiles);
        Assert.Empty(result.Diagnostics);
        Assert.True(result.CanRunStrictChecks);
        Assert.Equal(mode == AnalysisMode.Text ? "text" : "roslyn", result.ScannerName);
        Assert.NotEmpty(result.PhaseTimings);

        Assert.Equal(ExpectedProjects, ProjectSignatures(result));
        Assert.Equal(ExpectedRequests, RequestSignatures(result));
        Assert.Equal(ExpectedHandlers, HandlerSignatures(result));
        Assert.Equal(ExpectedValidators, ValidatorSignatures(result));
        Assert.Equal(["LoggingBehavior|Golden.Application"], BehaviorSignatures(result));
        Assert.Equal(ExpectedNotifications, NotificationSignatures(result));
        Assert.Equal(ExpectedNotificationHandlers, NotificationHandlerSignatures(result));
        Assert.Equal(ExpectedDbContexts, DbContextSignatures(result));
        Assert.Equal(ExpectedEndpoints, EndpointSignatures(result));
        Assert.Equal(ExpectedChecks, CheckSignatures(result));
    }

    [Fact]
    public void TextAndRoslynModes_AreCharacterForCharacterEquivalent()
    {
        var text = Canonical(Analyzer.Analyze(Root, AnalysisMode.Text), includeLocations: true);
        var roslyn = Canonical(Analyzer.Analyze(Root, AnalysisMode.Roslyn), includeLocations: true);

        Assert.Equal(text, roslyn);
    }

    [Fact]
    public void RepeatingTheSameScan_IsDeterministic()
    {
        var first = Canonical(Analyzer.Analyze(Root, AnalysisMode.Text), includeLocations: true);
        var second = Canonical(Analyzer.Analyze(Root, AnalysisMode.Text), includeLocations: true);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Decoys_NeverBecomeShapesOrEndpoints()
    {
        var result = Analyzer.Analyze(Root, AnalysisMode.Text);

        var decoyNames = new[]
        {
            "CommentedOutCommand", "CommentedOutHandler", "StringLiteralCommand",
            "BlockCommentedHandler", "BlockCommentedCommand", "NotARequest",
            "NotAHandler", "CreateOrderCommandFactory", "IRequestHandlerFactory",
            "CreateOrderLine", "OrderDto",
        };

        var shapeNames = result.Requests.Select(r => r.Name)
            .Concat(result.Handlers.Select(h => h.Name))
            .Concat(result.Validators.Select(v => v.Name))
            .Concat(result.Notifications.Select(n => n.Name))
            .Concat(result.NotificationHandlers.Select(h => h.Name))
            .Concat(result.Behaviors.Select(b => b.Name))
            .ToList();

        foreach (var decoy in decoyNames)
            Assert.DoesNotContain(decoy, shapeNames);

        Assert.DoesNotContain(result.Endpoints, e =>
            e.Route.Contains("fake", StringComparison.OrdinalIgnoreCase) ||
            e.Route.Contains("999", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Requests, r =>
            r.FullName.Contains("CommentedOut", StringComparison.Ordinal) ||
            r.FullName.Contains("StringLiteral", StringComparison.Ordinal));
    }

    [Fact]
    public void EveryReportedLocation_IsRootRelativeAndExistsOnDisk()
    {
        var result = Analyzer.Analyze(Root, AnalysisMode.Text);
        var locations = result.Requests.Select(r => r.Location)
            .Concat(result.Handlers.Select(h => h.Location))
            .Concat(result.Validators.Select(v => v.Location))
            .Concat(result.Notifications.Select(n => n.Location))
            .Concat(result.NotificationHandlers.Select(h => h.Location))
            .Concat(result.DbContexts.Select(c => c.Location))
            .Concat(result.Endpoints.Select(e => e.Location))
            .Concat(result.Checks.Select(c => c.Location))
            .ToList();

        Assert.All(locations, location =>
        {
            Assert.NotNull(location);
            Assert.True(location!.Line >= 1);
            Assert.True(location.Column >= 1);
            Assert.DoesNotContain('\\', location.File);
            Assert.False(Path.IsPathRooted(location.File));
            Assert.StartsWith("Golden.", location.File);
            Assert.True(File.Exists(Path.Combine(Root, location.File)), location.File);
        });
    }

    private static string Canonical(AnalysisResult result, bool includeLocations)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"files={result.FileCount}/{result.DiscoveredFileCount} strict={result.CanRunStrictChecks}");
        Append(sb, "projects", ProjectSignatures(result));
        Append(sb, "requests", RequestSignatures(result));
        Append(sb, "handlers", HandlerSignatures(result));
        Append(sb, "validators", ValidatorSignatures(result));
        Append(sb, "behaviors", BehaviorSignatures(result));
        Append(sb, "notifications", NotificationSignatures(result));
        Append(sb, "notificationHandlers", NotificationHandlerSignatures(result));
        Append(sb, "dbContexts", DbContextSignatures(result));
        Append(sb, "endpoints", EndpointSignatures(result));
        Append(sb, "checks", CheckSignatures(result));

        if (includeLocations)
        {
            var locations = result.Requests.Select(r => (r.FullName, r.Location))
                .Concat(result.Handlers.Select(h => (h.FullName, h.Location)))
                .Concat(result.Validators.Select(v => (v.FullName, v.Location)))
                .Concat(result.Notifications.Select(n => (n.FullName, n.Location)))
                .Concat(result.NotificationHandlers.Select(h => (h.FullName, h.Location)))
                .Concat(result.DbContexts.Select(c => (c.Name, c.Location)))
                .Concat(result.Endpoints.Select(e => ($"{e.HttpMethod} {e.Route}", e.Location)))
                .Concat(result.Checks.Select(c => (c.Code + ":" + c.Target, c.Location)))
                .OrderBy(pair => pair.Item1, StringComparer.Ordinal)
                .Select(pair => $"{pair.Item1}|{pair.Location}");

            Append(sb, "locations", locations);
        }

        return sb.ToString();
    }

    private static void Append(StringBuilder sb, string title, IEnumerable<string> lines)
    {
        sb.AppendLine($"-- {title} --");
        foreach (var line in lines) sb.AppendLine(line);
    }

    private static List<string> ProjectSignatures(AnalysisResult result)
        => result.Projects
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => $"{p.Name}|{p.Layer}|refs={Join(p.ReferenceNames)}|packages={Join(p.PackageReferences)}")
            .ToList();

    private static List<string> RequestSignatures(AnalysisResult result)
        => result.Requests
            .OrderBy(r => r.FullName, StringComparer.Ordinal)
            .Select(r => $"{r.FullName}|{r.Kind}|{r.ResponseType}|{r.ViaMarker}|{r.ProjectName}|" +
                         $"handlers={Join(r.Handlers)}|validators={Join(r.Validators)}|" +
                         $"props={JoinRaw(r.Properties.Select(p => $"{p.Name}:{p.Type}"))}|" +
                         $"entry={Join(r.Entrypoints)}|touched={Join(r.TouchedEntities)}|" +
                         $"pubs={Join(r.PublishedNotifications)}|dispatch={Join(r.DispatchedRequests)}")
            .ToList();

    private static List<string> HandlerSignatures(AnalysisResult result)
        => result.Handlers
            .OrderBy(h => h.FullName, StringComparer.Ordinal)
            .Select(h => $"{h.FullName}|{h.RequestName}|{h.RequestNamespace}|deps={Join(h.Dependencies)}|" +
                         $"touched={Join(h.TouchedEntities)}|pubs={Join(h.PublishedNotifications)}|" +
                         $"dispatch={Join(h.DispatchedRequests)}")
            .ToList();

    private static List<string> ValidatorSignatures(AnalysisResult result)
        => result.Validators
            .OrderBy(v => v.FullName, StringComparer.Ordinal)
            .Select(v => $"{v.FullName}|{v.TargetType}|{v.TargetNamespace}")
            .ToList();

    private static List<string> BehaviorSignatures(AnalysisResult result)
        => result.Behaviors
            .OrderBy(b => b.Name, StringComparer.Ordinal)
            .Select(b => $"{b.Name}|{b.ProjectName}")
            .ToList();

    private static List<string> NotificationSignatures(AnalysisResult result)
        => result.Notifications
            .OrderBy(n => n.FullName, StringComparer.Ordinal)
            .Select(n => $"{n.FullName}|{n.ViaMarker ?? ""}|handlers={Join(n.Handlers)}")
            .ToList();

    private static List<string> NotificationHandlerSignatures(AnalysisResult result)
        => result.NotificationHandlers
            .OrderBy(h => h.FullName, StringComparer.Ordinal)
            .Select(h => $"{h.FullName}|{h.NotificationName}|{h.NotificationNamespace}|" +
                         $"deps={Join(h.Dependencies)}|touched={Join(h.TouchedEntities)}")
            .ToList();

    private static List<string> DbContextSignatures(AnalysisResult result)
    {
        var lines = new List<string>();
        foreach (var context in result.DbContexts.OrderBy(c => c.Name, StringComparer.Ordinal))
        {
            lines.Add($"{context.Namespace}.{context.Name}|sets=" +
                      Join(context.DbSets.OrderBy(s => s.Property, StringComparer.Ordinal)
                          .Select(s => $"{s.Property}:{s.Entity}:{s.EntityFullName}")));
            foreach (var entity in context.Entities.OrderBy(e => e.FullName, StringComparer.Ordinal))
            {
                lines.Add($"  entity {entity.FullName}|" + JoinRaw(entity.Properties.Select(p =>
                    $"{p.Name}:{p.Type}:key={p.IsKey}:fk={p.IsForeignKey}:nav={p.IsNavigation}:" +
                    $"col={p.IsCollection}:target={p.TargetEntity}:req={p.IsRequired}")));
            }

            foreach (var relationship in context.Relationships
                         .OrderBy(r => r.PrincipalEntity, StringComparer.Ordinal)
                         .ThenBy(r => r.DependentEntity, StringComparer.Ordinal))
            {
                lines.Add($"  rel {relationship.PrincipalEntity}->{relationship.DependentEntity}|" +
                          $"pn={relationship.PrincipalNavigation}|dn={relationship.DependentNavigation}|" +
                          $"fk={relationship.ForeignKey}|req={relationship.IsRequired}|" +
                          $"kind={relationship.Kind}|by={relationship.ConfiguredBy}");
            }
        }

        return lines;
    }

    private static List<string> EndpointSignatures(AnalysisResult result)
        => result.Endpoints
            .OrderBy(e => e.HttpMethod, StringComparer.Ordinal)
            .ThenBy(e => e.Route, StringComparer.Ordinal)
            .Select(e => $"{e.HttpMethod} {e.Route}|{e.Name}|{e.TargetRequestName}|" +
                         $"{e.TargetRequestFullName}|{e.IsInferred}|{e.ProjectName}")
            .ToList();

    private static List<string> CheckSignatures(AnalysisResult result)
        => result.Checks
            .OrderBy(c => c.Code, StringComparer.Ordinal)
            .ThenBy(c => c.Target, StringComparer.Ordinal)
            .Select(c => $"{c.Code}|{c.Severity}|{c.Target}|{c.ProjectName}|{c.Location?.File}")
            .ToList();

    private static string Join(IEnumerable<string> values)
        => string.Join(",", values.OrderBy(v => v, StringComparer.Ordinal));

    private static string JoinRaw(IEnumerable<string> values)
        => string.Join(",", values);

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
