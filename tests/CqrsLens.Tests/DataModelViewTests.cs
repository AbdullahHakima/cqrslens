using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// Phase 6 Stage 1 aspect F: the combined solution-level data view. Counts must
/// reconcile with the model's DbContexts, per-context entity sets must equal the
/// ER sets, and entity-less contexts must be listed explicitly.
/// </summary>
public sealed class DataModelViewTests
{
    private static AnalysisResult Fixture()
    {
        var result = new AnalysisResult { RootPath = "/tmp/app" };

        var shop = new DbContextInfo { Name = "ShopDbContext", Namespace = "Shop.Infrastructure", ProjectName = "Shop.Infrastructure" };
        var customer = Entity("Customer", "Shop.Domain");
        customer.Properties.Add(new EntityPropertyInfo { Name = "Id", Type = "int", IsKey = true });
        var order = Entity("Order", "Shop.Domain");
        order.Properties.Add(new EntityPropertyInfo { Name = "Id", Type = "int", IsKey = true });
        order.Properties.Add(new EntityPropertyInfo { Name = "CustomerId", Type = "int", IsForeignKey = true });
        shop.Entities.Add(customer);
        shop.Entities.Add(order);
        shop.DbSets.Add(new DbSetInfo("Customer", "Customers") { EntityFullName = "Shop.Domain.Customer" });
        shop.DbSets.Add(new DbSetInfo("Order", "Orders") { EntityFullName = "Shop.Domain.Order" });
        shop.Relationships.Add(new EntityRelationshipInfo(
            "Shop.Domain.Customer", "Shop.Domain.Order",
            PrincipalNavigation: "Orders", DependentNavigation: "Customer",
            ForeignKey: "CustomerId", IsRequired: true, Kind: EntityRelationshipKind.OneToMany));
        // A relationship whose endpoint is not in the entity set must be ignored (never invented).
        shop.Relationships.Add(new EntityRelationshipInfo(
            "Shop.Domain.Customer", "Shop.Domain.Ghost",
            PrincipalNavigation: "Ghosts", DependentNavigation: "Customer",
            ForeignKey: "GhostId", IsRequired: false, Kind: EntityRelationshipKind.OneToMany));

        var billing = new DbContextInfo { Name = "BillingDbContext", Namespace = "Billing.Infrastructure", ProjectName = "Billing.Infrastructure" };
        var invoice = Entity("Invoice", "Billing.Domain");
        invoice.Properties.Add(new EntityPropertyInfo { Name = "Id", Type = "int", IsKey = true });
        billing.Entities.Add(invoice);
        billing.DbSets.Add(new DbSetInfo("Invoice", "Invoices") { EntityFullName = "Billing.Domain.Invoice" });

        var empty = new DbContextInfo { Name = "ApplicationDbContext", Namespace = "App.Infrastructure", ProjectName = "App.Infrastructure" };

        result.DbContexts.Add(shop);
        result.DbContexts.Add(billing);
        result.DbContexts.Add(empty);
        return result;
    }

    private static EntityInfo Entity(string name, string ns)
        => new() { Name = name, Namespace = ns, ProjectName = "Shop.Domain" };

    [Fact]
    public void ContextCount_ReconcilesWithTheModel()
    {
        var result = Fixture();
        var view = DataModelView.Build(result);

        Assert.Equal(result.DbContexts.Count, view.ContextCount);
        Assert.Equal(3, view.ContextCount);
        Assert.Equal(2, view.ContextsWithEntities);
        Assert.Equal(1, view.ContextsWithoutEntities);
        Assert.Equal(3, view.EntityCount);
        Assert.Equal(1, view.RelationshipCount); // the Ghost endpoint is not an entity and is excluded
    }

    [Fact]
    public void PerContextEntitySets_EqualTheErDiagramSets()
    {
        var result = Fixture();
        var view = DataModelView.Build(result);

        foreach (var context in result.DbContexts)
        {
            var er = DiagramBuilder.ErDiagram(context);
            var viewContext = view.Contexts.Single(c => c.Name == context.Name);

            // The combined view uses exactly the context's entity set (ER returns null iff empty).
            Assert.Equal(
                context.Entities.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal),
                viewContext.Entities.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal));
            Assert.Equal(er is not null, viewContext.EntityCount > 0);
        }
    }

    [Fact]
    public void EntityLessContexts_AreListedExplicitly()
    {
        var result = Fixture();
        var view = DataModelView.Build(result);

        var empty = Assert.Single(view.Contexts.Where(c => c.EntityCount == 0));
        Assert.Equal("ApplicationDbContext", empty.Name);
        Assert.Contains(view.Contexts, c => c.Name == "ApplicationDbContext");
    }

    [Fact]
    public void MarkdownAndHtml_ReportTheSameCombinedNumbers()
    {
        var result = Fixture();

        var md = ReportGenerator.Generate(result);
        var html = HtmlReportGenerator.Generate(result, DiagramRenderMode.Svg);

        const string summary = "2 with entities, 1 with none · 3 entities · 1 relationships.";
        Assert.Contains("### Data model — all DbContexts", md);
        Assert.Contains(summary, md);
        Assert.Contains("Data model — all DbContexts", html);
        Assert.Contains(summary, html);

        // Entity-less context shown in both.
        Assert.Contains("ApplicationDbContext", md);
        Assert.Contains("ApplicationDbContext", html);
        Assert.Contains("DbContexts with no entities discovered (1)", md);
        Assert.Contains("DbContexts with no entities discovered (1)", html);

        // Per-context entity + relationship detail present in both.
        Assert.Contains("ShopDbContext", md);
        Assert.Contains("ShopDbContext", html);
        Assert.Contains("Orders / Customer", md);
        Assert.Contains("Orders / Customer", html);
    }
}
