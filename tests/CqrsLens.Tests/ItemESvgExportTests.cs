using System.Xml.Linq;
using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// Item E: standalone SVG export coverage. Verifies the file set and names,
/// well-formed XML, additive behavior, and byte-for-byte determinism.
/// </summary>
public sealed class ItemESvgExportTests : IDisposable
{
    private static readonly XNamespace SvgNs = "http://www.w3.org/2000/svg";

    private readonly string _root;

    public ItemESvgExportTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cqrslens-iteme-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private static AnalysisResult RepresentativeResult()
    {
        var result = new AnalysisResult { RootPath = "/tmp/shop" };

        var app = new ProjectInfo
        {
            Name = "Shop.Application",
            FilePath = "/tmp/Shop.Application/Shop.Application.csproj",
            DirectoryPath = "/tmp/Shop.Application",
            Layer = "Application",
        };
        var infra = new ProjectInfo
        {
            Name = "Shop.Infrastructure",
            FilePath = "/tmp/Shop.Infrastructure/Shop.Infrastructure.csproj",
            DirectoryPath = "/tmp/Shop.Infrastructure",
            Layer = "Infrastructure",
        };
        app.ReferenceNames.Add("Shop.Infrastructure");
        result.Projects.Add(app);
        result.Projects.Add(infra);

        var commandA = new CqrsRequest
        {
            Name = "CommandA",
            Namespace = "Shop.Application.Orders",
            ProjectName = "Shop.Application",
        };
        commandA.Handlers.Add("Shop.Application.Orders.CommandAHandler");
        result.Requests.Add(commandA);
        result.Handlers.Add(new HandlerInfo
        {
            Name = "CommandAHandler",
            Namespace = "Shop.Application.Orders",
            RequestName = "CommandA",
            ProjectName = "Shop.Application",
        });

        result.FlowEdges.Add(new FlowEdge
        {
            Kind = FlowEdgeKind.EndpointToRequest,
            From = "OrdersController.Create",
            To = commandA.FullName,
            ProjectName = "Shop.Application",
        });
        result.FlowEdges.Add(new FlowEdge
        {
            Kind = FlowEdgeKind.RequestToHandler,
            From = commandA.FullName,
            To = "Shop.Application.Orders.CommandAHandler",
            ProjectName = "Shop.Application",
        });
        result.FlowEdges.Add(new FlowEdge
        {
            Kind = FlowEdgeKind.HandlerToNotification,
            From = "Shop.Application.Orders.CommandAHandler",
            To = "Shop.Application.Orders.OrderPlaced",
            ProjectName = "Shop.Application",
        });

        var notification = new CqrsNotification
        {
            Name = "OrderPlaced",
            Namespace = "Shop.Application.Orders",
            ProjectName = "Shop.Application",
        };
        notification.Handlers.Add("Shop.Application.Orders.OrderPlacedHandler");
        result.Notifications.Add(notification);
        result.NotificationHandlers.Add(new NotificationHandlerInfo
        {
            Name = "OrderPlacedHandler",
            Namespace = "Shop.Application.Orders",
            NotificationName = "OrderPlaced",
            ProjectName = "Shop.Application",
        });
        result.DispatchCallSites.Add(new DispatchCallSite
        {
            TargetFullName = notification.FullName,
            TargetKind = DispatchTargetKind.Notification,
            SenderFullName = "Shop.Application.Orders.CommandAHandler",
            ProjectName = "Shop.Application",
        });

        result.Behaviors.Add(new BehaviorInfo { Name = "LoggingBehavior", ProjectName = "Shop.Application" });

        var db = new DbContextInfo
        {
            Name = "ShopDbContext",
            Namespace = "Shop.Infrastructure",
            ProjectName = "Shop.Infrastructure",
        };
        var customer = new EntityInfo { Name = "Customer", Namespace = "Shop.Domain" };
        customer.Properties.Add(new EntityPropertyInfo { Name = "Id", Type = "int", IsKey = true });
        var order = new EntityInfo { Name = "Order", Namespace = "Shop.Domain" };
        order.Properties.Add(new EntityPropertyInfo { Name = "Id", Type = "int", IsKey = true });
        db.Entities.Add(customer);
        db.Entities.Add(order);
        db.Relationships.Add(new EntityRelationshipInfo(
            "Shop.Domain.Customer",
            "Shop.Domain.Order",
            PrincipalNavigation: "Orders",
            DependentNavigation: "Customer",
            ForeignKey: "CustomerId",
            IsRequired: true,
            Kind: EntityRelationshipKind.OneToMany));
        result.DbContexts.Add(db);

        return result;
    }

    [Fact]
    public void Export_ProducesExpectedFileSetWithExpectedNames_AndEveryFileParsesAsXml()
    {
        var directory = Path.Combine(_root, "diagrams");
        var written = SvgExporter.Export(RepresentativeResult(), directory);

        var names = written.Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.Equal(
            new[]
            {
                "behavior-chain.svg",
                "cqrs-map.svg",
                "er-diagram.svg",
                "flow.svg",
                "layers.svg",
                "notifications.svg",
                "project-graph.svg",
            },
            names);

        foreach (var path in written)
        {
            var document = XDocument.Load(path);
            Assert.Equal("svg", document.Root!.Name.LocalName);
            Assert.Equal(SvgNs, document.Root.Name.Namespace);
            Assert.False(string.IsNullOrEmpty(document.Root.Attribute("viewBox")?.Value));
        }

        Assert.Contains("CommandA", File.ReadAllText(Path.Combine(directory, "flow.svg")));
        Assert.DoesNotContain("<div", File.ReadAllText(Path.Combine(directory, "flow.svg")));
    }

    [Fact]
    public void Export_DoesNotChangeNormalReportOutputs()
    {
        var result = RepresentativeResult();
        var mdBefore = ReportGenerator.Generate(result);
        var svgBefore = HtmlReportGenerator.Generate(result, DiagramRenderMode.Svg);

        SvgExporter.Export(result, Path.Combine(_root, "diagrams"));

        Assert.Equal(mdBefore, ReportGenerator.Generate(result));
        Assert.Equal(svgBefore, HtmlReportGenerator.Generate(result, DiagramRenderMode.Svg));
    }

    [Fact]
    public void Export_TwiceProducesIdenticalBytes()
    {
        var result = RepresentativeResult();
        var one = Path.Combine(_root, "one");
        var first = SvgExporter.Export(result, one);
        var firstBytes = first.ToDictionary(Path.GetFileName, File.ReadAllBytes)!;

        var second = SvgExporter.Export(result, Path.Combine(_root, "two"));
        Assert.Equal(
            firstBytes.Keys.OrderBy(n => n, StringComparer.Ordinal),
            second.Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal));
        foreach (var path in second)
            Assert.Equal(firstBytes[Path.GetFileName(path)!], File.ReadAllBytes(path));

        // Re-exporting into the same directory overwrites with identical bytes.
        var again = SvgExporter.Export(result, one);
        foreach (var path in again)
            Assert.Equal(firstBytes[Path.GetFileName(path)!], File.ReadAllBytes(path));
    }

    [Fact]
    public void SplitCqrsMap_ExportsOneFilePerPart()
    {
        var result = new AnalysisResult { RootPath = "/tmp/shop" };
        result.Projects.Add(new ProjectInfo
        {
            Name = "Shop.Application",
            FilePath = "/tmp/Shop.Application/Shop.Application.csproj",
            DirectoryPath = "/tmp/Shop.Application",
            Layer = "Application",
        });
        for (var i = 0; i < 16; i++)
        {
            var request = new CqrsRequest
            {
                Name = $"Command{i:00}",
                Namespace = "Shop.Application.Orders",
                ProjectName = "Shop.Application",
            };
            request.Handlers.Add($"Shop.Application.Orders.Command{i:00}Handler");
            result.Requests.Add(request);
        }

        var directory = Path.Combine(_root, "split");
        var names = SvgExporter.Export(result, directory).Select(Path.GetFileName).ToList();

        Assert.Contains("cqrs-map.svg", names);
        Assert.Contains("cqrs-map-2.svg", names);
    }

    [Fact]
    public void Export_ToAPathThatIsAFile_FailsWithNonZeroExitCode()
    {
        var filePath = Path.Combine(_root, "not-a-directory");
        File.WriteAllText(filePath, "x");

        var ex = Assert.Throws<CliFailureException>(() => SvgExporter.Export(RepresentativeResult(), filePath));
        Assert.NotEqual(ExitCodes.Success, ex.ExitCode);
        Assert.Contains("--export-svg", ex.Message);
    }

    [Fact]
    public void CliParser_ReadsExportSvgDirectory()
    {
        var command = CliParser.Parse(new[] { "src", "--export-svg", "diagrams" });

        Assert.Equal(CliCommandKind.Run, command.Kind);
        var options = Assert.IsType<CliOptions>(command.Options);
        Assert.Equal("diagrams", options.ExportSvgDirectory);
    }
}
