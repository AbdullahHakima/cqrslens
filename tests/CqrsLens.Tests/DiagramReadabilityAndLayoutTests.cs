using System.Globalization;
using System.Xml.Linq;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// Mathematical geometric and CSS verification for the SVG/HTML readability fixes:
/// 0 subgraph collisions, 0 node overlaps, 0 pill-pill/pill-node overlaps, 0 out-of-bounds shapes,
/// non-overflowing sequence labels, non-crushing 1:1 SVG scaling, and dark-mode CSS coverage.
/// </summary>
public sealed class DiagramReadabilityAndLayoutTests
{
    private static readonly XNamespace SvgNs = "http://www.w3.org/2000/svg";

    [Fact]
    public void SystemLandscape_SubgraphsNodesAndPills_HaveZeroOverlapsAndZeroOutOfBounds()
    {
        var result = BuildMultiTierSystemResult();
        var mermaid = DiagramBuilder.SystemLandscape(result);
        Assert.NotNull(mermaid);

        var svg = MermaidSvgRenderer.Render(mermaid!);
        AssertCleanFlowchartGeometry(svg, expectSubgraphs: true, minExpectedSubgraphs: 4);
        Assert.Contains("class=\"svg-sg-box\"", svg);
        Assert.Contains("class=\"svg-edge-path\"", svg);
        Assert.Contains("class=\"svg-node-box\"", svg);
        Assert.Contains("class=\"svg-node-text\"", svg);
    }

    [Fact]
    public void DenseDirectFlows_And_ResolvedFlowMap_HaveZeroOverlapsAndOmitRedundantCallsPills()
    {
        var result = BuildMultiTierSystemResult();

        var directSections = DirectFlowDiagramBuilder.Build(result);
        Assert.NotEmpty(directSections);
        foreach (var section in directSections)
        {
            Assert.DoesNotContain("-->|calls|", section.Source);
            Assert.DoesNotContain("-->|uses|", section.Source);
            var svg = MermaidSvgRenderer.Render(section.Source);
            AssertCleanFlowchartGeometry(svg, expectSubgraphs: true, minExpectedSubgraphs: 1);
        }

        var flowSections = DiagramBuilder.FlowDiagramSections(result);
        Assert.NotEmpty(flowSections);
        foreach (var section in flowSections)
        {
            var svg = MermaidSvgRenderer.Render(section.Source);
            AssertCleanFlowchartGeometry(svg, expectSubgraphs: false, minExpectedSubgraphs: 0);
        }
    }

    [Fact]
    public void ImpactDiagramBuilder_CapsToThreeCleanSubgraphsWithZeroOverlaps()
    {
        var target = new ChangeImpactTarget("Order", "entity", "Shop.Domain.Order", "Shop.Domain");
        var items = new List<ChangeImpactItem>();
        for (var i = 0; i < 30; i++)
        {
            items.Add(new ChangeImpactItem(
                i % 3 == 0 ? "entrypoint" : "service",
                i < 3 ? "DuplicateConsumer" : $"Controller{i:00}.Action{i:00}",
                $"Shop.Api.Type{i:00}",
                "Shop.Api",
                "upstream",
                i % 2 + 1,
                "reads entity",
                ["Order"]));
        }
        for (var i = 0; i < 20; i++)
        {
            items.Add(new ChangeImpactItem(
                i == 0 ? "datastore" : "dbcontext",
                i == 0 ? "Linq2Db (Shop.Data)" : $"Context{i:00}",
                $"Shop.Data.Context{i:00}",
                "Shop.Data",
                "downstream",
                i % 2 + 1,
                "persists entity",
                ["Order"]));
        }

        var report = new ChangeImpactReport(
            Status: "analyzed",
            Confidence: "high",
            MaxDepth: 4,
            Target: target,
            Candidates: [target],
            ImpactedItems: items,
            AffectedProjects: ["Shop.Api", "Shop.Data"],
            ExistingTestReferences: [new TestMethodReference("Shop.Tests", "OrderTests.cs", 12, "OrderTests", "CanCreate", "Shop.Api.Type01", "service", "direct")],
            UncoveredHotspots: ["Shop.Api.Type02", "Shop.Api.Type05"],
            BlindSpots: []);

        var mermaid = ImpactDiagramBuilder.BuildMermaid(report);
        Assert.NotNull(mermaid);
        Assert.Contains("subgraph grp_upstream", mermaid);
        Assert.Contains("subgraph grp_target", mermaid);
        Assert.Contains("subgraph grp_downstream", mermaid);

        var svg = MermaidSvgRenderer.Render(mermaid!);
        var (_, nodeCount) = AssertCleanFlowchartGeometry(svg, expectSubgraphs: true, minExpectedSubgraphs: 3);
        Assert.InRange(nodeCount, 4, 25);
    }

    [Fact]
    public void MermaidSequenceRenderer_PreventsTextOverflow_HandlesSelfCall_AndRepeatsBottomHeaders()
    {
        var mermaid = """
            sequenceDiagram
                %% Request sequence: ProcessEnterpriseOrderCommand
                participant ep as POST /api/v2/enterprise/orders/{orderId}
                participant svc as EnterpriseOrderOrchestrationService
                participant repo as EnterpriseOrderSqlRepository
                participant db as EnterpriseOrderingDbContext
                ep->>svc: calls ProcessEnterpriseOrderWithFullValidationAsync
                svc->>svc: NormalizeAndValidateLineItems
                svc->>repo: resolved via IEnterpriseOrderRepository -> EnterpriseOrderSqlRepository
                repo-->>db: uses Order, OrderLine, Customer, PaymentMethod, AuditEntry
                Note over svc,db: Transactional boundary
                db-->>ep: returns OrderReceiptDto
            """;

        var svg = MermaidSvgRenderer.Render(mermaid);
        var doc = ParseEmbeddedSvg(svg, out var canvasW, out var canvasH);

        var participantBoxes = doc.Descendants(SvgNs + "g")
            .Where(g => g.Attribute("class")?.Value == "seq-participant")
            .ToList();
        // 4 participants × 2 (top + bottom because rows >= 6).
        Assert.Equal(8, participantBoxes.Count);

        var centers = participantBoxes
            .Take(4)
            .ToDictionary(
                g => g.Attribute("data-id")!.Value,
                g =>
                {
                    var rect = g.Element(SvgNs + "rect")!;
                    return Num(rect, "x") + Num(rect, "width") / 2.0;
                });

        var messages = doc.Descendants(SvgNs + "g")
            .Where(g => g.Attribute("class")?.Value == "seq-message")
            .ToList();
        Assert.Equal(5, messages.Count);

        foreach (var msg in messages)
        {
            var from = msg.Attribute("data-from")!.Value;
            var to = msg.Attribute("data-to")!.Value;
            var bg = msg.Element(SvgNs + "rect");
            Assert.NotNull(bg);
            var bgX = Num(bg!, "x");
            var bgY = Num(bg!, "y");
            var bgW = Num(bg!, "width");
            var bgH = Num(bg!, "height");

            Assert.True(bgX >= 0 && bgX + bgW <= canvasW, $"Sequence pill outside X bounds: {bgX}..{bgX + bgW} vs {canvasW}");
            Assert.True(bgY >= 0 && bgY + bgH <= canvasH, $"Sequence pill outside Y bounds: {bgY}..{bgY + bgH} vs {canvasH}");

            if (from != to)
            {
                var span = Math.Abs(centers[to] - centers[from]);
                Assert.True(span >= bgW, $"Sequence label pill width ({bgW}) overflows lifeline span ({span}) for {from}->{to}.");
            }
            else
            {
                Assert.NotNull(msg.Element(SvgNs + "path"));
            }
        }
    }

    [Fact]
    public void ErDiagramSvgWriter_DistributesLabelsWithoutOverlapInsideBounds()
    {
        var db = new DbContextInfo
        {
            Name = "ShopDbContext",
            Namespace = "Shop.Data",
            ProjectName = "Shop.Data",
        };

        var customer = new EntityInfo { Name = "Customer", Namespace = "Shop.Domain" };
        customer.Properties.Add(new EntityPropertyInfo { Name = "Id", Type = "int", IsKey = true });
        var order = new EntityInfo { Name = "Order", Namespace = "Shop.Domain" };
        order.Properties.Add(new EntityPropertyInfo { Name = "Id", Type = "int", IsKey = true });
        order.Properties.Add(new EntityPropertyInfo { Name = "CustomerId", Type = "int", IsForeignKey = true });
        var item = new EntityInfo { Name = "OrderItem", Namespace = "Shop.Domain" };
        item.Properties.Add(new EntityPropertyInfo { Name = "Id", Type = "int", IsKey = true });
        item.Properties.Add(new EntityPropertyInfo { Name = "OrderId", Type = "int", IsForeignKey = true });
        var employee = new EntityInfo { Name = "Employee", Namespace = "Shop.Domain" };
        employee.Properties.Add(new EntityPropertyInfo { Name = "Id", Type = "int", IsKey = true });

        db.Entities.AddRange([customer, order, item, employee]);
        db.Relationships.Add(new EntityRelationshipInfo(
            "Shop.Domain.Customer", "Shop.Domain.Order", "Orders", "Customer", "CustomerId", true, EntityRelationshipKind.OneToMany));
        db.Relationships.Add(new EntityRelationshipInfo(
            "Shop.Domain.Order", "Shop.Domain.OrderItem", "Items", "Order", "OrderId", true, EntityRelationshipKind.OneToMany));
        db.Relationships.Add(new EntityRelationshipInfo(
            "Shop.Domain.Customer", "Shop.Domain.OrderItem", "PurchasedItems", "Buyer", "BuyerId", false, EntityRelationshipKind.OneToMany));
        db.Relationships.Add(new EntityRelationshipInfo(
            "Shop.Domain.Employee", "Shop.Domain.Employee", "Reports", "Manager", "ManagerId", false, EntityRelationshipKind.OneToMany));

        var erMermaid = DiagramBuilder.ErDiagram(db)!;
        var svg = MermaidSvgRenderer.Render(erMermaid);
        var doc = ParseEmbeddedSvg(svg, out var canvasW, out var canvasH);

        var entities = doc.Descendants(SvgNs + "g")
            .Where(g => g.Attribute("class")?.Value == "er-entity")
            .Select(g => ToBox(g.Element(SvgNs + "rect")!))
            .ToList();
        var pills = doc.Descendants(SvgNs + "g")
            .Where(g => g.Attribute("class")?.Value == "er-relationship-label")
            .Select(g => ToBox(g.Element(SvgNs + "rect")!))
            .ToList();

        Assert.Equal(4, entities.Count);
        Assert.NotEmpty(pills);
        Assert.Equal(0, CountPairwiseOverlaps(entities));
        Assert.Equal(0, CountPairwiseOverlaps(pills));
        Assert.Equal(0, CountCrossOverlaps(pills, entities));

        foreach (var box in entities.Concat(pills))
        {
            Assert.True(box.X >= 0 && box.X + box.W <= canvasW + 0.5, $"ER shape outside X bounds: {box}");
            Assert.True(box.Y >= 0 && box.Y + box.H <= canvasH + 0.5, $"ER shape outside Y bounds: {box}");
        }
    }

    [Fact]
    public void HtmlReports_PreserveUnscaledSvg_WrapCards_AndIncludeDarkModeStyling()
    {
        var result = BuildMultiTierSystemResult();

        var svgHtml = HtmlReportGenerator.Generate(result, DiagramRenderMode.Svg);
        Assert.Contains("max-width: none", svgHtml);
        Assert.Contains("card-value card-value-text", svgHtml);
        Assert.Contains(".svg-sg-box", svgHtml);
        Assert.Contains(".svg-edge-path", svgHtml);
        Assert.Contains(".svg-seq-box", svgHtml);
        Assert.Contains(".svg-er-box", svgHtml);
        Assert.DoesNotContain("<script", svgHtml, StringComparison.OrdinalIgnoreCase);

        var mermaidHtml = HtmlReportGenerator.Generate(result, DiagramRenderMode.Mermaid);
        Assert.Contains("mermaid-zoom-btn", mermaidHtml);
        Assert.Contains("pre.mermaid { background: #f1f5f9;", mermaidHtml);

        var impact = ChangeImpactAnalyzer.Analyze(result, "Shop.Orders.Api", "project");
        var impactHtml = ImpactReportGenerator.GenerateHtml(impact, DiagramRenderMode.Svg);
        Assert.Contains(".callout-warn { background: #383016;", impactHtml);
        Assert.DoesNotContain("<script", impactHtml, StringComparison.OrdinalIgnoreCase);
    }

    private static AnalysisResult BuildMultiTierSystemResult()
    {
        var result = new AnalysisResult { RootPath = "/workspace/ShopDemo" };

        var domain = new ProjectInfo { Name = "Shop.Domain", FilePath = "/workspace/ShopDemo/Shop.Domain.csproj", DirectoryPath = "/workspace/ShopDemo/Domain", Layer = "Domain" };
        var appHost = new ProjectInfo { Name = "Shop.AppHost", FilePath = "/workspace/ShopDemo/Shop.AppHost.csproj", DirectoryPath = "/workspace/ShopDemo/AppHost", Layer = "Other" };
        var webUi = new ProjectInfo { Name = "Shop.WebApp", FilePath = "/workspace/ShopDemo/Shop.WebApp.csproj", DirectoryPath = "/workspace/ShopDemo/WebApp", Layer = "Web" };
        var catalogApi = new ProjectInfo { Name = "Shop.Catalog.Api", FilePath = "/workspace/ShopDemo/Shop.Catalog.Api.csproj", DirectoryPath = "/workspace/ShopDemo/Catalog", Layer = "Api" };
        var ordersApi = new ProjectInfo { Name = "Shop.Orders.Api", FilePath = "/workspace/ShopDemo/Shop.Orders.Api.csproj", DirectoryPath = "/workspace/ShopDemo/Orders", Layer = "Api" };
        var worker = new ProjectInfo { Name = "Shop.OrderProcessor", FilePath = "/workspace/ShopDemo/Shop.OrderProcessor.csproj", DirectoryPath = "/workspace/ShopDemo/Worker", Layer = "Infrastructure" };

        result.Projects.AddRange([domain, appHost, webUi, catalogApi, ordersApi, worker]);

        result.ServiceProfiles.Add(new ServiceProfileInfo { ProjectName = "Shop.AppHost", RuntimeRole = ServiceRuntimeRole.Orchestrator });
        result.ServiceProfiles.Add(new ServiceProfileInfo { ProjectName = "Shop.WebApp", RuntimeRole = ServiceRuntimeRole.Ui, HttpEndpointCount = 4 });
        result.ServiceProfiles.Add(new ServiceProfileInfo { ProjectName = "Shop.Catalog.Api", RuntimeRole = ServiceRuntimeRole.Api, HttpEndpointCount = 6, Technologies = { "EF Core", "PostgreSQL" }, EntryFiles = { "src/Catalog/Apis/CatalogApi.cs" } });
        result.ServiceProfiles.Add(new ServiceProfileInfo { ProjectName = "Shop.Orders.Api", RuntimeRole = ServiceRuntimeRole.Api, HttpEndpointCount = 4, RequestCount = 2, DbContextCount = 1, Technologies = { "EF Core", "PostgreSQL" }, EntryFiles = { "src/Orders/Controllers/VeryLongNamespaceArea/OrdersAdminController.cs" } });
        result.ServiceProfiles.Add(new ServiceProfileInfo { ProjectName = "Shop.OrderProcessor", RuntimeRole = ServiceRuntimeRole.Worker, WorkerCount = 2, MessageConsumerCount = 2 });

        result.DbContexts.Add(new DbContextInfo { Name = "OrdersContext", Namespace = "Shop.Orders.Data", ProjectName = "Shop.Orders.Api" });
        result.DataStores.Add(new DataStoreInfo { Name = "OrdersContext", Kind = DataStoreKind.EfCore, Technology = "EF Core", Provider = "PostgreSQL", ProjectName = "Shop.Orders.Api" });
        result.DataStores.Add(new DataStoreInfo { Name = "Redis (Shop.Orders.Api)", Kind = DataStoreKind.Redis, Technology = "Redis", Provider = "Redis", ProjectName = "Shop.Orders.Api" });

        result.IntegrationFlows.Add(new IntegrationFlowEdge { FromProject = "Shop.AppHost", ToProject = "Shop.Orders.Api", Kind = IntegrationFlowKind.AspireReference, Label = "orders-api" });
        result.IntegrationFlows.Add(new IntegrationFlowEdge { FromProject = "Shop.AppHost", ToProject = "Shop.Catalog.Api", Kind = IntegrationFlowKind.AspireReference, Label = "catalog-api" });
        result.IntegrationFlows.Add(new IntegrationFlowEdge { FromProject = "Shop.WebApp", ToProject = "Shop.Catalog.Api", Kind = IntegrationFlowKind.HttpClient, Label = "CatalogClient" });
        result.IntegrationFlows.Add(new IntegrationFlowEdge { FromProject = "Shop.WebApp", ToProject = "Shop.Orders.Api", Kind = IntegrationFlowKind.GrpcClient, Label = "OrdersGrpcClient" });
        result.IntegrationFlows.Add(new IntegrationFlowEdge { FromProject = "Shop.Orders.Api", ToProject = "Shop.OrderProcessor", Kind = IntegrationFlowKind.IntegrationEvent, Label = "OrderPlacedIntegrationEvent" });
        result.IntegrationFlows.Add(new IntegrationFlowEdge { FromProject = "Shop.OrderProcessor", ToProject = "Shop.Catalog.Api", Kind = IntegrationFlowKind.IntegrationEvent, Label = "StockConfirmedIntegrationEvent" });

        for (var i = 0; i < 6; i++)
        {
            var ep = new SystemEntrypointInfo
            {
                Name = $"CatalogApi.Action{i}",
                Kind = SystemEntrypointKind.Http,
                ExecutionStyle = SystemExecutionStyle.Direct,
                Method = i % 2 == 0 ? "GET" : "POST",
                RouteOrTrigger = $"/api/catalog/items/{i}",
                ProjectName = "Shop.Catalog.Api",
            };
            ep.CalledServices.AddRange(["CatalogQueryService", "CatalogInventoryService", "PricingService"]);
            ep.TouchedDbContexts.Add("OrdersContext");
            ep.TouchedDataStores.Add("Redis (Shop.Orders.Api)");
            ep.PublishedMessages.Add("CatalogUpdatedIntegrationEvent");
            result.SystemEntrypoints.Add(ep);
        }

        // CQRS Flow edges including long DI-resolved label.
        var cmd = new CqrsRequest { Name = "PlaceEnterpriseOrderCommand", Namespace = "Shop.Orders", ProjectName = "Shop.Orders.Api" };
        cmd.Handlers.Add("Shop.Orders.PlaceEnterpriseOrderHandler");
        result.Requests.Add(cmd);

        result.FlowEdges.Add(new FlowEdge { Kind = FlowEdgeKind.EndpointToRequest, From = "OrdersAdminController.Create", To = cmd.FullName, ProjectName = "Shop.Orders.Api" });
        result.FlowEdges.Add(new FlowEdge { Kind = FlowEdgeKind.RequestToHandler, From = cmd.FullName, To = "Shop.Orders.PlaceEnterpriseOrderHandler", ProjectName = "Shop.Orders.Api" });
        result.ResolvedEdges.Add(new ResolvedEdge
        {
            Kind = FlowEdgeKind.HandlerToDbContextResolved,
            From = "Shop.Orders.PlaceEnterpriseOrderHandler",
            Via = "IOrderFulfillmentService",
            To = "Shop.Orders.Data.OrdersContext",
            ViaChain = ["IOrderFulfillmentService", "OrderFulfillmentService", "IOrderRepository", "OrderRepository"],
        });

        return result;
    }

    private static (int SubgraphCount, int NodeCount) AssertCleanFlowchartGeometry(
        string svg,
        bool expectSubgraphs,
        int minExpectedSubgraphs)
    {
        var doc = ParseEmbeddedSvg(svg, out var canvasW, out var canvasH);

        var subgraphs = doc.Descendants(SvgNs + "g")
            .Where(g => g.Attribute("class")?.Value == "subgraph")
            .Select(g => ToBox(g.Element(SvgNs + "rect")!))
            .ToList();

        var nodes = doc.Descendants(SvgNs + "g")
            .Where(g => (g.Attribute("class")?.Value ?? "").Split(' ').Contains("node"))
            .Select(g => ToBox(g.Element(SvgNs + "rect")!))
            .ToList();

        var pills = doc.Descendants(SvgNs + "g")
            .Where(g => (g.Attribute("class")?.Value ?? "").Split(' ').Contains("edge-label"))
            .Select(g => ToBox(g.Element(SvgNs + "rect")!))
            .ToList();

        if (expectSubgraphs)
            Assert.True(subgraphs.Count >= minExpectedSubgraphs, $"Expected >= {minExpectedSubgraphs} subgraphs, got {subgraphs.Count}.");

        Assert.NotEmpty(nodes);
        Assert.Equal(0, CountPairwiseOverlaps(subgraphs));
        Assert.Equal(0, CountPairwiseOverlaps(nodes));
        Assert.Equal(0, CountPairwiseOverlaps(pills));
        Assert.Equal(0, CountCrossOverlaps(pills, nodes));

        foreach (var box in subgraphs.Concat(nodes).Concat(pills))
        {
            Assert.True(box.X >= 0 && box.X + box.W <= canvasW + 0.5, $"Box outside X bounds [0..{canvasW}]: {box}");
            Assert.True(box.Y >= 0 && box.Y + box.H <= canvasH + 0.5, $"Box outside Y bounds [0..{canvasH}]: {box}");
        }

        return (subgraphs.Count, nodes.Count);
    }

    private static XDocument ParseEmbeddedSvg(string htmlSnippet, out double width, out double height)
    {
        var start = htmlSnippet.IndexOf("<svg", StringComparison.Ordinal);
        var end = htmlSnippet.IndexOf("</svg>", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Expected embedded <svg>...</svg> markup.");
        var svgXml = htmlSnippet[start..(end + "</svg>".Length)];
        var doc = XDocument.Parse(svgXml);
        var root = doc.Root!;
        width = double.Parse(root.Attribute("width")!.Value, CultureInfo.InvariantCulture);
        height = double.Parse(root.Attribute("height")!.Value, CultureInfo.InvariantCulture);
        return doc;
    }

    private readonly record struct Box(double X, double Y, double W, double H);

    private static Box ToBox(XElement rect)
        => new(Num(rect, "x"), Num(rect, "y"), Num(rect, "width"), Num(rect, "height"));

    private static double Num(XElement el, string attr)
        => double.Parse(el.Attribute(attr)!.Value, CultureInfo.InvariantCulture);

    private static int CountPairwiseOverlaps(IReadOnlyList<Box> boxes)
    {
        var count = 0;
        for (var i = 0; i < boxes.Count; i++)
        {
            for (var j = i + 1; j < boxes.Count; j++)
            {
                if (Overlaps(boxes[i], boxes[j]))
                    count++;
            }
        }
        return count;
    }

    private static int CountCrossOverlaps(IReadOnlyList<Box> a, IReadOnlyList<Box> b)
    {
        var count = 0;
        foreach (var x in a)
        {
            foreach (var y in b)
            {
                if (Overlaps(x, y))
                    count++;
            }
        }
        return count;
    }

    private static bool Overlaps(Box a, Box b)
        => a.X < b.X + b.W && a.X + a.W > b.X && a.Y < b.Y + b.H && a.Y + a.H > b.Y;
}
